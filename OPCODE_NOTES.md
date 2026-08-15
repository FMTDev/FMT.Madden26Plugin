# Anti-Cheat Bypass Investigation Notes (opencode sessions)

## Goal
Get the anti-cheat bypass working for all 4 supported games (Madden 25, Madden 26, Madden 27, CFB 27).
Secondary (paused): Reverse-engineer FBChunks roster file checksum algorithm.

## Key Facts About Paul's Bypass

### The Launcher (EAAntiCheat.GameServiceLauncher.exe)
- 92,160-byte (Paul's original) or 95,232-byte (user's variant) x64 native C++ EXE
- 6 PE sections including `.detour`, `.detourc`, `.detourd` (Microsoft Detours API hooking)
- Full injection engine using Detours to hook into EA Desktop
- Hardcoded game exe detection + community messages
- Uses `CreateProcessA`, `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread`, `LoadLibraryW`

### What the Launcher Does
1. Detects which game is in the directory (Madden 25/26/27, CFB27, trial variants)
2. Sets `LCP.BootOverride` registry key per game
3. Injects `CryptBase.dll` and `dpapi.dll` into EA Desktop via Detours
4. Shows community message
5. Launches the game process
6. Waits for game to exit

### Proxy DLLs (CryptBase.dll, dpapi.dll)
- These are Frostbite mod injection hooks, NOT anti-cheat bypass
- They need to be in the game directory — the game loads them via DLL search order hijacking
- FMT already deploys these in PostCompile

## Session 2026-07-28: Custom Stub + Binary Patched Launcher

### Attempt 1: Pure Stub (exits 0)
- Built `Tools/ACBypassStub/ACBypassStub.exe` — simple C console app
- Detected game, set registry, exited 0
- **Result**: Flashed cmd window, then crashed back to EA app
- **Fix**: Added game process waiting (stays alive until game exits)
- **Result**: Stub kept running, still crashed back to EA app

### Attempt 2: Pure Stub + Wait + DLLs in directory
- User clarified: DLLs go in game directory, no injection needed
- Stub waits for game process, stays alive
- DLLs handled by FMT (PostCompile)
- **Result**: Still crashed — game checks launcher does more than just stay alive

### Attempt 3: Binary Patch Working Launcher
- User had `xEAAntiCheat.GameServiceLauncher.exe` (95,232 bytes) that WORKS
- Created `Tools/PatchLauncher/` — C# patcher tool
- Nulled out community/offline message strings at offsets 0xA070 and 0xA1A0
- Patched trial version warning at 0xA2F0
- **Result: WORKS standalone** — game launches with patched launcher
- Deployed patched launcher as embedded resource in plugin

### Binary Patched String Offsets (for x variant)
| Offset | Original | Replacement |
|--------|----------|-------------|
| 0xA070 | "Mods require being offline..." (143 chars) | "Anti-Cheat bypass active." |
| 0xA1A0 | "Mods require being offline..." (135 chars) | "Anti-Cheat bypass active." |
| 0xA2F0 | "Trial version detected..." (110 chars) | "Trial detected." |

### Plugin Build + Deploy
- Built plugin with `FMT_PRO` config — embeds patched 95KB launcher
- Deployed to `C:\Users\Ninja\Documents\Mods\FMT_PRO\Plugins\Madden26Plugin.dll`
- **Unresolved**: Launch from FMT mod manager failed (crashed back to EA app)

## Critical Issue: PostCompile Unreliability (STILL UNRESOLVED)

### FMT Launch Flow
1. `BuildModData` (always runs)
2. Compiler (sometimes runs, sometimes skipped) → PostCompile fires here
3. `RunEADesktop` (always runs)

### PostCompile Only Runs When Compiler Runs
- PostCompile is only called when FMT compiler decides to rebuild mod data
- If compiler is skipped → bypass files (patched launcher, DLLs) are NOT deployed
- This means game launches without bypass → crashes back to EA app

### Madden26AssetCompiler2025.OnProcessEnded Bug
- `OnProcessEnded` calls `CleanUp()` which **DELETES** CryptBase.dll and dpapi.dll
- This runs AFTER game exits — so bypass is deleted for next launch
- Makes the bypass deployment one-shot only

### Possible Fixes (Not Yet Implemented)
1. Move bypass deployment to a hook that runs on EVERY launch (not just compile)
2. Add deployment to `Madden26AssetLoader.LoadData()` or similar always-run method
3. Remove cleanup from `OnProcessEnded` (keep it only in PreCompile)
4. Add deployment to `Madden26ModsCleanupFunctions` so it's self-healing

