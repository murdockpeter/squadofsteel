# Squad Of Steel

Squad Of Steel augments Hex of Steel’s tactical layer with squad-scale mechanics layered on top of the vanilla division engine. Direct-fire combat now respects terrain line of sight, uses probabilistic hit resolution, and applies suppression that lingers across turns. The mod also ships with rich debugging tools so you can watch the calculations unfold in real time while tuning scenarios.

## Feature Highlights

- **Line of sight enforcement** – direct-fire attacks are cancelled (with a notification) if terrain, units, or fog block the shot.
- **Hit probability** – every shot rolls against a distance/cover/suppression-aware hit chance; misses truly inflict zero damage.
- **Suppression tracking** – incoming fire builds suppression on the target, reducing its future accuracy while boosting the attacker’s odds. Suppression decays each turn and persists through saves.
- **Counter overlays** – a suppression indicator sits on the right edge of each counter, re-colouring from green → yellow → red as suppression climbs.
- **Combat telemetry** – enable debug mode to receive scrolling logs, popup summaries, and Player.log entries for every engagement (hit chance, roll, damage, suppression deltas, HP before/after).
- **Damage guard rails** – if a shot is declared a miss, but the base game still shaved HP, Squad Of Steel restores the defender’s HP so the result matches the roll.

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
2. Copy it to your mods folder, e.g.:
   ```powershell
   Copy-Item .\output\net48\SquadOfSteel.dll `
     "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel\Libraries\SquadOfSteel.dll" -Force
   ```
3. Launch Hex of Steel, open the Mods menu, and enable **Squad Of Steel**.
4. Optional debug controls:
   - `K` – dumps an at-a-glance summary (suppression, estimated hit chance, expected damage) for the selected vs targeted units to the console.
   - `F9` – toggles combat debug mode. When enabled you get:
     - A left-hand overlay listing every combat event with full math.
     - Floating popups summarising hits/misses (hit chance, roll, damage).
     - Player.log entries (including HP before/after, suppression deltas, overlay diagnostics).
5. Play as normal. Direct-fire attacks can miss, suppression numbers float beside counters, and suppression decays each turn while persisting across saves.

## GitHub Kickoff (optional)

If you want to version-control the mod:

```powershell
git init
git add .
git commit -m "Initial commit"
```

Add your remote and push (`git remote add origin …`, `git push -u origin main`) as usual. Adjust branch/remote names to suit your workflow.
