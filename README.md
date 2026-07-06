# FMT.MaddenPlugin

This is a plugin for FMT that allows you to edit and load mod files within Madden 26, Madden 27 and College Football 27 on PC.

## Anti Tamper Module - Madden 26
Uses Madden Modding Community anti tampering module (dpapi.dll and EAAntiCheat.exe) to be able to mod. 

**BE AWARE: This module may trigger antivirus software due to its nature of bypassing anti tampering mechanisms. Use at your own risk.**

## Status
In Development

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