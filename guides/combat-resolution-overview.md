# Combat Resolution System Overview

This document summarizes how the Squad of Steel mod intercepts and resolves combat events once a player issues an attack order. Use it to understand the runtime flow, the data structures that capture combat state, and the points where suppression, line of sight, and movement status influence the final outcome.

## Attack Lifecycle

1. **Preview capture** – The `Unit.GetPotentialDamage` postfix (see `Scripts/Combat/SquadOfSteelCombatPatches.cs`) computes a `CombatPreview` keyed by attacker/defender ids. This snapshot stores raw `FinalDamage`, contextual flags (retaliation/support), suppression totals, distance, line-of-sight status, and the derived hit and damage expectations.
2. **Direct-fire gating** – Before any projectile is spawned, `SquadCombatRuntime.ValidateDirectFire` ensures the attacker has line of sight unless the unit is categorized as indirect fire (`LineOfSightService.IsIndirectFire`). Blocks generate an on-map player notification and cancel the attack animation.
3. **Resolution roll** – At the start of `UnitGO.Attack`, the cached preview is consumed. `SquadCombatRuntime.ResolveOutcome` rolls a uniform `Random.value`, compares it to the stored hit chance, and applies a ±15% damage spread when the shot connects. The rolled `CombatOutcome` is written back to the attacker via `unit.FinalDamage`.
4. **Post-hit reconciliation** – The Attack postfix cross-checks actual HP loss against the predicted hit flag, fixes stray animation damage when a miss is reported, updates suppression, refreshes indicators, and forwards the outcome to `CombatDebugReporter`.
5. **Turn maintenance** – `TurnManager.NextTurn` triggers suppression decay, saves serialized suppression state, refreshes overlays, and resets per-unit combat caches.

## Core Calculations

### Hit Chance

`SquadOfSteelCombatMath.ComputeHitChance` starts from a 78% baseline and applies additive modifiers before clamping the result to 5–95%. Major inputs include:

- **Distance** – +5% at melee range, −10% per hex beyond the first.
- **Cover** – Tile penalties up to −28% for trenches and −25% inside cities (see `s_coverPenalties` table).
- **Unit traits** – Tanks gain +5%; infantry within two hexes gain +4%; retaliation and support fire apply −10% and −5% respectively.
- **Suppression** – Attacker suppression can subtract up to 45%; target suppression can add up to 25%.
- **Movement mode** – Attacks initiated while moving incur `SquadMovementRuntime.AttackerPenalty`; targets caught moving gain both hit chance and damage adjustments.

If line of sight is lost, the hit chance is forced to zero upstream, guaranteeing a miss.

### Damage on Hit

`ComputeDamageOnHit` scales the base damage from the preview:

- Applies an 8% falloff per hex beyond range 1.
- Amplifies damage up to +35% based on target suppression.
- Multiplies damage when the defender is still flagged in `MovementMode.Move` via `SquadMovementRuntime.IncomingDamageMultiplier`.

The rolled outcome adds a ±15% spread to avoid deterministic results.

### Suppression Effects

`SquadOfSteelSuppression` tracks a 0–100 suppression meter per unit, persists it across turns, and passively decays 15 points each turn. Post-attack logic adds 30 suppression on successful hits (or 12 for near misses with line of sight) and reduces attacker suppression by 8 when the shot lands. These values flow back into future hit and damage calculations and drive the UI indicator refresh.

## Supporting Services

- **Line of Sight** – `LineOfSightService.HasLineOfSight` samples the hex line between attacker and defender, blocking on intervening units or terrain (`Forest`, `Mountain`, `City`, `Trench`, `Factory`, `Hill`). Indirect fire classifications (planes, artillery, bombers, rockets/CAS) bypass the check.
- **Combat state caches** – `SquadCombatRuntime` persists previews and outcomes keyed by attacker/defender ids for the duration of a turn, clearing entries once consumed or when a unit is destroyed.
- **Debug instrumentation** – `CombatDebugOverlay` and `CombatDebugReporter` render the stored preview/outcome fields (hit chance, roll, suppression deltas, damage expectations) to help tune balance or diagnose irregularities.
- **Movement coupling** – `SquadMovementRuntime` switches active attackers out of movement mode and resyncs counters post-attack, ensuring movement-derived penalties and bonuses remain in sync with the combat state machine.

## Data Persistence

Suppression values serialize through `SquadOfSteelState.Save()` so persistent campaigns retain morale pressure. Combat previews/outcomes live only in-memory, scoped to the current turn, and are flushed on unit destruction or turn advance. Random rolls rely on Unity’s global `Random.value`, meaning seeds are shared with other random events unless a dedicated RNG is introduced.

## Improvement Opportunities for Squad/Platoon Scale

1. **Formation-aware modifiers** – Introduce adjacency or formation bonuses that evaluate nearby friendly units (e.g., squad cohesion boosts hit chance when two friendly infantry occupy flanking tiles) and morale penalties when isolated. This would align counters with platoon tactics rather than single-unit duels.
2. **Focused fire and suppression spillover** – Track accumulated suppression from multiple attackers within a turn and escalate penalties once a threshold is reached, applying diminishing returns to repeated shots by the same unit but increasing impact when multiple squad members participate.
3. **Support weapon channels** – Differentiate support fire from direct fire by granting machine gun or mortar teams a persistent overwatch state, allowing them to react to enemy movement and reinforce squad-level defenses beyond the current retaliation flag.
4. **Rally and leadership mechanics** – Allow nearby HQ or command units to spend actions to clear suppression or provide temporary defensive buffs, promoting platoon-level resource management and giving meaning to non-combat unit counters.
5. **Contextualized RNG** – Replace the single ±15% damage spread with dice pools or binomial logic representing multiple squad members firing. This would narrow variance for large formations while retaining uncertainty for small recon teams.
6. **Terrain saturation effects** – Extend `LineOfSightService` to consider partial cover when multiple squads occupy the same terrain, reducing hit chance further until attackers maneuver for better angles, reinforcing platoon movement and positioning.

These adjustments would let the combat layer reflect the dynamics of squads operating together—sharing suppression, leveraging leadership, and coordinating fire—while reusing the existing preview/outcome scaffolding.
