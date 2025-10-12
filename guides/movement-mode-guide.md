# Movement Mode Guide

This document covers the infantry movement-mode system introduced in **Squad Of Steel**. It explains how the feature is modelled, how calculations hook into combat, and how players interact with it in game.

## Concept Overview

- **Movement modes**: Every infantry-capable squad now has two operational states — `Combat` (default) and `Move`.
- **Move mode** represents troops hopping into their inherent transport to cover ground faster. It is strictly a per-unit toggle; no embark failures occur, and disembarking returns the squad to the original hex instantly.
- **Trade-offs**: Move mode grants bonus action points but makes the squad more vulnerable to direct fire, while also discouraging attacks launched from within transports.

## State Management

- Runtime data lives in `Scripts/SquadMovementRuntime.cs`. Modes are stored in a dictionary keyed by unit ID (`s_modes`), with a companion dictionary for temporary action-point buffs (`s_apBuffs`).
- Persistence: `Scripts/SquadOfSteelState.cs` serializes the `MovementModes` dictionary so save/load cycles restore every unit’s last known mode.
- On initialization (`SquadOfSteelState.Initialize`), the runtime receives the persisted snapshot and replays Move mode buffs/indicators.
- Cleanup occurs on unit destruction (`SquadOfSteelCombatPatches.CleanupOnDestroy`), guaranteeing no stale buffs or state remain after a squad is removed.

## Action Point Handling

- Entering Move mode applies a flat +3 AP boost (`MoveModeActionPointBonus`). The runtime records the grant per unit so it can reverse or reapply it exactly once per turn.
- When a unit flips back to Combat, the stored buff is subtracted, returning AP totals to the original baseline.
- If a save/load or turn advance occurs, `EnsureActionPointBuff` re-grants the bonus only if the current turn differs from the last application, preventing duplicate stacking.
- Alongside action points, Move mode also raises the unit's movement allowance by +3 (`MaxMP` and `CurrMP`). This widens the pathfinding radius immediately after the toggle, and the runtime reapplies the allowance each time the turn advances while the unit stays in Move mode.

## Combat Modifiers

Combat math integrates Move mode through small adjustments inside `SquadOfSteelCombatRuntime`:

- **Incoming fire**: Targets in Move mode take `+15%` hit chance (`IncomingHitChanceBonus`) and `+20%` damage (`IncomingDamageMultiplier`). The bonus is applied when either previewing or resolving direct fire.
- **Outgoing fire**: Units attacking while still in Move mode suffer `-12%` accuracy (`AttackerPenalty`). Before the combat actually resolves, Harmony forces them back into Combat mode to avoid transparent mis-synchronization with the vanilla engine.
- Suppression indicators automatically hide while a squad is in Move mode (`SquadOfSteelSuppressionIndicator.Refresh`). When the unit returns to Combat mode the indicator reactivates and refreshes suppression values.

## Player Interaction

- **Toggle key**: The keybind handler (`Scripts/SquadOfSteelKeybindHandler.cs`) assigns `V` as the default toggle. Pressing `V` with an infantry unit selected switches modes instantly.
- **Console feedback**: Toggling logs a confirmation (`[SquadOfSteel] {unit} switched to MOVE/COMBAT mode`). The Squad panel (`Scripts/SquadOfSteelUI.cs`) displays current mode, the toggle hint, and a short reminder of bonuses/penalties.
- **Automatic safeguards**: If a player orders an attack while still in Move mode, the mod automatically reverts the unit to Combat mode before the shot resolves, ensuring combat values align with the vulnerability penalties.
- **Indicators**: Suppression badges disappear while in Move mode (mimicking transport embark), and a small truck icon appears above the counter. On return to Combat mode the badge and icon revert automatically via `SquadMovementRuntime.Resync`.

## Eligibility Rules

- Currently, Move mode is available to any unit whose `FilterType` or `Type` string contains `"Infantry"`. This is a temporary heuristic until we enumerate all infantry archetypes from the base game.
- Units that fail the eligibility check receive a quick “cannot enter move mode” notification and remain in Combat mode.

## Extending the System

- **Transport visuals**: At present only suppression indicators hide; the vanilla transport graphics remain unchanged. Future work could spawn dedicated transport overlays or change unit sprites when in Move mode.
- **Suppression sharing**: Transports do not yet accumulate suppression. If the design pivots, `SquadMovementRuntime` can relay hits to the passenger squad’s suppression pool when in Move mode.
- **Squad-wide toggles**: Currently toggling is per-unit. For multi-unit squads, a helper could walk group rosters and call `TrySetMode` in sequence, applying rollback logic if any member fails the check.

---

**Summary**: Movement mode adds a controlled risk/reward layer for infantry repositioning. The runtime tracks mode state, action-point buffs, and combat modifiers, while the UI and suppression systems reflect the squad’s current stance to the player. Use `V` to embark, redeploy rapidly, and disembark before contact to avoid the vulnerability penalties.
