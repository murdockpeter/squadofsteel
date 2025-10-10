// =============================================================================
// Squad Of Steel persistent state management.
// Handles serialization of suppression values and future combat settings.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SquadOfSteelMod.Combat;
using UnityEngine;

namespace SquadOfSteelMod
{
    public static class SquadOfSteelState
    {
        const string StorageKey = "SquadOfSteel.State";

        static readonly JsonSerializerSettings s_jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        static PersistedState _state = new PersistedState();
        static bool _loaded;
        static bool _dirty;
        static Mod _mod;

        sealed class PersistedState
        {
            public Dictionary<int, int> Suppression { get; set; } = new Dictionary<int, int>();
            public bool DebugEnabled { get; set; }
        }

        public static bool IsLoaded => _loaded;

        public static void Initialize(Mod mod)
        {
            _mod = mod;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            LoadFromBag();
            SquadOfSteelSuppression.InitializeFromSave(_state.Suppression);
            SquadCombatRuntime.SetDebugEnabled(_state.DebugEnabled);

            Debug.Log($"[SquadOfSteel] State storage ready (suppression entries: {_state.Suppression.Count})");
        }

        public static void MarkDirty()
        {
            _dirty = true;
        }

        public static void Save()
        {
            if (!_loaded || !_dirty)
                return;

            if (_mod == null)
            {
                Debug.LogWarning("[SquadOfSteel] Cannot save state - mod reference missing.");
                return;
            }

            if (GameData.Instance == null || GameData.Instance.map == null)
            {
                Debug.Log("[SquadOfSteel] Deferring save - game data not ready.");
                return;
            }

            try
            {
                _state.Suppression = SquadOfSteelSuppression.ExportState();
                _state.DebugEnabled = SquadCombatRuntime.DebugEnabled;
                string json = JsonConvert.SerializeObject(_state, s_jsonSettings);
                GameData.Instance.ModDataBag.TrySet(StorageKey, json, preferKnownOverUnknown: true);
                _dirty = false;
                Debug.Log($"[SquadOfSteel] Saved state ({_state.Suppression.Count} suppression entries).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to save state: {ex.Message}");
            }
        }

        static void LoadFromBag()
        {
            if (GameData.Instance == null || GameData.Instance.ModDataBag == null)
            {
                _state = new PersistedState();
                return;
            }

            if (GameData.Instance.ModDataBag.TryGet(StorageKey, out string json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    _state = JsonConvert.DeserializeObject<PersistedState>(json) ?? new PersistedState();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SquadOfSteel] Failed to parse saved state: {ex.Message}");
                    _state = new PersistedState();
                }
            }
            else
            {
                _state = new PersistedState();
            }
        }

        public static void SetCombatDebug(bool enabled)
        {
            if (!_loaded)
                EnsureLoaded();

            if (_state.DebugEnabled == enabled)
                return;

            _state.DebugEnabled = enabled;
            SquadCombatRuntime.SetDebugEnabled(enabled);
            MarkDirty();

            Debug.Log($"[SquadOfSteel] Combat debug {(enabled ? "enabled" : "disabled")}.");
        }
    }
}
