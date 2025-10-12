# Squad of Steel Combat Computation Guide

This reference explains how the Squad of Steel mod evaluates combat, derives suppression, and emits debugging output. Use it while tuning balance numbers, extending the overlay, or interpreting logs.

## Runtime Flow

1. **Potential damage hook** - `Unit.GetPotentialDamage` is patched to compute a `CombatPreview` and to cache it by attacker/defender id pair (`CombatKey`). The patched value becomes the unit's advertised damage before the attack actually fires (`Scripts/Combat/SquadOfSteelCombatPatches.cs:21`).
2. **Pre-attack guard** - `UnitGO.AttackUnit` and `UnitGO.Retaliate` call `SquadCombatRuntime.ValidateDirectFire` to reject blocked shots and notify the player (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:169`).
3. **Resolution** - At the start of `UnitGO.Attack` the cached preview is consumed, an outcome is rolled, and final damage is written back to the attacker (`Scripts/Combat/SquadOfSteelCombatPatches.cs:78`).
4. **After effects** - Postfix logic adjusts suppression, updates indicators, and reports debugging telemetry (`Scripts/Combat/SquadOfSteelCombatPatches.cs:118`).
5. **Turn maintenance** - Each `TurnManager.NextTurn` decay pass saves state and refreshes suppression indicators (`Scripts/Combat/SquadOfSteelCombatPatches.cs:180`).

## Cached Combat Snapshots

### `CombatPreview`

| Field | Source | Notes |
| --- | --- | --- |
| `BaseDamage` | `Unit.FinalDamage` before modifiers | Snapshot of the game's raw soft damage (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:241`). |
| `DamageOnHit` | `ComputeDamageOnHit` result | Post-distance and suppression scaling (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:361`). |
| `ExpectedDamage` | `DamageOnHit * HitChance` | Rounded to nearest int for UI predictions. |
| `HitChance` | `ComputeHitChance` result | Float clamped between 0.05 and 0.95. |
| `HasLineOfSight` | `LineOfSightService.HasLineOfSight` | Includes terrain and unit blockers. |
| `Distance` | Hex distance from attacker to defender | Uses cube-coordinate hex math. |
| `AttackerSuppression` / `TargetSuppression` | `SquadOfSteelSuppression.Get` | Stored so post-resolution deltas can be shown. |
| `IsRetaliation` / `IsSupport` | Harmony postfix flags | Informs hit chance penalties. |

### `CombatOutcome`

| Field | Populated When | Purpose |
| --- | --- | --- |
| `Hit` | After actual HP delta is measured | Guards against animation-only hits. |
| `Damage` | `TargetHpBefore - TargetHpAfter` | Real damage dealt after safeguards. |
| `HitChance`, `Roll` | During resolution | `Roll` is `Random.value`, used for debug. |
| `DamageOnHit`, `BaseDamage`, `ExpectedDamage` | Copied from preview | Supports overlay comparisons. |
| `HasLineOfSight`, `Distance`, retaliation/support flags | Copied from preview | Provide context in debug rows. |
| `AttackerSuppressionBefore/After`, `TargetSuppressionBefore/After` | Pre/post suppression values | Highlight suppression swings. |
| `TargetHpBefore/After` | Around `UnitGO.Attack` postfix | Enables true damage calculation. |

## Hit Chance Calculation

`chance = clamp(0.78 + sum of modifiers, 0.05, 0.95)` (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:314`).

| Modifier | Condition | Adjustment |
| --- | --- | --- |
| Melee bonus | Distance <= 1 | +0.05 |
| Range falloff | Each hex beyond 1 | -0.10 * (distance - 1) |
| Terrain cover penalty | Tile type in `{Forest, Mountain, City, Trench, Hill, Marsh, Harbour, Factory}` | Subtract per-table penalty (e.g., City -0.25). |
| Tank targeting | Attacker `FilterType == "FilterTank"` | +0.05 |
| Infantry bracing | Attacker `FilterType == "FilterInfantry"` and distance <= 2 | +0.04 |
| Retaliation | `isRetaliation == true` | -0.10 |
| Support fire | `isSupport == true` | -0.05 |
| Attacker suppression | `attackerSuppression` (0-100) | -0.45 * clamp01(suppression / 100) |
| Target suppression | `targetSuppression` (0-100) | +0.25 * clamp01(suppression / 100) |
| Line of sight | `HasLineOfSight == false` | Force 0% hit chance upstream. |

