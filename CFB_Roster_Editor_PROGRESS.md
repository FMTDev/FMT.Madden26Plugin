# CFB27RosterEditor - Progress

## Goal
Standalone Windows tool (no FMT dependency) to edit CFB27 roster files, especially player positions.

## What's Done
- WPF project `CFB27RosterEditor.csproj` targeting `net10.0-windows`, DotNetZip only dependency
- UI: dark mode, team dropdown, body type assignment, debug round-trip test
- Name parsing: filters binary markers `"Hn"` and `"Np"`, extracts team names from strings
- `FranchiseTable.cs` + `RecordCodec.cs`: C# port of `DaiyronW/tool` Python code for reading FrTk table format (SPBF/ASTO/SPEX magic) — **only works for Dynasty/RTG saves, NOT roster files**
- `BinaryRecordHelper.cs`: `ReplacePosition()` method (requires existing position string)
- **TDB2 position extraction — SOLVED.** `Roster/Tdb2.cs` is a full TDB2 table parser (six-bit
  packed keys + type byte, modified-LEB integers, gzip/stored-deflate records). The player
  attribute records are a `PLAY` subtable (type 4) in the trailing C2 flat section. All 11,730
  records parse with 0 errors; `PPOS` → position mapping is decoded and verified against known
  players. Public API in `Roster/Tdb2RosterExtractor.cs`.
- `Tools/Tdb2Dump`: console tool to dump all player positions from a roster file (verification
  harness). Sample output committed to `SampleOutput/play_records.txt`.

## The TDB2 format (roster files)
- Outer zlib/deflate at `0x4A` → inner buffer.
- Inner: `BLOB` table (type 3, unk1=138, 2 entries) → `BLBM` subtable (type 5, unk2=2,
  12,154 keyed gzip records = per-player profile) → … → flat section → `PLAY` subtable
  (type 4, 11,730 records). `PLAY` found via packed key `C2 C8 79 04` + 1 unknown byte +
  modified-LEB count (offset 8,314,829 on the test roster).
- `PPOS` is a small int. Mapping: blank=QB, 1=HB, 2=FB, 3=WR, 4=TE, 5=LT, 6=LG, 7=C, 8=RG,
  9=RT, 10=LEDG, 11=REDG, 12=DT, 13=SAM, 14=MIKE, 15=WILL, 16=CB, 17=FS, 18=SS, 19=K, 20=P.
- Other `PLAY` fields: PEPS (asset name), PGID/POID (player id), PFNA/PLNA (name), PHTN
  (hometown), POVR (overall), PSPD/PSTR/PACC/… (ratings), PHGT/PWGT (height/weight ratings).

## Pick up here (next session)
- **Everything below this line was verified working on 2026-08-02** and committed as `9099ff1`.
- TDB2 position read side is DONE: `Roster/Tdb2.cs` + `Roster/Tdb2RosterExtractor.cs`
  (all 11,730 PLAY records parse, 0 errors). Reference Madden JS implementation is in
  `Reference/` (TDB2Parser.js, utilService.js, etc.).
- Resume commands:
  1. `dotnet build -c Release` (WPF app) and `dotnet build -c Release` in
     `Tools/Tdb2Dump` (console verifier).
  2. `.\bin\Release\net10.0\Tdb2Dump.exe "<roster path>"` — should print `players parsed: 11730`.
- Next task candidates (see "Still Open"):
  - **Position writing**: locate the PPOS byte in the PLAY record and re-serialize — read side
    is fully decoded; this closes the app's main feature.
  - **FBCHUNKS stream-size fix** for save-after-edit.
  - **646-record BLBM#2 subtable**: different record layout, keys like `BCCS`/`HBL]`
    (428/646 still error). Probably a non-position record kind (stats/override).
- Test roster: `C:\Users\Ninja\Documents\EA SPORTS College Football 27\saves\ROSTER-test`
  (PLAY subtable at inner[8,314,829], 11,730 records).

## Still Open
- **Save-after-edit breaks official rosters** (compressed size changes, FBCHUNKS headers need
  updating) — no-edit round-trip is byte-identical.
- **Position writing** not wired into the writer yet (read side is fully decoded).
- Second `BLBM` subtable (646 small gzip records, ~24–28 B decompressed) has a different record
  layout (indices sparse: 137/141/146/…; keys like `BCCS`/`HBL]`); 428 of 646 still fail to
  parse. Not needed for positions.
- ComboBox dark-mode popup styling unreliable
