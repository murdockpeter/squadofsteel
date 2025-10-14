# Squad Of Steel

Squad Of Steel augments Hex of Steel's tactical layer with squad-scale mechanics layered on top of the vanilla division engine. Direct-fire combat now respects terrain line of sight, uses probabilistic hit resolution, and applies suppression that lingers across turns. Infantry units can toggle between Combat and Move modes, transforming into their transport vehicles for faster repositioning at the cost of increased vulnerability. The mod also ships with rich debugging tools so you can watch the calculations unfold in real time while tuning scenarios.

## Feature Highlights

- **Line of sight enforcement** – direct-fire attacks are cancelled (with a notification) if terrain, units, or fog block the shot.
- **Hit probability** – every shot rolls against a distance/cover/suppression-aware hit chance; misses truly inflict zero damage.
- **Suppression tracking** – incoming fire builds suppression on the target, reducing its future accuracy while boosting the attacker's odds. Suppression decays each turn and persists through saves.
- **Counter overlays** – a suppression indicator sits on the right edge of each counter, re-colouring from green → yellow → red as suppression climbs.
- **Movement mode system** – infantry units can press `V` to toggle between Combat (foot infantry) and Move (mounted in transport) modes. Units visually transform into their designated carrier vehicle, gaining movement points and action points but becoming more vulnerable to enemy fire.
- **Transport mappings** – configure which transport vehicle each infantry type uses via `Assets/transport-mappings.json`. All unit stats (HP, XP, ammo, fuel) are preserved when toggling between modes.
- **Combat telemetry** – enable debug mode to receive scrolling logs, popup summaries, and Player.log entries for every engagement (hit chance, roll, damage, suppression deltas, HP before/after).
- **Damage guard rails** – if a shot is declared a miss, but the base game still shaved HP, Squad Of Steel restores the defender's HP so the result matches the roll.

## Repository Layout

- `SquadOfSteel.csproj` – .NET Framework 4.8 class library that builds the mod.
- `Scripts/` – Harmony entry point, combat patches, suppression state/indicator logic, keybind handler, and debug UI.
- `Libraries/` – reference assemblies pulled from Hex of Steel (Harmony, Assembly-CSharp, UnityEngine modules, etc.).
- `output/` – build output (`net48/SquadOfSteel.dll`).

## Building

```powershell
dotnet build SquadOfSteelMod.sln -c Release
```

The compiled DLL lands at `output\net48\SquadOfSteel.dll`.

## Installation & Usage

1. Build the project or grab the freshly built DLL from `output\net48`.
2. Deploy the build output:
   - Preferred: run `pwsh .\Scripts\DeployToGame.ps1` (or `powershell` on Windows) to copy both the DLL and `Assets\transport-mappings.json` into the game mod folder.
   - Manual copy, if you prefer explicit commands:
     ```powershell
     Copy-Item .\output\net48\SquadOfSteel.dll `
       "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel\Libraries\SquadOfSteel.dll" -Force
     Copy-Item .\Assets\transport-mappings.json `
       "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel\Libraries\transport-mappings.json" -Force
     ```
3. **Configure transport mappings** (optional):
   - Edit `Assets\transport-mappings.json` to map infantry unit names to their carrier vehicles.
   - **Important**: Carrier vehicles must exist for the faction playing that infantry type. For example, British infantry should use "Daimler Dingo" or "Humber Armored Car", not "M3 Scout Car" (which only exists for Americans).
   - Default mappings support most common infantry types across all factions.
4. Launch Hex of Steel, open the Mods menu, and enable **Squad Of Steel**.
5. In-game controls:
   - `V` – toggles the selected infantry unit between Combat and Move modes. In Move mode, the unit transforms into its carrier vehicle with increased movement/AP but higher vulnerability.
   - `K` – dumps an at-a-glance summary (suppression, estimated hit chance, expected damage) for the selected vs targeted units to the console.
   - `F9` – toggles combat debug mode. When enabled you get:
     - A left-hand overlay listing every combat event with full math.
     - Floating popups summarising hits/misses (hit chance, roll, damage).
     - Player.log entries (including HP before/after, suppression deltas, overlay diagnostics).
6. Play as normal. Direct-fire attacks can miss, suppression numbers float beside counters, suppression decays each turn, and infantry can toggle transport modes for tactical repositioning. All state persists across saves.

### Reference Data

- Run `pwsh .\Scripts\ExportOfficialUnitNames.ps1` to dump the official unit catalog to `output\official-units-export.json` / `.txt`. Pass `-GameInstallPath` if Hex of Steel is not installed in the default Steam location.

## GitHub Kickoff (optional)

If you want to version-control the mod:

```powershell
git init
git add .
git commit -m "Initial commit"
```

Add your remote and push (`git remote add origin …`, `git push -u origin main`) as usual. Adjust branch/remote names to suit your workflow.
