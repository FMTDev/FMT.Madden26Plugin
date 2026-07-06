# Compilation Guide

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- An IDE: [Visual Studio 2022+](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/) with C# extensions
- NuGet package sources configured to restore the FMT packages (`FMT.Core`, `FMT.Compilers`, `FMT.FileTools`)

## Build Configurations

| Configuration | Debug Symbols | Use Case |
|---|---|---|
| `Debug` | Yes (full PDB) | Development and troubleshooting |
| `Release` | None | Standard release build |
| `FMT_PRO` | None | FMT Pro distribution build |

## Building

### Command Line

```bash
# Debug build
dotnet build -c Debug

# Release build
dotnet build -c Release

# FMT_PRO build
dotnet build -c FMT_PRO

# Clean build artifacts
dotnet clean

# Create NuGet package
dotnet pack -c Release
```

### Visual Studio

1. Open `Madden26Plugin.sln`
2. Select the desired configuration from the Solution Configurations dropdown
3. Build → Build Solution (`Ctrl+Shift+B`)

### VS Code

1. Open the workspace folder
2. `Terminal` → `Run Build Task` (`Ctrl+Shift+B`) or select a specific task from `Terminal` → `Run Task`
3. Pre-configured tasks are available in `.vscode/tasks.json`:
   - `build` — default build
   - `build (Debug)` / `build (Release)` / `build (FMT_PRO)` — configuration-specific
   - `clean` — clean artifacts
   - `restore` — restore NuGet packages
   - `pack` — create NuGet package

## NuGet Package Source

The project depends on FMT NuGet packages (`FMT.Core`, `FMT.Compilers`, `FMT.FileTools`). Ensure your NuGet configuration has the appropriate package source (e.g., a local/private feed or the FMT registry). If you encounter restore errors, verify your `NuGet.config` includes the source where these packages are hosted.

## Output

The compiled plugin DLL (`Madden26Plugin.dll`) is placed in:

```
bin/{Configuration}/net10.0/Madden26Plugin.dll
```

For example: `bin/Release/net10.0/Madden26Plugin.dll`

## Notes

- Unsafe code is enabled (`AllowUnsafeBlocks=true`) — required for Oodle P/Invoke and binary reading/writing performance.
- Embedded resources (`Launcher/CryptBase.dll`, `dpapi.dll`, `EAAntiCheat.GameServiceLauncher.exe`) are automatically included during build.
