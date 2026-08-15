# FMT.MaddenPlugin

This is a plugin for FMT that allows you to edit and load mod files within Madden 26, Madden 27 and College Football 27 on PC.

## Anti Tamper Module - Madden 26
Uses Madden Modding Community anti tampering module (dpapi.dll and EAAntiCheat.exe) to be able to mod. 

**BE AWARE: This module may trigger antivirus software due to its nature of bypassing anti tampering mechanisms. Use at your own risk.**

## Status

In Development. Plugin loads in FMT and supports editing Madden 26, Madden 27 and College Football 27 (CFB27). Newer features (Roster Editor, face cloning) are CFB27-focused.

### Feature Status

**Working**
- Load/save CFB27 roster containers; export to JSON and CSV
- Player editor: browse + search tabs, names, height, weight, jersey number, equipment slots, face ID
- Bulk swap equipment across players
- Assign teams (TeamMap / On3TeamMap / SidearmTeamMap)
- Generate roster from NCAA / On3 / Sidearm Sports websites (scrapers + preview + manual adjust)
- Face template matching and hair color mapping for generated players
- Clone face to Unique:
  - Clone selected face to a Unique_ template, or clone all Generic_ faces at once
  - Creates a new player recipe EBX + its `_playerhead_brt` entry in FMT's asset manager
  - Applies the matched **hair** color recipe (root/tip colors to hair, eyebrow, beard)

**Known Incomplete / Not Working**
- **Eye color cloning is not applied.** Cloned faces inherit the source template's eye color (`ApplyEyeColor` is stubbed out in `Roster/CyberfaceCloner.cs`). Plan: read the target eye recipe EBX, extract its file/class GUIDs, build an external reference object, and set it via `ComplexionPresetMapper.SetFieldValue`.
- Face cloning has not been verified in-game end-to-end (works in-editor, output `.fbmod` not yet tested inside the game).
- Auto-generated CFB27 roster container writing is experimental — saved rosters may be rejected by the game until the container hash algorithm is fully reversed (see commit history on roster hash investigation).

## Code Architecture
See [ARCHITECTURE.md](ARCHITECTURE.md) for a detailed file-by-file explanation of the codebase.

## Compilation
See [COMPILATION.md](COMPILATION.md) for build instructions.

### Deploying to FMT

After building, copy `Madden26Plugin.dll` to your FMT installation's `Plugins` folder (usually `FMT/Plugins/`).

**VS Code auto-deploy:** Set a `FMT_PLUGINS_DIR` environment variable pointing to your FMT Plugins folder, then use the build tasks:
- `copy-to-fmt (Debug)` — builds Debug and copies the DLL
- `copy-to-fmt (Release)` — builds Release and copies the DLL
- `copy-only (Debug)` / `copy-only (Release)` — copy an already-built DLL only

### Attaching a Debugger to FMT

1. Build the plugin in **Debug** configuration (with debug symbols)
2. Copy the DLL to the FMT `Plugins` folder
3. Launch FMT
4. In VS Code, open the Run & Debug view (`Ctrl+Shift+D`)
5. Select **"Attach to FMT"** from the dropdown
6. Click the green play button (or press `F5`)
7. Set breakpoints in the plugin code — they will be hit when FMT loads and uses the plugin

> The pre-configured launch profile in `.vscode/launch.json` uses `processName: "FMT.exe"` to find the target process.