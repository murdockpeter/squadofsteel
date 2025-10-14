# Movement Mode Guide

This document covers the infantry movement-mode system introduced in **Squad Of Steel**. It explains how the feature is modelled, how transport swapping works, how calculations hook into combat, and how players interact with it in game.

## Concept Overview

- **Movement modes**: Every infantry-capable squad now has two operational states — `Combat` (default) and `Move`.
- **Move mode** represents troops mounting their organic transport vehicles to cover ground faster. Units visually transform into their designated carrier vehicle (e.g., Light Infantry → Daimler Dingo for British forces).
- **Transport swapping**: The mod maintains two snapshots per unit — infantry and carrier forms. When toggling modes, the unit's stats (HP, XP, ammo, fuel, veterancy) are preserved and transferred between forms using resource ratios.
- **Trade-offs**: Move mode grants bonus action points and movement but makes the squad more vulnerable to direct fire, while also discouraging attacks launched from within transports.

## Transport Mapping System

### Configuration

Transport mappings are defined in `Assets/transport-mappings.json`:

```json
{
  "Light infantry": "Daimler Dingo",
  "Engineers": "Daimler Dingo",
  "Commandos": "Daimler Dingo",
  "Rangers": "M3 Scout Car",
  "Panzergrenadier": "Sd. Kfz. 251 9 Stummel"
}
```

**Critical Requirement**: Carrier vehicles must exist for the faction playing that infantry type. The game has faction-specific unit rosters, so:
- **British/Commonwealth**: Use "Daimler Dingo", "Humber Armored Car Mk. III/IV", or "Daimler Armoured Car"
- **American**: Use "M3 Scout Car", "M3 Halftrack", or "Willys MB"
- **Soviet**: Use "ZiS-5 x2 DshK" or similar Soviet trucks
- **German**: Use "Sd. Kfz. 251 9 Stummel" or similar German halftracks
- **Japanese**: Use "Type 94 TK" or "Type 97 Chi-Ha"

If a carrier vehicle doesn't exist for a faction, the mod falls back to legacy movement buffs (stat bonuses without visual transformation).

### Prototype Resolution

The runtime (`SquadMovementRuntime.cs`) loads unit definitions from the game's official unit catalog:
1. On first toggle, the mod searches for both infantry and carrier prototypes by name
2. Prototypes are cloned and cached in the transport mapping
3. If a prototype is missing, a warning is logged and the mapping is disabled for that unit

## State Management

- **Transport states**: Each unit with a valid transport mapping has a `TransportState` object storing:
  - Infantry snapshot (base stats for foot infantry form)
  - Carrier snapshot (base stats for transport form)
  - Current form (Combat or Move)
  - Mapping reference (links to infantry/carrier prototypes)

- **Snapshot synchronization**: When toggling modes:
  1. The unit's current stats are captured in the "from" snapshot
  2. Persistent state (XP, kills, veterancy, custom names) is copied to the "to" snapshot
  3. Resources (HP, ammo, fuel, MP) are converted by ratio (e.g., 50% HP infantry → 50% HP carrier)
  4. The unit synchronizes from the "to" snapshot, visually transforming and updating stats

- **Persistence**: `SquadOfSteelState.cs` serializes the `MovementModes` dictionary (unit ID → mode) so save/load cycles restore every unit's last known mode. Transport states are reconstructed on demand when units toggle modes.

- **Cleanup**: On unit destruction, all tracking data (modes, buffs, transport states) are cleared to prevent memory leaks.

## Snapshot Preservation

The mod carefully preserves unit state across mode changes:

### Persistent State (Always Preserved)
- Custom names and forced names
- Owner and faction
- Hero status
- Experience points and level
- Kill statistics (total, tanks, infantry, aircraft, etc.)
- Combat state (attacked this turn, has moved, etc.)
- Veterancy bonuses (elite unit, winter specialized, mountaineer, etc.)
- Core unit and reserve flags
- Morale and entrenchment

### Resources (Converted by Ratio)
- **HP**: Infantry at 50% HP → Carrier at 50% HP (absolute values differ, percentage preserved)
- **Movement Points**: Converted by MaxMP ratio, plus Move mode bonus
- **Ammo**: Converted by MaxAmmo ratio
- **Fuel**: Converted by MaxAutonomy ratio

### Base Stats (Replaced by Target Form)
- Attack, defense, range, armor
- Max HP, max ammo, max fuel, max MP
- Unit type, filter type, movement class
- Cost, icon, sprite

This ensures that a veteran infantry squad with 3 kills and 75% HP becomes a veteran carrier with 3 kills and 75% HP (relative to carrier max HP).

## Action Point & Movement Handling

### Legacy Mode (No Transport Mapping)
- Flat +3 AP boost (`MoveModeActionPointBonus`)
- +3 movement points (`MoveModeMovementBonus`)
- Runtime records the grant per unit to reverse it when exiting Move mode
- `EnsureActionPointBuff` and `EnsureMovementBuff` prevent duplicate stacking across turns/saves

### Transport Swap Mode (With Valid Mapping)
- Action points and movement come from the carrier unit's base stats
- Resource ratios preserve current MP relative to max
- No explicit bonuses needed — the carrier vehicle naturally has higher MaxMP and ActionPoints than infantry

## Combat Modifiers

Combat math integrates Move mode through adjustments inside `SquadCombatRuntime`:

