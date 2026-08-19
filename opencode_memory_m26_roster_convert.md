# Madden 27 Beta Roster → Madden 26 Converter (COMPLETE)

## Status: DONE ✅
Converted `ROSTER-MADDEN27` (Madden 27 Beta) into a working Madden 26 save.
**Verified in-game**: loads, played plays with Browns + Bengals, no crash. (Aug 16, 2026)

The converted file sits in the Madden 26 saves folder as `ROSTER-M27CONVERTED`.
Next user task (unrelated to this doc): import M27 assets into M26 via FMT (may need help getting FMT to function).

---

## Objective
Convert the Madden 27 Beta roster (`ROSTER-MADDEN27`) into a Madden-26-compatible save,
preserving M27 player/team asset IDs, while writing a file Madden 26 will load.

## Files
| File | Purpose |
|------|---------|
| `C:\Users\Ninja\Documents\Madden NFL 26\saves\ROSTER-Official27TEST` | M26 source (header/container template, PLGS record) |
| `C:\Users\Ninja\Documents\Madden NFL 27 Beta\Saves\ROSTER-MADDEN27` | M27 source (player data) |
| `C:\Users\Ninja\Documents\Madden NFL 26\saves\ROSTER-M27CONVERTED` | **Final converted output (in-game verified)** |
| `C:\Users\Ninja\Documents\Madden NFL 26\saves\ROSTER-CHKSUMTEST` | Failed test file (proved checksum validation) |

## Tooling / Working Files (all in Temp, NOT in repos)
- `C:\Users\Ninja\AppData\Local\Temp\opencode\madden-file-tools` — clone of `bep713/madden-file-tools` (MIT). THE key library.
  - `helpers\MaddenRosterHelper.js` — load/save with checksum. **SAVE() IS THE ANSWER**.
  - `filetypes\TDB2\TDB2Writer.js` + `subTableWriter.js` — serializer (string-preserving, lossless via raw bytes).
  - `filetypes\TDB2\TDB2File.js` — **EDITED**: `_assignReflectiveProperty` now defines accessor with `configurable: true` (fix for `tables` setter throwing `Cannot delete property` when reassigning table lists).
  - `roundtrip.js` — no-op load/save roundtrip verifier (parse + CRC + len match).
  - `convert.js` — **THE CONVERTER** (drop INJY/PLCT/PRSN, add PLGS from M26, migrate PLCT→PLAY salaries, save via M26 container).
  - `dump_extra.js`, `play_fields.js` — schema-diff inspection scripts.
  - `package.json` moved to `package.json.bak` (native `lz4` node-gyp build fails without toolchain); pure-JS deps installed: `bit-buffer@0.2.5`, `crc-32@1.2.0`, `stream-parser@0.3.1`.
