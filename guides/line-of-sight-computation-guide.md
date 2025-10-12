# Squad of Steel Line of Sight Guide

This document drills into the line-of-sight (LoS) pipeline implemented by the Squad of Steel mod and how the results cascade into combat math, suppression, and debug tooling. Keep it nearby when adjusting terrain behaviour or expanding the LoS system.

## High-Level Flow

| Stage | Method | Summary |
| --- | --- | --- |
| Classification | `LineOfSightService.IsIndirectFire` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:330`) | Tags units that are allowed to ignore LoS (artillery, bombers, CAS, planes, bomb/rocket carriers). |
| Eligibility | `SquadCombatRuntime.ValidateDirectFire` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:169`) | Blocks player-initiated direct fire when LoS cannot be established and surfaces the reason. |
| Geometry | `LineOfSightService.HasLineOfSight` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:338`) | Executes the ray march across the hex grid to detect blocking tiles and unit occupiers. |
| Combat preview | Harmony postfix on `Unit.GetPotentialDamage` (`Scripts/Combat/SquadOfSteelCombatPatches.cs:21`) | Embeds `HasLineOfSight` inside the `CombatPreview` used by the rest of the combat pipeline. |
| Resolution | `SquadCombatRuntime.ResolveOutcome` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:75`) | Auto-misses when LoS is absent so the game never lands a stray hit. |
| Player feedback | `CombatDebugReporter.Report` (`Scripts/Combat/CombatDebugReporter.cs:13`) | Prints “LoS blocked” context in logs, overlay entries, popups, and notifications. |

## Detailed Algorithm

### Input Conditioning

1. **Null guards** – Any missing attacker, map, or target tile defaults to “LoS true” to avoid blocking scripted sequences. (`HasLineOfSight` early exits with `true` when either tile is null.)
2. **Same tile** – Occupants sharing a tile are considered to have clear LoS, letting melee resolution bypass geometry checks.
3. **Indirect fire bypass** – Units identified by `IsIndirectFire` skip LoS checks entirely. This is the mod’s way of emulating arcing or aerial attacks.

### Distance & Ray Construction