## Files to Know

### Plugin Source
- `Madden26Plugin.csproj` — project file (embeds CryptBase.dll, dpapi.dll, launcher)
- `Compiler/Madden26AssetCompiler2025.cs` — main compiler with PostCompile + OnProcessEnded
- `Compiler/Madden26AssetCompiler.cs` — alternative compiler with PostCompile
- `Madden26ModsCleanupFunctions.cs` — cleanup (deletes bypass, restores backup launcher)
- `Madden26AssetLoader.cs` — loads assets, runs on EVERY launch (potential deploy hook)
- `Launcher/EAAntiCheat.GameServiceLauncher.exe` — NOW the patched working launcher (95KB)
- `Launcher/CryptBase.dll` — Frostbite mod injection hook (95,744 bytes)
- `Launcher/dpapi.dll` — Frostbite mod injection hook (276,480 bytes)

### Tools
- `Tools/ACBypassStub/` — C stub attempt (main.c, build.bat) — no injection, not used
- `Tools/PatchLauncher/` — C# binary patcher for launcher strings
- `Tools/ACBypass/Program.cs` — simple C# launcher runner

### Build
- MSVC at: `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\cl.exe`
- Plugin DLL deployed to: `C:\Users\Ninja\Documents\Mods\FMT_PRO\Plugins\Madden26Plugin.dll`
- FMT logs: `C:\Users\Ninja\Documents\Mods\FMT_PRO\Logging\`

### Game Directories
- `C:\Program Files\EA Games\Madden NFL 26\`
- `C:\Program Files\EA Games\EA SPORTS College Football 27\`

## Roster Checksum Investigation
See `opencode_memory.md` for full details.

## Session 2026-08-15: Environment Rebuild + DotNetZip Swap (PAUSED — awaiting PaulV)

### Environment status (tools)
- Git reinstalled (2.55.0.3) via winget — was missing after opencode reinstall
- .NET 10 SDK 10.0.400 present, MSVC cl.exe 19.51 present
- All 3 projects build clean (CFB27RosterEditor, PinkSlipsTool, Madden26Plugin)
- PinkSlipsTool root has leftover `PinkSlipsTool_tlo4etbx_wpftmp.csproj` — bare `dotnet build` in that folder fails with MSB1011; must target the csproj explicitly
- Git status: FMT plugin has uncommitted changes (Launcher exe, csproj, untracked Tools/, OPCODE_NOTES.md); PinkSlipsTool has 2 untracked Views; CFB27RosterEditor 2 commits ahead of origin

### Pending: DotNetZip -> ProDotNetZip swap (BLOCKED on PaulV answer)
- NU1903: DotNetZip 1.16.0 has CVE-2024-48510 (GHSA-xhg6-9j5j-w4vf), high severity 8.6, directory traversal via ZipEntry.Extract.cs. No patched release in original package (unmaintained); fixed fork is `ProDotNetZip` >= 1.19.0 (drop-in, same Ionic.Zip/Ionic.Zlib API).
- Our code only uses `Ionic.Zlib.GZipStream` (gzip/deflate) — vulnerable zip-extract path NOT used.
- Compatibility concern: `CopyLocalLockFileAssemblies=true` in Madden26Plugin.csproj means plugin ships its own DotNetZip.dll; if FMT host loads DotNetZip too, ProDotNetZip (same assembly name Ionic.Zip) could collide/shadow. MUST ask PaulV before swapping in FMT plugin.
- CFB27RosterEditor and PinkSlipsTool are standalone WPF apps — safe to swap anytime without asking.
- Draft summary message to PaulV written in session (see user chat history 2026-08-15); resume: user sends PaulV answer, then perform swap.

## Session 2026-08-15 (part 2): Porting Madden 27 player models/heads into Madden 20 (PAUSED — user will prepare exports)

### Goal
Port M27 player bodies/heads/textures ("the looks") into M20, at scale (many/all players), without losing existing M20 players (duplication needed) and ideally without per-player manual Blender work. Also port M27 player portraits into M20's portrait `.ast` atlas.

### Toolchain reality (important correction)
- **MMC Editor supports M24–M27 only — NOT Madden 20.** Its MADDEN20SDK.dll is not loadable by MMC. M20 uses the **classic FrostyEditor** (`C:\Users\Ninja\Documents\Mods\M20 mods\FrostyEditor\`).
- M20 FrostyEditor DOES ship its own `Plugins\DuplicationPlugin.dll` (35KB, "Asset Duplication" by Cade, v1.0.0.0, Copyright 2020) + `Plugins\MeshSetPlugin.dll` (291KB). Decompiled to `%TEMP%\opencode\m20_dup_src\DuplicationPlugin.decompiled.cs`.
- **M20's own DuplicationPlugin already has**: right-click "Duplicate" on any asset (DuplicateContextMenuItem) + DuplicateAssetWindow + all type extensions: MeshExtension, TextureExtension, AtlasTextureExtension, SvgImageExtension, SoundWaveExtension, PathfindingExtension, BlueprintBundleExtension, SubWorldDataExtension, ClothWrappingExtension, ClothExtension, ObjectVariationExtension. Plus static DuplicateChunk / DuplicateRes.
- **M20's plugin LACKS (vs MMC's newer DuplicationPlugin.dll)**: the `DuplicateFaceMenuExtension` (Cyberface Duplicator, Tools > "Duplicate Face") — the whole player-head duplication flow (enumerates player head path, dups ObjectBlueprint+SkinnedMeshAsset+Textures, rewires refs, registers into `content/common/logic/bundlereftables/playerhead_brt`). That flow is what we'd add to M20.
- MMC's DuplicationPlugin source: `%TEMP%\opencode\dup_src\DuplicationPlugin\DuplicationTool.cs` (996 lines). DuplicateFaceMenuExtension gated at line 605: `if (ProfilesLibrary.DataVersion < 20240613)` → "Face duplication is not supported for this game." M20's DataVersion is ~20200714, so the gate blocks M20 — would need to remove/bypass it.
- DuplicateFace flow details (from MMC source): uses `brtTypes = {ObjectBlueprint, TextureAsset}`, `unwantedTypes` filter, dups each asset via the same extension registry, then fixes refs on ObjectBlueprint (rootObject.Object.Internal.Mesh) and MeshVariationDatabase (Entries[0].Mesh + Materials TextureParameters).
- M20 FrostyEditor plugin list (all present): AtlasTexture, BiowareLocalization, Blank, BundleEditor, ChunkResEditor, Connection, Conversation, DelayLoadBundle, DifficultyWeaponTableData, Duplication, EbxToXml, FsLocalization, IesResource, LaunchPlatform, LocalizedString, Lua, MeshSet, ObjectVariation, ProjectMerger, References, RefreshMeshVariations, RootInstanceEntries, SoundEditor, SvgImage, Test, Texture, TypeExplorer, VersionData + Legacy*. **No BundleRefTablePlugin.dll in M20 editor** — need to check whether BundleRefTableResource/EnumerateRes is in M20's FrostyCore/SDK (BundleRefTableResource.cs exists in both m20sdk_src and m27sdk_src under FrostySdk.Ebx).

### Confirmed M20 SDK APIs for building the flow
- `AssetManager.AddEbx` (line 1891), `AddRes` (1923), `AddChunk` (1958) exist in M20 FrostySdk.dll.
- `EnumerateRes(uint resType=0, bool modifiedOnly=false, string bundleSubPath="")` exists (AssetManager.cs:2244). `ProfilesLibrary.DataVersion` exists (ProfilesLibrary.cs:46).

### The head-vs-hair mystery (MAIN TECHNICAL BLOCKER, still open)
- User's finding: **hair meshes transfer onto the M20 `fbhero` skeleton fine, but heads do not** — "something is different with the heads."
- Hypothesis: bodies/hair skin to the shared fbhero body skeleton (bone layout stable across games); heads bind to a head/facial skeleton (FacePoser4 joints: jaw/eyes/brows) whose bone set/ordering differs M20 vs M27. M27 also added AntSkeletonAsset + AntRef `_RigAsset` on SkeletonAsset + CharacterMorphHeadRecipeItem recipe classes (CharacterHeadCheek/Eye/Ear/Chin/EyeBrow/FaceSkinDetailItem) — heads moved to a morph-recipe/Ant rig system.
- M20 head EBX classes: FacePoser4DataAsset, FacePoser4JointData, FaceAnimationWaveMappings(Asset), CreateFacePoser4Params.
- Plan to prove + automate: export one head set (SkinnedMeshAsset EBX + SkeletonAsset BoneNames + MeshSet) from M27 and the same from M20, diff BoneNames arrays → build bone-name→index remap → tool rewrites M27 head MeshSet bone indices to M20 indices → inject into M20 as duplicated asset (via M20's Duplicate plugin path) → kills per-player Blender retarget.

### FMT mass export
- FMT has a **mass export feature** — user can export entire folders of M27 assets (covers extraction side; MMC not needed for bulk).

### Portrait porting (confirmed doable, not yet built)
- M20 portrait system: `PortraitData` (FirstName/LastName/PortraitImageId/PortraitLibraryId/LogoImageId/LogoLibraryId/TeamColor*/StatText) + `PlayerPortraitIdElement` (ActorName→PlayerPortraitIdValue). Portrait images live in an atlas: `AtlasTextureAsset : AtlasTextureBaseAsset` → `ResourceRef Resource` → AtlasTexture RES (the `.ast` file). IDs are ints.
- M27 `Library_PlayerPortraitsImageId` shows bounded ID space: `AssetId_plpo_Blank=0`, `Plpo_Library_Count=9466`, `Plpo_Library_Max=14374`.
- User's approach: export all M27 player portraits, add into M20 player-portraits `.ast`, import `.ast` into M20 via their AST editor; **the task to automate = assigning unique portrait IDs** (computer-perfect job).
- Tool to build: read M20 atlas used-ID set → read M27 exported portraits (names + native IDs from M27 data) → allocate IDs (keep M27 ID if free, else next free in range) → emit mapping (M27 player → assigned ID) and optionally patch M20 roster/PortraitData so ported player's PortraitId matches atlas sub-image.
- **Requirement to ask user for**: M20 portraits `.ast` + AST editor export/import format, M27 portrait export folder. (NOT yet received — user paused before providing.)

### Next steps when resumed
1. Receive user's exported M27 head set + M20 head set → build bone-name diff/remap script.
2. Receive M20 `.ast` + AST editor format + M27 portrait export folder → build portrait ID allocator.
3. Add missing DuplicateFaceMenuExtension flow to M20's DuplicationPlugin (port from MMC source, bypass DataVersion gate, verify against M20 SDK APIs incl. BundleRefTableResource).

## Session 2026-08-15 (part 3): FMT Madden26Plugin — upstream merge + ProDotNetZip + deploy (ACTIVE — awaiting PaulV review + FMT in-game test)

### Goal / current state
- Plugin now ONE DLL (`Madden26Plugin.dll`) serving Madden 26, Madden 27, AND College Football 27 (CFB27). College stuff is STILL in the Madden26 plugin — no separate CFB plugin. `FC27Plugin.dll` = EA FC 27 soccer, unrelated.
- User asked Paul to review the merged repo before we continue finishing roster display + the other bits.

### FMT install locations
- NEW FMT (v27): `C:\Users\Ninja\Documents\Mods\FMT_PROFMT_PRO.27.0.9716.24243` (FMT.exe 27.0.9716.24243). Note: the correct path is `FMT_PRO` + `FMT_PRO.27...` — there is NO `FMT_PRO\FMT_PRO\` subfolder.
- OLD FMT (v26): `C:\Users\Ninja\Documents\Mods\FMT_PRO` (FMT.exe 26.11.9680.21261).
- Profiles: `Plugins\Madden26Profile.json`, `Plugins\Madden27Profile.json` (both `PluginNames: ["Madden26"]`); `FrostbiteProfiles\CFB27Profile.json` (`AssetCompiler: CFB27AssetCompiler`, `SDKFilename: CFB27SDK`, `CanImportMeshes: true`, DataVersion 20260709). Madden27 profile: `Madden27AssetCompiler`, `Madden27SDK`, DataVersion 20260813. Madden27/CFB27 use `Madden27TOCFileWriter`.
- FMT host ships NO DotNetZip.dll / Ionic.* (verified recursively in both installs) — plugin provides its own copy.

### Repo / merge history
- Local repo: `C:\Users\Ninja\Source\Repos\FMT.Madden26Plugin`. Remotes: `origin`/`fork` = DarthNinja0/FMT.Madden26Plugin, `upstream` = FMTDev/FMT.Madden26Plugin (added this session).
- Upstream `main` = f8c1149 (2026-08-01, "Update with latest Nuget Packages and support latest FMT"). Local `main` had only merged through upstream 2026-07-03.
- **Local-only commits NOT in upstream** (2026-07-08/09): the entire Roster tooling — `Roster/CyberfaceCloner.cs`, `RosterTool.cs`, `CFB27RosterReader.cs`, `CFB27RosterWriter.cs` (uses `Ionic.Zlib` → DotNetZip), `ComplexionPresetMapper.cs`, `FaceTemplateMatcher.cs`, `HairColorMapper.cs`, `TeamMap.cs`/`On3TeamMap.cs`/`SidearmTeamMap.cs`, scrapers (NCAAStats/On3/SidearmSports), `Roster/Views/*` (RosterEditorWindow, GenerateRosterDialog, BulkSwapDialog, AssignTeamDialog).
- Commits this session: d843c33 (save uncommitted local work pre-merge), 9d605aa (merge upstream), e6b2bdf (ProDotNetZip swap), 031761d (README status). Pushed to fork: `31ca16b..e6b2bdf`, then `e6b2bdf..031761d`.

### Merge conflict (csproj only) — resolution kept
- Local WPF setup kept: `net10.0-windows`, `UseWPF`, AssemblyVersion/FileVersion, `CopyLocalLockFileAssemblies`, RosterAnalyzer/Tools/TestDeflate csproj exclusions, `EmbedBamlResources` target (needed for Roster WPF windows).
- Upstream packages adopted: FMT.Compilers 2026.10.0, FMT.FileTools 2026.10.0, FMT.Core 2026.10.1.
- Added upstream's `Launcher\CFB27\EAAntiCheat.GameServiceLauncher.exe` as embedded resource.
- Build: `dotnet build Madden26Plugin.csproj -c FMT_PRO` → `bin\FMT_PRO\net10.0-windows\Madden26Plugin.dll` (840,192 B). Only warnings: CA2200 (pre-existing).

### DotNetZip -> ProDotNetZip swap (DONE — no longer blocked)
- ProDotNetZip **1.20.0** (nuget.org, drop-in: keeps `Ionic.Zip`/`Ionic.Zlib` namespaces) replaces DotNetZip 1.16.0 → NU1903/CVE-2024-48510 warning gone.
- Why safe: FMT host ships no DotNetZip (no shadow/collision risk); only usage is `CFB27RosterWriter.cs:119` `Ionic.Zlib.GZipStream` (safe compress/decompress path — never the vulnerable `ZipEntry.Extract`).
- Deployed: `Madden26Plugin.dll` + **`ProDotNetZip.dll`** (312,320 B) to `...\FMT_PROFMT_PRO.27.0.9716.24243\Plugins\`. IMPORTANT: ProDotNetZip must sit NEXT TO the plugin (host doesn't provide it). Original shipped DLL backed up as `Madden26Plugin.dll.upstream.bak` (648,704 B) in same folder.

### Verification
- ilspycmd correct syntax: `ilspycmd -l c <dll>` lists classes (NOT `-t -l`). Confirmed deployed DLL contains BOTH roster classes (CyberfaceCloner, RosterEditorWindow, RosterEditorPluginTool, all scrapers/dialogs) AND all 3 compilers (Madden26AssetCompiler2025, Madden27AssetCompiler, CFB27AssetCompiler).

### In-game test (user, after deploy)
- FMT loads the plugin cleanly with new FMT v27. "Duplicate faces" flow was NOT in-game tested (still incomplete — see below). User will have Paul review repo before we continue.

### Known incomplete (documented in README, commit 031761d)
- **Eye color cloning is a stub**: `CyberfaceCloner.ApplyEyeColor` (`Roster/CyberfaceCloner.cs:86-99`) does nothing — cloned faces inherit source template eye color. Plan (in code comment): read target eye recipe EBX at `ContentShared/content/characters/HS/HS_common/HS_eye_color/{recipe}`, extract file/class GUIDs, build External reference object via reflection, `ComplexionPresetMapper.SetFieldValue(rootObj, "EyeColorRecipe", ref)`.
- Face cloning verified in-editor only; output `.fbmod` not yet tested in-game.
- CFB27 roster container writing experimental (hash algorithm not fully reversed — see commit history 31ca16b/b457637).

### Resume checklist
1. Get PaulV's review feedback on merged repo.
2. Finish making all roster info display properly (RosterEditorWindow).
3. Possibly finish eye color cloning + in-game verify cloned faces.
4. Long-term: CFB27 roster container hash reversal (from earlier notes) if needed for in-game roster acceptance.
