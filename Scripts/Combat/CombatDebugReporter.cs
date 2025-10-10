// =============================================================================
// Optional combat debug reporter that surfaces hit calculations in-game.
// =============================================================================

using System.Text;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    static class CombatDebugReporter
    {
        public static void Report(UnitGO attacker, UnitGO defender, CombatOutcome outcome)
        {
            if (!SquadCombatRuntime.DebugEnabled)
                return;

            if (attacker?.unit == null || defender?.unit == null || outcome == null)
                return;

            bool attackerIsPlayer = attacker.unit.OwnerPlayer != null && !attacker.unit.OwnerPlayer.IsComputer;
            bool defenderIsPlayer = defender.unit.OwnerPlayer != null && !defender.unit.OwnerPlayer.IsComputer;

            if (!attackerIsPlayer && !defenderIsPlayer)
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"Combat debug: {attacker.unit.Name} -> {defender.unit.Name}");
            sb.AppendLine($" - Hit chance: {Mathf.RoundToInt(outcome.HitChance * 100f)}% (roll: {outcome.Roll:0.00})");
            sb.AppendLine($" - Outcome: {(outcome.Hit ? "HIT" : "MISS")}, damage: {outcome.Damage}/{outcome.DamageOnHit} (base {outcome.BaseDamage}, expected {outcome.ExpectedDamage})");
            sb.AppendLine($" - Distance: {outcome.Distance} hex, LoS: {(outcome.HasLineOfSight ? "clear" : "blocked")}, retaliation: {(outcome.IsRetaliation ? "yes" : "no")}, support: {(outcome.IsSupport ? "yes" : "no")}");
            sb.AppendLine($" - Suppression attacker {outcome.AttackerSuppressionBefore}->{outcome.AttackerSuppressionAfter}, target {outcome.TargetSuppressionBefore}->{outcome.TargetSuppressionAfter}");

            string message = sb.ToString();
            Debug.Log($"[SquadOfSteel] {message}");
            CombatDebugOverlay.AddEntry(message);

            try
            {
                var anchorTile = attacker.tileGO?.tile ?? defender.tileGO?.tile;
                float posX = anchorTile?.PosX ?? 0;
                float posY = anchorTile?.PosY ?? 0;
                string ownerName = attacker.unit.OwnerPlayer?.Name ?? "Unknown";
                new Notification(NotificationTypes.DEFAULT, ownerName, posX, posY, message, p_isImportant: false);
            }
            catch
            {
                // Notifications can fail before map/init; ignore.
            }
        }
    }
}
