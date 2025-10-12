// =============================================================================
// Handles squad-level movement mode (Combat vs Move) and transport state.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using SquadOfSteelMod.Combat;

namespace SquadOfSteelMod
{
    public static class SquadMovementRuntime
    {
        public enum MovementMode
        {
            Combat = 0,
            Move = 1
        }

        const int MoveModeActionPointBonus = 3;
        const byte MoveModeMovementBonus = 3;
        const float MoveModeIncomingHitChanceBonus = 0.15f;
        const float MoveModeIncomingDamageMultiplier = 1.2f;
        const float MoveModeAttackPenalty = 0.12f;
        const string TransportMappingFileName = "transport-mappings.json";

        static readonly Dictionary<int, MovementMode> s_modes = new Dictionary<int, MovementMode>();
        static readonly Dictionary<int, int> s_apBuffs = new Dictionary<int, int>();
        static readonly Dictionary<int, MovementBuffRecord> s_movementBuffs = new Dictionary<int, MovementBuffRecord>();
        static readonly Dictionary<int, int> s_lastAppliedTurn = new Dictionary<int, int>();
        static readonly Dictionary<int, TransportState> s_transportStates = new Dictionary<int, TransportState>();
        static readonly Dictionary<string, CarrierMapping> s_mappingsByInfantry = new Dictionary<string, CarrierMapping>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CarrierMapping> s_mappingsByCarrier = new Dictionary<string, CarrierMapping>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Unit> s_unitDefinitions = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> s_missingUnitDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> s_missingTransportMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static bool s_mappingsLoaded;

        static readonly PropertyInfo s_filterTypeProp = AccessTools.Property(typeof(Unit), "FilterType");
        static readonly PropertyInfo s_typeProp = AccessTools.Property(typeof(Unit), "Type");

        public static float IncomingHitChanceBonus => MoveModeIncomingHitChanceBonus;
        public static float IncomingDamageMultiplier => MoveModeIncomingDamageMultiplier;
        public static float AttackerPenalty => MoveModeAttackPenalty;
        static int CurrentTurn => GameData.Instance?.map?.turnCount ?? -1;

        sealed class MovementBuffRecord
        {
            public byte Bonus;
            public byte BaselineMax;
            public byte BaselineCurr;
            public byte BaselineTampMax;
            public byte BaselineTampCurr;
            public bool Applied;
        }

        sealed class CarrierMapping
        {
            public string InfantryName;
            public string CarrierName;
            public Unit InfantryPrototype;
            public Unit CarrierPrototype;
        }

        sealed class TransportState
        {
            public int UnitId;
            public CarrierMapping Mapping;
            public Unit InfantrySnapshot;
            public Unit CarrierSnapshot;
            public MovementMode CurrentForm;
        }

        public static void InitializeFromSave(Dictionary<int, MovementMode> snapshot)
        {
            s_modes.Clear();
            s_apBuffs.Clear();
            s_movementBuffs.Clear();
            s_lastAppliedTurn.Clear();
            s_transportStates.Clear();
            s_missingTransportMappings.Clear();
            EnsureCarrierMappingsLoaded();

            if (snapshot == null || snapshot.Count == 0)
                return;

            foreach (var pair in snapshot)
            {
                s_modes[pair.Key] = pair.Value;
            }
        }

        public static Dictionary<int, MovementMode> ExportState()
        {
            return new Dictionary<int, MovementMode>(s_modes);
        }

        public static MovementMode GetMode(Unit unit)
        {
            if (unit == null)
                return MovementMode.Combat;

            return s_modes.TryGetValue(unit.ID, out var mode) ? mode : MovementMode.Combat;
        }

        public static bool TryToggleMoveMode(UnitGO unitGO, bool showFeedback = true)
        {
            if (unitGO?.unit == null)
                return false;

            MovementMode current = GetMode(unitGO.unit);
            MovementMode desired = current == MovementMode.Combat ? MovementMode.Move : MovementMode.Combat;
            return TrySetMode(unitGO, desired, showFeedback);
        }

