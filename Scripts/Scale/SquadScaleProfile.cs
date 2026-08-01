using System.Collections.Generic;
using Newtonsoft.Json;

namespace SquadOfSteelMod.Scale
{
    public sealed class SquadScaleProfileCatalog
    {
        [JsonProperty("profiles")]
        public List<SquadScaleProfile> Profiles { get; set; } = new List<SquadScaleProfile>();
    }

    public sealed class SquadScaleProfile
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("hexMeters")]
        public float HexMeters { get; set; }

        [JsonProperty("turnMinutes")]
        public int TurnMinutes { get; set; }

        [JsonProperty("distanceModel")]
        public string DistanceModel { get; set; } = "perHex";

        [JsonProperty("adjacentHitBonus")]
        public float AdjacentHitBonus { get; set; } = 0.05f;

        [JsonProperty("accuracyPenaltyPerHex")]
        public float AccuracyPenaltyPerHex { get; set; } = 0.10f;

        [JsonProperty("accuracyPenaltyAtMaximumRange")]
        public float AccuracyPenaltyAtMaximumRange { get; set; } = 0.30f;

        [JsonProperty("damageLossPerHex")]
        public float DamageLossPerHex { get; set; } = 0.08f;

        [JsonProperty("damageMultiplierAtMaximumRange")]
        public float DamageMultiplierAtMaximumRange { get; set; } = 0.70f;

        [JsonProperty("groundUnitsBlockLineOfSight")]
        public bool GroundUnitsBlockLineOfSight { get; set; } = true;

        [JsonProperty("airUnitsBlockLineOfSight")]
        public bool AirUnitsBlockLineOfSight { get; set; } = true;

        [JsonProperty("blockingTerrain")]
        public List<string> BlockingTerrain { get; set; } = new List<string>();

        [JsonProperty("coverPenalties")]
        public Dictionary<string, float> CoverPenalties { get; set; } =
            new Dictionary<string, float>();

        [JsonProperty("passiveSuppressionRecovery")]
        public int PassiveSuppressionRecovery { get; set; } = 15;
    }
}
