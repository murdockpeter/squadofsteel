// =============================================================================
// Squad Of Steel Keybind Handler
// Watches for the toggle key and opens the Squad management panel.
// =============================================================================

using System;
using System.Reflection;
using SquadOfSteelMod.Combat;
using UnityEngine;

namespace SquadOfSteelMod
{
    public class SquadOfSteelKeybindHandler : MonoBehaviour
    {
        public static KeyCode toggleKey = KeyCode.K;
        public static KeyCode debugToggleKey = KeyCode.F9;
        public static KeyCode moveToggleKey = KeyCode.V;

        static Type _inputType;
        static MethodInfo _getKeyDown;
        static bool _initialized;
        static bool _loggedUpdate;

        void Start()
        {
            Debug.Log("[SquadOfSteel] Keybind handler active");
        }

        void Update()
        {
            if (!_loggedUpdate)
            {
                Debug.Log("[SquadOfSteel] Update() receiving frames");
                _loggedUpdate = true;
            }

            if (GameData.Instance == null || TurnManager.currPlayer == null)
                return;

            if (TurnManager.currPlayer.IsComputer)
                return;

            if (GetKeyDown(toggleKey))
            {
                Debug.Log($"[SquadOfSteel] Toggle key '{toggleKey}' pressed");
                SquadOfSteelUI.TogglePanel();
            }

            if (GetKeyDown(moveToggleKey))
            {
                var selectedUnit = MapGO.selectedUnitGO;
                if (selectedUnit != null)
                {
                    if (!SquadMovementRuntime.TryToggleMoveMode(selectedUnit))
                    {
                        Debug.Log("[SquadOfSteel] Move mode toggle failed (see prior log).");
                    }
                }
            }

            if (GetKeyDown(debugToggleKey))
            {
                bool newValue = !SquadCombatRuntime.DebugEnabled;
                SquadOfSteelState.SetCombatDebug(newValue);
            }
        }

        static bool GetKeyDown(KeyCode key)
        {
            EnsureInputReflection();

            if (_getKeyDown == null)
                return false;

            try
            {
                return (bool)_getKeyDown.Invoke(null, new object[] { key });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SquadOfSteel] Failed to read key state: {ex.Message}");
                return false;
            }
        }

        static void EnsureInputReflection()
        {
            if (_initialized)
                return;

            _initialized = true;

            Debug.Log("[SquadOfSteel] Initializing Unity input reflection");

            string[] candidates =
            {
                "UnityEngine.Input, UnityEngine.InputLegacyModule",
                "UnityEngine.Input, UnityEngine",
                "UnityEngine.Input, UnityEngine.CoreModule"
            };

            foreach (var candidate in candidates)
            {
                _inputType = Type.GetType(candidate);
                if (_inputType != null)
                {
                    Debug.Log($"[SquadOfSteel] Found Input type: {_inputType.AssemblyQualifiedName}");
                    _getKeyDown = _inputType.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                    break;
                }
            }

            if (_getKeyDown == null)
            {
                Debug.LogWarning("[SquadOfSteel] Could not resolve Input.GetKeyDown; keybind disabled.");
            }
            else
            {
                Debug.Log("[SquadOfSteel] Input reflection ready");
            }
        }
    }
}
