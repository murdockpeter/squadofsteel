// =============================================================================
// Suppression tracking for Squad Of Steel combat layer.
// Stores values per unit and exposes helpers for decay and updates.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    public static class SquadOfSteelSuppression
    {
        const int MaxSuppression = 100;
        const int MinSuppression = 0;
        const int PassiveRecoveryPerTurn = 15;

        static readonly Dictionary<int, SuppressionRecord> s_values = new Dictionary<int, SuppressionRecord>();

        sealed class SuppressionRecord
        {
            public int Value;
        }

        static int CurrentTurn => GameData.Instance?.map?.turnCount ?? 0;

        public static void InitializeFromSave(Dictionary<int, int> data)
        {
            s_values.Clear();

            if (data == null)
                return;

            foreach (var pair in data)
            {
                if (pair.Key == 0)
                    continue;

                s_values[pair.Key] = new SuppressionRecord
                {
                    Value = Mathf.Clamp(pair.Value, MinSuppression, MaxSuppression)
                };
            }
        }

        public static Dictionary<int, int> ExportState()
        {
            var snapshot = new Dictionary<int, int>(s_values.Count);
            foreach (var pair in s_values)
            {
                snapshot[pair.Key] = pair.Value.Value;
            }
            return snapshot;
        }

        public static int Get(Unit unit)
        {
            if (unit == null)
                return 0;

            return s_values.TryGetValue(unit.ID, out var record) ? record.Value : 0;
        }

        public static void Add(Unit unit, int amount)
        {
            if (unit == null || amount <= 0)
                return;

            var record = GetOrCreate(unit);
            int newValue = Mathf.Clamp(record.Value + amount, MinSuppression, MaxSuppression);
            if (newValue == record.Value)
                return;

            record.Value = newValue;
            SquadOfSteelState.MarkDirty();
            DebugSuppression($"Added {amount} suppression to {unit.Name} (#{unit.ID}) -> {record.Value}");
        }

        public static void Reduce(Unit unit, int amount)
        {
            if (unit == null || amount <= 0)
                return;

            if (!s_values.TryGetValue(unit.ID, out var record))
                return;

            int newValue = Mathf.Clamp(record.Value - amount, MinSuppression, MaxSuppression);
            if (newValue == record.Value)
                return;

            record.Value = newValue;
            if (record.Value == 0)
            {
                s_values.Remove(unit.ID);
            }
            SquadOfSteelState.MarkDirty();
            DebugSuppression($"Reduced suppression on {unit.Name} (#{unit.ID}) by {amount} -> {record.Value}");
        }

        public static void DecayAll(int amount = PassiveRecoveryPerTurn)
        {
            if (s_values.Count == 0 || amount <= 0)
                return;

            var keys = new List<int>(s_values.Keys);
            bool anyChange = false;
            foreach (int key in keys)
            {
                var record = s_values[key];
                int newValue = Mathf.Clamp(record.Value - amount, MinSuppression, MaxSuppression);
                if (newValue != record.Value)
                {
                    record.Value = newValue;
                    anyChange = true;
                }

                if (record.Value == 0)
                {
                    s_values.Remove(key);
                }
            }

            if (anyChange)
            {
                SquadOfSteelState.MarkDirty();
                DebugSuppression($"Passive suppression decay applied ({amount}) at turn {CurrentTurn}. Remaining entries: {s_values.Count}");
            }
        }

        public static void Clear(Unit unit)
        {
            if (unit == null)
                return;

            if (s_values.Remove(unit.ID))
            {
                SquadOfSteelState.MarkDirty();
                DebugSuppression($"Cleared suppression for {unit.Name} (#{unit.ID})");
            }
        }

        static SuppressionRecord GetOrCreate(Unit unit)
        {
            if (!s_values.TryGetValue(unit.ID, out var record))
            {
                record = new SuppressionRecord();
                s_values[unit.ID] = record;
            }
            return record;
        }

        [System.Diagnostics.Conditional("SQUADOFSTEEL_DEBUG_SUPPRESSION")]
        static void DebugSuppression(string message)
        {
            Debug.Log($"[SquadOfSteel] {message}");
        }
    }
}