All modifiers stack additively before clamping, so large penalties can bottom out at 5%.

## Damage on Hit & Expectations

`damage = round(baseDamage * rangeScaling * targetSuppBoost)` with zero floor (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:361`).

| Step | Formula | Notes |
| --- | --- | --- |
| Range scaling | `rangeScaling = clamp01(1 - 0.08 * (distance - 1))` | Each extra hex past melee removes 8% up to 100%. |
| Suppression boost | `targetSuppBoost = 1 + clamp01(targetSupp / 100) * 0.35` | Up to +35% damage against heavily suppressed targets. |
| Expected damage | `ExpectedDamage = round(DamageOnHit * HitChance)` | Used for tooltips and AI previews. |
| Miss or no LoS | If `HasLineOfSight == false` or `DamageOnHit <= 0` | Outcome forced to miss with zero damage (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:75`). |
| Random spread | On actual hit | Final damage multiplied by random 0.85-1.15, then rounded (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:99`). |

## Suppression Lifecycle

| Event | Change | Implementation |
| --- | --- | --- |
| Direct hit with damage | Target +30, attacker -8 | `ApplySuppression` postfix (`Scripts/Combat/SquadOfSteelCombatPatches.cs:126`). |
| On-target miss (clear LoS, damage potential > 0) | Target +12 | Encourages repeated fire even when missing (`Scripts/Combat/SquadOfSteelCombatPatches.cs:134`). |
| Turn advance | All units -15 (floor at 0) | `SquadOfSteelSuppression.DecayAll` (`Scripts/Combat/SquadOfSteelSuppression.cs:99`). |
| Unit destroyed | Suppression cleared | Cleanup postfix (`Scripts/Combat/SquadOfSteelCombatPatches.cs:164`). |
| Manual decay | `Reduce` / `Clear` helpers | Called when removing suppression proactively (`Scripts/Combat/SquadOfSteelSuppression.cs:63`). |

Suppression values stay within 0-100. When a record reaches zero it is removed from the cache and future lookups become free.

## Attack Resolution Sequence

1. **Preview retrieval** - Pre-computed preview is consumed; if missing, a fallback preview is synthesised using current board state (`Scripts/Combat/SquadOfSteelCombatPatches.cs:87`).
2. **Outcome roll** - Line-of-sight, hit chance, and damage roll produce a `CombatOutcome`. The attacker's `FinalDamage` is overwritten with the rolled damage so the base game consumes the right amount (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:99`).
3. **HP reconciliation** - Postfix compares HP before/after and corrects stray animation damage on declared misses (`Scripts/Combat/SquadOfSteelCombatPatches.cs:118`).
4. **Suppression + indicators** - Suppression adjustments, badge refresh, and cache eviction run (`Scripts/Combat/SquadOfSteelCombatPatches.cs:140`).

## Line of Sight Interaction

Line-of-sight (LoS) controls both whether a shot may be taken and how the combat pipeline interprets the attempt. The dedicated LoS guide dives into the geometry; this section summarises the parts that directly affect combat.

### LoS Determination Steps

| Phase | Details | Source |
| --- | --- | --- |
| Classify indirect fire | `LineOfSightService.IsIndirectFire` flags planes, artillery, bombers, CAS, and units with bombs or rockets so they always return LoS true (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:330`). |
| Distance and adjacency | Shared tiles or distance <= 1 auto-succeed so melee units never hang on LoS checks. |
| Hex ray casting | `HexGridHelper.GetLine` builds a cube-space ray; intermediate tiles are inspected for blockers (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:409`). |
| Unit obstruction | Any `tileGO` hosting ground or air units stops LoS and produces a blocking tile reference. |
| Terrain obstruction | Tiles in `{Forest, Mountain, City, Trench, Factory, Hill}` block LoS outright; Marsh and Harbour only penalise hit chance. |
| Null map guard | When the map is unavailable (loading), the method returns true so the game does not deadlock scripted attacks. |

### Combat Consequences

| Situation | Effect | Implementation |
| --- | --- | --- |
| LoS fails during validation | `SquadCombatRuntime.ValidateDirectFire` returns false, cancelling `UnitGO.AttackUnit` and queuing a user-facing notification (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:169`). |
| LoS fails during preview | Hit chance and damage on hit are zeroed before the preview is cached, so UI and AI see a blocked shot (`Scripts/Combat/SquadOfSteelCombatPatches.cs:47`). |
| LoS fails during fallback preview | The emergency preview used inside `UnitGO.Attack` repeats the LoS check, maintaining identical behaviour (`Scripts/Combat/SquadOfSteelCombatPatches.cs:85`). |
| LoS fails during resolution | `SquadCombatRuntime.ResolveOutcome` returns an auto-miss (`Hit = false`, `Roll = 1.0f`) with zero damage (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:75`). |

