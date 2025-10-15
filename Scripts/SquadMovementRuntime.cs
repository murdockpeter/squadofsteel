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
        const string TransportMappingFilePattern = "transport-mappings*.json";

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
        static bool s_officialUnitsLoadAttempted;
        static bool s_loggedOfficialUnitsLoadFailure;
        static bool s_loggedOfficialUnitCount;

        static bool s_mappingsLoaded;
        static bool s_traceMoveMode;
        static readonly HashSet<int> s_activelyChangingMode = new HashSet<int>();

        static readonly PropertyInfo s_filterTypeProp = AccessTools.Property(typeof(Unit), "FilterType");
        static readonly PropertyInfo s_typeProp = AccessTools.Property(typeof(Unit), "Type");

        static void Trace(Unit unit, string message)
        {
            if (!s_traceMoveMode)
                return;

            string name = unit?.Name ?? "<null>";
            int id = unit?.ID ?? -1;
            Debug.Log($"[SquadOfSteel][MoveMode] {name} (ID:{id}) - {message}");
        }

        static void Trace(UnitGO unitGO, string message)
        {
            if (!s_traceMoveMode)
                return;

            string name = unitGO?.unit?.Name ?? "<null>";
            int id = unitGO?.unit?.ID ?? -1;
            Debug.Log($"[SquadOfSteel][MoveMode] {name} (ID:{id}) - {message}");
        }

        public static bool TraceEnabled => s_traceMoveMode;

        public static void SetTraceEnabled(bool enabled)
        {
            if (s_traceMoveMode == enabled)
                return;

            s_traceMoveMode = enabled;
            Debug.Log($"[SquadOfSteel][MoveMode] Diagnostic tracing {(enabled ? "ENABLED" : "disabled")}.");
        }

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
            public Dictionary<string, string> NationalityMappings; // nationality -> carrier name
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
            Trace(unit, $"TrySetMode requested -> {desired}");
            MovementMode current = GetMode(unit);
            if (current == desired)
            {
                Trace(unit, $"Already in {desired}; no change required.");
                return true;
            }

            if (desired == MovementMode.Move && !EligibleForMoveMode(unit))
            {
                if (showFeedback)
                {
                    string hint = $"{unit.Name} cannot enter move mode (requires foot infantry with inherent transport).";
                    SquadCombatRuntime.NotifyBlocked(unitGO, hint);
                }
                Trace(unit, "Move mode denied - unit not eligible.");
                return false;
            }

            bool success = desired == MovementMode.Move ? EnterMoveMode(unitGO) : ExitMoveMode(unitGO);
            if (!success)
            {
                Trace(unit, $"TrySetMode -> {desired} failed.");
                return false;
            }

            if (desired == MovementMode.Move)
            {
                s_modes[unit.ID] = MovementMode.Move;
                Trace(unit, "Mode recorded as MOVE.");
            }
            else
            {
                s_modes.Remove(unit.ID);
                Trace(unit, "Mode entry removed (COMBAT).");
            }

            SquadOfSteelState.MarkDirty();

            if (showFeedback)
            {
                Debug.Log($"[SquadOfSteel] {unit.Name} switched to {(desired == MovementMode.Move ? "MOVE" : "COMBAT")} mode.");
            }

            Trace(unit, $"TrySetMode -> {desired} complete.");
            return true;
        }

        public static void Resync(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return;

            // Prevent recursion if we're already changing mode for this unit
            if (s_activelyChangingMode.Contains(unitGO.unit.ID))
            {
                Trace(unitGO, "Resync: Skipping - mode change already in progress.");
                return;
            }

            MovementMode mode = GetMode(unitGO.unit);

            // Only perform transport swap if needed - avoid calling Enter/ExitMoveMode
            // which trigger RefreshMovementOverlay and cause infinite loops via UpdateCounter
            var state = s_transportStates.TryGetValue(unitGO.unit.ID, out var ts) ? ts : null;
            if (state != null && state.CurrentForm == mode)
            {
                // Already in correct state, no action needed
                Trace(unitGO, $"Resync: Already in {mode}, skipping to avoid recursion.");
                return;
            }

            // Only call the full Enter/Exit if we actually need to change state
            if (mode == MovementMode.Move)
            {
                // Don't call EnterMoveMode - just ensure transport state without visual refresh
                TryApplyTransportSwap(unitGO, MovementMode.Move);
            }
            else
            {
                // Don't call ExitMoveMode - just ensure transport state without visual refresh
                TryApplyTransportSwap(unitGO, MovementMode.Combat);
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

            s_activelyChangingMode.Add(unitGO.unit.ID);
            try
            {
                Trace(unitGO, "Entering MOVE mode.");
                bool swapped = TryApplyTransportSwap(unitGO, MovementMode.Move);
                bool success = swapped || ApplyLegacyMoveModeBuff(unitGO.unit);
                if (!success)
                {
                    Trace(unitGO, "Move mode entry failed (transport swap false, legacy fallback unavailable).");
                    return false;
                }

                Trace(unitGO, swapped ? "Transport swap applied for move mode." : "Legacy move-mode buffs applied (no mapping).");
                HideSuppressionIndicator(unitGO);
                ShowMoveIndicator(unitGO, true);
                RefreshMovementOverlay(unitGO);
                Trace(unitGO, "Move mode visuals refreshed.");
                return true;
            }
            finally
            {
                s_activelyChangingMode.Remove(unitGO.unit.ID);
            }
        }

        static bool ExitMoveMode(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return false;

            var unit = unitGO.unit;
            s_activelyChangingMode.Add(unit.ID);
            try
            {
                Trace(unitGO, "Exiting MOVE mode.");
                bool swapped = TryApplyTransportSwap(unitGO, MovementMode.Combat);
                if (!swapped)
                {
                    RemoveActionPointBuff(unit);
                    RemoveMovementBuff(unit);
                    Trace(unit, "Legacy move-mode buffs removed.");
                }
                else
                {
                    Trace(unit, "Transport swap reverted to combat form.");
                }

                ShowSuppressionIndicator(unitGO);
                ShowMoveIndicator(unitGO, false);
                RefreshMovementOverlay(unitGO);
                Trace(unitGO, "Combat mode visuals refreshed.");
                return true;
            }
            finally
            {
                s_activelyChangingMode.Remove(unit.ID);
            }
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
            {
                Trace(unitGO, "Move indicator unavailable (component missing).");
                return;
            }

            if (visible)
            {
                indicator.Show();
                Trace(unitGO, "Move indicator Show() invoked.");
            }
            else
            {
                indicator.Hide();
                Trace(unitGO, "Move indicator Hide() invoked.");
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
            Trace(unitGO, $"TryApplyTransportSwap -> {desired}");
            var state = EnsureTransportState(unit);
            if (state == null)
            {
                Trace(unitGO, "EnsureTransportState returned null; swap aborted.");
                return false;
            }

            if (desired == state.CurrentForm)
            {
                Trace(unitGO, $"Already in {desired}; refreshing snapshots only.");
                if (desired == MovementMode.Move && state.CarrierSnapshot != null)
                {
                    state.CarrierSnapshot.SyncFromNetwork(unit, false);
                }
                else if (desired == MovementMode.Combat && state.InfantrySnapshot != null)
                {
                    state.InfantrySnapshot.SyncFromNetwork(unit, false);
                }

                Trace(unitGO, $"No form change needed; skipping visual refresh to avoid recursion.");
                return true;
            }

            if (desired == MovementMode.Move)
            {
                Trace(unitGO, "Building carrier snapshot for MOVE mode.");
                if (state.InfantrySnapshot == null)
                    state.InfantrySnapshot = unit.Clone(false);

                state.InfantrySnapshot.SyncFromNetwork(unit, false);

                if (!UpdateCarrierSnapshotFromInfantry(state))
                {
                    Trace(unitGO, "UpdateCarrierSnapshotFromInfantry failed.");
                    return false;
                }

                unit.SyncFromNetwork(state.CarrierSnapshot, true);
                state.CurrentForm = MovementMode.Move;
                state.CarrierSnapshot.SyncFromNetwork(unit, false);
                Trace(unitGO, "Unit synced from carrier snapshot.");
            }
            else
            {
                Trace(unitGO, "Restoring infantry snapshot for COMBAT mode.");
                if (state.CarrierSnapshot == null)
                    state.CarrierSnapshot = unit.Clone(false);

                state.CarrierSnapshot.SyncFromNetwork(unit, false);

                if (!UpdateInfantrySnapshotFromCarrier(state))
                {
                    Trace(unitGO, "UpdateInfantrySnapshotFromCarrier failed.");
                    return false;
                }

                unit.SyncFromNetwork(state.InfantrySnapshot, true);
                state.CurrentForm = MovementMode.Combat;
                state.InfantrySnapshot.SyncFromNetwork(unit, false);
                Trace(unitGO, "Unit synced from infantry snapshot.");
            }

            unitGO.UpdateCounter();
            RefreshTransportVisuals(unitGO, desired);
            Trace(unitGO, $"Transport swap complete -> {desired}.");
            SquadOfSteelSuppressionIndicator.For(unitGO)?.Refresh();
            return true;
        }

        static void RefreshTransportVisuals(UnitGO unitGO, MovementMode mode)
        {
            if (unitGO == null)
                return;

            try
            {
                Trace(unitGO, "Refreshing UnitGO visuals (SetSprite & ManageTwoUnitsIndicator).");
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
                Trace(unitGO, "Move indicator toggled ON.");
            }
            else
            {
                ShowMoveIndicator(unitGO, false);
                Trace(unitGO, "Move indicator toggled OFF.");
            }
        }

        static bool EnsureMappingPrototypes(CarrierMapping mapping)
        {
            if (mapping == null)
                return false;

            bool resolved = true;

            if (mapping.InfantryPrototype == null)
            {
                if (TryGetUnitDefinition(mapping.InfantryName, out var infantryDefinition))
                {
                    mapping.InfantryPrototype = infantryDefinition.Clone(false);
                    Trace(mapping.InfantryPrototype, $"Infantry prototype cached for mapping -> '{mapping.CarrierName}'.");
                }
                else
                {
                    resolved = false;
                    Debug.LogWarning($"[SquadOfSteel] Transport mapping infantry prototype unresolved for '{mapping.InfantryName}'.");
                }
            }

            if (mapping.CarrierPrototype == null)
            {
                if (TryGetUnitDefinition(mapping.CarrierName, out var carrierDefinition))
                {
                    mapping.CarrierPrototype = carrierDefinition.Clone(false);
                    Trace(mapping.CarrierPrototype, $"Carrier prototype cached for mapping from '{mapping.InfantryName}'.");
                }
                else
                {
                    resolved = false;
                    Debug.LogWarning($"[SquadOfSteel] Transport mapping carrier prototype unresolved for '{mapping.CarrierName}'.");
                }
            }

            return resolved;
        }

        static TransportState EnsureTransportState(Unit unit)
        {
            if (unit == null)
                return null;

            if (!TryGetMappingForUnit(unit, out var mapping))
            {
                s_transportStates.Remove(unit.ID);
                Trace(unit, "No transport mapping available; using legacy move mode.");
                return null;
            }

            if (!EnsureMappingPrototypes(mapping))
            {
                Debug.LogWarning($"[SquadOfSteel] Transport mapping '{mapping.InfantryName}' -> '{mapping.CarrierName}' missing prototypes; move-mode swap disabled.");
                s_transportStates.Remove(unit.ID);
                Trace(unit, "Transport mapping prototypes unresolved.");
                return null;
            }

            if (s_transportStates.TryGetValue(unit.ID, out var existing))
            {
                Trace(unit, "Existing transport state re-used.");
                return existing;
            }

            var state = new TransportState
            {
                UnitId = unit.ID,
                Mapping = mapping
            };

            bool isCarrier = string.Equals(unit.Name, mapping.CarrierName, StringComparison.OrdinalIgnoreCase);
            if (isCarrier)
            {
                Trace(unit, "Unit currently in carrier form; cloning infantry snapshot from mapping.");
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
                Trace(unit, "Unit currently in infantry form; generating carrier snapshot.");
                state.InfantrySnapshot = unit.Clone(false);
                if (!UpdateCarrierSnapshotFromInfantry(state))
                {
                    s_transportStates.Remove(unit.ID);
                    Trace(unit, "Carrier snapshot generation failed.");
                    return null;
                }

                state.CurrentForm = MovementMode.Combat;
            }

            s_transportStates[unit.ID] = state;
            Trace(unit, $"Transport state established. Current form: {state.CurrentForm}.");
            return state;
        }

        static bool UpdateCarrierSnapshotFromInfantry(TransportState state)
        {
            if (state?.InfantrySnapshot == null)
            {
                Trace(state?.InfantrySnapshot, "Carrier snapshot update aborted - infantry snapshot missing.");
                return false;
            }

            var mapping = state.Mapping;
            if (mapping == null)
            {
                Trace(state.InfantrySnapshot, "Carrier snapshot update aborted - mapping missing.");
                return false;
            }

            if (!EnsureMappingPrototypes(mapping))
            {
                Trace(state.InfantrySnapshot, "Carrier snapshot update aborted - mapping prototypes missing.");
                return false;
            }

            // CRITICAL: If carrier prototype is null after EnsureMappingPrototypes, abort to prevent corruption
            if (mapping.CarrierPrototype == null)
            {
                Debug.LogError($"[SquadOfSteel] Carrier prototype null for '{mapping.CarrierName}' - aborting to prevent stat corruption.");
                Trace(state.InfantrySnapshot, "Carrier prototype null after EnsureMappingPrototypes - corruption risk detected.");
                return false;
            }

            if (state.CarrierSnapshot == null)
            {
                state.CarrierSnapshot = mapping.CarrierPrototype.Clone(false);
                Trace(state.InfantrySnapshot, "Carrier snapshot cloned from mapping prototype.");
            }
            else
            {
                // Carrier snapshot already exists - keep it and just update it from infantry
                Trace(state.InfantrySnapshot, "Carrier snapshot exists; will update from infantry state.");
            }

            if (state.CarrierSnapshot == null)
            {
                Trace(state.InfantrySnapshot, "Carrier snapshot remained null after clone - critical failure.");
                return false;
            }

            // Update carrier snapshot with current infantry's persistent state and resources
            CopyPersistentState(state.InfantrySnapshot, state.CarrierSnapshot);
            ApplyResourceRatios(state.InfantrySnapshot, state.CarrierSnapshot);
            Trace(state.InfantrySnapshot, "Carrier snapshot updated from infantry snapshot.");
            return true;
        }

        static bool UpdateInfantrySnapshotFromCarrier(TransportState state)
        {
            if (state?.CarrierSnapshot == null)
            {
                Trace(state?.CarrierSnapshot, "Infantry snapshot update aborted - carrier snapshot missing.");
                return false;
            }

            var mapping = state.Mapping;
            if (mapping == null)
            {
                Trace(state?.CarrierSnapshot, "Infantry snapshot update aborted - mapping missing.");
                return false;
            }

            if (!EnsureMappingPrototypes(mapping))
            {
                Trace(state?.CarrierSnapshot, "Infantry snapshot update aborted - mapping prototypes missing.");
                return false;
            }

            // CRITICAL: If infantry prototype is null after EnsureMappingPrototypes, abort to prevent corruption
            if (mapping.InfantryPrototype == null)
            {
                Debug.LogError($"[SquadOfSteel] Infantry prototype null for '{mapping.InfantryName}' - aborting to prevent stat corruption.");
                Trace(state.CarrierSnapshot, "Infantry prototype null after EnsureMappingPrototypes - corruption risk detected.");
                return false;
            }

            if (state.InfantrySnapshot == null)
            {
                state.InfantrySnapshot = mapping.InfantryPrototype.Clone(false);
                Trace(state.CarrierSnapshot, "Infantry snapshot cloned from mapping prototype.");
            }
            else
            {
                // Infantry snapshot already exists - keep it and just update it from carrier
                Trace(state.CarrierSnapshot, "Infantry snapshot exists; will update from carrier state.");
            }

            if (state.InfantrySnapshot == null)
            {
                Trace(state.CarrierSnapshot, "Infantry snapshot remained null after clone - critical failure.");
                return false;
            }

            // Update infantry snapshot with current carrier's persistent state and resources
            CopyPersistentState(state.CarrierSnapshot, state.InfantrySnapshot);
            ApplyResourceRatios(state.CarrierSnapshot, state.InfantrySnapshot);
            Trace(state.CarrierSnapshot, "Infantry snapshot updated from carrier snapshot.");
            return true;
        }

        static bool TryGetMappingForUnit(Unit unit, out CarrierMapping mapping)
        {
            mapping = null;
            if (unit == null)
                return false;

            EnsureCarrierMappingsLoaded();

            string name = unit.Name ?? string.Empty;
            string nationality = GetUnitNationality(unit);

            // Try infantry lookup first
            if (s_mappingsByInfantry.TryGetValue(name, out var baseMapping))
            {
                mapping = ResolveNationalityMapping(baseMapping, nationality, unit);
                if (mapping != null)
                    return true;
            }

            // Try carrier lookup as fallback
            if (s_mappingsByCarrier.TryGetValue(name, out baseMapping))
            {
                mapping = ResolveNationalityMapping(baseMapping, nationality, unit);
                if (mapping != null)
                    return true;
            }

            if (s_missingTransportMappings.Add(name))
            {
                Debug.Log($"[SquadOfSteel] No transport mapping configured for '{name}' (nationality: {nationality}). Falling back to legacy move-mode buffs.");
                Trace(unit, "No transport mapping configured (logged once).");
            }

            return false;
        }

        static string GetUnitNationality(Unit unit)
        {
            if (unit == null)
                return "generic";

            string ownerName = unit.OwnerName ?? string.Empty;
            string normalized = ownerName.ToLowerInvariant().Trim();

            // Map common nationality names to standardized keys
            if (normalized.Contains("us") || normalized.Contains("america") || normalized.Contains("usa") || normalized.Contains("united states"))
                return "us";
            if (normalized.Contains("uk") || normalized.Contains("brit") || normalized.Contains("commonwealth") || normalized.Contains("england"))
                return "uk";
            if (normalized.Contains("german") || normalized.Contains("reich") || normalized.Contains("nazi"))
                return "german";
            if (normalized.Contains("soviet") || normalized.Contains("ussr") || normalized.Contains("russia"))
                return "soviet";
            if (normalized.Contains("japan") || normalized.Contains("nippon"))
                return "japanese";
            if (normalized.Contains("ital"))
                return "italian";
            if (normalized.Contains("french") || normalized.Contains("france"))
                return "french";
            if (normalized.Contains("china") || normalized.Contains("chinese"))
                return "chinese";
            if (normalized.Contains("poland") || normalized.Contains("polish"))
                return "polish";

            // Fallback: return the original owner name normalized
            return string.IsNullOrWhiteSpace(normalized) ? "generic" : normalized;
        }

        static CarrierMapping ResolveNationalityMapping(CarrierMapping baseMapping, string nationality, Unit unit)
        {
            if (baseMapping == null)
                return null;

            // If this mapping has nationality-specific entries, try to resolve them
            if (baseMapping.NationalityMappings != null && baseMapping.NationalityMappings.Count > 0)
            {
                // Try exact nationality match first
                if (baseMapping.NationalityMappings.TryGetValue(nationality, out string carrierName))
                {
                    var resolved = new CarrierMapping
                    {
                        InfantryName = baseMapping.InfantryName,
                        CarrierName = carrierName,
                        NationalityMappings = baseMapping.NationalityMappings
                    };
                    Debug.Log($"[SquadOfSteel] Resolved nationality-specific mapping: {baseMapping.InfantryName} ({nationality}) -> {carrierName}");
                    Trace(unit, $"Resolved nationality-specific mapping: {nationality} -> {carrierName}");
                    return resolved;
                }

                // Try generic fallback
                if (baseMapping.NationalityMappings.TryGetValue("generic", out carrierName))
                {
                    var resolved = new CarrierMapping
                    {
                        InfantryName = baseMapping.InfantryName,
                        CarrierName = carrierName,
                        NationalityMappings = baseMapping.NationalityMappings
                    };
                    Debug.Log($"[SquadOfSteel] Resolved generic mapping fallback: {baseMapping.InfantryName} ({nationality}) -> {carrierName}");
                    Trace(unit, $"Resolved generic mapping fallback: {carrierName}");
                    return resolved;
                }

                // No match found in nationality mappings
                Debug.LogWarning($"[SquadOfSteel] No nationality mapping for '{baseMapping.InfantryName}' with nationality '{nationality}' and no generic fallback.");
                return null;
            }

            // Legacy format - no nationality mappings, use as-is
            return baseMapping;
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
                EnsureOfficialUnitsReady();

                // Find all transport mapping files
                var mappingFiles = FindAllTransportMappingFiles();
                if (mappingFiles.Count == 0)
                {
                    Debug.Log("[SquadOfSteel] No transport mapping files found; using legacy move mode behaviour.");
                    return;
                }

                Debug.Log($"[SquadOfSteel] Found {mappingFiles.Count} transport mapping file(s): {string.Join(", ", mappingFiles.Select(Path.GetFileName))}");

                int totalLoadedCount = 0;

                // Load and merge all mapping files in order
                // Files loaded later override earlier ones
                foreach (var filePath in mappingFiles)
                {
                    try
                    {
                        int loadedCount = LoadTransportMappingFile(filePath);
                        totalLoadedCount += loadedCount;
                        Debug.Log($"[SquadOfSteel] Loaded {loadedCount} mapping(s) from '{Path.GetFileName(filePath)}'");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SquadOfSteel] Failed to load '{Path.GetFileName(filePath)}': {ex.Message}");
                    }
                }

                Debug.Log($"[SquadOfSteel] Total: {s_mappingsByInfantry.Count} infantry types mapped with {totalLoadedCount} entries loaded.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to initialize transport mappings: {ex.Message}");
            }
        }

        static List<string> FindAllTransportMappingFiles()
        {
            var files = new List<string>();

            try
            {
                string assemblyDir = Path.GetDirectoryName(typeof(SquadMovementRuntime).Assembly.Location);
                if (string.IsNullOrEmpty(assemblyDir))
                    return files;

                var searchLocations = new List<string>
                {
                    assemblyDir,
                    Path.Combine(assemblyDir, "Assets")
                };

                var parent = Directory.GetParent(assemblyDir);
                if (parent != null)
                {
                    searchLocations.Add(parent.FullName);
                    searchLocations.Add(Path.Combine(parent.FullName, "Assets"));
                }

                foreach (var location in searchLocations)
                {
                    if (!Directory.Exists(location))
                        continue;

                    var foundFiles = Directory.GetFiles(location, TransportMappingFilePattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in foundFiles)
                    {
                        if (!files.Contains(file))
                            files.Add(file);
                    }
                }

                // Sort files alphabetically so loading order is predictable
                // This means transport-mappings.json loads first, then transport-mappings-custom.json, etc.
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Error searching for transport mapping files: {ex.Message}");
            }

            return files;
        }

        static int LoadTransportMappingFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return 0;

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return 0;

            int loadedCount = 0;

            // Try to parse as nested nationality-specific format first
            try
            {
                var nestedEntries = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (nestedEntries != null && nestedEntries.Count > 0)
                {
                    foreach (var pair in nestedEntries)
                    {
                        string infantryName = pair.Key;

                        // Skip comment fields
                        if (infantryName.StartsWith("_", StringComparison.Ordinal))
                            continue;

                        // Check if this is a nested object (nationality mappings) or a simple string (legacy format)
                        if (pair.Value is Newtonsoft.Json.Linq.JObject nestedObj)
                        {
                            var nationalityMappings = nestedObj.ToObject<Dictionary<string, string>>();
                            if (nationalityMappings != null && nationalityMappings.Count > 0)
                            {
                                RegisterNationalityMapping(infantryName, nationalityMappings);
                                loadedCount++;
                            }
                        }
                        else if (pair.Value is string carrierName)
                        {
                            // Legacy flat format
                            RegisterMapping(infantryName, carrierName);
                            loadedCount++;
                        }
                    }

                    return loadedCount;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to parse as nested format, trying legacy format: {ex.Message}");
            }

            // Fallback: try legacy flat format
            try
            {
                var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (entries != null && entries.Count > 0)
                {
                    foreach (var pair in entries)
                    {
                        RegisterMapping(pair.Key, pair.Value);
                        loadedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to parse transport mapping file as any known format: {ex.Message}");
            }

            return loadedCount;
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

            var mapping = new CarrierMapping
            {
                InfantryName = infantryName,
                CarrierName = carrierName
            };

            if (TryGetUnitDefinition(infantryName, out var infantryDefinition))
            {
                mapping.InfantryPrototype = infantryDefinition.Clone(false);
            }

            if (TryGetUnitDefinition(carrierName, out var carrierDefinition))
            {
                mapping.CarrierPrototype = carrierDefinition.Clone(false);
            }

            s_mappingsByInfantry[infantryName] = mapping;
            s_mappingsByCarrier[carrierName] = mapping;
        }

        static void RegisterNationalityMapping(string infantryName, Dictionary<string, string> nationalityMappings)
        {
            if (string.IsNullOrWhiteSpace(infantryName) || nationalityMappings == null || nationalityMappings.Count == 0)
                return;

            infantryName = infantryName.Trim();

            if (infantryName.StartsWith("#", StringComparison.Ordinal) ||
                infantryName.StartsWith("//", StringComparison.Ordinal) ||
                infantryName.StartsWith("_", StringComparison.Ordinal))
                return;

            var mapping = new CarrierMapping
            {
                InfantryName = infantryName,
                NationalityMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            // Normalize all nationality keys to lowercase for consistent lookups
            foreach (var pair in nationalityMappings)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                string nationalityKey = pair.Key.Trim().ToLowerInvariant();
                string carrierName = pair.Value.Trim();

                mapping.NationalityMappings[nationalityKey] = carrierName;

                // Register carrier in reverse lookup
                if (!s_mappingsByCarrier.ContainsKey(carrierName))
                {
                    s_mappingsByCarrier[carrierName] = mapping;
                }
            }

            // Try to cache infantry prototype if available
            if (TryGetUnitDefinition(infantryName, out var infantryDefinition))
            {
                mapping.InfantryPrototype = infantryDefinition.Clone(false);
            }

            s_mappingsByInfantry[infantryName] = mapping;
        }

        static bool TryGetUnitDefinition(string unitName, out Unit definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(unitName))
                return false;

            if (s_unitDefinitions.TryGetValue(unitName, out definition))
            {
                Debug.Log($"[SquadOfSteel] Unit definition '{unitName}' found in cache.");
                return true;
            }

            Debug.Log($"[SquadOfSteel] Searching for unit definition '{unitName}'...");
            var source = FindUnitDefinitionInternal(unitName);
            if (source == null)
            {
                if (s_missingUnitDefinitions.Add(unitName))
                {
                    Debug.LogError($"[SquadOfSteel] CRITICAL: Could not find unit definition '{unitName}' in official units OR map units!");
                    LogUnitLookupHints(unitName);
                }

                return false;
            }

            Debug.Log($"[SquadOfSteel] Unit definition '{unitName}' found and cached.");
            definition = source.Clone(false);
            s_unitDefinitions[unitName] = definition;
            return true;
        }

        static Unit FindUnitDefinitionInternal(string unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName))
                return null;

            try
            {
                var official = OfficialUnits.GetInstance();
                var units = official?.Units;

                if (units == null || units.Count == 0)
                {
                    EnsureOfficialUnitsReady();
                    official = OfficialUnits.GetInstance();
                    units = official?.Units;
                }

                if (units != null && units.Count > 0)
                {
                    if (!s_loggedOfficialUnitCount)
                    {
                        s_loggedOfficialUnitCount = true;
                        Debug.Log($"[SquadOfSteel] Official unit catalog populated ({units.Count} entries).");
                    }

                    var match = units.FirstOrDefault(u => string.Equals(u?.Name, unitName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to query official units for '{unitName}': {ex.Message}");
            }

            try
            {
                var mapUnits = MapGO.instance?.listOfAllUnits;
                if (mapUnits != null)
                {
                    var match = mapUnits.FirstOrDefault(u => u != null && string.Equals(u.Name, unitName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to inspect map units for '{unitName}': {ex.Message}");
            }

            return null;
        }

        static void EnsureOfficialUnitsReady()
        {
            try
            {
                var official = OfficialUnits.GetInstance();
                var units = official?.Units;

                if (units != null && units.Count > 0)
                {
                    if (!s_loggedOfficialUnitCount)
                    {
                        s_loggedOfficialUnitCount = true;
                        Debug.Log($"[SquadOfSteel] Official unit catalog populated ({units.Count} entries).");
                    }
                    return;
                }

                if (!s_officialUnitsLoadAttempted)
                {
                    s_officialUnitsLoadAttempted = true;
                    try
                    {
                        OfficialUnits.Load();
                    }
                    catch (Exception ex)
                    {
                        if (!s_loggedOfficialUnitsLoadFailure)
                        {
                            s_loggedOfficialUnitsLoadFailure = true;
                            Debug.LogWarning($"[SquadOfSteel] Unable to load official unit catalog: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!s_loggedOfficialUnitsLoadFailure)
                {
                    s_loggedOfficialUnitsLoadFailure = true;
                    Debug.LogWarning($"[SquadOfSteel] Encountered error while ensuring official units are ready: {ex.Message}");
                }
            }
        }

        static void LogUnitLookupHints(string unitName)
        {
            try
            {
                var normalizedTarget = NormalizeUnitName(unitName);
                if (string.IsNullOrEmpty(normalizedTarget))
                    return;

                var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var official = OfficialUnits.GetInstance();
                var units = official?.Units;
                if (units != null)
                {
                    foreach (var unit in units)
                    {
                        if (unit?.Name == null)
                            continue;

                        if (IsSimilarUnitName(normalizedTarget, unit.Name))
                            suggestions.Add(unit.Name);
                    }
                }

                var mapUnits = MapGO.instance?.listOfAllUnits;
                if (mapUnits != null)
                {
                    foreach (var unit in mapUnits)
                    {
                        if (unit?.Name == null)
                            continue;

                        if (IsSimilarUnitName(normalizedTarget, unit.Name))
                            suggestions.Add(unit.Name);
                    }
                }

                if (suggestions.Count > 0)
                {
                    Debug.Log($"[SquadOfSteel] Unit lookup hints for '{unitName}': {string.Join(", ", suggestions.Take(6))}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to produce unit lookup hints for '{unitName}': {ex.Message}");
            }
        }

        static bool IsSimilarUnitName(string normalizedTarget, string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;

            string normalizedCandidate = NormalizeUnitName(candidate);
            if (string.IsNullOrEmpty(normalizedCandidate))
                return false;

            if (normalizedCandidate.Contains(normalizedTarget))
                return true;

            if (normalizedTarget.Contains(normalizedCandidate))
                return true;

            return normalizedCandidate.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                   normalizedTarget.StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeUnitName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
            return new string(chars);
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
