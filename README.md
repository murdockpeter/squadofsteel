# Squad Of Steel

Squad Of Steel augments Hex of Steel's tactical layer with squad-focused mechanics layered on top of the vanilla division engine. Direct fire now respects terrain line of sight, combat uses probabilistic resolution instead of deterministic exchanges, suppression lingers across turns, and infantry formations can embark or disembark from their transport counters in real time. Rich debugging tools make every calculation observable while you tune balance or author scenarios.

## Compatibility

- Mod version: **0.4.0**
- Verified game version: **Hex of Steel 8.4.11+**
- Bundled patching library: **Harmony 2.4.2**
- Build target: **.NET Framework 4.8**

## Feature Highlights

- **Line of sight enforcement** - direct-fire attacks are cancelled (with a notification) if terrain, units, or fog block the shot.
- **Hit probability** - every shot rolls against a distance/cover/suppression-aware hit chance; misses truly inflict zero damage.
- **Suppression tracking** - incoming fire builds suppression on the target, reducing its future accuracy while boosting the attacker's odds. Suppression decays passively each turn and persists through saves.
- **Counter overlays** - a suppression indicator sits on the right edge of each counter, shifting from green to yellow to red as suppression climbs.
- **Movement mode system** - infantry units can press `V` to toggle between Combat (foot infantry) and Move (mounted transport) modes. Units visually transform into their designated carrier vehicle, gaining mobility at the cost of increased vulnerability.
- **Transport mappings** - configure which transport vehicle each infantry type uses via `Assets/transport-mappings.json`, including nationality-specific fallbacks and per-mod overrides.
- **Combat telemetry** - enable debug mode (`F9`) to receive an auto-scrolling overlay, popup summaries, and Player.log entries for every engagement (hit chance, roll, damage, suppression deltas, HP before/after).
- **Damage guard rails** - if a shot is declared a miss but the base game still shaved HP, Squad Of Steel restores the defender so the battlefield matches the roll.
- **Living documentation** - the `guides/` folder ships deep dives for combat resolution, line of sight math, movement modes, and custom integration workflows.

## Repository Layout

- `SquadOfSteel.csproj` - .NET Framework 4.8 class library that builds the mod.
- `Scripts/` - Harmony entry point, combat runtime, suppression logic, keybind handlers, and debug UI.
- `Assets/` - configuration such as transport mappings.
- `Libraries/` - reference assemblies pulled from Hex of Steel (Harmony, Assembly-CSharp, Unity modules, etc.).
- `guides/` - documentation bundles including the combat resolution overview.
- `Tests/CompatibilityHarness/` - resolves every Harmony patch against the current HoS API without starting a match.
- `output/` - build products (primary DLL at `net48/SquadOfSteel.dll`).

## Building

```powershell
dotnet build SquadOfSteelMod.sln -c Release
```

The compiled DLL lands at `output\net48\SquadOfSteel.dll`.

## Compatibility Testing

With Hex of Steel installed through Steam, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\TestCompatibility.ps1
```

The compatibility suite performs a release build, verifies that committed reference assemblies match the installed game, resolves all Harmony targets, exercises both official-unit exporters, validates transport carrier names, and checks that assembly/package versions agree.

## Installation & Usage

1. Build the project or grab the DLL from `output\net48`.
2. Deploy the build output:
   - Preferred: run `powershell -ExecutionPolicy Bypass -File .\Scripts\DeployToGame.ps1` to copy the DLL, mappings, manifest, and bundled dependencies into the game mod folder.
   - Manual copy, if you prefer explicit commands:
     ```powershell
    Copy-Item .\output\net48\SquadOfSteel.dll `
      "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel Beta 1.0\Libraries\SquadOfSteel.dll" -Force
    Copy-Item .\Assets\transport-mappings.json `
      "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel Beta 1.0\Libraries\transport-mappings.json" -Force
     ```
3. Configure transport mappings (optional):
   - Edit `Assets\transport-mappings.json` to map infantry unit names to their carrier vehicles.
   - Nationality-specific overrides are supported via nested objects (e.g., `"Paratroopers": { "us": "M3 Scout Car", "uk": "Daimler Dingo", "generic": "M3 Scout Car" }`).
   - Custom mods can drop additional `transport-mappings-*.json` files; the loader merges them in alphabetical order so later files override earlier ones. See `guides/custom-mod-integration-guide.md` for full details.
   - Legacy flat mappings (e.g., `"Rangers": "M3 Scout Car"`) still work.
4. Launch Hex of Steel, open the Mods menu, and enable **Squad Of Steel**.
5. In-game controls:
   - `V` - toggles the selected infantry unit between Combat and Move modes, updating visuals and movement/action points.
   - `K` - dumps an at-a-glance summary (suppression, hit chance, expected damage) for the selected vs targeted units to the console.
   - `F9` - toggles combat debug mode. When enabled you get:
     - A left-hand overlay listing every combat event with full math (mouse-wheel scrolling supported).
     - Floating popups summarising hits/misses (hit chance, roll, damage).
     - Player.log entries (including HP before/after, suppression deltas, overlay diagnostics).
6. Play as normal. Direct-fire attacks can miss, suppression floats beside counters, suppression decays each turn, and infantry can toggle transport modes for tactical repositioning. All state persists across saves.

### Reference Data

- Run `powershell -ExecutionPolicy Bypass -File .\Scripts\ExportOfficialUnitNames.ps1` to dump the official unit catalog to JSON, CSV, and text files under `output/`.
- Run `powershell -ExecutionPolicy Bypass -File .\Scripts\ExportOfficialUnitStats.ps1` for the complete stat and mobility exports.
- Both scripts auto-detect common Steam locations. Pass `-GameInstallPath` for a custom library.

## Workshop Release Checklist

1. Run `powershell -ExecutionPolicy Bypass -File .\Scripts\TestCompatibility.ps1`.
2. Run `powershell -ExecutionPolicy Bypass -File .\Scripts\DeployToGame.ps1`.
3. Start a scenario and verify direct fire, retaliation, line of sight, suppression, movement-mode switching, and save/reload.
4. Confirm `Player.log` contains the Squad Of Steel initialization and no Harmony exceptions.
5. Upload the tested mod directory to the Workshop.

## GitHub Kickoff (optional)

If you want to version-control the mod:

```powershell
git init
git add .
git commit -m "Initial commit"
```

Add your remote and push (`git remote add origin ...`, `git push -u origin main`) as usual. Adjust branch/remote names to suit your workflow.