        public static bool TrySetMode(UnitGO unitGO, MovementMode desired, bool showFeedback = true)
        {
            if (unitGO?.unit == null)
                return false;

            var unit = unitGO.unit;
            MovementMode current = GetMode(unit);
            if (current == desired)
                return true;

            if (desired == MovementMode.Move && !EligibleForMoveMode(unit))
            {
                if (showFeedback)
                {
                    string hint = $"{unit.Name} cannot enter move mode (requires foot infantry with inherent transport).";
                    SquadCombatRuntime.NotifyBlocked(unitGO, hint);
                }
                return false;
            }

            bool success = desired == MovementMode.Move ? EnterMoveMode(unitGO) : ExitMoveMode(unitGO);
            if (!success)
                return false;

            if (desired == MovementMode.Move)
            {
                s_modes[unit.ID] = MovementMode.Move;
            }
            else
            {
                s_modes.Remove(unit.ID);
            }

            SquadOfSteelState.MarkDirty();

            if (showFeedback)
            {
                Debug.Log($"[SquadOfSteel] {unit.Name} switched to {(desired == MovementMode.Move ? "MOVE" : "COMBAT")} mode.");
            }

            return true;
        }

        public static void Resync(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return;

            MovementMode mode = GetMode(unitGO.unit);
            if (mode == MovementMode.Move)
            {
                EnterMoveMode(unitGO);
            }
            else
            {
                ExitMoveMode(unitGO);
            }
        }

        public static void Clear(Unit unit)
        {
            if (unit == null)
                return;

            s_modes.Remove(unit.ID);
            s_apBuffs.Remove(unit.ID);
            s_movementBuffs.Remove(unit.ID);
            s_lastAppliedTurn.Remove(unit.ID);
            s_transportStates.Remove(unit.ID);
        }

        static bool EnterMoveMode(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return false;

            bool swapped = TryApplyTransportSwap(unitGO, MovementMode.Move);
            bool success = swapped || ApplyLegacyMoveModeBuff(unitGO.unit);
            if (!success)
                return false;

            HideSuppressionIndicator(unitGO);
            ShowMoveIndicator(unitGO, true);
            RefreshMovementOverlay(unitGO);
            return true;
        }

        static bool ExitMoveMode(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return false;

            var unit = unitGO.unit;
            bool swapped = TryApplyTransportSwap(unitGO, MovementMode.Combat);
            if (!swapped)
            {
                RemoveActionPointBuff(unit);
                RemoveMovementBuff(unit);
            }

            ShowSuppressionIndicator(unitGO);
            ShowMoveIndicator(unitGO, false);
            RefreshMovementOverlay(unitGO);
            return true;
        }

        static bool ApplyLegacyMoveModeBuff(Unit unit)
        {
            if (unit == null)
                return false;

            if (s_apBuffs.ContainsKey(unit.ID))
            {
                EnsureActionPointBuff(unit);
            }
            else
            {
                ApplyActionPointDelta(unit, MoveModeActionPointBonus);
            }

            if (s_movementBuffs.TryGetValue(unit.ID, out var existing))
            {
                EnsureMovementBuff(unit, existing);
            }
            else
            {
                var record = new MovementBuffRecord
                {
                    Bonus = MoveModeMovementBonus,
                    Applied = false
                };
                ApplyMovementBuff(unit, record);
                s_movementBuffs[unit.ID] = record;
            }

            return true;
        }

        static void ApplyActionPointDelta(Unit unit, int delta)
        {
            if (unit == null || delta == 0)
                return;

            s_apBuffs[unit.ID] = delta;

            unit.AddRemoveActionPoints(delta);
            s_lastAppliedTurn[unit.ID] = CurrentTurn;
        }

        static void RemoveActionPointBuff(Unit unit)
        {
            if (unit == null)
                return;

            if (!s_apBuffs.TryGetValue(unit.ID, out int buff) || buff == 0)
                return;

            unit.AddRemoveActionPoints(-buff);
            s_apBuffs.Remove(unit.ID);
            s_lastAppliedTurn.Remove(unit.ID);
        }