- `C:\Users\Ninja\AppData\Local\Temp\opencode\MaddenRosterEditor` — clone of `kn1meR/MaddenRosterEditor` (fork of the above, PyQt6 GUI; identical helper/CRC code).
- `C:\Users\Ninja\AppData\Local\Temp\opencode\tblinv\tblinv\Program.cs` — C# CRC-32B verifier (3 files, all match).
- `C:\Users\Ninja\AppData\Local\Temp\opencode\rtout\` — roundtrip outputs (`ROSTER-RT26`, `ROSTER-RT27`) + `ROSTER-M27CONVERTED`.
- Node.js LTS v24.19.0: `C:\Program Files\nodejs\node.exe`; npm blocked by exec policy → use `npm.cmd`.
  Run scripts as: `& "C:\Program Files\nodejs\node.exe" convert.js` from the `madden-file-tools` dir.
  - **Source**: official OpenJS Foundation build, installed via Microsoft winget (trusted channel). User approved keeping it installed.
  - **Security notes** (user is aware; may decide to uninstall later): Node is inert when not running — no services, no ports, no background activity. Risks only come from executing untrusted `.js` scripts or `npm install` packages (they run code). Treat like downloaded `.exe` files. Only 3 trusted packages installed (`bit-buffer`, `crc-32`, `stream-parser`). If user later wants removal, ALSO remove the npm packages (live copies live in `Temp\opencode\madden-file-tools\node_modules\`, plus the `package.json.bak`/`package-lock.json.bak` there), then: `winget uninstall OpenJS.NodeJS.LTS`. Converter + library already backed up in this repo (`m27converter\`), so Node + packages are re-installable on demand.

## File Format (M26/M27 roster saves)
- Exactly **6,291,530 bytes**. `FBCHUNKS` magic (0-7). 74-byte header (0x00-0x49), then zlib stream at **0x4A** (78 DA + deflate + Adler32), then zero padding.
- Header layout (offsets):
  - `0x12` (18-21): u32 LE **uncompressed inner length** — VALIDATED by game
  - `0x1A` (26-29): u32 LE **CRC-32B checksum** — VALIDATED by game
  - `0x2E` (46+): version string, e.g. M27 `Madden-27-MTRE_RL2-8965830`, M26 `Madden-26-RL11-8904890`
- Inner payload = the whole TDB2 roster database (uncompressed).

## THE CHECKSUM (was the blocker, now solved) ✅
- **Standard CRC-32 (MSB-first bit order, poly 0x04C11DB7, init=0xFFFFFFFF, xorout=0xFFFFFFFF)** computed over the **entire uncompressed inner TDB2 payload**, written as **u32 LE** at offset `0x1A`. Uncompressed length at `0x12`.
- Source of truth: `helpers/MaddenRosterHelper.js` `save()` (~lines 127-130). Its `~x ^ 0xFFFFFFFF` wrapper is a no-op — plain CRC-32.
- Note: bytes 26-29 = the LE32 CRC (earlier notes read this as two u16 checksums @26-27 and @28-29 — it's one u32).
- Verified pairs: M26 `0xBF8E1F42`/4201836 ✓ · M27 `0x8CF0273D`/6427573 ✓ · converted `0xA3C2BEAD`/5718364 ✓ (C# cross-check).
- The FMT memory file's old "find the hash" quest (CFB27 .bin files) is a DIFFERENT format (gzip container, u16 checksums) — do not confuse them.

## TDB2 Inner Structure
- No table directory — tables are concatenated sequentially; parser reads 5-byte table keys until done.
- Table types: type 3 = BLOB (raw subtable data, e.g. BLBM), type 4 = standard records, type 5 = subtable records.
- Field keys: 4-byte key + type byte; INT type 0 (LEB compressed), string type 10 etc.
- Parser/writer round-trip is lossless via raw field bytes; writer preserves outer header/padding; `save()` recomputes len+CRC.

## Table Inventory (record counts)
| Table | M26 (Official27TEST) | M27 (MADDEN27) | Converted |
|-------|---------------------|----------------|-----------|
| BLOB | type 3, BLBM subtable 3162 | type 3, BLBM 2993 | M27's (2993) |
| DCHT (depth chart) | type 4, 2957 | type 4, 2899 | M27's (2899) |
| DFTP | type 4, 672 | type 4, 672 | M27's |
| INJY | — | type 4, 2 | **dropped** |
| PLAY (players) | type 4, 3162 | type 4, 2993 | M27's (2993) |
| PLCT (contracts) | — | type 4, 2993 | **dropped** (salaries migrated to PLAY first) |
| PRSN (personas) | — | type 4, 2993 | **dropped** |
| PLGS (legends) | type 4, 1 | — | **added from M26** |
| TEAM | type 4, 33 | type 4, 33 | M27's |

## Schema Differences
- **PLAY fields: M26=140, M27=220. M27 is a strict superset** (0 M26-only fields missing).
  - 87 M27-only fields stripped implicitly by the game (they're just ignored — we did NOT strip them in convert.js; kept in file harmlessly). Full list in play_fields.js output.
  - M26 salary fields live IN PLAY: `PSA0-6, PSB0-6, PSBO`. M27 keeps salaries in PLCT (`PCON, PSA0-9, PSB0-4`) with M27 PLAY salary fields **zeroed**.
- **TEAM fields: M26=60, M27=110 (superset)** — game ignores extras.
- **Salary migration** in convert.js: for each PLAY record (by PGID), copies PLCT's `PSA0-6, PSB0-4, PCON` into PLAY (38,909 field values migrated). Verified: Burrow (PGID 20948) PSA0=101, POVR=97, PTSA=20015 all present after conversion.

## Conversion Recipe (what convert.js does)
1. `load(M26)` → M26 container/header + PLGS record.
2. `load(M27)` → M27 tables.
3. Build PGID→PLCT map from M27; migrate salary fields into M27 PLAY records.
4. New table list = M27 tables minus {INJY, PLCT, PRSN} plus M26 PLGS.
5. Assign into M26 file object (`m26.tables = newTables`) — requires the TDB2File.js configurable fix.
6. `h26.save(OUT)` — writes M26 header + deflated new inner + recomputed len/CRC.
7. Pad/truncate output to exactly 6,291,530 bytes.

## Next Steps (when revisiting)
- **Import M27 assets into M26 via FMT** — user's pending task; may need FMT help.
- If `ROSTER-M27CONVERTED` needs to become the active roster, rename it to `ROSTER-Official27TEST` (or user's preferred name; game requires `ROSTER-NAME` format, no extra hyphens).
- Any future M27→M26 conversions reuse `convert.js` as-is (source M27 path is hardcoded — parameterize if repeated).

## Pitfalls / Gotchas
- Don't `npm install` in madden-file-tools with package.json present (native lz4 build fails). Use the `.bak` setup.
- `tables` setter in TDB2File.js throws unless accessor properties are `configurable: true` (already patched).
- Outer file size must stay exactly 6,291,530 B.
- npm.ps1 blocked by execution policy → `npm.cmd` or run node.exe directly.
