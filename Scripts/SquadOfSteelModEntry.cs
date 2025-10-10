// =============================================================================
// Squad Of Steel Mod Entry Point
// Sets up Harmony patching and bootstraps runtime helpers.
// =============================================================================

using HarmonyLib;
using SquadOfSteelMod.Combat;
using UnityEngine;

namespace SquadOfSteelMod
{
    public class SquadOfSteelModEntry : GameModification
    {
        Harmony _harmony;

        public SquadOfSteelModEntry(Mod mod) : base(mod)
        {
            Debug.Log("[SquadOfSteel] Registering mod...");
            SquadOfSteelState.Initialize(mod);
        }

        public override void OnModInitialization(Mod modInstance)
        {
            Debug.Log("[SquadOfSteel] Initializing mod...");

            mod = modInstance;

            ApplyPatches();
        }

        public override void OnModUnloaded()
        {
            Debug.Log("[SquadOfSteel] Unloading mod...");

            SquadOfSteelUI.Shutdown();
            SquadCombatRuntime.Shutdown();
            SquadOfSteelState.Save();

            if (_harmony != null)
            {
                _harmony.UnpatchAll(_harmony.Id);
                _harmony = null;
            }
        }

        void ApplyPatches()
        {
            Debug.Log("[SquadOfSteel] Applying Harmony patches...");

            _harmony = new Harmony("com.hexofsteel.squadofsteel");
            _harmony.PatchAll();

            Debug.Log("[SquadOfSteel] Harmony patches applied");

            SquadCombatRuntime.Initialize();
            SquadOfSteelUI.Initialize();

            var keybindHost = new GameObject("SquadOfSteelKeybindHost");
            keybindHost.AddComponent<SquadOfSteelKeybindHandler>();
            GameObject.DontDestroyOnLoad(keybindHost);

            Debug.Log("[SquadOfSteel] Mod initialized; press the toggle key to open the Squad Panel (defaults to 'K').");
        }
    }
}
