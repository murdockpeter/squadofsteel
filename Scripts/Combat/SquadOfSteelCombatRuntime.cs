// =============================================================================
// Combat runtime helpers that manage line of sight, hit chance evaluation,
// random outcomes, and cross-turn caches.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SquadOfSteelMod.Combat
{
    public static class SquadCombatRuntime
    {
        static readonly Dictionary<CombatKey, CombatPreview> s_previews = new Dictionary<CombatKey, CombatPreview>();
        static readonly Dictionary<CombatKey, CombatOutcome> s_outcomes = new Dictionary<CombatKey, CombatOutcome>();

        static bool _initialized;
        static bool _debugEnabled;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Debug.Log($"[SquadOfSteel] Combat runtime initialized (debug {(DebugEnabled ? "ON" : "OFF")}).");
        }

        public static void Shutdown()
        {
            s_previews.Clear();
            s_outcomes.Clear();
            _initialized = false;
        }

        public static bool DebugEnabled => _debugEnabled;

        public static void SetDebugEnabled(bool value)
        {
            _debugEnabled = value;
            if (value)
            {
                CombatDebugOverlay.SetVisible(true);
            }
            else
            {
                CombatDebugOverlay.SetVisible(false);
            }
        }

        public static void StorePreview(Unit attacker, Unit target, CombatPreview preview)
        {
            if (attacker == null || target == null)
                return;

            var key = new CombatKey(attacker.ID, target.ID);
            s_previews[key] = preview;
        }

        public static bool TryConsumePreview(Unit attacker, Unit target, out CombatPreview preview)
        {
            preview = default;
            if (attacker == null || target == null)
                return false;

            var key = new CombatKey(attacker.ID, target.ID);
            if (!s_previews.TryGetValue(key, out preview))
                return false;

            s_previews.Remove(key);
            return true;
        }

        public static CombatOutcome ResolveOutcome(CombatPreview preview)
        {
            if (preview.DamageOnHit <= 0 || preview.HitChance <= 0f || !preview.HasLineOfSight)
            {
                return new CombatOutcome
                {
                    Hit = false,
                    Damage = 0,
                    HitChance = Mathf.Clamp01(preview.HitChance),
                    HasLineOfSight = preview.HasLineOfSight,
                    DamageOnHit = preview.DamageOnHit,
                    Distance = preview.Distance,
                    Roll = 1f,
                    BaseDamage = preview.BaseDamage,
                    ExpectedDamage = preview.ExpectedDamage,
                    AttackerSuppressionBefore = preview.AttackerSuppression,
                    TargetSuppressionBefore = preview.TargetSuppression,
                    IsRetaliation = preview.IsRetaliation,
                    IsSupport = preview.IsSupport
                };
            }

            float roll = UnityEngine.Random.value;
            bool hit = roll <= preview.HitChance;

            int damage = 0;
            if (hit)
            {
                float spread = UnityEngine.Random.Range(0.85f, 1.15f);
                damage = Mathf.Max(0, Mathf.RoundToInt(preview.DamageOnHit * spread));
            }

            return new CombatOutcome
            {
                Hit = hit,
                Damage = damage,
                HitChance = Mathf.Clamp01(preview.HitChance),
                HasLineOfSight = preview.HasLineOfSight,
                DamageOnHit = preview.DamageOnHit,
                Distance = preview.Distance,
                Roll = roll,
                BaseDamage = preview.BaseDamage,
                ExpectedDamage = preview.ExpectedDamage,
                AttackerSuppressionBefore = preview.AttackerSuppression,
                TargetSuppressionBefore = preview.TargetSuppression,
                IsRetaliation = preview.IsRetaliation,
                IsSupport = preview.IsSupport
            };
        }

        public static void RecordOutcome(Unit attacker, Unit target, CombatOutcome outcome)
        {
            if (attacker == null || target == null)
                return;

            var key = new CombatKey(attacker.ID, target.ID);
            s_outcomes[key] = outcome;
        }

        public static bool TryGetOutcome(Unit attacker, Unit target, out CombatOutcome outcome)
        {
            outcome = default;
            if (attacker == null || target == null)
                return false;

            return s_outcomes.TryGetValue(new CombatKey(attacker.ID, target.ID), out outcome);
        }

        public static void ClearOutcome(Unit attacker, Unit target)
        {
            if (attacker == null || target == null)
                return;

            s_outcomes.Remove(new CombatKey(attacker.ID, target.ID));
        }

        public static void ClearForUnit(Unit unit)
        {
            if (unit == null)
                return;

            var keys = s_previews.Keys.Where(k => k.Contains(unit.ID)).ToList();
            foreach (var key in keys)
            {
                s_previews.Remove(key);
            }

            keys = s_outcomes.Keys.Where(k => k.Contains(unit.ID)).ToList();
            foreach (var key in keys)
            {
                s_outcomes.Remove(key);
            }
        }

        public static bool ValidateDirectFire(UnitGO attacker, UnitGO defender, out string reason)
        {
            reason = null;

            if (attacker == null || defender == null)
                return true;

            if (LineOfSightService.IsIndirectFire(attacker.unit))
                return true;

            Tile attackerTile = attacker.tileGO?.tile;
            Tile defenderTile = defender.tileGO?.tile;

            if (attackerTile == null || defenderTile == null)
                return true;

            bool hasLoS = LineOfSightService.HasLineOfSight(attacker, defenderTile, out Tile blockingTile);
            if (hasLoS)
                return true;

            reason = blockingTile != null
                ? $"Line of sight blocked by {blockingTile.enumType}"
                : "Line of sight blocked";
            return false;
        }

        public static void NotifyBlocked(UnitGO attacker, string reason)
        {
            if (attacker == null)
                return;

            var player = attacker.unit?.OwnerPlayer;
            if (player == null || player.IsComputer)
                return;

            try
            {
                var tile = attacker.tileGO?.tile;
                new Notification(NotificationTypes.DEFAULT,
                    player.Name,
                    tile?.PosX ?? 0,
                    tile?.PosY ?? 0,
                    $"Squad Of Steel: {reason}",
                    p_isImportant: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SquadOfSteel] Failed to post notification: {ex.Message}");
            }
        }

        readonly struct CombatKey : IEquatable<CombatKey>
        {
            public readonly int AttackerId;
            public readonly int TargetId;

            public CombatKey(int attackerId, int targetId)
            {
                AttackerId = attackerId;
                TargetId = targetId;
            }

            public bool Contains(int unitId) => AttackerId == unitId || TargetId == unitId;

            public bool Equals(CombatKey other) => AttackerId == other.AttackerId && TargetId == other.TargetId;

            public override bool Equals(object obj) => obj is CombatKey other && Equals(other);

            public override int GetHashCode() => (AttackerId * 397) ^ TargetId;
        }
    }

    public readonly struct CombatPreview
    {
        public readonly int BaseDamage;
        public readonly int DamageOnHit;
        public readonly int ExpectedDamage;
        public readonly float HitChance;
        public readonly bool HasLineOfSight;
        public readonly int Distance;
        public readonly int AttackerSuppression;
        public readonly int TargetSuppression;
        public readonly bool IsRetaliation;
        public readonly bool IsSupport;

        public CombatPreview(
            int baseDamage,
            int damageOnHit,
            int expectedDamage,
            float hitChance,
            bool hasLineOfSight,
            int distance,
            int attackerSuppression,
            int targetSuppression,
            bool isRetaliation,
            bool isSupport)
        {
            BaseDamage = baseDamage;
            DamageOnHit = damageOnHit;
            ExpectedDamage = expectedDamage;
            HitChance = hitChance;
            HasLineOfSight = hasLineOfSight;
            Distance = distance;
            AttackerSuppression = attackerSuppression;
            TargetSuppression = targetSuppression;
            IsRetaliation = isRetaliation;
            IsSupport = isSupport;
        }
    }

    public sealed class CombatOutcome
    {
        public bool Hit { get; set; }
        public int Damage { get; set; }
        public float HitChance { get; set; }
        public bool HasLineOfSight { get; set; }
        public int DamageOnHit { get; set; }
        public int Distance { get; set; }
        public float Roll { get; set; }
        public int BaseDamage { get; set; }
        public int ExpectedDamage { get; set; }
        public int AttackerSuppressionBefore { get; set; }
        public int TargetSuppressionBefore { get; set; }
        public bool IsRetaliation { get; set; }
        public bool IsSupport { get; set; }
        public int AttackerSuppressionAfter { get; set; }
        public int TargetSuppressionAfter { get; set; }
    }

    public static class SquadOfSteelCombatMath
    {
        static readonly Dictionary<TileTypes, float> s_coverPenalties = new Dictionary<TileTypes, float>
        {
            { TileTypes.FOREST, 0.18f },
            { TileTypes.MOUNTAIN, 0.22f },
            { TileTypes.CITY, 0.25f },
            { TileTypes.TRENCH, 0.28f },
            { TileTypes.HILL, 0.12f },
            { TileTypes.MARSH, 0.10f },
            { TileTypes.HARBOUR, 0.10f },
            { TileTypes.FACTORY, 0.18f }
        };

        public static float ComputeHitChance(Unit attacker, UnitGO attackerGO, UnitGO targetGO, int distance, bool hasLoS, int attackerSuppression, int targetSuppression, bool isRetaliation, bool isSupport)
        {
            if (!hasLoS)
                return 0f;

            float chance = 0.78f;

            if (distance <= 1)
            {
                chance += 0.05f;
            }
            else
            {
                chance -= 0.1f * (distance - 1);
            }

            if (targetGO?.tileGO?.tile != null && s_coverPenalties.TryGetValue(targetGO.tileGO.tile.enumType, out float penalty))
            {
                chance -= penalty;
            }

            if (!string.IsNullOrEmpty(attacker?.FilterType) && attacker.FilterType == "FilterTank")
            {
                chance += 0.05f;
            }

            if (!string.IsNullOrEmpty(attacker?.FilterType) && attacker.FilterType == "FilterInfantry" && distance <= 2)
            {
                chance += 0.04f;
            }

            if (isRetaliation)
            {
                chance -= 0.1f;
            }

            if (isSupport)
            {
                chance -= 0.05f;
            }

            chance -= Mathf.Clamp01(attackerSuppression / 100f) * 0.45f;
            chance += Mathf.Clamp01(targetSuppression / 100f) * 0.25f;

            return Mathf.Clamp(chance, 0.05f, 0.95f);
        }

        public static int ComputeDamageOnHit(int baseDamage, int distance, int targetSuppression)
        {
            if (baseDamage <= 0)
                return 0;

            float damage = baseDamage;

            if (distance > 1)
            {
                damage *= Mathf.Clamp01(1f - 0.08f * (distance - 1));
            }

            damage *= 1f + Mathf.Clamp01(targetSuppression / 100f) * 0.35f;

            return Mathf.Max(0, Mathf.RoundToInt(damage));
        }
    }

    public static class LineOfSightService
    {
        static readonly TileTypes[] s_blockingTiles =
        {
            TileTypes.FOREST,
            TileTypes.MOUNTAIN,
            TileTypes.CITY,
            TileTypes.TRENCH,
            TileTypes.FACTORY,
            TileTypes.HILL
        };

        public static bool IsIndirectFire(Unit unit)
        {
            if (unit == null)
                return true;

            if (unit.Type == "Plane")
                return true;

            if (unit.FilterType == "FilterArtillery" || unit.FilterType == "FilterBomber" || unit.FilterType == "FilterCAS")
                return true;

            if (unit.HasBombs || unit.HasRockets)
                return true;

            return false;
        }

        public static bool HasLineOfSight(UnitGO attacker, Tile targetTile) => HasLineOfSight(attacker, targetTile, out _);

        public static bool HasLineOfSight(UnitGO attacker, Tile targetTile, out Tile blockingTile)
        {
            blockingTile = null;

            if (attacker?.tileGO?.tile == null || targetTile == null)
                return true;

            if (ReferenceEquals(attacker.tileGO.tile, targetTile))
                return true;

            if (IsIndirectFire(attacker.unit))
                return true;

            int distance = HexGridHelper.GetDistance(attacker.tileGO.tile, targetTile);
            if (distance <= 1)
                return true;

            var line = HexGridHelper.GetLine(attacker.tileGO.tile, targetTile);
            if (line.Count == 0)
                return true;

            for (int i = 1; i < line.Count - 1; i++)
            {
                Tile tile = line[i];
                if (tile == null)
                    continue;

                if (tile.tileGO != null)
                {
                    if (tile.tileGO.groundUnitGO != null || tile.tileGO.airUnitGO != null)
                    {
                        blockingTile = tile;
                        return false;
                    }
                }

                if (s_blockingTiles.Contains(tile.enumType))
                {
                    blockingTile = tile;
                    return false;
                }
            }

            return true;
        }
    }

    public static class HexGridHelper
    {
        public static int GetDistance(Tile a, Tile b)
        {
            if (a == null || b == null)
                return int.MaxValue;

            var ac = ToCube(a.PosX, a.PosY);
            var bc = ToCube(b.PosX, b.PosY);
            return Mathf.Max(Mathf.Abs(ac.x - bc.x), Mathf.Abs(ac.y - bc.y), Mathf.Abs(ac.z - bc.z));
        }

        public static List<Tile> GetLine(Tile start, Tile end)
        {
            var result = new List<Tile>();
            if (start == null || end == null)
                return result;

            var map = GameData.Instance?.map;
            if (map == null)
                return result;

            var startCube = ToCube(start.PosX, start.PosY);
            var endCube = ToCube(end.PosX, end.PosY);
            int distance = GetDistance(start, end);
            if (distance == int.MaxValue)
                return result;

            for (int i = 0; i <= distance; i++)
            {
                float t = distance == 0 ? 0f : (1f / distance) * i;
                var cube = CubeRound(Vector3.Lerp((Vector3)startCube, (Vector3)endCube, t));
                if (TryGetTile(cube.x, cube.z, out var tile))
                {
                    result.Add(tile);
                }
            }

            return result;
        }

        static Vector3Int ToCube(int col, int row)
        {
            int x = col;
            int z = row - (col + (col & 1)) / 2;
            int y = -x - z;
            return new Vector3Int(x, y, z);
        }

        static Vector3Int CubeRound(Vector3 cube)
        {
            int rx = Mathf.RoundToInt(cube.x);
            int ry = Mathf.RoundToInt(cube.y);
            int rz = Mathf.RoundToInt(cube.z);

            float xDiff = Mathf.Abs(rx - cube.x);
            float yDiff = Mathf.Abs(ry - cube.y);
            float zDiff = Mathf.Abs(rz - cube.z);

            if (xDiff > yDiff && xDiff > zDiff)
            {
                rx = -ry - rz;
            }
            else if (yDiff > zDiff)
            {
                ry = -rx - rz;
            }
            else
            {
                rz = -rx - ry;
            }

            return new Vector3Int(rx, ry, rz);
        }

        static bool TryGetTile(int cubeX, int cubeZ, out Tile tile)
        {
            tile = null;
            var map = GameData.Instance?.map;
            if (map == null)
                return false;

            int col = cubeX;
            int row = cubeZ + (cubeX + (cubeX & 1)) / 2;

            if (col < 0 || col >= map.SizeX || row < 0 || row >= map.SizeY)
                return false;

            tile = map.TilesTable[col, row];
            return tile != null;
        }
    }
}
