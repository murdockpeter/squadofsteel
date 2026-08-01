# Changelog

## 0.5.0 - 2026-07-27

- Added a blocking scale-selection modal when a scenario starts for the first time under Squad of Steel.
- Added Default, Operational (5 km/hex), Company (1 km/hex), Platoon (250 m/hex), and Squad (50 m/hex) interpretation profiles.
- Persisted the selected profile in the scenario's `ModDataBag` so saves retain their interpretation.
- Made Squad of Steel distance falloff, cover, LOS blockers, and passive suppression recovery profile-driven.
- Added physical-distance labels to the squad panel and combat telemetry.
- Preserved all base-game unit statistics and database records; scale selection does not rewrite range, movement, visibility, or damage values.
- Added deployable `Assets/scale-profiles.json` configuration and built-in fallbacks.
- Refreshed `Assembly-CSharp.dll` against the current 2026-07-27 Steam installation after the game binary changed.
- Moved the combat telemetry toggle from F9 to F8 because Hex of Steel maps F9 to its supplies overlay.

## 0.4.0 - 2026-07-26

- Verified and rebuilt against Hex of Steel 8.4.11 (Steam build 24203557).
- Updated bundled Harmony from 2.4.1 to 2.4.2.
- Refreshed all committed Hex of Steel and Unity reference assemblies.
- Added automated Harmony target, reference, export, mapping, and release-metadata checks.
- Repaired official-unit export scripts for the current serialized catalog.
- Refreshed the official unit snapshots for the 8.4.11 catalog.
- Added the renamed `Panzergrenadiers` transport mapping while retaining the legacy singular mapping.
- Cleaned up the persistent keybind host when the mod is unloaded or reinitialized.
- Fixed recursive transport synchronization during scenario loading that could crash the game.
