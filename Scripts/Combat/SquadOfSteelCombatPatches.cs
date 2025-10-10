// =============================================================================
// Harmony patches wiring Squad Of Steel combat logic into the base game.
// =============================================================================

using System;
using HarmonyLib;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    [HarmonyPatch]
    static class SquadOfSteelCombatPatches
    {
        [HarmonyPatch(typeof(Unit), nameof(Unit.GetPotentialDamage))]
        [HarmonyPostfix]
        static void InjectPotentialDamage(Unit __instance, UnitGO p_targetUnitGO, bool p_isRetaliation, bool p_isSupportiveFire, ref int __result)
        {
            if (__instance?.unitGO == null || p_targetUnitGO == null)
                return;

            var attackerGO = __instance.unitGO;
            var targetGO = p_targetUnitGO;

            Tile attackerTile = attackerGO.tileGO?.tile;
            Tile targetTile = targetGO.tileGO?.tile;
            if (attackerTile == null || targetTile == null)
                return;

            int baseDamage = __instance.FinalDamage;
            bool hasLoS = LineOfSightService.HasLineOfSight(attackerGO, targetTile, out _);
            int distance = HexGridHelper.GetDistance(attackerTile, targetTile);
            int attackerSupp = SquadOfSteelSuppression.Get(__instance);
            int targetSupp = SquadOfSteelSuppression.Get(targetGO.unit);

            float hitChance = SquadOfSteelCombatMath.ComputeHitChance(__instance, attackerGO, targetGO, distance, hasLoS, attackerSupp, targetSupp, p_isRetaliation, p_isSupportiveFire);
            int damageOnHit = SquadOfSteelCombatMath.ComputeDamageOnHit(baseDamage, distance, targetSupp);

            if (!hasLoS)
            {
                hitChance = 0f;
                damageOnHit = 0;
            }

            int expectedDamage = Mathf.RoundToInt(damageOnHit * hitChance);

            __instance.FinalDamage = expectedDamage;
            __result = expectedDamage;

            var preview = new CombatPreview(
                baseDamage,
                damageOnHit,
                expectedDamage,
                hitChance,
                hasLoS,
                distance,
                attackerSupp,
                targetSupp,
                p_isRetaliation,
                p_isSupportiveFire);

            SquadCombatRuntime.StorePreview(__instance, targetGO.unit, preview);
        }

        [HarmonyPatch(typeof(UnitGO), nameof(UnitGO.AttackUnit))]
        [HarmonyPrefix]
        static bool GuardDirectFire(UnitGO __instance, UnitGO p_unitGO)
        {
            if (!SquadCombatRuntime.ValidateDirectFire(__instance, p_unitGO, out string reason))
            {
                SquadCombatRuntime.NotifyBlocked(__instance, reason);
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(UnitGO), "Attack")]
        [HarmonyPrefix]
        static void ResolveDamage(UnitGO __instance, UnitGO p_targetUnitGO, ref CombatOutcome __state)
        {
            if (__instance?.unit == null || p_targetUnitGO?.unit == null)
                return;

            if (!SquadCombatRuntime.TryConsumePreview(__instance.unit, p_targetUnitGO.unit, out var preview))
            {
                Tile attackerTile = __instance.tileGO?.tile;
                Tile targetTile = p_targetUnitGO.tileGO?.tile;
                bool hasLoS = LineOfSightService.HasLineOfSight(__instance, targetTile);
                int distance = HexGridHelper.GetDistance(attackerTile, targetTile);
                int attackerSupp = SquadOfSteelSuppression.Get(__instance.unit);
                int targetSupp = SquadOfSteelSuppression.Get(p_targetUnitGO.unit);
                float fallbackHit = SquadOfSteelCombatMath.ComputeHitChance(__instance.unit, __instance, p_targetUnitGO, distance, hasLoS, attackerSupp, targetSupp, isRetaliation: false, isSupport: false);
                int fallbackDamage = SquadOfSteelCombatMath.ComputeDamageOnHit(__instance.unit.FinalDamage, distance, targetSupp);
                int fallbackExpected = Mathf.RoundToInt(fallbackDamage * fallbackHit);

                preview = new CombatPreview(
                    __instance.unit.FinalDamage,
                    fallbackDamage,
                    fallbackExpected,
                    fallbackHit,
                    hasLoS,
                    distance,
                    attackerSupp,
                    targetSupp,
                    isRetaliation: false,
                    isSupport: false);
            }

            var outcome = SquadCombatRuntime.ResolveOutcome(preview);
            __instance.unit.FinalDamage = outcome.Damage;
            __state = outcome;
            SquadCombatRuntime.RecordOutcome(__instance.unit, p_targetUnitGO.unit, outcome);
        }

        [HarmonyPatch(typeof(UnitGO), "Attack")]
        [HarmonyPostfix]
        static void ApplySuppression(UnitGO __instance, UnitGO p_targetUnitGO, CombatOutcome __state)
        {
            if (__instance?.unit == null || p_targetUnitGO?.unit == null)
                return;

            if (__state == null)
                return;

            if (__state.Hit && __state.Damage > 0)
            {
                SquadOfSteelSuppression.Add(p_targetUnitGO.unit, 30);
                SquadOfSteelSuppression.Reduce(__instance.unit, 8);
            }
            else if (__state.HasLineOfSight && __state.DamageOnHit > 0)
            {
                SquadOfSteelSuppression.Add(p_targetUnitGO.unit, 12);
            }

            __state.AttackerSuppressionAfter = SquadOfSteelSuppression.Get(__instance.unit);
            __state.TargetSuppressionAfter = SquadOfSteelSuppression.Get(p_targetUnitGO.unit);

            CombatDebugReporter.Report(__instance, p_targetUnitGO, __state);

            SquadCombatRuntime.ClearOutcome(__instance.unit, p_targetUnitGO.unit);
        }

        [HarmonyPatch(typeof(UnitGO), "Retaliate")]
        [HarmonyPrefix]
        static bool GuardRetaliation(UnitGO __instance, UnitGO p_attackerUnitGO, float p_distance)
        {
            return SquadCombatRuntime.ValidateDirectFire(__instance, p_attackerUnitGO, out _);
        }

        [HarmonyPatch(typeof(UnitGO), nameof(UnitGO.DestroyUnit))]
        [HarmonyPostfix]
        static void CleanupOnDestroy(UnitGO __instance)
        {
            if (__instance?.unit == null)
                return;

            SquadOfSteelSuppression.Clear(__instance.unit);
            SquadCombatRuntime.ClearForUnit(__instance.unit);
        }

        [HarmonyPatch(typeof(UIManager), "Start")]
        [HarmonyPostfix]
        static void InitializeOverlay()
        {
            CombatDebugOverlay.Initialize();
        }

        [HarmonyPatch(typeof(TurnManager), nameof(TurnManager.NextTurn), typeof(bool))]
        [HarmonyPostfix]
        static void HandleTurnAdvance()
        {
            SquadOfSteelSuppression.DecayAll();
            SquadOfSteelState.Save();
        }
    }
}
