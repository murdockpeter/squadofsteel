# Squad Of Steel

Squad Of Steel augments Hex of Steel's tactical layer with squad-scale combat systems layered on top of the vanilla division engine. The mod introduces true line of sight for direct fire, probabilistic hit resolution, and a suppression model that persists across turns. It also embeds an optional debug overlay so you can watch every combat computation in real time while tuning scenarios or troubleshooting behaviour.

## Feature Highlights

- Line-of-sight enforcement: direct-fire attacks are cancelled (with a notification) if terrain, units, or other blockers obscure the path between shooter and target.
- Hit probability: each attack now rolls against a distance/cover/suppression-aware hit chance, so shots can miss and damage varies around the vanilla baseline.
- Suppression tracking: targets accumulate suppression when fired upon, lowering their future accuracy and boosting the attacker's odds. Suppression decays automatically each turn and persists across saves.
- Combat debug overlay: when enabled, a slim scrolling panel on the left side of the screen lists every engagement with the underlying calculations (hit chance, RNG roll, damage, suppression deltas).

## Repository Layout

- `SquadOfSteel.csproj` - .NET Framework 4.8 class library that builds the mod DLL.
- `Scripts/` - Harmony entry point, combat systems, suppression state, UI helpers, keybinds, and overlay logic.
- `Libraries/` - reference assemblies pulled from the Hex of Steel installation (Harmony, Assembly-CSharp, UnityEngine modules, etc.).
- `output/` - build output (`net48/SquadOfSteel.dll`).

## Building

```powershell
dotnet build SquadOfSteelMod.sln -c Release
```

The compiled DLL is emitted to `output\net48\SquadOfSteel.dll`.

## Installation & Usage

1. Build the project (or use the latest DLL in `output\net48` after a build).
2. Copy the DLL into your Hex of Steel mods directory, for example:
   ```powershell
   Copy-Item .\output\net48\SquadOfSteel.dll `
     "$Env:LOCALAPPDATA\..\LocalLow\War Frogs Studio\Hex of Steel\MODS\Squad Of Steel\Libraries\SquadOfSteel.dll" -Force
   ```
3. Launch Hex of Steel, open the Mods menu, and enable **Squad Of Steel**.
4. In-game keybinds:
   - `K` - log a quick briefing for the selected vs targeted unit pair (suppression, estimated hit chance, expected damage) to the console.
   - `F9` - toggle combat debug mode. When enabled, the left-hand overlay appears and every attack streams to it (and to the console/notifications) with full math.
5. Play as normal. Direct-fire attacks now respect line of sight, can miss, and apply suppression. Suppression values decay when turns advance and are saved/restored transparently.

## GitHub Kickoff (optional)

If you plan to version-control the mod:

```powershell
git init
git add .
git commit -m "Initial commit"
```

Add your remote and push as usual (`git remote add origin ...`, `git push -u origin main`). Adjust branch/remote names to match your workflow.
