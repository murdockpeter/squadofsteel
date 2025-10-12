// =============================================================================
// Squad Of Steel UI placeholder
// Provides a simple toggleable panel hook that can be expanded later.
// =============================================================================

using System.Text;
using SquadOfSteelMod.Combat;
using UnityEngine;

namespace SquadOfSteelMod
{
    public static class SquadOfSteelUI
    {
        static bool _initialized;
        static bool _panelVisible;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Debug.Log("[SquadOfSteel] UI system initialized (placeholder)");
        }

        public static void TogglePanel()
        {
            if (!_initialized)
                Initialize();

            _panelVisible = !_panelVisible;

            if (_panelVisible)
            {
                ShowPanel();
            }
            else
            {
                HidePanel();
            }
        }

        public static void Shutdown()
        {
            if (!_initialized)
                return;

            HidePanel();
            _initialized = false;
            Debug.Log("[SquadOfSteel] UI system shut down");
        }

        static void ShowPanel()
        {
            var selectedUnitGO = MapGO.selectedUnitGO;
            if (selectedUnitGO == null || selectedUnitGO.unit == null)
            {
                Debug.Log("[SquadOfSteel] Squad panel: select a unit to inspect combat metrics.");
                return;
            }

            var sb = new StringBuilder();
            int attackerSuppression = SquadOfSteelSuppression.Get(selectedUnitGO.unit);
            var attackerMode = SquadMovementRuntime.GetMode(selectedUnitGO.unit);
            sb.AppendLine($"[SquadOfSteel] {selectedUnitGO.unit.Name} briefing");
            sb.AppendLine($" - Suppression: {attackerSuppression}");
            sb.AppendLine($" - Movement mode: {attackerMode} (toggle {SquadOfSteelKeybindHandler.moveToggleKey})");
            if (attackerMode == SquadMovementRuntime.MovementMode.Move)
            {
                sb.AppendLine("   - Move mode: +3 AP, incoming fire +15% hit chance / +20% dmg, own fire -12% accuracy.");
            }
            sb.AppendLine($" - Combat debug: {(SquadCombatRuntime.DebugEnabled ? "enabled" : "disabled")} (toggle {SquadOfSteelKeybindHandler.debugToggleKey})");

            var targetedUnitGO = selectedUnitGO.targetedUnitGO;
            if (targetedUnitGO != null && targetedUnitGO.unit != null)
            {
                Tile attackerTile = selectedUnitGO.tileGO?.tile;
                Tile targetTile = targetedUnitGO.tileGO?.tile;
                bool hasLoS = LineOfSightService.HasLineOfSight(selectedUnitGO, targetTile);
                int distance = HexGridHelper.GetDistance(attackerTile, targetTile);
                int targetSuppression = SquadOfSteelSuppression.Get(targetedUnitGO.unit);
                var targetMode = SquadMovementRuntime.GetMode(targetedUnitGO.unit);
                float hitChance = SquadOfSteelCombatMath.ComputeHitChance(
                    selectedUnitGO.unit,
                    selectedUnitGO,
                    targetedUnitGO,
                    distance,
                    hasLoS,
                    attackerSuppression,
                    targetSuppression,
                    isRetaliation: false,
                    isSupport: false);

                int damageOnHit = SquadOfSteelCombatMath.ComputeDamageOnHit(
                    selectedUnitGO.unit.FinalDamage > 0 ? selectedUnitGO.unit.FinalDamage : selectedUnitGO.unit.BaseSoftDamage,
                    distance,
                    targetSuppression,
                    targetedUnitGO.unit);

                sb.AppendLine($" - Target: {targetedUnitGO.unit.Name}");
                sb.AppendLine($"   - Suppression: {targetSuppression}");
                sb.AppendLine($"   - Movement mode: {targetMode}");
                sb.AppendLine($"   - Distance: {distance} hex");
                sb.AppendLine($"   - Line of sight: {(hasLoS ? "clear" : "blocked")}");
                sb.AppendLine($"   - Hit chance: {Mathf.RoundToInt(hitChance * 100f)}%");
                sb.AppendLine($"   - Damage on hit (est.): {damageOnHit}");
            }
            else
            {
                sb.AppendLine(" - No target selected.");
            }

            Debug.Log(sb.ToString());
        }

        static void HidePanel()
        {
            if (_panelVisible)
            {
                Debug.Log("[SquadOfSteel] Hiding Squad management panel");
            }

            _panelVisible = false;
        }
    }
}