- **Incoming fire**: Targets in Move mode take `+15%` hit chance (`IncomingHitChanceBonus`) and `+20%` damage (`IncomingDamageMultiplier`). The bonus is applied when either previewing or resolving direct fire.
- **Outgoing fire**: Units attacking while still in Move mode suffer `-12%` accuracy (`AttackerPenalty`).
- Suppression indicators automatically hide while a squad is in Move mode (`SquadOfSteelSuppressionIndicator.Refresh`). When the unit returns to Combat mode the indicator reactivates and refreshes suppression values.

## Visual Indicators

- **Move mode indicator**: A small truck icon appears above the unit counter when in Move mode (`SquadMoveModeIndicator.cs`)
- **Suppression badges**: Hidden during Move mode (mimicking the vulnerability of being in transport)
- **Unit sprite**: The counter visually transforms to show the carrier vehicle sprite, updated via `UnitGO.SetSprite()` and `UnitGO.ManageTwoUnitsIndicator()`
- **Movement overlay**: Automatically refreshes when toggling modes via `RefreshMovementOverlay()`, showing the new movement range

## Player Interaction

- **Toggle key**: The keybind handler (`Scripts/SquadOfSteelKeybindHandler.cs`) assigns `V` as the default toggle. Pressing `V` with an infantry unit selected switches modes instantly.
- **Console feedback**: Toggling logs a confirmation (`[SquadOfSteel] {unit} switched to MOVE/COMBAT mode`).
- **Automatic safeguards**:
  - If a mapping is invalid or the carrier can't be found, the unit uses legacy movement buffs instead
  - Mode changes are guarded against infinite recursion via `s_activelyChangingMode` tracking
  - `Resync()` calls during Unity's update cycle are prevented when mode changes are in progress

## Eligibility Rules

Infantry units can enter Move mode if:
1. A valid transport mapping exists AND both infantry/carrier prototypes can be loaded, OR
2. The unit's `FilterType` or `Type` string contains `"Infantry"` (legacy fallback)

Units that fail both checks receive a "cannot enter move mode" notification and remain in Combat mode.

## Technical Implementation

### Key Functions

- **`TrySetMode()`**: Main entry point for mode changes, validates eligibility and calls Enter/ExitMoveMode
- **`EnterMoveMode()`**: Applies transport swap or legacy buffs, hides suppression, shows truck icon, refreshes overlay
- **`ExitMoveMode()`**: Reverts transport swap or removes legacy buffs, shows suppression, hides truck icon
- **`TryApplyTransportSwap()`**: Handles the actual snapshot swapping logic
- **`EnsureTransportState()`**: Creates/retrieves the TransportState for a unit, initializing snapshots if needed
- **`UpdateCarrierSnapshotFromInfantry()`**: Builds carrier snapshot from infantry state when entering Move mode
- **`UpdateInfantrySnapshotFromCarrier()`**: Restores infantry snapshot from carrier state when exiting Move mode
- **`CopyPersistentState()`**: Transfers all non-resource stats (XP, kills, names, etc.) between snapshots
- **`ApplyResourceRatios()`**: Converts HP/ammo/fuel/MP by ratio to preserve percentage-based state
- **`Resync()`**: Called by Harmony patches during `UnitGO.UpdateCounter()` to ensure visual state matches mode

### Recursion Prevention

The mod uses a `HashSet<int> s_activelyChangingMode` to track units currently toggling modes:
- `EnterMoveMode()` and `ExitMoveMode()` add the unit ID before operations, remove it in a `finally` block
- `Resync()` checks this set and exits early if a mode change is already in progress
- This prevents infinite loops when Unity's visual update cycle triggers `UpdateCounter()` → `Resync()` during mode transitions

### Edge Cases Handled

1. **First toggle**: If no transport state exists, one is created by cloning prototypes and establishing initial snapshots
2. **Subsequent toggles**: Existing snapshots are reused and updated with current state, avoiding prototype overwrites
3. **Save/load**: Mode state persists via `SquadOfSteelState`, transport states are reconstructed on demand
4. **Missing prototypes**: Falls back to legacy movement buffs if carrier/infantry can't be found
5. **UpdateCounter recursion**: Prevented via `s_activelyChangingMode` tracking
6. **Already in desired mode**: Early exit without visual refresh to avoid triggering update loops

## Extending the System

### Faction-Specific Mappings
The current system uses a single global mapping file. To support faction-aware mappings:
1. Extend `CarrierMapping` to include faction filters
2. Modify `TryGetMappingForUnit()` to check unit owner against mapping factions
3. Allow multiple mappings per infantry type, selecting by faction match

### Additional Transport Types
To add new carrier vehicles:
1. Identify the exact unit name in-game (use `ExportOfficialUnitNames.ps1`)
2. Add the infantry → carrier mapping to `transport-mappings.json`
3. Ensure the carrier exists for all factions that use that infantry type

### Custom Movement Bonuses
To adjust Move mode bonuses:
- Modify constants: `MoveModeActionPointBonus`, `MoveModeMovementBonus`, `MoveModeIncomingHitChanceBonus`, `MoveModeIncomingDamageMultiplier`, `MoveModeAttackPenalty`
- These affect both legacy mode and transport swap mode

---

**Summary**: The movement mode system provides tactical infantry repositioning through organic transport. Units visually transform between infantry and carrier forms, preserving all veteran status and resources while gaining mobility at the cost of vulnerability. Use `V` to embark, redeploy rapidly, and disembark before contact to avoid the defensive penalties. Configure faction-appropriate carriers in `transport-mappings.json` for proper visual representation.
