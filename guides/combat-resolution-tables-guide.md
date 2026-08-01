# Editable Combat Resolution Tables

Squad Of Steel exposes combat tuning through two JSON files copied beside the mod DLL:

- `hex-of-steel-core-crt.json` controls the strength of the base game's native damage-factor groups.
- `squad-of-steel-crt.json` controls the additional hit, damage-spread, suppression, and move-mode factors introduced by Squad Of Steel.

The defaults are the current as-is mechanics. Editing either file therefore starts from the same result seen in game today. Restart Hex of Steel after changing a table; the files load once per mod session.

## Core Hex of Steel CRT

Hex of Steel has no open CRT data file. Its `Unit.GetPotentialDamage` method is compiled into `Assembly-CSharp.dll` and evaluates a long ordered set of conditional modifiers. It does, however, publish the contribution of each major factor in fields used by its own damage breakdown.

Squad Of Steel leaves that method intact, takes its native `FinalDamage`, and applies the editable weights in `hex-of-steel-core-crt.json`:

`adjusted = native damage + sum(native contribution * (weight - 1))`

It then applies `finalDamageMultiplier`. A factor weight means:

| Weight | Effect |
|---:|---|
| `1.0` | Native/as-is contribution |
| `0.0` | Remove that contribution |
| `0.5` | Half-strength contribution |
| `1.5` | 50% stronger contribution |
| `2.0` | Double-strength contribution |

This works for bonuses and penalties. For example, `entrenchment: 0.5` halves the native entrenchment deduction, while `entrenchment: 1.5` makes it 50% stronger. The `otherVanillaFactors` group catches native matchup rules that Hex of Steel combines into its general “others” breakdown.

The weighting approach is intentional: it retains native attack eligibility, damage-type selection, calculation order, policies, special-unit rules, database values, rounding, and compatibility with game updates. Reimplementing the entire method in the mod would fork core combat and make the table misleading whenever Hex of Steel changes.

## Squad Of Steel CRT

`squad-of-steel-crt.json` holds values that previously existed as constants in mod code:

- Hit chance: baseline, floor/ceiling, tank and close-in infantry bonuses, retaliation/support penalties, and suppression accuracy effects.
- Damage: maximum target-suppression bonus and random hit-damage spread.
- Suppression: cap, hit/miss gain, and attacker recovery after a hit.
- Move mode: incoming hit bonus, incoming damage multiplier, and attack penalty.

Probability values are decimals: `0.10` is ten percentage points, while a damage multiplier of `1.20` means 120% damage.

## Scale-dependent factors

Distance falloff, cover, line-of-sight terrain, unit blocking, and passive suppression recovery vary by selected map scale. Those settings remain in `scale-profiles.json`; see `scale-profiles-guide.md`. Keeping them there prevents the selected Company/Platoon/Squad profile from being silently overridden by a second file.

## Suggested tuning workflow

1. Copy the two CRT files before experimenting.
2. Change one family of values at a time.
3. Restart the game, load the same scenario, and enable combat debug with `F8`.
4. Compare hit chance, native base damage, damage on hit, actual damage, and suppression across repeated engagements.
5. Return a factor to `1.0` (Core) or the committed default (Squad) to restore as-is behavior.

Malformed or missing files do not prevent the mod from loading. The runtime logs a warning and uses built-in as-is defaults for that table.

## Core CRT factor reference

Core factor values are **weights**, not percentages added directly to damage. A factor only changes an engagement when Hex of Steel reports a non-zero contribution for it. Increasing the weight strengthens the native result in its original direction: it enlarges a bonus, but it also enlarges a penalty. Decreasing the weight moves that contribution toward zero.

