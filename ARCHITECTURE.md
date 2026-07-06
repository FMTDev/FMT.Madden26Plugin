# Architecture Overview

**Madden26Plugin** is a plugin for the [FMT (Frostbite Modding Tool)](https://fmt.dev) framework that enables reading, editing, compiling, and exporting assets for Madden NFL 26, Madden NFL 27, and College Football 27 (PC). It handles Frostbite engine proprietary formats (TOC, CAS, EBX, RES, chunks, textures, meshes) and includes anti-cheat bypass support.

## Technology Stack

- .NET 10.0, C#
- NuGet: `FMT.Core`, `FMT.FileTools`, `FMT.Compilers`, `FMT.PluginInterfaces`
- `HashDepot` for hashing
- P/Invoke `Oodle` compression (`oo2core_*` DLLs)
- Embedded `CryptBase.dll`, `dpapi.dll`, `EAAntiCheat.GameServiceLauncher.exe`

## Profiles

| File | Description |
|---|---|
| `Madden26Profile.json` | Profile for Madden NFL 26 |
| `Madden27Profile.json` | Profile for Madden NFL 27 |
| `CFB27Profile.json` | Profile for College Football 27 |

## Data Flow

1. **Loading** — `Madden26AssetLoader` resolves `.toc` files, `Madden26TOCFile` reads chunk metadata + CAS bundle references, CAS files are read in parallel, assets registered with `IAssetManagementService`.
2. **Caching** — `Madden26CacheWriter` serializes loaded data into a binary `.cache` file. On next launch `Madden26CacheReader` loads from cache if still valid.
3. **Compilation** — `Madden26AssetCompiler` (or `Madden26AssetCompiler2025`) fixes bundle-less mods, iterates super bundles, writes modified EBX/RES/chunk data into CAS files, rewrites TOC files with new offsets/sizes, deploys anti-cheat bypass DLLs post-compile.
4. **Texture I/O** — `Madden26TextureResourceReader`/`Writer` handles `.texture` resource binary format.
5. **Mesh I/O** — `Madden26MeshSetReader`/`Writer` handles LODs, sections, bone data, relocation tables for rigid (static) and skinned (animated) meshes.

---

## File-by-File Explanation

### Root — Plugin Entry Points

| File | Purpose |
|---|---|
| `Madden26AssetLoader.cs` | Implements `IAssetLoader`. Loads game assets from `.toc` files and associated CAS bundles. Resolves TOC paths, reads CAS bundles in parallel, populates the asset management service. |
| `Madden26CustomAssetEntryEnumerations.cs` | Implements `ICustomAssetEntryEnumerations`. Provides custom asset entry filters — specifically groups jersey assets from player uniform paths. |
| `Madden26ModsCleanupFunctions.cs` | Implements `ICleanupFunction`. Cleans up after mod compilation: deletes injected DLLs (`CryptBase.dll`, `dpapi.dll`) and restores original `EAAntiCheat.GameServiceLauncher.exe` from backup. |

### TOC Layer (`TOC/`)

| File | Purpose |
|---|---|
| `Madden26TOCFile.cs` | Extends `TOCFile`. Reads the Madden 26 `.toc` format: chunk GUIDs, offsets, sizes, catalog/CAS/patch info, and CAS bundle entries. Maps assets to physical locations. |
| `Madden26TOCFileWriter.cs` | Extends `TOCFileWriter`. Writes modified CAS bundle data back into `.toc` files — CAS identification headers and bundle entry offsets/sizes. |

### Cache Layer (`Cache/`)

| File | Purpose |
|---|---|
| `Madden26CacheReader.cs` | Implements `ICacheReader`. Reads binary cache file (`_GameCaches/{ProfileName}.cache`) with pre-loaded asset metadata. Validates version, profile, system iteration, EXE timestamp to detect staleness. |
| `Madden26CacheWriter.cs` | Implements `ICacheWriter`. Serializes in-memory asset data (bundles, EBX, RES, chunks) to binary cache with SHA1 hashes, sizes, and locations. |
| `Madden26CacheHelpers.cs` | Internal helper: cache path resolution, system iteration calculation from `layout.toc`, and EXE last-write-time retrieval for cache invalidation. |

### Compiler Layer (`Compiler/`)

| File | Purpose |
|---|---|
| `Madden26AssetCompiler.cs` | Implements `IAssetCompiler` (base: `FrostbiteNullCompiler`). Main compile pipeline: pre-compile cleanup, mod bundle reading, bundle-less mod fix, writing modified data to CAS, TOC rewrite, chunk modification, and anti-cheat deployment. Contains `OodleCompress`/`CompressFile` helpers. |
| `Madden26AssetCompiler2025.cs` | Implements `IAssetCompiler` (base: `Frostbite2025AssetCompiler`). Alternative compiler targeting Frostbite 2025 SDK. Fixes bundle-less mods, delegates to base class, overrides `WriteNewDataChangesToSuperBundles` to use `BundleWriter` + `Madden26TOCFileWriter`. |
| `BinarySbWriter.cs` | Implements `DbWriter`. Writes Frostbite super-bundle binary format: magic (`0xDEADBABE`), salted magic, ebx/res/chunk counts, SHA1s, name hashes, offsets, chunk GUIDs. |
| `HuffmanDecoder.cs` | Huffman decoder for reading Huffman-encoded string tables. Contains `HuffmanNode` tree and methods to decode bit-packed strings. |

### Texture Layer (`Textures/`)

| File | Purpose |
|---|---|
| `Madden26TextureResourceReader.cs` | Implements `ITextureResourceReader`. Parses mip offsets, type, pixel format, dimensions, mip count, chunk ID, texture group name, and loads texture data via chunk entries. |
| `Madden26TextureResourceWriter.cs` | Implements `ITextureResourceWriter`. Serializes texture to binary: mip offsets, type, pixel format, dimensions, chunk ID, mip sizes, texture group, unknown bytes. |

### Mesh Layer (`Meshes/`)

#### Readers

| File | Purpose |
|---|---|
| `Madden26MeshSetHeader.cs` | POCO for mesh set header (HeaderSize, HeaderUnk1–3). |
| `Readers/Madden26MeshSetReader.cs` | Implements `IMeshSetReader`. Orchestrates reading: bounding box, LOD offsets, name hash, mesh type, then delegates to `RigidMeshReader` or `SkinnedMeshReader`. |
| `Readers/Madden26MeshHeaderReader.cs` | Reads the 16-byte mesh header (4 int32s). |
| `Readers/Madden26MeshSetLodReader.cs` | Reads a single LOD: type, instance/section counts, category subset indices, layout flags, buffer sizes, chunk ID, bone arrays (skinned), part bounding boxes (composite), sections, debug names. |
| `Readers/Madden26MeshSetSectionReader.cs` | Implements `IMeshSetSectionReader`. Reads a mesh section: offsets, name, bone count, vertex stride, primitive type/count, texture coords, geometry declarations, bone lists. Handles Madden27/CFB27 differences. |
| `Readers/Madden26RigidMeshReader.cs` | Reads rigid (non-skinned) mesh set: header, bounding box, LOD offsets, fade values, layout flags, draw order, delegates LODs. |
| `Readers/Madden26SkinnedMeshReader.cs` | Reads skinned mesh set: similar to rigid with additional bone data, cull box, inline vertex/index buffers. Reads layout/vertex sizes from RES metadata. |
| `Readers/Madden26CompositeMeshReader.cs` | (Disabled.) Would read composite mesh sets with multiple parts. |

#### Writers

| File | Purpose |
|---|---|
| `Writers/Madden26MeshSetWriter.cs` | Implements `IMeshSetWriter`. Entry-point writer: bounding box, LOD/name pointers, type, layout flags, LOD data, sections, bones, strings, category subsets. Delegates to rigid writer. |
| `Writers/Madden26RigidMeshSetWriter.cs` | Writes rigid mesh sets (slightly different layout from skinned). |
| `Writers/Madden26CompositeMeshSetWriter.cs` | (Stub.) Not implemented. |
| `Writers/Madden26MeshSetLodWriter.cs` | Implements `IMeshSetLodWriter`. Writes a LOD: type, instance/section counts, section/category subset pointers, flags, buffer sizes, chunk ID, inline data offset, bone arrays, debug names. |
| `Writers/Madden26MeshSetSectionWriter.cs` | Implements `IMeshSetSectionWriter`. Writes a mesh section: offset, name/bone-list pointers, bone count, vertex stride, primitive data, texture coords, geometry declarations, trailing bytes. |
| `Writers/Madden26MeshSetHeaderWriter.cs` | Writes the 16-byte header (fixed values: 192, 188, 368, 0). |
| `Writers/Madden26MeshContainer.cs` | Extends `MeshContainer`. Handles relocation pointer/array/string tracking for position-independent mesh serialization. |

### Third Party (`ThirdParty/`)

| File | Purpose |
|---|---|
| `Oodle.cs` | P/Invoke wrapper for `oo2core_*` DLL. Provides `Bind`, `Compress`, `CompressKraken`, `CompressLeviathan` for Oodle texture compression. |
| `LoadLibraryHandle.cs` | Safe handle wrapper around `LoadLibraryEx`/`FreeLibrary` for loading the native Oodle DLL. |