        static void HideSuppressionIndicator(UnitGO unitGO)
        {
            SquadOfSteelSuppressionIndicator.For(unitGO)?.SetBadgeVisible(false);
        }

        static void ShowSuppressionIndicator(UnitGO unitGO)
        {
            SquadOfSteelSuppressionIndicator.For(unitGO)?.SetBadgeVisible(true);
        }

        static void ShowMoveIndicator(UnitGO unitGO, bool visible)
        {
            var indicator = SquadMoveModeIndicator.For(unitGO);
            if (indicator == null)
                return;

            if (visible)
            {
                indicator.Show();
            }
            else
            {
                indicator.Hide();
            }
        }

        static void RefreshMovementOverlay(UnitGO unitGO)
        {
            if (unitGO == null)
                return;

            if (MapGO.instance == null)
                return;

            if (MapGO.selectedUnitGO != unitGO)
                return;

            try
            {
                MapGO.instance.DestroyPotentialTilesAboveMap();
                unitGO.ShowPotentialPaths();
                MapGO.instance.ShowPotentialtileColor(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to refresh movement overlay: {ex.Message}");
            }
        }

        static void EnsureActionPointBuff(Unit unit)
        {
            if (unit == null)
                return;

            if (!s_apBuffs.TryGetValue(unit.ID, out int buff) || buff == 0)
                return;

            int currentTurn = CurrentTurn;
            if (!s_lastAppliedTurn.TryGetValue(unit.ID, out int lastTurn) || lastTurn != currentTurn)
            {
                unit.AddRemoveActionPoints(buff);
                s_lastAppliedTurn[unit.ID] = currentTurn;
            }
        }

        static void ApplyMovementBuff(Unit unit, MovementBuffRecord record)
        {
            if (unit == null || record == null)
                return;

            record.BaselineMax = unit.MaxMP;
            record.BaselineCurr = unit.CurrMP;
            record.BaselineTampMax = unit.TampMaxMP;
            record.BaselineTampCurr = unit.TampMP;
            byte targetMax = (byte)Mathf.Clamp(record.BaselineMax + record.Bonus, 0, byte.MaxValue);
            byte targetCurr = (byte)Mathf.Clamp(record.BaselineCurr + record.Bonus, 0, byte.MaxValue);
            unit.MaxMP = targetMax;
            unit.CurrMP = targetCurr;
            unit.TampMaxMP = targetMax;
            unit.TampMP = targetCurr;
            record.Applied = true;
        }

        static void EnsureMovementBuff(Unit unit, MovementBuffRecord record)
        {
            if (unit == null || record == null)
                return;

            byte expectedMax = (byte)Mathf.Clamp(record.BaselineMax + record.Bonus, 0, byte.MaxValue);
            if (!record.Applied || unit.MaxMP != expectedMax || unit.TampMaxMP != expectedMax)
            {
                record.Applied = false;
                ApplyMovementBuff(unit, record);
                return;
            }

        }

        static void RemoveMovementBuff(Unit unit)
        {
            if (unit == null)
                return;

            if (!s_movementBuffs.TryGetValue(unit.ID, out var record))
                return;

            byte restoredMax = record.BaselineMax;
            int spent = (record.BaselineCurr + record.Bonus) - unit.CurrMP;
            int restoredCurr = record.BaselineCurr - spent;
            restoredCurr = Mathf.Clamp(restoredCurr, 0, restoredMax);

            unit.MaxMP = restoredMax;
            unit.CurrMP = (byte)Mathf.Clamp(restoredCurr, 0, byte.MaxValue);
            unit.TampMaxMP = record.BaselineTampMax;

            int tampRestoredCurr = record.BaselineTampCurr - spent;
            tampRestoredCurr = Mathf.Clamp(tampRestoredCurr, 0, record.BaselineTampMax);
            unit.TampMP = (byte)Mathf.Clamp(tampRestoredCurr, 0, byte.MaxValue);

            s_movementBuffs.Remove(unit.ID);
        }

        static bool TryApplyTransportSwap(UnitGO unitGO, MovementMode desired)
        {
            if (unitGO?.unit == null)
                return false;

            var unit = unitGO.unit;
            var state = EnsureTransportState(unit);
            if (state == null)
                return false;

            if (desired == state.CurrentForm)
            {
                if (desired == MovementMode.Move && state.CarrierSnapshot != null)
                {
                    state.CarrierSnapshot.SyncFromNetwork(unit, false);
                }
                else if (desired == MovementMode.Combat && state.InfantrySnapshot != null)
                {
                    state.InfantrySnapshot.SyncFromNetwork(unit, false);
                }

                RefreshTransportVisuals(unitGO, desired);
                return true;
            }

            if (desired == MovementMode.Move)
            {
                if (state.InfantrySnapshot == null)
                    state.InfantrySnapshot = unit.Clone(false);

                state.InfantrySnapshot.SyncFromNetwork(unit, false);

                if (!UpdateCarrierSnapshotFromInfantry(state))
                    return false;

                unit.SyncFromNetwork(state.CarrierSnapshot, true);
                state.CurrentForm = MovementMode.Move;
                state.CarrierSnapshot.SyncFromNetwork(unit, false);
            }
            else
            {
                if (state.CarrierSnapshot == null)
                    state.CarrierSnapshot = unit.Clone(false);

                state.CarrierSnapshot.SyncFromNetwork(unit, false);

                if (!UpdateInfantrySnapshotFromCarrier(state))
                    return false;

                unit.SyncFromNetwork(state.InfantrySnapshot, true);
                state.CurrentForm = MovementMode.Combat;
                state.InfantrySnapshot.SyncFromNetwork(unit, false);
            }

            RefreshTransportVisuals(unitGO, desired);
            unitGO.UpdateCounter();
            SquadOfSteelSuppressionIndicator.For(unitGO)?.Refresh();
            return true;
        }

        static void RefreshTransportVisuals(UnitGO unitGO, MovementMode mode)
        {
            if (unitGO == null)
                return;

            try
            {
                unitGO.SetSprite();
                unitGO.ManageTwoUnitsIndicator();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to refresh transport visuals: {ex.Message}");
            }

            if (mode == MovementMode.Move)
            {
                ShowMoveIndicator(unitGO, true);
            }
            else
            {
                ShowMoveIndicator(unitGO, false);
            }
        }

        static TransportState EnsureTransportState(Unit unit)
        {
            if (unit == null)
                return null;

            if (!TryGetMappingForUnit(unit, out var mapping))
            {
                s_transportStates.Remove(unit.ID);
                return null;
            }

            if (s_transportStates.TryGetValue(unit.ID, out var existing))
                return existing;

            var state = new TransportState
            {
                UnitId = unit.ID,
                Mapping = mapping
            };

            bool isCarrier = string.Equals(unit.Name, mapping.CarrierName, StringComparison.OrdinalIgnoreCase);
            if (isCarrier)
            {
                state.CarrierSnapshot = unit.Clone(false);
                state.InfantrySnapshot = mapping.InfantryPrototype != null
                    ? mapping.InfantryPrototype.Clone(false)
                    : unit.Clone(false);

                CopyPersistentState(state.CarrierSnapshot, state.InfantrySnapshot);
                ApplyResourceRatios(state.CarrierSnapshot, state.InfantrySnapshot);
                state.CurrentForm = MovementMode.Move;
            }
            else
            {
                state.InfantrySnapshot = unit.Clone(false);
                if (!UpdateCarrierSnapshotFromInfantry(state))
                {
                    s_transportStates.Remove(unit.ID);
                    return null;
                }

                state.CurrentForm = MovementMode.Combat;
            }

            s_transportStates[unit.ID] = state;
            return state;
        }

        static bool UpdateCarrierSnapshotFromInfantry(TransportState state)
        {
            if (state?.InfantrySnapshot == null)
                return false;

            var mapping = state.Mapping;
            if (mapping == null)
                return false;

            if (state.CarrierSnapshot == null)
            {
                state.CarrierSnapshot = mapping.CarrierPrototype != null
                    ? mapping.CarrierPrototype.Clone(false)
                    : state.InfantrySnapshot.Clone(false);
            }
            else if (mapping.CarrierPrototype != null)
            {
                state.CarrierSnapshot.SyncFromNetwork(mapping.CarrierPrototype, true);
            }

            if (state.CarrierSnapshot == null)
                return false;

            CopyPersistentState(state.InfantrySnapshot, state.CarrierSnapshot);
            ApplyResourceRatios(state.InfantrySnapshot, state.CarrierSnapshot);
            return true;
        }

        static bool UpdateInfantrySnapshotFromCarrier(TransportState state)
        {
            if (state?.CarrierSnapshot == null)
                return false;

            var mapping = state.Mapping;
            if (mapping == null)
                return false;

            if (state.InfantrySnapshot == null)
            {
                state.InfantrySnapshot = mapping.InfantryPrototype != null
                    ? mapping.InfantryPrototype.Clone(false)
                    : state.CarrierSnapshot.Clone(false);
            }
            else if (mapping.InfantryPrototype != null)
            {
                state.InfantrySnapshot.SyncFromNetwork(mapping.InfantryPrototype, true);
            }

            if (state.InfantrySnapshot == null)
                return false;

            CopyPersistentState(state.CarrierSnapshot, state.InfantrySnapshot);
            ApplyResourceRatios(state.CarrierSnapshot, state.InfantrySnapshot);
            return true;
        }

        static bool TryGetMappingForUnit(Unit unit, out CarrierMapping mapping)
        {
            mapping = null;
            if (unit == null)
                return false;

            EnsureCarrierMappingsLoaded();

            string name = unit.Name ?? string.Empty;
            if (s_mappingsByInfantry.TryGetValue(name, out mapping))
                return true;

            if (s_mappingsByCarrier.TryGetValue(name, out mapping))
                return true;

            if (s_missingTransportMappings.Add(name))
            {
                Debug.Log($"[SquadOfSteel] No transport mapping configured for '{name}'. Falling back to legacy move-mode buffs.");
            }

            return false;
        }

        static void EnsureCarrierMappingsLoaded()
        {
            if (s_mappingsLoaded)
                return;

            s_mappingsLoaded = true;
            s_mappingsByInfantry.Clear();
            s_mappingsByCarrier.Clear();

            try
            {
                string configPath = ResolveTransportMappingPath();
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                {
                    Debug.Log("[SquadOfSteel] Transport mapping file not found; using legacy move mode behaviour.");
                    return;
                }

                string json = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (entries == null || entries.Count == 0)
                    return;

                foreach (var pair in entries)
                {
                    RegisterMapping(pair.Key, pair.Value);
                }

                Debug.Log($"[SquadOfSteel] Loaded {s_mappingsByInfantry.Count} transport mappings.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to load transport mappings: {ex.Message}");
            }
        }

        static string ResolveTransportMappingPath()
        {
            try
            {
                string assemblyDir = Path.GetDirectoryName(typeof(SquadMovementRuntime).Assembly.Location);
                if (string.IsNullOrEmpty(assemblyDir))
                    return null;

                var candidates = new List<string>
                {
                    Path.Combine(assemblyDir, TransportMappingFileName),
                    Path.Combine(assemblyDir, "Assets", TransportMappingFileName)
                };

                var parent = Directory.GetParent(assemblyDir);
                if (parent != null)
                {
                    candidates.Add(Path.Combine(parent.FullName, TransportMappingFileName));
                    candidates.Add(Path.Combine(parent.FullName, "Assets", TransportMappingFileName));
                }

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to locate transport mapping file: {ex.Message}");
            }

            return null;
        }

        static void RegisterMapping(string infantryName, string carrierName)
        {
            if (string.IsNullOrWhiteSpace(infantryName) || string.IsNullOrWhiteSpace(carrierName))
                return;

            infantryName = infantryName.Trim();
            carrierName = carrierName.Trim();

            if (infantryName.StartsWith("#", StringComparison.Ordinal) ||
                infantryName.StartsWith("//", StringComparison.Ordinal) ||
                infantryName.StartsWith("_", StringComparison.Ordinal))
                return;

            if (!TryGetUnitDefinition(infantryName, out var infantryDefinition))
                return;

            if (!TryGetUnitDefinition(carrierName, out var carrierDefinition))
                return;

            var mapping = new CarrierMapping
            {
                InfantryName = infantryName,
                CarrierName = carrierName,
                InfantryPrototype = infantryDefinition.Clone(false),
                CarrierPrototype = carrierDefinition.Clone(false)
            };

            s_mappingsByInfantry[infantryName] = mapping;
            s_mappingsByCarrier[carrierName] = mapping;
        }

        static bool TryGetUnitDefinition(string unitName, out Unit definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(unitName))
                return false;

            if (s_unitDefinitions.TryGetValue(unitName, out definition))
                return true;

            var source = FindUnitDefinitionInternal(unitName);
            if (source == null)
            {
                if (s_missingUnitDefinitions.Add(unitName))
                {
                    Debug.LogWarning($"[SquadOfSteel] Could not find unit definition '{unitName}' while loading transport mappings.");
                }

                return false;
            }

            definition = source.Clone(false);
            s_unitDefinitions[unitName] = definition;
            return true;
        }