| Setting | What it represents in play | Expected result when changed |
|---|---|---|
| `enabled` | Master switch for Core reweighting. | `false` bypasses this file and uses native damage exactly. `true` enables the weights and final multiplier. |
| `finalDamageMultiplier` | Global multiplier after all native factor reweighting. | Above `1.0` makes every damaging attack more lethal; below `1.0` lengthens engagements. `0.0` makes core potential damage zero. |
| `baseDamage` | The attacker's native Air, Hard, or Soft damage selected for the target. | Increasing it raises the basic damage foundation in every eligible attack. Lowering it makes contextual bonuses relatively more important; `0.0` removes the base contribution but does not automatically remove separately reported bonuses. |
| `health` | Loss of attack output because the attacker is below its base maximum HP. This is normally a negative contribution. | Above `1.0` makes damaged units lose effectiveness faster. Below `1.0` lets depleted units retain more firepower; `0.0` removes the native HP-loss contribution. |
| `veterancy` | Extra attack output from the native HP-for-damage ratio contributed by levels above 1. | Above `1.0` makes veteran formations more lethal. Below `1.0` narrows the gap between green and veteran units. |
| `morale` | Native morale curve, ranging from a penalty at poor morale to a bonus at high morale when the game option is enabled. | Above `1.0` makes morale states more decisive in both directions. Below `1.0` flattens morale differences. It has no effect when native morale rules are disabled. |
| `hero` | Damage contribution attributed by the native breakdown to a unit's attached hero. | Above `1.0` increases hero-driven attack differences; below `1.0` reduces them. Some game versions fold part of hero-modified base stats into another category, so only the amount reported here is reweighted. |
| `commander` | Nearby commander/general attack bonus, scaled by native health rules. | Above `1.0` rewards fighting within command coverage more strongly. `0.0` removes the reported commander attack contribution. |
| `armour` | Damage deducted by the target's armour after the attacker's native penetration/class rule is selected. Normally negative. | Above `1.0` makes armour more protective. Below `1.0` makes armoured targets easier to damage; `0.0` removes the reported armour deduction. |
| `entrenchment` | Damage prevented by target entrenchment, based on post-armour base damage, the database entrenchment modifier, and entrenchment level. Normally negative. | Above `1.0` makes prepared positions harder to crack. Below `1.0` speeds assaults against dug-in units; `0.0` removes this deduction where it applies. |
| `terrain` | General damage protection supplied by the defending tile's native `DamageModificator`. Normally negative. | Above `1.0` makes defensive terrain more valuable. Below `1.0` makes terrain less decisive. This is separate from Squad Of Steel's hit-chance cover penalty. |
| `armouredTerrain` | Additional native close-terrain penalty for tanks, anti-tank units, and cavalry in specified terrain. Normally negative. | Above `1.0` punishes armour/cavalry in cities, forests, marshes, mountains, and similar terrain more severely. Below `1.0` improves their performance there. |
| `hill` | Native retaliation bonus when a qualifying defender fires from a hill. Normally positive. | Above `1.0` strengthens hill-based return fire. Below `1.0` reduces that defensive firing advantage. |
| `river` | Contribution that the native game labels as river-related. | Increasing or decreasing it affects only attacks where the current game build emits a non-zero river breakdown. If the build folds a river rule into `otherVanillaFactors`, changing this entry alone has no visible effect. |
| `weather` | General native bad-weather damage penalty. Normally negative. Aircraft also have separate native policy handling. | Above `1.0` makes bad weather suppress damage more strongly. Below `1.0` reduces weather impact; `0.0` removes the reported general weather deduction. |
| `biome` | Native biome/season contribution when reported separately. | Above `1.0` intensifies the emitted biome effect; below `1.0` softens it. It does nothing in combats where this breakdown is zero. |
| `recon` | Attack bonus supplied by nearby reconnaissance support, with native HP scaling. Normally positive. | Above `1.0` makes recon-supported attacks more rewarding. Below `1.0` reduces the value of recon positioning. |
| `combinedArms` | Native non-retaliation bonus from the attacker's qualifying combined-arms support. Normally positive. | Above `1.0` rewards mixed formations more heavily. `0.0` removes the reported combined-arms damage bonus but does not change eligibility or positioning rules. |
| `encirclement` | Native modifier associated with the attacker's encirclement calculation. In the bundled build it is a damage penalty when the attacker is judged exposed to multiple sides. | Above `1.0` magnifies the emitted encirclement penalty. Below `1.0` reduces it. Consult the debug breakdown rather than assuming it is always an attacker bonus. |
| `flamethrower` | Extra native damage from flamethrowers against buildings and troops in close defensive terrain. Normally positive. | Above `1.0` makes flamethrowers more specialized and lethal in their intended targets. Below `1.0` reduces that advantage. |
| `landing` | Native attack bonus or penalty for units fighting while embarked/landing, including special marine/commando handling. | Above `1.0` enlarges both positive marine-type bonuses and ordinary landing penalties. Below `1.0` makes landing status matter less. |
| `mountaineer` | Native use of the tile damage modifier as an attack bonus for mountaineers in hills or mountains. Normally positive. | Above `1.0` further specializes mountaineers for elevated terrain. Below `1.0` narrows their terrain advantage. |
| `submarineVsLandingCraft` | Native bonus for submarines attacking embarked units or patrol boats. Normally positive. | Above `1.0` makes those targets more vulnerable to submarines. Below `1.0` improves their survival. |
| `shipsVsGround` | Native naval-versus-land contribution when reported in its dedicated field. | Increasing it strengthens the emitted effect in its original direction. The bundled build places several ship-versus-land reductions in `otherVanillaFactors`, so this entry may be zero for those attacks. |
| `heavyBomberVsShips` | Native heavy-bomber/naval matchup contribution when reported separately. | Above `1.0` intensifies that matchup rule; below `1.0` softens it. No effect when the native breakdown is zero. |
| `torpedo` | Flat native torpedo bonus against boats. Normally positive. | Above `1.0` makes torpedo carriers more lethal to ships; `0.0` removes the reported torpedo bonus without removing torpedo attack eligibility. |
| `destroyerVsSubmarine` | Native modifier for destroyers attacking submerged submarines. In the bundled build this is a deduction from base damage. | Above `1.0` enlarges that deduction; below `1.0` lets destroyers deal more damage. The name identifies the matchup, not whether its contribution is beneficial. |
| `policies` | Sum of native doctrine/policy attack bonuses and defensive deductions active in the engagement. | Above `1.0` makes policy choices more powerful in both directions. Below `1.0` makes unit stats dominate more. Because all policies share this group, opposing active policy effects are reweighted together. |
| `politicalUnits` | Native bonus for political units fighting a target of a different ideology. Normally positive. | Above `1.0` increases their ideological-matchup advantage. Below `1.0` reduces it. |
| `repeatedAttacks` | Native reduction for qualifying air, artillery, or naval fire against a target already attacked that turn, when the player setting is enabled. Normally negative. | Above `1.0` gives stronger diminishing returns to repeated fire. Below `1.0` makes focus fire more effective; `0.0` removes the reported reduction. |
| `otherVanillaFactors` | Native catch-all: unit matchups and situational rules not published in a dedicated breakdown, including several ship/landing/frozen-terrain cases. | Above `1.0` amplifies all reported catch-all bonuses and penalties together. Change cautiously because it covers unrelated rules; use combat debug to see its sign and size for the matchup being tested. |

