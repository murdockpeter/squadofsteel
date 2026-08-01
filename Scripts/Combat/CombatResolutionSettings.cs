// =============================================================================
// Loads the editable Core Hex of Steel and Squad Of Steel combat-resolution
// tables. Defaults deliberately reproduce the current in-game behaviour.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    public static class CombatResolutionSettings
    {
        const string CoreFileName = "hex-of-steel-core-crt.json";
        const string SquadFileName = "squad-of-steel-crt.json";

        static bool s_loaded;
        static CoreCombatResolutionTable s_core;
        static SquadCombatResolutionTable s_squad;

        public static CoreCombatResolutionTable Core
        {
            get
            {
                EnsureLoaded();
                return s_core;
            }
        }

        public static SquadCombatResolutionTable Squad
        {
            get
            {
                EnsureLoaded();
                return s_squad;
            }
        }

        public static int ApplyCoreFactors(Unit unit, int vanillaDamage)
        {
            if (unit == null)
                return vanillaDamage;

            var table = Core;
            if (!table.Enabled)
                return vanillaDamage;

            double adjusted = vanillaDamage;
            adjusted += WeightedDelta(unit.baseDamageBreakdown, table.FactorWeight("baseDamage"));
            adjusted += WeightedDelta(unit.HPbreakdown, table.FactorWeight("health"));
            adjusted += WeightedDelta(unit.veterancyBreakdown, table.FactorWeight("veterancy"));
            adjusted += WeightedDelta(unit.moraleBreakdown, table.FactorWeight("morale"));
            adjusted += WeightedDelta(unit.heroBreakdown, table.FactorWeight("hero"));
            adjusted += WeightedDelta(unit.generalBreakdown, table.FactorWeight("commander"));
            adjusted += WeightedDelta(unit.entrenchmentBreakdown, table.FactorWeight("entrenchment"));
            adjusted += WeightedDelta(unit.reconBreakdown, table.FactorWeight("recon"));
            adjusted += WeightedDelta(unit.combinedArmsBreakdown, table.FactorWeight("combinedArms"));
            adjusted += WeightedDelta(unit.encirclementBreakdown, table.FactorWeight("encirclement"));
            adjusted += WeightedDelta(unit.flamethrowerBreakdown, table.FactorWeight("flamethrower"));
            adjusted += WeightedDelta(unit.riverBreakdown, table.FactorWeight("river"));
            adjusted += WeightedDelta(unit.terrainBreakdown, table.FactorWeight("terrain"));
            adjusted += WeightedDelta(unit.terrainArmouredBreakdown, table.FactorWeight("armouredTerrain"));
            adjusted += WeightedDelta(unit.hillBreakdown, table.FactorWeight("hill"));
            adjusted += WeightedDelta(unit.landingBreakdown, table.FactorWeight("landing"));
            adjusted += WeightedDelta(unit.mountaineerBreakdown, table.FactorWeight("mountaineer"));
            adjusted += WeightedDelta(unit.subVSlandingCraftBreakdown, table.FactorWeight("submarineVsLandingCraft"));
            adjusted += WeightedDelta(unit.shipVSgroundUnitsBreakdown, table.FactorWeight("shipsVsGround"));
            adjusted += WeightedDelta(unit.heavyBomberVSshipsWithoutTorpedoBreakdown, table.FactorWeight("heavyBomberVsShips"));
            adjusted += WeightedDelta(unit.torpedoBreakdown, table.FactorWeight("torpedo"));
            adjusted += WeightedDelta(unit.destroyerVSsubmergedSubBreakdown, table.FactorWeight("destroyerVsSubmarine"));
            adjusted += WeightedDelta(unit.armourBreakdown, table.FactorWeight("armour"));
            adjusted += WeightedDelta(unit.policyBreakdown, table.FactorWeight("policies"));
            adjusted += WeightedDelta(unit.politicalUnitsBreakdown, table.FactorWeight("politicalUnits"));
            adjusted += WeightedDelta(unit.biomeBreakdown, table.FactorWeight("biome"));
            adjusted += WeightedDelta(unit.repeatedAttacksBreakdown, table.FactorWeight("repeatedAttacks"));
            adjusted += WeightedDelta(unit.weatherBreakdown, table.FactorWeight("weather"));
            adjusted += WeightedDelta(unit.othersBreakdown, table.FactorWeight("otherVanillaFactors"));
            adjusted *= Math.Max(0d, table.FinalDamageMultiplier);

            return Mathf.Clamp((int)Math.Round(adjusted, MidpointRounding.ToEven), 0, int.MaxValue);
        }

        static double WeightedDelta(int contribution, float weight) => contribution * (weight - 1d);

        static void EnsureLoaded()
        {
            if (s_loaded)
                return;

            s_loaded = true;
            s_core = new CoreCombatResolutionTable();
            s_squad = new SquadCombatResolutionTable();

            TryLoad<CoreCombatResolutionTable>(CoreFileName, value => s_core = value);
            TryLoad<SquadCombatResolutionTable>(SquadFileName, value => s_squad = value);
            s_core.Normalize();
            s_squad.Normalize();
        }

        static void TryLoad<T>(string fileName, Action<T> assign) where T : class
        {
            string path = FindFile(fileName);
            if (path == null)
            {
                Debug.LogWarning($"[SquadOfSteel][CRT] '{fileName}' was not found; built-in as-is values will be used.");
                return;
            }

            try
            {
                var value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                if (value != null)
                {
                    assign(value);
                    Debug.Log($"[SquadOfSteel][CRT] Loaded '{path}'.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel][CRT] Failed to load '{path}': {ex.Message}. Built-in as-is values will be used.");
            }
        }

        static string FindFile(string fileName)
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(CombatResolutionSettings).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
                return null;

            string parent = Directory.GetParent(assemblyDirectory)?.FullName ?? assemblyDirectory;
            string[] candidates =
            {
                Path.Combine(assemblyDirectory, fileName),
                Path.Combine(assemblyDirectory, "Assets", fileName),
                Path.Combine(parent, fileName),
                Path.Combine(parent, "Assets", fileName)
            };

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
        }
    }

    public sealed class CoreCombatResolutionTable
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("finalDamageMultiplier")]
        public float FinalDamageMultiplier { get; set; } = 1f;

        [JsonProperty("factorWeights")]
        public Dictionary<string, float> FactorWeights { get; set; } =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public float FactorWeight(string name) =>
            FactorWeights.TryGetValue(name, out float value) ? Mathf.Max(0f, value) : 1f;

        public void Normalize()
        {
            FinalDamageMultiplier = Mathf.Max(0f, FinalDamageMultiplier);
            FactorWeights = FactorWeights != null
                ? new Dictionary<string, float>(FactorWeights, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class SquadCombatResolutionTable
    {
        [JsonProperty("hitChance")]
        public HitChanceFactors HitChance { get; set; } = new HitChanceFactors();

        [JsonProperty("damage")]
        public DamageFactors Damage { get; set; } = new DamageFactors();

        [JsonProperty("suppression")]
        public SuppressionFactors Suppression { get; set; } = new SuppressionFactors();

        [JsonProperty("moveMode")]
        public MoveModeCombatFactors MoveMode { get; set; } = new MoveModeCombatFactors();

        public void Normalize()
        {
            HitChance = HitChance ?? new HitChanceFactors();
            Damage = Damage ?? new DamageFactors();
            Suppression = Suppression ?? new SuppressionFactors();
            MoveMode = MoveMode ?? new MoveModeCombatFactors();
            HitChance.Normalize();
            Damage.Normalize();
            Suppression.Normalize();
            MoveMode.Normalize();
        }
    }

    public sealed class HitChanceFactors
    {
        public float BaseChance { get; set; } = 0.78f;
        public float Minimum { get; set; } = 0.05f;
        public float Maximum { get; set; } = 0.95f;
        public float TankBonus { get; set; } = 0.05f;
        public float InfantryCloseRangeBonus { get; set; } = 0.04f;
        public int InfantryCloseRangeMaximumHexes { get; set; } = 2;
        public float RetaliationPenalty { get; set; } = 0.10f;
        public float SupportiveFirePenalty { get; set; } = 0.05f;
        public float AttackerSuppressionPenaltyAtMaximum { get; set; } = 0.45f;
        public float TargetSuppressionBonusAtMaximum { get; set; } = 0.25f;

        public void Normalize()
        {
            BaseChance = Mathf.Clamp01(BaseChance);
            Minimum = Mathf.Clamp01(Minimum);
            Maximum = Mathf.Clamp(Maximum, Minimum, 1f);
            InfantryCloseRangeMaximumHexes = Math.Max(0, InfantryCloseRangeMaximumHexes);
        }
    }

    public sealed class DamageFactors
    {
        public float TargetSuppressionBonusAtMaximum { get; set; } = 0.35f;
        public float RandomSpreadMinimum { get; set; } = 0.85f;
        public float RandomSpreadMaximum { get; set; } = 1.15f;

        public void Normalize()
        {
            TargetSuppressionBonusAtMaximum = Math.Max(0f, TargetSuppressionBonusAtMaximum);
            RandomSpreadMinimum = Math.Max(0f, RandomSpreadMinimum);
            RandomSpreadMaximum = Math.Max(RandomSpreadMinimum, RandomSpreadMaximum);
        }
    }

    public sealed class SuppressionFactors
    {
        public int Maximum { get; set; } = 100;
        public int TargetGainOnHit { get; set; } = 30;
        public int AttackerRecoveryOnHit { get; set; } = 8;
        public int TargetGainOnMiss { get; set; } = 12;

        public void Normalize()
        {
            Maximum = Math.Max(1, Maximum);
            TargetGainOnHit = Math.Max(0, TargetGainOnHit);
            AttackerRecoveryOnHit = Math.Max(0, AttackerRecoveryOnHit);
            TargetGainOnMiss = Math.Max(0, TargetGainOnMiss);
        }
    }

    public sealed class MoveModeCombatFactors
    {
        public float IncomingHitChanceBonus { get; set; } = 0.15f;
        public float IncomingDamageMultiplier { get; set; } = 1.20f;
        public float AttackerHitChancePenalty { get; set; } = 0.12f;

        public void Normalize()
        {
            IncomingDamageMultiplier = Math.Max(0f, IncomingDamageMultiplier);
        }
    }
}