| Step | Implementation | Notes |
| --- | --- | --- |
| Distance | `HexGridHelper.GetDistance` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:399`) | Converts axial coordinates to cube space and measures Chebyshev distance. Returns `int.MaxValue` when data is missing. |
| Early exit | If distance ≤ 1 | Adjacent fire auto-succeeds on LoS, matching artillery minimum ranges from the base game. |
| Ray | `HexGridHelper.GetLine` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:409`) | Samples the straight line between start/end cubes using linear interpolation and cube rounding. |
| Map lookup | `HexGridHelper.TryGetTile` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:450`) | Guards array bounds and returns only valid tiles. |

The resulting tile sequence includes both endpoints; intermediate points are inspected for blockers.

### Blocker Evaluation

1. **Occupied tiles** – Any intermediate tile whose `tileGO` hosts a ground or air `UnitGO` stops line of sight. The blocking tile instance is captured for downstream messaging.
2. **Static terrain** – Terrain types in the blocking array `{Forest, Mountain, City, Trench, Factory, Hill}` prevent LoS. These are deliberately stricter than the cover penalties listed in combat math; Marsh and Harbour only degrade hit chance.
3. **Fallback** – Empty or passable tiles allow the ray to continue. If the loop completes, LoS is clear.

When a blocking tile is discovered, `HasLineOfSight` returns `false` and passes the tile through the out parameter used by `ValidateDirectFire`.

### Null & Map Edge Behaviour

- If `GameData.Instance?.map` is unavailable (common during loading screens), `GetLine` returns an empty list, which `HasLineOfSight` treats as “LoS true”. The guard message (“UIManager canvas not yet available”) is printed elsewhere if the UI is not ready.
- Cube conversion gracefully handles odd-r offset hex coordinates (`ToCube`/`CubeRound` maintain parity).

## LoS Classifier (`IsIndirectFire`)

| Condition | Rationale |
| --- | --- |
| `unit == null` | Safeguard; considers missing references indirect to keep the game responsive. |
| `unit.Type == "Plane"` | Planes always bypass LoS. |
| `unit.FilterType` in `{FilterArtillery, FilterBomber, FilterCAS}` | Standard indirect/air tags. |
| `unit.HasBombs` or `unit.HasRockets` | Ensures tactical bombers with payloads arc over blockers. |

Any future unit categories that should ignore LoS must extend this method.

## Combat Impact Matrix

| LoS Result | Combat Effect | Source |
| --- | --- | --- |
| `false` at preview time | `HitChance` forced to 0 and `DamageOnHit` forced to 0 inside the Harmony postfix. `ExpectedDamage` collapses to 0 so the UI reflects the blocked shot (`SquadOfSteelCombatPatches.cs:47`). |
| `false` at validation | `ValidateDirectFire` returns `false`, interrupting `UnitGO.AttackUnit` and causing a notification/overlay entry (`SquadOfSteelCombatRuntime.cs:169`). |
| `false` during fallback preview | If cached preview is missing, the backup calculation still checks LoS. A blocked fallback preview mimics the same zeroed stats (`SquadOfSteelCombatPatches.cs:85`). |
| `false` during resolution | `ResolveOutcome` immediately returns a miss with roll `1.0` to ensure downstream logic treats it as a miss even if HP would have changed due to animation side effects (`SquadOfSteelCombatRuntime.cs:75`). |
| `true` but damage zero | Damage roll proceeds but results are tempered by distance/suppression modifiers. |

Because LoS is captured in `CombatPreview.HasLineOfSight`, every downstream consumer—tooltips, overlay, debug logs—can echo the state without re-calculating geometry.

## Player Feedback & Telemetry

| Surface | LoS Messaging |
| --- | --- |
| **Combat notification** | `ValidateDirectFire` builds a user-facing string: “Line of sight blocked by {tile.enumType}”, delivered via `Notification` (`SquadOfSteelCombatRuntime.cs:193`). |
| **Debug overlay** | Entries include “LoS: clear/blocked” inside the multi-line summary that the overlay renders (`CombatDebugReporter.cs:19`). |
| **Console logs** | Unity log mirrors the overlay text with the `[SquadOfSteel]` prefix for easy filtering. |
| **Squad panel** | When the placeholder panel is toggled, it prints LoS state for the selected attacker/target to the console (`Scripts/SquadOfSteelUI.cs:58`). |

### Troubleshooting Tips

- When LoS appears “always true”, verify that the attacking unit qualifies as indirect fire; test with a known direct-fire unit (e.g., tanks with `FilterTank`).
- To validate terrain blockers, temporarily enable `SquadCombatRuntime.DebugEnabled` (press `F9` in-game) and inspect overlay entries for the LoS status and blocking tile reports.
- During load sequences, a warning (`"[SquadOfSteel][Overlay] Entry queued; overlay not yet ready."`) can accompany queued debug entries; once the canvas is live, LoS results will display retroactively.

## Extensibility Notes

- Adding new blocking terrain is as simple as updating the `s_blockingTiles` array. Remember to also adjust any art assets or map generation to indicate the obstruction visually.
- To support partial cover, consider expanding `ComputeHitChance` instead of hard-blocking LoS; the dedicated blocking array currently reflects “full wall” tiles.
- If you introduce per-unit LoS modifiers (e.g., scouts that can see through forests), extend `HasLineOfSight` with unit-specific checks before the blocker loop.

## Call Graph Snapshot

```
UnitGO.AttackUnit
 └─ SquadCombatRuntime.ValidateDirectFire
     └─ LineOfSightService.HasLineOfSight
         ├─ HexGridHelper.GetDistance
         └─ HexGridHelper.GetLine

Unit.GetPotentialDamage (postfix)
 └─ LineOfSightService.HasLineOfSight → CombatPreview.HasLineOfSight

SquadCombatRuntime.ResolveOutcome
 └─ Uses CombatPreview.HasLineOfSight to auto-miss
```

Understanding this flow makes it easier to reason about why a shot is disallowed, how the UI reports it, and where to intervene when balancing or debugging LoS behaviour.