### Core weighting examples

- Setting `armour` to `1.5` turns a native `-8` armour contribution into `-12`, reducing the result by 4 more damage.
- Setting `entrenchment` to `0.5` turns a native `-10` contribution into `-5`, adding 5 damage back to the attack.
- Setting `combinedArms` to `2.0` turns a native `+6` contribution into `+12`.
- A weight cannot create a factor where native Hex of Steel reports zero. For example, doubling `torpedo` changes nothing when the attacker has no active torpedo bonus.

## Squad Of Steel CRT factor reference

Squad settings are direct probabilities, bonuses, penalties, gains, or multipliers rather than Core-style weights. Hit-chance bonuses and penalties are additive percentage-point changes before the minimum/maximum clamp.

### Hit chance

| Setting | What it represents in play | Expected result when changed |
|---|---|---|
| `baseChance` | Starting probability for every clear-LOS Squad Of Steel attack before range, cover, class, context, suppression, and move-mode effects. | Raising it improves accuracy broadly. Lowering it increases misses broadly. A `+0.10` change is ten percentage points before clamping, not a 10% relative increase. |
| `minimum` | Lowest allowed hit probability after all modifiers. Blocked LOS still forces 0%. | Raising it makes even extremely poor shots more reliable. Lowering it permits near-hopeless shots. Setting it to `0.0` allows a clear-LOS attack to reach a true 0% chance. |
| `maximum` | Highest allowed hit probability after all modifiers. | Raising it lets ideal shots become more dependable. Lowering it preserves uncertainty even for adjacent, well-supported attacks. It cannot normalize below `minimum`. |
| `tankBonus` | Additive accuracy bonus when the attacker has `FilterTank`. | Raising it separates tank accuracy from other weapon classes. Lowering it narrows that gap; a negative value would penalize tanks. |
| `infantryCloseRangeBonus` | Additive accuracy bonus for `FilterInfantry` within the configured close-range limit. | Raising it rewards infantry closing with the enemy. Lowering it reduces that incentive; a negative value makes close infantry fire less accurate. |
| `infantryCloseRangeMaximumHexes` | Furthest distance, inclusive, at which the close-range infantry bonus applies. | Raising it extends the bonus to more ranged shots. Lowering it restricts the bonus; `0` effectively disables it in normal different-hex combat. |
| `retaliationPenalty` | Accuracy subtracted from return fire marked as retaliation. | Raising it weakens reactive fire and favors the initiating attacker. Lowering it makes defenders return fire more effectively; `0.0` removes this penalty. |
| `supportiveFirePenalty` | Accuracy subtracted from supportive fire. | Raising it makes support shots less reliable. Lowering it strengthens fire support; `0.0` puts it on the same contextual footing as a normal shot. |
| `attackerSuppressionPenaltyAtMaximum` | Accuracy lost when the attacker reaches the configured suppression maximum; partial suppression scales linearly. | Raising it makes suppression more effective at neutralizing enemy fire. Lowering it lets suppressed formations retain accuracy. At the default `0.45`, half suppression costs 22.5 points. |
| `targetSuppressionBonusAtMaximum` | Accuracy gained against a target at maximum suppression; partial suppression scales linearly. | Raising it makes suppressed targets progressively easier to hit. Lowering it separates suppression from hit probability. This is independent of the damage bonus with a similar name. |

