# Roster Load Investigation

## Objective
Make modified CFB27 roster files (`.bin`) load in-game by fixing outer-header validation.

## File Format
FBChunks: 74-byte header (offset 0x00-0x49) + zlib stream (78 DA + deflate + 4B Adler32) + zero padding → exactly **12,582,986 bytes**.

Inner payload = decompressed zlib = 23-byte container header + N gzip streams.

## Known Outer-Header Layout (74 bytes at 0x00-0x49)
| Offset | Size | Description | Validation |
|--------|------|-------------|------------|
| 0-7 | 8 | `FBCHUNKS` magic | - |
| 8-15 | 8 | `01 00 38 00 00 00 00 00` (version/flags) | - |
| 16-17 | u16 LE | `(total_file_size - 0x4A) >> 16` = always 0x00C0 | **YES** (TESTB failed) |
| 18-21 | u32 LE | Decompressed inner payload size | **YES** (TESTE+TES L both changed inner) |
| 22-23 | u16 LE | 0x07EB = 2027 (constant) | - |
| 24-25 | u16 LE | 0x0002 = 2 (constant) | - |
| 26-27 | u16 LE | Varies — **checksum #1** (C:0x5E7F O:0x78A7) | **YES** (TESTH failed) |
| 28-29 | u16 LE | Varies — **checksum #2** (C:0xDF42 O:0x2AEC) | **YES** (TESTI failed) |
| 30-31 | u16 LE | 0x0038 = 56 (constant) | - |
| 32-33 | u16 LE | Same as offset 16 = 0x00C0 | - |
| 34-35 | u16 LE | 0x07EA = 2026 (close to 2027) | - |
| 36-37 | u16 LE | 0x0007 = 7 (constant) | - |
| 38-39 | u16 LE | Varies (C:2 O:8) | **NO** (TESTG passed) |
| 40-41 | u16 LE | Varies (C:13 O:17) | **NO** (TESTJ passed) |
| 42-43 | u16 LE | 0x000B = 11 (constant) | - |
| 44-45 | u16 LE | Varies (C:47 O:52) | **NO** (TESTK passed) |
| 46+ | string | `"College-27-RL1-90391..."` version string | - |

## Container Header (23 bytes at inner payload offset 0)
Both share same structure; only bytes 16-17 and 21-22 differ:
C: `8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 8C 99 01 AB 01 A5 09`
O: `8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 BA BD 01 AB 01 B6 0A`

## Test Results (in-game)
| File | Change | Result |
|------|--------|--------|
| TESTA | Ionic deflate (different bytes), same inner, same header | **LOADED** |
| TESTB | Flipped bit at header[16] (u16 192→64) | **FAILED** |
| TESTC | Changed 1 padding byte (after zlib stream) | **LOADED** |
| TESTD | Removed zero padding (file too small) | **FAILED** |
| TESTE | 1 gzip stream recompressed (malformed gzip) | **FAILED** |
| TESTF | Zeroed header fields 26,28,38,40,44 | **FAILED** |
| TESTG | Changed header[38] from 2→8 only | **LOADED** |
| TESTH | Zeroed header[26-27] only | **FAILED** |
| TESTI | Zeroed header[28-29] only | **FAILED** |
| TESTJ | Zeroed header[40-41] only | **LOADED** |
| TESTK | Zeroed header[44-45] only | **LOADED** |
| TESTL | .NET GZipStream recompress 1 stream, update size, preserve 26/28 | **FAILED** |

## Key Findings
1. Only **fields 16-17, 18-21, 26-27, 28-29** are validated checksums
2. Fields 38-39, 40-41, 44-45 are metadata only — NOT validated
3. Checksums 26-27, 28-29 are computed from the **inner payload content** (TES L failure proves this — container header unchanged but recompressed stream data triggered rejection)
4. Outer zlib deflate can use ANY valid compressor (Ionic, .NET, etc.) — only the inner payload matters for checksums
5. Padding beyond the zlib stream is ignored
6. File must be exactly 12,582,986 bytes

## Hash Algorithms Rejected (none match fields 26 and 28)
- CRC32 (all standard init/xor variants)
- CRC32C (Castagnoli)
- JamCRC
- CRC16 (CCITT, XMODEM, IBM, ARC, Modbus, DNP, MAXIM, KERMIT, etc.)
- Fletcher-32
- Adler32
- FNV-1a, FNV-1, DJB2, SDBM
- Sum, XOR, mul_sum, xor_rot
- SHA1 (truncated to 4 bytes/u16)
- MD5 (truncated to 4 bytes/u16)
- All of the above on: full inner payload, gzip-only section, container header only, outer header slices, combined header+payload, byte-reversed data, word-swapped data

## Next Steps
### Approach A: Reverse-engineer the hash
- Try MurmurHash3 32-bit (common in Frostbite/games)
- Try Jenkins lookup3 32-bit
- Try xxHash 32-bit
- Try hash only over individual decompressed gzip streams (first N, or XOR of all stream hashes)
- Try Buzhash / rolling hash variants
- Could be a custom non-crypto hash where each iteration does rotates/XORs with specific constants

### Approach B: Binary reverse-engineering
- Find the checksum function in the game executable
- Use Ghidra/x64dbg to locate the validation code
- Search for references to the FBCHUNKS header fields

### Approach C: Brute-force with known data
- Create many test files that each flip one bit in the inner payload and test which bits affect the checksum
- If we can isolate which bytes affect the checksum, we can determine the data range
- Then use the two data points (Community vs Official) to constrain possible hash functions

## Data Points
**Community file:** 10,451 gzip streams, inner payload = 11,705,512 bytes
- hdr[26] = 0x5E7F, hdr[28] = 0xDF42

**Official file:** 12,802 gzip streams (1+ can't decompress with .NET GZipStream), inner payload = 14,264,490 bytes
- hdr[26] = 0x78A7, hdr[28] = 0x2AEC

**Container header (23 bytes):**
C: 8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 8C 99 01 AB 01 A5 09
O: 8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 BA BD 01 AB 01 B6 0A

## Open Questions
- What is the exact data range the hash covers? (Some/all of inner payload?)
- Is it one u32 hash split across two u16 fields, or two independent u16 hashes?
- Does the hash use a standard algorithm with non-standard init/params, or a completely custom algorithm?
- Does the hash start from the container header or skip it?

## How to Resume
1. The last test (TESTL) proved checksums at 26-28 cover more than the container header
2. Try MurmurHash3 32-bit, Jenkins lookup3, or xxHash on the inner payload
3. If those also fail, consider Approach B (binary RE) or Approach C (brute force)

## Relevant Files
- `ROSTER-Community.bin` — working baseline
- `ROSTER-Official.bin` — second data point  
- `ROSTER-TESTA.bin` through `ROSTER-TESTL.bin` — test files
- `Roster/CFB27RosterWriter.cs` — writer stub (needs hash to fully work)
- `Roster/CFB27RosterReader.cs` — reader with built-in CRC32
- `opencode_memory.md` — this file

## Tooling
- Test files built with `C:\Users\Ninja\AppData\Local\Temp\opencode\deflate_test\Program.cs`
- Raw files stored in `Documents\EA SPORTS College Football 27\saves\` (no `.bin` extension)
- Files committed to repo have `.bin` extension
