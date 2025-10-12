# Squad of Steel Combat Computation Guide

This reference explains how the Squad of Steel mod evaluates combat, derives suppression, and emits debugging output. Use it while tuning balance numbers, extending the overlay, or interpreting logs.

## Runtime Flow

1. **Potential damage hook** – `Unit.GetPotentialDamage` is patched to compute a `CombatPreview` and to cache it by attacker/defender id pair (`CombatKey`). The patched value becomes the unit's advertised damage before the attack actually fires (`Scripts/Combat/SquadOfSteelCombatPatches.cs:21`).
2. **Pre-attack guard** – `UnitGO.AttackUnit` and `UnitGO.Retaliate` call `SquadCombatRuntime.ValidateDirectFire` to reject blocked shots and notify the player (`Scripts/Combat/SquadOfSteelCombatRuntime.cs:169`).
3. **Resolution** – At the start of `UnitGO.Attack` the cached preview is consumed, an outcome is rolled, and final damage is written back to the attacker (`Scripts/Combat/SquadOfSteelCombatPatches.cs:78`).
4. **After effects** – Postfix logic adjusts suppression, updates indicators, and reports debugging telemetry (`Scripts/Combat/SquadOfSteelCombatPatches.cs:118`).
5. **Turn maintenance** – Each `TurnManager.NextTurn` decay pass saves state and refreshes suppression indicators (`Scripts/Combat/SquadOfSteelCombatPatches.cs:180`).

## Cached Combat Snapshots

### `CombatPreview`

| Field | Source | Notes |
| --- | --- | --- |
| `BaseDamage` | `Unit.FinalDamage` before modifiers | Snapshot of the game's raw soft damage (`SquadOfSteelCombatRuntime.cs:241`). |
| `DamageOnHit` | `ComputeDamageOnHit` result | Post-distance and suppression scaling (`SquadOfSteelCombatRuntime.cs:361`). |
| `ExpectedDamage` | `DamageOnHit * HitChance` | Rounded to nearest int for UI predictions. |
| `HitChance` | `ComputeHitChance` result | Float clamped between 5–95%. |
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
| `DamageOnHit`, `BaseDamage`, `ExpectedDamage` | Copied from preview | Support overlay comparisons. |
| `HasLineOfSight`, `Distance`, retaliation/support flags | Copied from preview | Provide context in debug rows. |
| `AttackerSuppressionBefore/After`, `TargetSuppressionBefore/After` | Pre/post suppression values | Highlight suppression swings. |
| `TargetHpBefore/After` | Around `UnitGO.Attack` postfix | Enables true damage calculation. |

## Hit Chance Calculation

`chance = clamp(0.78 + Σ modifiers, 0.05, 0.95)` (`SquadOfSteelCombatRuntime.cs:314`).

| Modifier | Condition | Adjustment |
| --- | --- | --- |
| Melee bonus | Distance ≤ 1 | +0.05 |
| Range falloff | Each hex beyond 1 | −0.10 × (distance − 1) |
| Terrain cover penalty | Tile type in `{Forest, Mountain, City, Trench, Hill, Marsh, Harbour, Factory}` | Subtract per-table penalty (e.g., City −0.25). |
| Tank targeting | Attacker `FilterType == "FilterTank"` | +0.05 |
| Infantry bracing | Attacker `FilterType == "FilterInfantry"` and distance ≤ 2 | +0.04 |
| Retaliation | `isRetaliation == true` | −0.10 |
| Support fire | `isSupport == true` | −0.05 |
| Attacker suppression | `attackerSuppression` (0–100) | −0.45 × clamp01(suppression / 100) |
| Target suppression | `targetSuppression` (0–100) | +0.25 × clamp01(suppression / 100) |
| Line of sight | `HasLineOfSight == false` | Force 0% hit chance upstream. |

All modifiers stack additively before clamping, so large penalties can bottom out at 5%.

## Damage on Hit & Expectations

`damage = round( baseDamage × rangeScaling × targetSuppBoost )` with zero floor (`SquadOfSteelCombatRuntime.cs:361`).

| Step | Formula | Notes |
| --- | --- | --- |
| Range scaling | `rangeScaling = clamp01(1 − 0.08 × (distance − 1))` | Each extra hex past melee removes 8% up to 100%. |
| Suppression boost | `targetSuppBoost = 1 + clamp01(targetSupp/100) × 0.35` | Up to +35% damage against heavily suppressed targets. |
| Expected damage | `ExpectedDamage = round(DamageOnHit × HitChance)` | Used for tooltips and AI previews. |
| Miss or no LoS | If `HasLineOfSight == false` or `DamageOnHit <= 0` | Outcome forced to miss with zero damage (`SquadOfSteelCombatRuntime.cs:75`). |
| Random spread | On actual hit | Final damage multiplied by random 0.85–1.15, then rounded (`SquadOfSteelCombatRuntime.cs:99`). |

## Suppression Lifecycle