Because `CombatPreview.HasLineOfSight` is stored with each attack, every downstream consumer (debug overlay, combat logs, suppression logic, squad panel) can react without recalculating geometry.

### Feedback Surfaces

| Surface | LoS Messaging |
| --- | --- |
| Notification | "Line of sight blocked by {tile.enumType}" when a direct shot is rejected. |
| Debug overlay | Multi-line entry includes "LoS: clear/blocked" alongside hit chance and roll (`Scripts/Combat/CombatDebugReporter.cs:19`). |
| Console log | Overlay text mirrored with the `[SquadOfSteel]` prefix for easy filtering. |
| Squad panel | Console briefing prints LoS state for the selected attacker/target pair (`Scripts/SquadOfSteelUI.cs:58`). |

Toggle debug mode with `F9` to monitor these outputs while iterating on LoS rules. For a full geometry breakdown, see `guides/line-of-sight-computation-guide.md`.

## Debugging & Telemetry Surfaces

| Surface | Trigger / Toggle | What You See | Implementation |
| --- | --- | --- | --- |
| Unity log | Always on | Detailed multi-line combat breakdown per attack | `CombatDebugReporter.Report` (`Scripts/Combat/CombatDebugReporter.cs:13`). |
| Overlay panel | Toggle with `F9` (debug on) | Scrollable list of recent combats, docked left | `CombatDebugOverlay` (`Scripts/Combat/CombatDebugOverlay.cs:18`). |
| Toast popup | Needs `UIManager` instance | Short summary (`Attacker -> Target: HIT 72% / roll 0.41`) | `UIManager.ShowMessage` call in reporter (`Scripts/Combat/CombatDebugReporter.cs:45`). |
| Map notification | When attacker owner is a human | Notification pinned to attacker tile with full debug text | `Notification` created in reporter (`Scripts/Combat/CombatDebugReporter.cs:36`). |
| Suppression badge | Always on once indicator attaches | Ring plus numeric badge next to unit counter, colour-coded by suppression level | `SquadOfSteelSuppressionIndicator` (`Scripts/Combat/SquadOfSteelSuppressionIndicator.cs:12`). |

Debug visibility persists across sessions via `SquadOfSteelState.SetCombatDebug`, so disabling with `F9` sticks (`Scripts/SquadOfSteelState.cs:120`).

## Keybinds & UI Hooks

- `K` (default) toggles the placeholder Squad panel, which prints a console briefing for the selected unit and its current target (`Scripts/SquadOfSteelKeybindHandler.cs:14`, `Scripts/SquadOfSteelUI.cs:30`).
- `F9` toggles combat debug mode and overlay visibility (`Scripts/SquadOfSteelKeybindHandler.cs:16`).
- All key reads go through reflected `UnityEngine.Input.GetKeyDown`, so keep the Unity input module loaded (`Scripts/SquadOfSteelKeybindHandler.cs:58`).

## Persistence Notes

- Suppression values and the debug toggle are saved in `GameData.Instance.ModDataBag` under the key `SquadOfSteel.State` (`Scripts/SquadOfSteelState.cs:18`).
- Saves are deferred until both game data and mod references are ready; failed saves log a Unity error with the exception message (`Scripts/SquadOfSteelState.cs:64`).
- Suppression caches export/import as plain integer dictionaries keyed by unit id, clamped during load (`Scripts/Combat/SquadOfSteelSuppression.cs:26`).

## Extending or Troubleshooting

- When adding new hit modifiers, update both `ComputeHitChance` and the debug reporter so tables remain accurate.
- Use the overlay queue (max 50 pending entries) to buffer events that fire before the UI is ready (`Scripts/Combat/CombatDebugOverlay.cs:22`).
- After tweaking suppression, call `SquadOfSteelSuppressionIndicator.RefreshAll` to force repainting existing badges (`Scripts/Combat/SquadOfSteelSuppressionIndicator.cs:206`).

Refer back to the source files cited above whenever you need exact implementation details or plan to adjust coefficients.
