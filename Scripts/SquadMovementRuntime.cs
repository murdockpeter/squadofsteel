// =============================================================================
// Handles squad-level movement mode (Combat vs Move) and transport state.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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

        static readonly Dictionary<int, MovementMode> s_modes = new Dictionary<int, MovementMode>();
        static readonly Dictionary<int, int> s_apBuffs = new Dictionary<int, int>();
        static readonly Dictionary<int, MovementBuffRecord> s_movementBuffs = new Dictionary<int, MovementBuffRecord>();
        static readonly Dictionary<int, int> s_lastAppliedTurn = new Dictionary<int, int>();

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

        public static void InitializeFromSave(Dictionary<int, MovementMode> snapshot)
        {
            s_modes.Clear();
            s_apBuffs.Clear();
            s_movementBuffs.Clear();
            s_lastAppliedTurn.Clear();

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

            s_modes[unit.ID] = desired;
            if (desired == MovementMode.Combat)
                s_modes.Remove(unit.ID);

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
        }

        static bool EnterMoveMode(UnitGO unitGO)
        {
            if (unitGO?.unit == null)
                return false;

            var unit = unitGO.unit;

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
            RemoveActionPointBuff(unit);
            RemoveMovementBuff(unit);
            ShowSuppressionIndicator(unitGO);
            ShowMoveIndicator(unitGO, false);
            RefreshMovementOverlay(unitGO);
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

        static bool EligibleForMoveMode(Unit unit)
        {
            if (unit == null)
                return false;

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
