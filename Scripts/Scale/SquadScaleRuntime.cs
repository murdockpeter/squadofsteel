using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SquadOfSteelMod.Scale
{
    public static class SquadScaleRuntime
    {
        const string StorageKey = "SquadOfSteel.ScaleProfile";
        const string ProfileFileName = "scale-profiles.json";
        const string DefaultProfileId = "default";

        static readonly Dictionary<string, SquadScaleProfile> s_profiles =
            new Dictionary<string, SquadScaleProfile>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> s_profileOrder = new List<string>();

        static bool s_profilesLoaded;
        static object s_currentMap;
        static bool s_scenarioResolved;
        static bool s_needsSelection;
        static SquadScaleProfile s_activeProfile;

        public static SquadScaleProfile ActiveProfile
        {
            get
            {
                EnsureProfilesLoaded();
                return s_activeProfile;
            }
        }

        public static IReadOnlyList<SquadScaleProfile> Profiles
        {
            get
            {
                EnsureProfilesLoaded();
                return s_profileOrder
                    .Where(id => s_profiles.ContainsKey(id))
                    .Select(id => s_profiles[id])
                    .ToList();
            }
        }

        public static bool NeedsSelection => s_needsSelection;

        public static int PassiveSuppressionRecovery =>
            Mathf.Max(0, ActiveProfile?.PassiveSuppressionRecovery ?? 15);

        public static void Tick()
        {
            object map = GameData.Instance?.map;
            if (map == null)
                return;

            if (!ReferenceEquals(map, s_currentMap))
            {
                s_currentMap = map;
                s_scenarioResolved = false;
                s_needsSelection = false;
                s_activeProfile = GetProfile(DefaultProfileId);
                SquadScaleSelectionUI.ResetForScenario();
            }

            if (s_scenarioResolved)
                return;

            s_scenarioResolved = true;
            if (TryReadSavedProfile(out string profileId))
            {
                if (!TryActivate(profileId, persist: false))
                {
                    Debug.LogWarning($"[SquadOfSteel][Scale] Saved profile '{profileId}' is unavailable; using Default.");
                    TryActivate(DefaultProfileId, persist: false);
                }

                s_needsSelection = false;
                return;
            }

            s_activeProfile = GetProfile(DefaultProfileId);
            s_needsSelection = true;
        }

        public static bool SelectProfile(string profileId)
        {
            if (!TryActivate(profileId, persist: true))
                return false;

            s_needsSelection = false;
            return true;
        }

        public static float GetAccuracyDistanceModifier(Unit attacker, int distance)
        {
            var profile = ActiveProfile;
            if (distance <= 1)
                return profile.AdjacentHitBonus;

            if (UsesFractionOfRange(profile))
            {
                float fraction = GetRangeFraction(attacker, distance);
                return -Mathf.Max(0f, profile.AccuracyPenaltyAtMaximumRange) * fraction;
            }

            return -Mathf.Max(0f, profile.AccuracyPenaltyPerHex) * (distance - 1);
        }

        public static float GetDamageDistanceMultiplier(Unit attacker, int distance)
        {
            if (distance <= 1)
                return 1f;

            var profile = ActiveProfile;
            if (UsesFractionOfRange(profile))
            {
                float fraction = GetRangeFraction(attacker, distance);
                float atMaximum = Mathf.Clamp01(profile.DamageMultiplierAtMaximumRange);
                return Mathf.Lerp(1f, atMaximum, fraction);
            }

            return Mathf.Clamp01(1f - Mathf.Max(0f, profile.DamageLossPerHex) * (distance - 1));
        }

        public static bool TryGetCoverPenalty(TileTypes terrain, out float penalty)
        {
            penalty = 0f;
            var values = ActiveProfile?.CoverPenalties;
            if (values == null)
                return false;

            if (!values.TryGetValue(terrain.ToString(), out penalty))
                return false;

            penalty = Mathf.Clamp01(penalty);
            return true;
        }

        public static bool TerrainBlocksLineOfSight(TileTypes terrain)
        {
            var values = ActiveProfile?.BlockingTerrain;
            if (values == null)
                return false;

            string name = terrain.ToString();
            return values.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        }

        public static bool GroundUnitsBlockLineOfSight =>
            ActiveProfile?.GroundUnitsBlockLineOfSight ?? true;

        public static bool AirUnitsBlockLineOfSight =>
            ActiveProfile?.AirUnitsBlockLineOfSight ?? true;

        public static string FormatDistance(int hexDistance)
        {
            int hexes = Mathf.Max(0, hexDistance);
            float metersPerHex = ActiveProfile?.HexMeters ?? 0f;
            if (metersPerHex <= 0f)
                return $"{hexes} hex";

            float meters = hexes * metersPerHex;
            if (meters >= 1000f)
                return $"{hexes} hex / {meters / 1000f:0.##} km";

            return $"{hexes} hex / {meters:0} m";
        }

        static bool UsesFractionOfRange(SquadScaleProfile profile)
        {
            return string.Equals(profile?.DistanceModel, "fractionOfRange", StringComparison.OrdinalIgnoreCase);
        }

        static float GetRangeFraction(Unit attacker, int distance)
        {
            int maximumRange = Mathf.Max(1, attacker?.Range ?? 1);
            if (maximumRange <= 1)
                return 1f;

            return Mathf.Clamp01((distance - 1f) / (maximumRange - 1f));
        }

        static bool TryActivate(string profileId, bool persist)
        {
            var profile = GetProfile(profileId);
            if (profile == null)
                return false;

            s_activeProfile = profile;
            if (persist)
            {
                try
                {
                    GameData.Instance?.ModDataBag?.TrySet(StorageKey, profile.Id, preferKnownOverUnknown: true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SquadOfSteel][Scale] Could not persist profile selection: {ex.Message}");
                }
            }

            Debug.Log($"[SquadOfSteel][Scale] Active profile: {profile.DisplayName} ({profile.Id}).");
            return true;
        }

        static bool TryReadSavedProfile(out string profileId)
        {
            profileId = null;
            try
            {
                return GameData.Instance?.ModDataBag != null &&
                       GameData.Instance.ModDataBag.TryGet(StorageKey, out profileId) &&
                       !string.IsNullOrWhiteSpace(profileId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel][Scale] Could not read saved profile: {ex.Message}");
                return false;
            }
        }

        static SquadScaleProfile GetProfile(string profileId)
        {
            EnsureProfilesLoaded();
            if (string.IsNullOrWhiteSpace(profileId))
                profileId = DefaultProfileId;

            return s_profiles.TryGetValue(profileId, out var profile) ? profile : null;
        }

        static void EnsureProfilesLoaded()
        {
            if (s_profilesLoaded)
                return;

            s_profilesLoaded = true;
            LoadBuiltInFallbacks();

            foreach (string path in FindProfileFiles())
            {
                try
                {
                    var catalog = JsonConvert.DeserializeObject<SquadScaleProfileCatalog>(File.ReadAllText(path));
                    if (catalog?.Profiles == null)
                        continue;

                    foreach (var profile in catalog.Profiles)
                    {
                        if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
                            continue;

                        Normalize(profile);
                        AddOrReplaceProfile(profile);
                    }

                    Debug.Log($"[SquadOfSteel][Scale] Loaded scale profiles from '{path}'.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SquadOfSteel][Scale] Failed to load '{path}': {ex.Message}");
                }
            }

            s_activeProfile = s_profiles[DefaultProfileId];
        }

        static IEnumerable<string> FindProfileFiles()
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(SquadScaleRuntime).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
                yield break;

            string[] candidates =
            {
                Path.Combine(assemblyDirectory, ProfileFileName),
                Path.Combine(assemblyDirectory, "Assets", ProfileFileName),
                Path.Combine(Directory.GetParent(assemblyDirectory)?.FullName ?? assemblyDirectory, ProfileFileName),
                Path.Combine(Directory.GetParent(assemblyDirectory)?.FullName ?? assemblyDirectory, "Assets", ProfileFileName)
            };

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }

        static void LoadBuiltInFallbacks()
        {
            var blockingTerrain = new List<string>
            {
                "FOREST", "MOUNTAIN", "CITY", "TRENCH", "FACTORY", "HILL"
            };
            var cover = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                { "FOREST", 0.18f },
                { "MOUNTAIN", 0.22f },
                { "CITY", 0.25f },
                { "TRENCH", 0.28f },
                { "HILL", 0.12f },
                { "MARSH", 0.10f },
                { "HARBOUR", 0.10f },
                { "FACTORY", 0.18f }
            };

            AddOrReplaceProfile(new SquadScaleProfile
            {
                Id = DefaultProfileId,
                DisplayName = "Default / Existing SoS",
                Description = "Preserves the current Squad of Steel behavior and leaves physical scale abstract.",
                DistanceModel = "perHex",
                BlockingTerrain = new List<string>(blockingTerrain),
                CoverPenalties = new Dictionary<string, float>(cover),
                PassiveSuppressionRecovery = 15
            });

            AddOrReplaceProfile(new SquadScaleProfile
            {
                Id = "operational-5km",
                DisplayName = "Operational - 5 km/hex",
                Description = "Operational distance labels with the existing SoS combat and LOS interpretation.",
                HexMeters = 5000f,
                TurnMinutes = 360,
                DistanceModel = "perHex",
                BlockingTerrain = new List<string>(blockingTerrain),
                CoverPenalties = new Dictionary<string, float>(cover),
                PassiveSuppressionRecovery = 15
            });

            AddOrReplaceProfile(new SquadScaleProfile
            {
                Id = "company-1km",
                DisplayName = "Company - 1 km/hex",
                Description = "Range-relative falloff and company-scale distance reporting; unit statistics remain untouched.",
                HexMeters = 1000f,
                TurnMinutes = 60,
                DistanceModel = "fractionOfRange",
                AccuracyPenaltyAtMaximumRange = 0.30f,
                DamageMultiplierAtMaximumRange = 0.70f,
                GroundUnitsBlockLineOfSight = false,
                AirUnitsBlockLineOfSight = false,
                BlockingTerrain = new List<string> { "FOREST", "MOUNTAIN", "CITY", "FACTORY", "HILL" },
                CoverPenalties = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    { "FOREST", 0.20f },
                    { "MOUNTAIN", 0.20f },
                    { "CITY", 0.30f },
                    { "TRENCH", 0.32f },
                    { "HILL", 0.12f },
                    { "MARSH", 0.08f },
                    { "HARBOUR", 0.08f },
                    { "FACTORY", 0.24f }
                },
                PassiveSuppressionRecovery = 12
            });

            AddOrReplaceProfile(new SquadScaleProfile
            {
                Id = "platoon-250m",
                DisplayName = "Platoon - 250 m/hex",
                Description = "Tactical LOS, range-relative falloff, and 15-minute suppression recovery; unit statistics remain untouched.",
                HexMeters = 250f,
                TurnMinutes = 15,
                DistanceModel = "fractionOfRange",
                AccuracyPenaltyAtMaximumRange = 0.25f,
                DamageMultiplierAtMaximumRange = 0.75f,
                GroundUnitsBlockLineOfSight = false,
                AirUnitsBlockLineOfSight = false,
                BlockingTerrain = new List<string> { "FOREST", "MOUNTAIN", "CITY", "FACTORY", "HILL" },
                CoverPenalties = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    { "FOREST", 0.22f },
                    { "MOUNTAIN", 0.18f },
                    { "CITY", 0.35f },
                    { "TRENCH", 0.40f },
                    { "HILL", 0.10f },
                    { "MARSH", 0.06f },
                    { "HARBOUR", 0.06f },
                    { "FACTORY", 0.30f }
                },
                PassiveSuppressionRecovery = 8
            });

            AddOrReplaceProfile(new SquadScaleProfile
            {
                Id = "squad-50m",
                DisplayName = "Squad - 50 m/hex",
                Description = "Close tactical LOS, range-relative falloff, and 5-minute suppression recovery; unit statistics remain untouched.",
                HexMeters = 50f,
                TurnMinutes = 5,
                DistanceModel = "fractionOfRange",
                AccuracyPenaltyAtMaximumRange = 0.20f,
                DamageMultiplierAtMaximumRange = 0.80f,
                GroundUnitsBlockLineOfSight = false,
                AirUnitsBlockLineOfSight = false,
                BlockingTerrain = new List<string> { "FOREST", "MOUNTAIN", "CITY", "FACTORY", "HILL" },
                CoverPenalties = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    { "FOREST", 0.25f },
                    { "MOUNTAIN", 0.18f },
                    { "CITY", 0.38f },
                    { "TRENCH", 0.42f },
                    { "HILL", 0.08f },
                    { "MARSH", 0.05f },
                    { "HARBOUR", 0.05f },
                    { "FACTORY", 0.34f }
                },
                PassiveSuppressionRecovery = 5
            });
        }

        static void AddOrReplaceProfile(SquadScaleProfile profile)
        {
            if (!s_profiles.ContainsKey(profile.Id))
                s_profileOrder.Add(profile.Id);

            s_profiles[profile.Id] = profile;
        }

        static void Normalize(SquadScaleProfile profile)
        {
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id : profile.DisplayName;
            profile.Description = profile.Description ?? string.Empty;
            profile.DistanceModel = string.IsNullOrWhiteSpace(profile.DistanceModel) ? "perHex" : profile.DistanceModel;
            profile.BlockingTerrain = profile.BlockingTerrain ?? new List<string>();
            profile.CoverPenalties = profile.CoverPenalties != null
                ? new Dictionary<string, float>(profile.CoverPenalties, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
