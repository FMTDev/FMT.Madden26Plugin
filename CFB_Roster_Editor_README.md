# CFB27 Roster Editor

A standalone Windows tool (WPF / .NET 10) for editing **EA SPORTS College Football 27** roster
files — no FMT dependency. Main goal: edit player positions (and visual/gear attributes) in
community roster files.

> **Status: WORK IN PROGRESS.** Reading roster files works; reliable position editing and
> save-after-edit do not yet. Details in [Current Status](#current-status) below. Help wanted.

---

## Features

### Reading / UI
- Opens CFB27 roster files (detects the `FBCHUNKS` container, decompresses the outer deflate
  layer, and walks the inner gzip streams).
- **Browse** tab: players grouped by team in a tree.
- **Search** tab: filter by name with live player count.
- **Detail panel** per player:
  - First / last name, jersey number
  - Position, class year, body type
  - Height, weight
  - Face ID, skin tone, hair color, eye color
  - Team
  - Equipment grid (helmets, pads, sleeves, socks, towels, etc.)
- **Dark mode** toggle.

### Editing (in-memory)
- Position, class year, body type, height, weight, face ID, skin/hair/eye color, team, equipment
  values.
- **Assign Random Face** (with configurable tone maps).
- **Bulk Swap Equipment** across selected players.
- **Roster Summary** counts.
- Auto-save to `<file>.autosave` every 3 minutes while modified.

### Export / Generate
- **Export as JSON** / **CSV**.
- **Generate from NCAA website**: scrapers for NCAA official stats, On3, and Sidearm Sports
  (used by the roster-generation flow).

### Debug tools
- **Round-Trip Test**: saves a file with zero edits and diffs it against the original, byte by
  byte, to validate the writer.
- **Raw Strings dump**: hex + extracted strings for the selected player's record.
- **`Tools/Tdb2Dump`**: console tool that extracts every player's position (and key ratings)
  straight from the TDB2 `PLAY` subtable. Sample output in `SampleOutput/play_records.txt`.

---

## Current Status

### What works
- Full decompression + gzip-stream split of roster files.
- Player record parsing from embedded ASCII strings (name, team, body type, gear, height byte).
- **TDB2 position extraction (SOLVED the position blocker).** Roster files use the TDB2
  table format; the authoritative per-player records live in a `PLAY` subtable inside the
  trailing binary data. The parser (`Roster/Tdb2.cs` + `Roster/Tdb2RosterExtractor.cs`)
  decodes all **11,730** player records with 0 errors, including the `PPOS` → position map.
- In-memory edits and the full editing UI.
- Round-trip (no-edit save) is byte-identical.

### Position encoding (TDB2 `PLAY` subtable)
- Outer zlib/deflate at `0x4A` → inner buffer (14,264,525 B on the test roster).
- Inner: `BLOB` table (type 3, 2 entries) → `BLBM` subtable (type 5, gzip per-player profile
  records, 12,154 entries) → … → a **flat section** holding the real attribute data.
- The flat section contains the **`PLAY` subtable** (type 4) — located by the packed key
  `C2 C8 79 04` (inner offset 8,314,829 on the test roster), followed by 1 unknown byte and a
  modified-LEB entry count. `PPOS` lives here, NOT inside the gzip profile records.
- `PPOS` maps to the CFB27 21-position set (blank/0 = QB):

  | code | pos | code | pos | code | pos | code | pos | code | pos |
  |------|-----|------|-----|------|-----|------|-----|------|-----|
  | 1  | HB  | 5  | LT  | 10 | LEDG | 14 | MIKE | 19 | K  |
  | 2  | FB  | 6  | LG  | 11 | REDG | 15 | WILL | 20 | P  |
  | 3  | WR  | 7  | C   | 12 | DT   | 16 | CB   |    |    |
  | 4  | TE  | 8  | RG  | 13 | SAM  | 17 | FS   |    |    |
  |    |     | 9  | RT  |    |      | 18 | SS   |    |    |

- Verified against real players (Tyree Adams→LT, Adepoju Adebawore→REDG, Omillio Agard→CB,
  Joenel Aguero→FS, Bear Alexander→DT, Steve Angeli→QB, Nyck Harbor/Jeremiah Smith→WR).

### What does NOT work yet
1. **Save-after-edit breaks official rosters.** Editing changes the compressed stream sizes; the
   `FBCHUNKS` header offsets/checksums need to be updated on write (currently only the no-edit
   path round-trips byte-identically).
2. **Writing positions** (flip a `PPOS` byte and rebuild the flat section) is not yet wired into
   the writer, though the read side is fully decoded.
3. A second `BLBM` subtable (646 small gzip records, ~24–28 B decompressed) has a distinct
   record layout that still fails to parse cleanly — not needed for positions, but unfinished.

---

## File Format Notes (so far)

- Container magic: `FBCHUNKS`.
- Outer layer: zlib deflate starting at `0x4A`, compressed to a fixed length (padded), followed by
  the deflate stream.
- Inner layer: a 23-byte container header, then a series of **gzip streams** (`1F 8B 08`), one per
  player record / stats record (records < 100 bytes are treated as stats, larger ones as players).
- Player records embed ASCII strings: `FullId`, first/last name, `Unique_*` / `Generic_*` IDs,
  body type, position, class year, team name, gear tokens.
- Height is stored as a single byte right after the `A2 9B A3 00` marker (`heightByte = inches*2 - 12`).
- Trailing **C2 data** (binary tag–value pairs) is a **TDB2** table structure: it holds the
  authoritative per-player attribute records in a `PLAY` subtable (see [Position encoding](#position-encoding-tdb2-play-subtable)).
- TDB2 details: keys are 3 bytes six-bit packed + 1 type byte (0=int, 1=string, 3=nested field,
  4=subtable, 5=keyed subtable, 10=float); numbers use a modified LEB128; gzip records are stored
  uncompressed (`deflate stored` mode). Reference JS: `Reference/TDB2Parser.js`/`utilService.js`
  (Madden 21+ tooling) — the C# port is `Roster/Tdb2.cs`.

---

## Building from Source

Requires the **.NET 10 SDK**.

```powershell
dotnet build -c Release
```

Publish a self-contained exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

> Note: the `DotNetZip` 1.16.0 package triggers a NU1903 security advisory warning. It's used for
> gzip compression control; replacing it is on the roadmap.

---

## Project layout

```
Roster/
  Views/RosterEditorWindow.xaml(.cs)   # main WPF window + editing logic
  CFB27RosterReader.cs                 # FBCHUNKS deflate + gzip stream extraction + record parsing
  CFB27RosterWriter.cs                 # payload rebuild + gzip-to-size + deflate re-compression
  Tdb2.cs                              # TDB2 table parser (six-bit keys, modified-LEB, gzip records)
  Tdb2RosterExtractor.cs               # PLAY subtable extraction + PPOS -> position map (public API)
  FranchiseTable.cs / RecordCodec.cs   # FrTk table parser (Dynasty/RTG ONLY — port of DaiyronW/tool)
  PlayerVisualRecipe.cs                # player model (visual + gear fields)
  RosterData.cs                        # parsed roster container
  TeamMap.cs / On3TeamMap.cs / SidearmTeamMap.cs
  NCAAStatsScraper.cs / On3Scraper.cs / SidearmSportsScraper.cs
  ComplexionPresetMapper.cs / FaceTemplateMatcher.cs / HairColorMapper.cs
Tools/Tdb2Dump/                        # console: dump all player positions from a roster file
SampleOutput/play_records.txt          # extracted positions for the test roster (11,730 players)
Reference/                             # Madden TDB2 JS reference implementation (TDB2Parser.js, etc.)
PROGRESS.md                            # running log of what's done / stuck on
```

> The C# root namespace is `Madden26Plugin` (legacy from the original fork); don't be confused —
> this project targets College Football 27.

---

## How to help

The blockers above are the best places to contribute:

- **Wire position editing into the writer** — `PPOS` lives in the TDB2 `PLAY` subtable; the read
  side is fully decoded, so a position edit becomes "rewrite one PPOS value + rebuild the flat
  section + fix FBCHUNKS stream sizes."
- **Fix save-after-edit** so the `FBCHUNKS` headers are rebuilt for changed stream sizes.

See [Current Status](#current-status) and `PROGRESS.md` for the full picture. Issues and PRs
welcome.