        static Unit FindUnitDefinitionInternal(string unitName)
        {
            try
            {
                var official = OfficialUnits.GetInstance();
                var units = official?.Units;
                if (units != null)
                {
                    var match = units.FirstOrDefault(u => string.Equals(u.Name, unitName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to query official units for '{unitName}': {ex.Message}");
            }

            return null;
        }

        static void CopyPersistentState(Unit source, Unit target)
        {
            if (source == null || target == null)
                return;

            target.CustomName = source.CustomName;
            target.ForcedCustomName = source.ForcedCustomName;
            target.OwnerName = source.OwnerName;
            target.OwnerPlayer = source.OwnerPlayer;
            target.Hero = source.Hero;

            target.Lvl = source.Lvl;
            target.XP = source.XP;
            target.TotalKill = source.TotalKill;
            target.TotalDistance = source.TotalDistance;
            target.TankKill = source.TankKill;
            target.AntitankKill = source.AntitankKill;
            target.ArtilleryKill = source.ArtilleryKill;
            target.InfantryKill = source.InfantryKill;
            target.FighterKill = source.FighterKill;
            target.CASKill = source.CASKill;
            target.HeavyBomberKill = source.HeavyBomberKill;
            target.AAKill = source.AAKill;
            target.BoatKill = source.BoatKill;

            target.AttackedAmountThisTurn = source.AttackedAmountThisTurn;
            target.CommanderTargets = source.CommanderTargets;

            target.ActionPoints = source.ActionPoints;
            target.HasMoved = source.HasMoved;
            target.HasAttacked = source.HasAttacked;
            target.HasBuilt = source.HasBuilt;
            target.HasResuppliedAmmo = source.HasResuppliedAmmo;
            target.HasResuppliedFuel = source.HasResuppliedFuel;
            target.HasOverrun = source.HasOverrun;

            target.NumberOfTilesMovedDuringCurrentTurn = source.NumberOfTilesMovedDuringCurrentTurn;
            target.NumberOfTimesTheUnitMoved = source.NumberOfTimesTheUnitMoved;
            target.NumberOfTurnsUnderWater = source.NumberOfTurnsUnderWater;
            target.IsSubmerged = source.IsSubmerged;
            target.IsOnBoat = source.IsOnBoat;
            target.IsTrain = source.IsTrain;

            target.EntrenchmentLevel = source.EntrenchmentLevel;
            target.Morale = source.Morale;
            target.IsCoreUnit = source.IsCoreUnit;
            target.IsReserve = source.IsReserve;
            target.IsKillGoal = source.IsKillGoal;
            target.AutoSupplies = source.AutoSupplies;
            target.IsEliteUnit = source.IsEliteUnit;
            target.IsWinterSpecialized = source.IsWinterSpecialized;
            target.IsMountaineer = source.IsMountaineer;
            target.IsPoliticalUnit = source.IsPoliticalUnit;

            target.Waypoints = CloneWaypoints(source.Waypoints);
            target.StuffThatHappened = CloneStringList(source.StuffThatHappened);
        }

        static List<int[]> CloneWaypoints(List<int[]> source)
        {
            if (source == null)
                return null;

            if (source.Count == 0)
                return new List<int[]>();

            var clone = new List<int[]>(source.Count);
            foreach (var entry in source)
            {
                if (entry == null)
                {
                    clone.Add(Array.Empty<int>());
                }
                else
                {
                    var path = new int[entry.Length];
                    Array.Copy(entry, path, entry.Length);
                    clone.Add(path);
                }
            }

            return clone;
        }

        static List<string> CloneStringList(List<string> source)
        {
            if (source == null)
                return null;

            return new List<string>(source);
        }

        static void ApplyResourceRatios(Unit source, Unit target)
        {
            if (source == null || target == null)
                return;

            int sourceMaxHp = Math.Max(1, source.MaxHP);
            int targetMaxHp = Math.Max(1, target.MaxHP);
            int newHp = Mathf.RoundToInt(targetMaxHp * (source.CurrHP / (float)sourceMaxHp));
            newHp = Mathf.Clamp(newHp, 0, targetMaxHp);
            target.CurrHP = newHp;
            target.BaseMaxHP = target.MaxHP;

            target.CurrMP = ConvertToByteByRatio(source.CurrMP, source.MaxMP, target.MaxMP);
            target.TampMP = target.CurrMP;
            target.TampMaxMP = target.MaxMP;

            target.CurrAmmo = ConvertToInt16ByRatio(source.CurrAmmo, source.MaxAmmo, target.MaxAmmo);

            target.CurrAutonomy = ConvertToInt16ByRatio(source.CurrAutonomy, source.MaxAutonomy, target.MaxAutonomy);
            target.TampAutonomy = target.CurrAutonomy;
            target.TampMaxAutonomy = target.MaxAutonomy;
        }

        static byte ConvertToByteByRatio(byte currentValue, byte sourceMax, byte targetMax)
        {
            if (targetMax == 0)
                return 0;

            if (sourceMax == 0)
                return targetMax;

            float ratio = Mathf.Clamp01(currentValue / (float)sourceMax);
            int projected = Mathf.RoundToInt(targetMax * ratio);
            projected = Mathf.Clamp(projected, 0, targetMax);
            return (byte)projected;
        }

        static short ConvertToInt16ByRatio(short currentValue, short sourceMax, short targetMax)
        {
            if (targetMax == 0)
                return 0;

            if (sourceMax == 0)
                return targetMax;

            float ratio = Mathf.Clamp01(currentValue / (float)sourceMax);
            int projected = Mathf.RoundToInt(targetMax * ratio);
            projected = Mathf.Clamp(projected, 0, targetMax);
            return (short)projected;
        }

        static bool EligibleForMoveMode(Unit unit)
        {
            if (unit == null)
                return false;

            if (TryGetMappingForUnit(unit, out _))
                return true;

            object filterValue = s_filterTypeProp?.GetValue(unit);
            if (filterValue != null && filterValue.ToString().IndexOf("Infantry", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            object typeValue = s_typeProp?.GetValue(unit);
            if (typeValue != null && typeValue.ToString().IndexOf("Infantry", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public static void OnTurnAdvance()
        {
            if (MapGO.instance == null)
                return;

            var units = MapGO.instance.listOfAllUnits;
            if (units == null)
                return;

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                if (GetMode(unit) != MovementMode.Move)
                    continue;

                EnsureActionPointBuff(unit);

                if (s_movementBuffs.TryGetValue(unit.ID, out var record))
                {
                    EnsureMovementBuff(unit, record);
                }

                if (unit.unitGO != null)
                {
                    HideSuppressionIndicator(unit.unitGO);
                    ShowMoveIndicator(unit.unitGO, true);
                    RefreshMovementOverlay(unit.unitGO);
                }
            }
        }
    }
}