| Event | Change | Implementation |
| --- | --- | --- |
| Direct hit with damage | Target +30, attacker −8 | `ApplySuppression` postfix (`SquadOfSteelCombatPatches.cs:126`). |
| On-target miss (clear LoS, damage potential > 0) | Target +12 | Encourages repeated fire even when missing (`SquadOfSteelCombatPatches.cs:134`). |
| Turn advance | All units −15 (floor at 0) | `SquadOfSteelSuppression.DecayAll` (`SquadOfSteelSuppression.cs:99`). |
| Unit destroyed | Suppression cleared | Cleanup postfix (`SquadOfSteelCombatPatches.cs:164`). |
| Manual decay | `Reduce` / `Clear` helpers | Called when removing suppression proactively (`SquadOfSteelSuppression.cs:63`). |

Suppression values stay within 0–100. When a record reaches zero it is removed from the cache and future lookups become free.

## Attack Resolution Sequence

1. **Preview retrieval** – Pre-computed preview is consumed; if missing, a fallback preview is synthesised using current board state (`SquadOfSteelCombatPatches.cs:87`).
2. **Outcome roll** – Line-of-sight, hit chance, and damage roll produce a `CombatOutcome`. The attacker's `FinalDamage` is overwritten with the rolled damage so the base game consumes the right amount (`SquadOfSteelCombatRuntime.cs:99`).
3. **HP reconciliation** – Postfix compares HP before/after and corrects stray animation damage on declared misses (`SquadOfSteelCombatPatches.cs:118`).
4. **Suppression + indicators** – Suppression adjustments, badge refresh, and cache eviction run (`SquadOfSteelCombatPatches.cs:140`).

## Line of Sight Gating

| Check Order | Description | Notes |
| --- | --- | --- |
| Indirect fire bypass | Artillery, bombers, CAS, planes, or units with bombs/rockets skip all checks (`LineOfSightService.IsIndirectFire`) | Allows arcing shots (`SquadOfSteelCombatRuntime.cs:330`). |
| Same-tile & adjacent | If attacker and target are the same or within 1 hex, LoS is considered clear | Supports melee and shared-tile cases. |
| Hex line tracing | Generates cube-coordinate ray between tiles | Uses `HexGridHelper.GetLine` for map-aware sampling (`SquadOfSteelCombatRuntime.cs:387`). |
| Unit blockers | Stops on any occupied intermediate tile | Prevents firing through friendlies or enemies. |
| Terrain blockers | Tiles in `{Forest, Mountain, City, Trench, Factory, Hill}` block LoS | Marsh/harbour only penalise hit chance. |
| Failure path | Returns blocking tile for user notification | `ValidateDirectFire` surfaces the tile name in notifications. |

## Debugging & Telemetry Surfaces

| Surface | Trigger / Toggle | What You See | Implementation |
| --- | --- | --- | --- |
| Unity log | Always on | Detailed multi-line combat breakdown per attack | `CombatDebugReporter.Report` (`CombatDebugReporter.cs:13`). |
| Overlay panel | Toggle with `F9` (debug on) | Scrollable list of recent combats, docked left | `CombatDebugOverlay` (`CombatDebugOverlay.cs:18`). |
| Toast popup | Needs `UIManager` instance | Short summary (`Attacker -> Target: HIT 72% / roll 0.41`) | `UIManager.ShowMessage` call in reporter (`CombatDebugReporter.cs:45`). |
| Map notification | When attacker owner is a human | Notification pinned to attacker tile with full debug text | `Notification` created in reporter (`CombatDebugReporter.cs:36`). |
| Suppression badge | Always on once indicator attaches | Ring + numeric badge next to unit counter, colour-coded by suppression level | `SquadOfSteelSuppressionIndicator` (`SquadOfSteelSuppressionIndicator.cs:12`). |

Debug visibility persists across sessions via `SquadOfSteelState.SetCombatDebug`, so disabling with `F9` sticks (`SquadOfSteelState.cs:120`).

## Keybinds & UI Hooks

- `K` (default) toggles the placeholder Squad panel, which prints a console briefing for the selected unit and its current target (`SquadOfSteelKeybindHandler.cs:14` and `SquadOfSteelUI.cs:30`).
- `F9` toggles combat debug mode and overlay visibility (`SquadOfSteelKeybindHandler.cs:16`).
- All key reads go through reflected `UnityEngine.Input.GetKeyDown`, so keep the Unity input module loaded (`SquadOfSteelKeybindHandler.cs:58`).

## Persistence Notes

- Suppression values and the debug toggle are saved in `GameData.Instance.ModDataBag` under the key `SquadOfSteel.State` (`SquadOfSteelState.cs:18`).
- Saves are deferred until both game data and mod references are ready; failed saves log a Unity error with the exception message (`SquadOfSteelState.cs:64`).
- Suppression caches export/import as plain integer dictionaries keyed by unit id, clamped during load (`SquadOfSteelSuppression.cs:26`).

## Extending or Troubleshooting

- When adding new hit modifiers, update both `ComputeHitChance` and the debug reporter so tables remain accurate.
- Use the overlay's queue (max 50 pending entries) to buffer events that fire before the UI is ready (`CombatDebugOverlay.cs:22`).
- After tweaking suppression, call `SquadOfSteelSuppressionIndicator.RefreshAll` to force repainting existing badges (`SquadOfSteelSuppressionIndicator.cs:206`).

Refer back to the source files cited above whenever you need exact implementation details or plan to adjust coefficients.
