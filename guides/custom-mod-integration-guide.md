# Custom Mod Integration Guide

## Overview

Squad Of Steel now supports **multiple transport mapping files** that are loaded and merged at runtime. This allows custom mods to provide their own transport mappings without modifying the base configuration file.

## For Custom Mod Creators

### Quick Start

1. Create a new JSON file in your mod's Libraries folder
2. Name it `transport-mappings-yourmodname.json` (replace "yourmodname" with your mod's name)
3. Place it in the same directory as `SquadOfSteel.dll`
4. Squad Of Steel will automatically discover and load your mappings

### File Naming Convention

Files must match the pattern: `transport-mappings*.json`

**Examples:**
- `transport-mappings.json` (base Squad Of Steel mappings)
- `transport-mappings-atlantis.json` (custom Atlantis mod)
- `transport-mappings-mars-colony.json` (custom Mars Colony mod)
- `transport-mappings-zzz-user-overrides.json` (user customizations, loads last)

### Loading Order

Files are loaded in **alphabetical order**. Later files override earlier ones.

**Example loading sequence:**
1. `transport-mappings.json` (base)
2. `transport-mappings-atlantis.json` (mod A)
3. `transport-mappings-mars.json` (mod B)
4. `transport-mappings-zzz-custom.json` (user overrides)

**Tip:** Prefix with "zzz-" to ensure your file loads last and overrides everything else.

## Configuration Format

### Nationality-Specific Format (Recommended)

```json
{
  "Infantry Unit Name": {
    "nationality_key": "Carrier Vehicle Name",
    "another_nationality": "Different Carrier Vehicle Name",
    "generic": "Fallback Carrier Vehicle Name"
  }
}
```

### Example: Custom Nationalities

```json
{
  "Atlantean Warriors": {
    "atlantis": "Hover Transport",
    "atlantis_navy": "Submarine Transport",
    "generic": "Willys MB"
  },

  "Mars Colonists": {
    "mars": "Rover APC",
    "mars_red": "Red Faction APC",
    "mars_blue": "Blue Faction APC",
    "generic": "M3 Scout Car"
  }
}
```

### Example: Override Existing Units

You can override Squad Of Steel's default mappings:

```json
{
  "Rangers": {
    "us": "My Custom US Transport",
    "generic": "M3 Scout Car"
  },

  "Paratroopers": {
    "uk": "My Custom UK Transport",
    "generic": "Daimler Dingo"
  }
}
```

### Legacy Flat Format (Also Supported)

For simple scenarios where you don't need nationality-specific mappings:

```json
{
  "Custom Infantry Type": "Custom Carrier Vehicle",
  "Another Infantry Type": "Another Carrier Vehicle"
}
```

## How Nationality Detection Works

Squad Of Steel reads the unit's `OwnerName` field (the player/faction name) and uses it to look up the appropriate carrier vehicle.

### Nationality Matching

The mod normalizes the `OwnerName` to lowercase and checks for matches. For example:

- `OwnerName: "USA"` → matches nationality key `"us"`
- `OwnerName: "Great Britain"` → matches nationality key `"uk"` (contains "brit")
- `OwnerName: "Soviet Union"` → matches nationality key `"soviet"`
- `OwnerName: "Atlantis Empire"` → matches nationality key `"atlantis"` (custom)

### Standard Nationality Keys

The mod recognizes these standard keys:
- `"us"` - United States, America, USA
- `"uk"` - United Kingdom, British, Commonwealth
- `"german"` - Germany, Reich
- `"soviet"` - Soviet Union, USSR, Russia
- `"japanese"` - Japan
- `"italian"` - Italy
- `"french"` - France
- `"chinese"` - China
- `"polish"` - Poland
- `"generic"` - Fallback for unmatched nationalities

### Custom Nationality Keys

You can use **any custom nationality key** you want! Just make sure it matches (or is contained in) the `OwnerName` of your units.

**Example:**
- Your mod has faction `OwnerName: "Atlantis Empire"`
- Use nationality key `"atlantis"` in your mapping file
- The mod will match "atlantis" against "Atlantis Empire" (case-insensitive substring matching)

### Scenarios with Generic Player Names

Some scenarios use generic player names like "player1", "player2", etc. In these cases:
- Nationality-specific mappings are ignored
- The `"generic"` fallback is used instead
- Make sure your `"generic"` entry points to a widely available vehicle

## Best Practices

### 1. Always Include a Generic Fallback

```json
{
  "Custom Infantry": {
    "custom_nation": "Custom Transport",
    "generic": "Willys MB"
  }
}
```

The generic fallback ensures your units have a transport even in scenarios without proper nationality names.

### 2. Choose Universally Available Vehicles for Generic

**Good generic choices:**
- `"Willys MB"` - Available to most factions
- `"M3 Scout Car"` - Common Allied vehicle
- `"Daimler Dingo"` - Common Commonwealth vehicle

**Bad generic choices:**
- Faction-specific vehicles that might not exist in all scenarios
- Heavy armor vehicles (slow and expensive)
- Custom mod vehicles (only exist when your mod is active)

### 3. Test with Multiple Scenarios

- Test with scenarios that use proper nationality names
- Test with scenarios that use generic player names ("player1", etc.)
- Test with vanilla scenarios and custom mod scenarios

### 4. Name Your File Descriptively

**Good names:**
- `transport-mappings-atlantis-mod.json`
- `transport-mappings-ww1-expansion.json`
- `transport-mappings-future-warfare.json`

**Bad names:**
- `transport-mappings-custom.json` (too generic)
- `my-mappings.json` (doesn't match the pattern)
- `mappings.json` (doesn't match the pattern)

### 5. Document Your Mappings

Add comments to your JSON file using "_" prefix:

```json
{
  "_mod_name": "Atlantis: Rise of the Empire",
  "_version": "1.2.0",
  "_author": "ModAuthorName",
  "_description": "Transport mappings for Atlantean units",

  "Atlantean Warriors": {
    "atlantis": "Hover Transport",
    "generic": "Willys MB"
  }
}
```

## Integration with Squad Of Steel

### No Code Changes Required

Your custom mod does NOT need to reference or modify Squad Of Steel's code. Simply:
1. Create your JSON mapping file
2. Place it in the mod directory
3. Squad Of Steel automatically discovers and loads it

### Works with Any Number of Mods

Multiple custom mods can each provide their own mapping files. They all load and merge automatically.

**Example scenario:**
- Base game + Squad Of Steel
- Atlantis mod with `transport-mappings-atlantis.json`
- Mars Colony mod with `transport-mappings-mars.json`
- User overrides with `transport-mappings-zzz-my-custom.json`

All four files load and merge seamlessly.

### File Locations

Squad Of Steel searches for mapping files in these locations:
1. `[Mod Directory]/Libraries/`
2. `[Mod Directory]/Libraries/Assets/`
3. `[Mod Directory]/`
4. `[Mod Directory]/Assets/`

**Recommended location:** Place your file in `Libraries/` alongside `SquadOfSteel.dll`.

## Debugging

### Enable Logging

When Squad Of Steel loads, it logs information about discovered mapping files:

```
[SquadOfSteel] Found 3 transport mapping file(s): transport-mappings.json, transport-mappings-atlantis.json, transport-mappings-custom.json
[SquadOfSteel] Loaded 15 mapping(s) from 'transport-mappings.json'
[SquadOfSteel] Loaded 8 mapping(s) from 'transport-mappings-atlantis.json'
[SquadOfSteel] Loaded 2 mapping(s) from 'transport-mappings-custom.json'
[SquadOfSteel] Total: 18 infantry types mapped with 25 entries loaded.
```

### Check Player.log

View the game's log file at:
`C:\Users\[YourName]\AppData\LocalLow\War Frogs Studio\Hex of Steel\Player.log`

Search for `[SquadOfSteel]` to see mapping resolution:

```
[SquadOfSteel] Resolved nationality-specific mapping: Atlantean Warriors (atlantis) -> Hover Transport
[SquadOfSteel] Unit definition 'Hover Transport' found and cached.
```

### Common Issues

**"No transport mapping configured"**
- Your infantry unit name doesn't exist in any mapping file
- Check spelling and capitalization (mapping is case-sensitive)

**"Could not find unit definition"**
- The carrier vehicle name doesn't exist in the game
- Check spelling of the carrier vehicle name
- Ensure the vehicle exists in the current scenario

**Mappings not loading**
- File name doesn't match pattern `transport-mappings*.json`
- File is not in a searchable directory
- JSON syntax error (check for missing commas, brackets, etc.)

## Example: Complete Custom Mod File

```json
{
  "_mod_name": "Atlantis: Lost Empire",
  "_version": "2.0.0",
  "_author": "YourName",
  "_compatible_with_squad_of_steel": "1.0+",

  "Atlantean Infantry": {
    "atlantis": "Hover Scout",
    "atlantis_royal": "Royal Hover Transport",
    "generic": "Willys MB"
  },

  "Atlantean Heavy Infantry": {
    "atlantis": "Heavy Hover APC",
    "atlantis_royal": "Royal Heavy Hover",
    "generic": "M3 Scout Car"
  },

  "Crystal Warriors": {
    "atlantis": "Crystal-Powered Transport",
    "generic": "Daimler Dingo"
  },

  "Deep Sea Marines": {
    "atlantis": "Submarine Transport",
    "atlantis_surface": "Amphibious APC",
    "generic": "M3 Scout Car"
  }
}
```

## Performance Notes

- File discovery happens once at mod initialization (startup or scenario load)
- Mapping lookups are O(1) dictionary operations
- Zero performance impact during gameplay
- Safe to have dozens of mapping files with thousands of entries

## Questions or Issues?

If you encounter issues integrating your custom mod with Squad Of Steel, check:
1. The Player.log file for error messages
2. JSON syntax (use a JSON validator)
3. Vehicle names match exactly (case-sensitive)
4. File naming follows the `transport-mappings*.json` pattern

For support, provide:
- Your custom mapping file
- Relevant entries from Player.log
- Description of the issue (what works, what doesn't)
