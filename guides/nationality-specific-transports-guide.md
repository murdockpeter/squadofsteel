# Nationality-Specific Transport Mappings Guide

## Overview

Squad Of Steel now supports **nationality-specific transport mappings**, allowing different factions to use appropriate carrier vehicles for their infantry units. This means US Paratroopers will mount up in M3 Scout Cars, while British Paratroopers use Daimler Dingos, and German Paratroopers use Sd. Kfz. halftracks.

## Configuration File

Transport mappings are configured in `Assets/transport-mappings.json`. The mod loads this file at startup and uses it to determine which carrier vehicle each infantry unit should use when toggling to Move mode.

## Format

### Nationality-Specific Format (Recommended)

```json
{
  "Infantry Unit Name": {
    "nationality_key": "Carrier Vehicle Name",
    "another_nationality": "Another Carrier Vehicle Name",
    "generic": "Fallback Carrier Vehicle Name"
  }
}
```

### Example

```json
{
  "Paratroopers": {
    "us": "M3 Scout Car",
    "uk": "Daimler Dingo",
    "german": "Sd. Kfz. 251 9 Stummel",
    "soviet": "ZiS-5 x2 DshK",
    "japanese": "Type 94 TK",
    "generic": "M3 Scout Car"
  },

  "Rangers": {
    "us": "M3 Scout Car",
    "generic": "M3 Scout Car"
  }
}
```

## Standard Nationality Keys

The mod recognizes these standard nationality keys (case-insensitive):

- **us** - United States, America, USA
- **uk** - United Kingdom, British, Commonwealth, England
- **german** - Germany, Reich
- **soviet** - Soviet Union, USSR, Russia
- **japanese** - Japan, Nippon
- **italian** - Italy
- **french** - France
- **chinese** - China
- **polish** - Poland
- **generic** - Fallback for any nationality not matched above

## How Nationality Detection Works

1. The mod reads the unit's `OwnerName` field (the faction/player name)
2. It normalizes the name to lowercase and checks for substring matches
3. For example, "British Empire" matches the "uk" key because it contains "brit"
4. If no standard nationality is matched, the normalized owner name is used directly as the nationality key
5. If the nationality key isn't found in the mapping, the mod tries the "generic" entry
6. If no mapping is found, the unit falls back to legacy move mode (simple stat buffs without vehicle transformation)

## Custom/Mod Nationalities

**You can use ANY nationality key you want!** This system is fully extensible for mods that add custom factions.

### Example: Custom Mod Factions

```json
{
  "Elite Guards": {
    "atlantis": "Atlantean Hover Transport",
    "mars_colony": "Mars Rover APC",
    "generic": "M3 Scout Car"
  },

  "Shock Troops": {
    "my_custom_faction": "Custom Transport Vehicle",
    "another_mod_faction": "Another Custom Vehicle",
    "generic": "ZiS-5 x2 DshK"
  }
}
```

The mod will:
1. Check if the unit's `OwnerName` matches "atlantis" (using contains/substring matching)
2. If not, check if it matches "mars_colony"
3. If not, check if it matches "my_custom_faction"
4. If none match, fall back to "generic"

## Backward Compatibility

The old flat format is still supported for simple scenarios:

```json
{
  "Rangers": "M3 Scout Car",
  "Commandos": "Daimler Dingo"
}
```

This will use the same carrier for all nationalities. However, the nationality-specific format is recommended for better gameplay accuracy.

## Best Practices

1. **Always include a "generic" entry** for each infantry type to ensure units have a fallback if their nationality isn't explicitly mapped.

2. **Use exact unit names** - The infantry unit names must match exactly as they appear in the game (case-insensitive).

3. **Verify carrier vehicles exist** - The carrier vehicle names must exist for that faction in the game. If a carrier doesn't exist, the unit will fall back to legacy move mode.

4. **Test with all factions** - If you're mapping for multiple nationalities, test each one to ensure the correct carrier is selected.

5. **Check the logs** - The mod logs which mappings are loaded and which carrier is selected for each unit. Enable trace mode for detailed diagnostics:
   ```
   [SquadOfSteel] Loaded 25 transport mappings (nationality-aware format).
   [SquadOfSteel][MoveMode] Paratroopers (ID:42) - Resolved nationality-specific mapping: us -> M3 Scout Car
   ```

## Common Carrier Vehicles by Faction

### United States
- M3 Scout Car
- M3 Halftrack
- Willys MB

### United Kingdom / Commonwealth
- Daimler Dingo
- Humber Armored Car
- Universal Carrier

### Germany
- Sd. Kfz. 251 9 Stummel
- Sd. Kfz. 222
- Sd. Kfz. 250

### Soviet Union
- ZiS-5 x2 DshK
- BA-10
- GAZ-67

### Japan
- Type 94 TK
- Type 97 Chi-Ha

## Troubleshooting

### "No transport mapping configured" warning
- The infantry unit name doesn't exist in your `transport-mappings.json`
- Add an entry for that unit, or use the legacy move mode (which still works)

### "No nationality mapping for X with nationality Y" warning
- The nationality key wasn't found in the mapping for that infantry type
- Add the nationality key, or add a "generic" fallback

### Unit uses wrong carrier vehicle
- Check the unit's `OwnerName` field (visible in debug logs)
- Ensure the nationality key matches the `OwnerName` using substring matching
- Example: If `OwnerName` is "USA", it should match the "us" key

### Carrier vehicle doesn't exist
- The carrier vehicle name might be misspelled
- The carrier might not exist for that faction in the current scenario
- Check the official units export: run `pwsh .\Scripts\ExportOfficialUnitNames.ps1`

## Example Configuration

Here's a complete example showing various scenarios:

```json
{
  "_comment": "Nationality-specific transport mappings for Squad Of Steel",

  "Paratroopers": {
    "us": "M3 Scout Car",
    "uk": "Daimler Dingo",
    "german": "Sd. Kfz. 251 9 Stummel",
    "soviet": "ZiS-5 x2 DshK",
    "generic": "M3 Scout Car"
  },

  "Rangers": {
    "us": "M3 Scout Car",
    "generic": "M3 Scout Car"
  },

  "Commandos": {
    "uk": "Daimler Dingo",
    "generic": "Daimler Dingo"
  },

  "Panzergrenadier": {
    "german": "Sd. Kfz. 251 9 Stummel",
    "generic": "Sd. Kfz. 251 9 Stummel"
  },

  "Desantniki": {
    "soviet": "ZiS-5 x2 DshK",
    "generic": "ZiS-5 x2 DshK"
  },

  "Light infantry": {
    "us": "Willys MB",
    "uk": "Daimler Dingo",
    "german": "Sd. Kfz. 251 9 Stummel",
    "soviet": "ZiS-5 x2 DshK",
    "japanese": "Type 94 TK",
    "generic": "Willys MB"
  }
}
```

## Performance Notes

- Nationality detection happens once per unit when first toggling to Move mode
- The resolved carrier mapping is cached for that unit
- Lookups are case-insensitive and use efficient dictionary structures
- No performance impact on gameplay after initial load
