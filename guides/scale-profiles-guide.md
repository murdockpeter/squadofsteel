# Scale Interpretation Profiles

Squad of Steel 0.5.0 asks for a scale interpretation when a scenario is first started. This is deliberately an interpretation layer, not a database conversion. The selected profile changes how Squad of Steel reads distance and applies its own combat rules while leaving Hex of Steel's scenario and unit data intact.

## The Safety Boundary

Scale selection does **not** modify:

- `Unit.Range` or indirect-fire classification
- movement points, action points, fuel, or movement costs
- visibility, spotting, fog of war, or map dimensions
- base soft/hard damage, armor, HP, or unit type
- unit names, counters, formations, transports, or scenario files
- the official or custom unit database

A scenario or total-conversion author remains responsible for giving their counters statistics appropriate to the scale they depict. For example, if a platoon-scale unit should fire four 250 m hexes, its database range must already be `4`. Squad of Steel will interpret that as 1 km; it will not infer or write the value.

## Startup and Persistence

When `GameData.Instance.map` changes to a scenario with no saved Squad of Steel scale:

1. A blocking modal is placed over the game's main canvas.
2. Simulation time is paused.
3. The player chooses one of the configured profiles.
4. The profile id is stored as `SquadOfSteel.ScaleProfile` in the scenario `ModDataBag`.
5. Simulation time resumes.

Loading a save with that key restores the profile without prompting. A new scenario with no stored selection prompts again.

## Included Profiles

| Profile | Physical interpretation | Distance falloff | Intervening units block LOS | Passive suppression recovery |
| --- | ---: | --- | --- | ---: |
| Default / Existing SoS | Abstract | 10% hit and 8% damage loss per hex after range 1 | Ground and air | 15/turn |
| Operational | 5 km/hex, 6 hours/turn | Same as Default | Ground and air | 15/turn |
| Company | 1 km/hex, 1 hour/turn | Relative to the attacker's listed maximum range | No | 12/turn |
| Platoon | 250 m/hex, 15 minutes/turn | Relative to the attacker's listed maximum range | No | 8/turn |
| Squad | 50 m/hex, 5 minutes/turn | Relative to the attacker's listed maximum range | No | 5/turn |

Default is the exact pre-0.5.0 Squad of Steel interpretation.

Operational adds physical labels but intentionally retains Default combat behavior. Company, Platoon, and Squad treat a listed range as a scale-authored weapon envelope: their accuracy and damage falloff reaches the configured maximum-range value at the unit's existing `Range`, regardless of how many hexes that happens to be.

For Company, Platoon, and Squad profiles:

```text
range fraction = clamp((distance - 1) / (Unit.Range - 1), 0, 1)
accuracy modifier = -maximum-range accuracy penalty * range fraction
damage multiplier = lerp(1.0, maximum-range multiplier, range fraction)
```

At range 1, the configured adjacent hit bonus applies and damage has no distance loss. A unit with `Range <= 1` still has only range 1; the profile never grants it extra reach.

## Profile-Driven Levers

The scale layer currently controls only Squad of Steel mechanics:

- `hexMeters` and `turnMinutes`: declared interpretation. `hexMeters` is used in distance readouts; `turnMinutes` is metadata for authors and future mechanics.
- `distanceModel`: `perHex` uses fixed loss for each hex after the first; `fractionOfRange` spreads falloff across the attacker's existing range.
- `adjacentHitBonus`
- `accuracyPenaltyPerHex` or `accuracyPenaltyAtMaximumRange`
- `damageLossPerHex` or `damageMultiplierAtMaximumRange`
- `blockingTerrain`
- `groundUnitsBlockLineOfSight` and `airUnitsBlockLineOfSight`
- `coverPenalties`
- `passiveSuppressionRecovery`

The hex-line construction itself does not change. Indirect-fire detection also does not change: planes, artillery-filtered units, bombers, CAS, and bomb/rocket carriers continue to bypass Squad of Steel's direct-fire LOS gate.

## Configuration

The shipped configuration is `Assets/scale-profiles.json`. A release build copies it to `output/net48/Assets/`, and `Scripts/DeployToGame.ps1` places it beside `SquadOfSteel.dll` in the mod's `Libraries` directory.

At runtime, the loader looks beside the assembly and in an `Assets` child directory. Valid external entries replace built-in profiles with the same id; entries with a new id are added to the picker. If the file is absent or malformed, all four built-in profiles remain available.

Example:

```json
{
  "profiles": [
    {
      "id": "platoon-250m",
      "displayName": "Platoon - 250 m/hex",
      "description": "Custom platoon interpretation.",
      "hexMeters": 250,
      "turnMinutes": 15,
      "distanceModel": "fractionOfRange",
      "adjacentHitBonus": 0.05,
      "accuracyPenaltyAtMaximumRange": 0.25,
      "damageMultiplierAtMaximumRange": 0.75,
      "groundUnitsBlockLineOfSight": false,
      "airUnitsBlockLineOfSight": false,
      "blockingTerrain": ["FOREST", "MOUNTAIN", "CITY", "FACTORY", "HILL"],
      "coverPenalties": {
        "FOREST": 0.22,
        "CITY": 0.35,
        "TRENCH": 0.40
      },
      "passiveSuppressionRecovery": 8
    }
  ]
}
```

Terrain names must match Hex of Steel's `TileTypes` enum names. Cover values are probabilities (`0.25` means a 25 percentage-point hit penalty). Unknown terrain keys are harmless. Unknown `distanceModel` values behave as `perHex`.

## What Companion Mods Must Do

A companion map/unit mod does not need to call Squad of Steel code. It does need to make its own scale internally coherent:

- author unit ranges in hexes for its chosen physical scale;
- author movement and spotting values for that scale;
- use counter art and formation labels matching what each counter represents;
- tell players which Squad of Steel interpretation to select;
- optionally ship a replacement profile if its combat interpretation differs.

This loose contract avoids destructive runtime rescaling and avoids guessing whether a database value represents a squad, platoon, company, or division.