The final chance is:

`clamp(base + range + class + context + suppression + move mode - cover, minimum, maximum)`

Scale-profile range and cover values participate in that same sum.

### Damage

| Setting | What it represents in play | Expected result when changed |
|---|---|---|
| `targetSuppressionBonusAtMaximum` | Extra damage-on-hit multiplier against a target at maximum suppression; partial suppression scales linearly. | Raising it makes suppression set up high-damage follow-on attacks. Lowering it leaves suppression focused on accuracy. Default `0.35` means up to 135% damage before move-mode vulnerability and random spread. |
| `randomSpreadMinimum` | Lowest random multiplier on a successful hit. | Raising it removes weak hits and increases average damage. Lowering it creates more low-damage hits. Setting it equal to the maximum removes random damage spread. |
| `randomSpreadMaximum` | Highest random multiplier on a successful hit. | Raising it permits larger spike hits and increases average damage. Lowering it caps burst damage. It normalizes to at least the minimum. |

With the default spread of `0.85`–`1.15`, a 20-damage hit rolls between about 17 and 23 damage before the base game applies HP loss and rounding effects.

### Suppression

| Setting | What it represents in play | Expected result when changed |
|---|---|---|
| `maximum` | Suppression cap and denominator used by both accuracy effects and the damage bonus. | Raising it takes more fire to reach full penalties/bonuses and allows a longer buildup. Lowering it makes each suppression point proportionally stronger and reaches the cap sooner. |
| `targetGainOnHit` | Suppression added to a target after an attack deals real damage. | Raising it makes hits rapidly degrade the defender and empower follow-on fire. Lowering it slows the suppression cycle; `0` gives no hit-based suppression. |
| `attackerRecoveryOnHit` | Suppression removed from the attacker after it deals real damage. | Raising it creates stronger momentum for successful attackers. Lowering it makes accumulated suppression linger despite successful fire; `0` removes this recovery. |
| `targetGainOnMiss` | Suppression added by a clear-LOS miss that had positive damage potential. | Raising it makes near misses useful and volume of fire reliable for suppression. Lowering it makes actual hits more important; `0` prevents misses from suppressing. |

Passive suppression recovery is scale-dependent and remains under each profile's `passiveSuppressionRecovery` in `scale-profiles.json`.

### Move mode combat effects

| Setting | What it represents in play | Expected result when changed |
|---|---|---|
| `incomingHitChanceBonus` | Accuracy added when firing at a unit currently represented in Move/transport mode. | Raising it makes mounted/moving formations easier to hit. Lowering it reduces that exposure; `0.0` removes the accuracy vulnerability. |
| `incomingDamageMultiplier` | Damage-on-hit multiplier against a target in Move mode. | Above `1.0` makes transport mode more lethal when hit. `1.0` removes the damage vulnerability; below `1.0` would make Move mode protective. |
| `attackerHitChancePenalty` | Accuracy subtracted when an attacker is in Move mode during preview calculation. | Raising it discourages fighting while mounted. Lowering it makes Move mode more combat-capable; `0.0` removes the accuracy penalty. The attack path may still return a unit to Combat mode under existing move-mode rules. |

## Interactions to expect while tuning

- Core damage is calculated first. Squad Of Steel then applies range scaling, target-suppression damage, Move-mode vulnerability, hit probability, and random spread.
- A higher hit chance changes how often damage occurs, not the damage of an individual hit. Damage settings change hit severity, not whether it connects.
- Raising both target-suppression hit and damage bonuses compounds their effect: follow-on shots connect more often **and** hit harder.
- The hit floor and ceiling can hide modifier changes. If a shot is already clamped at `maximum`, another positive bonus will not change its displayed chance.
- Core weights act on the native rounded contribution. Very small attacks may therefore show stepwise rather than perfectly proportional changes.
- `finalDamageMultiplier` and random spread are multiplicative; most hit-chance modifiers are additive.
