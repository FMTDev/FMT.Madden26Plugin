# Roster Load Investigation - Memory File

## Goal
Make modified Madden 26 / CFB27 roster `.bin` files load in-game.

## File Format
FBChunks (74-byte header) + zlib (2-byte header `78 DA` + deflate data + 4-byte Adler-32) + zero padding to 12,582,986 bytes.

The inner payload (decompressed deflate) = 23-byte container header + N gzip streams + C2 trailing data (0 bytes for Community/Official).

## Test Results
| Test | Change | Result |
|------|--------|--------|
| TESTA | Ionic deflate (different bytes), same inner payload | **LOADED** |
| TESTB | Flipped bit at header offset 16 (u16 192→64) | **FAILED** |
| TESTC | Changed 1 padding byte (after zlib stream) | **LOADED** |
| TESTD | Removed zero padding (file truncated to 7.9MB) | **FAILED** |
| TESTE | 1 gzip stream recompressed + Ionic deflate | **FAILED** |
| TESTF | Zeroed header fields 26,28,38,40,44 | **FAILED** |
| TESTG | Changed header field 38 from 2→8 only | **LOADED** |

## Confirmed Knowledge
- **Offset 16-17 (u16 LE)**: `(file_size - 0x4A) >> 16` = 192 = 0x00C0 (constant for all 12.5MB files)
- **Offset 18-21 (u32 LE)**: Inner payload size ✓
- **Offset 22-23**: 2027 (constant, version/build?)
- **Offset 24-25**: 2 (constant)
- **Offset 30-31**: 56 (constant)
- **Offset 32-33**: Same as offset 16-17 = 192 (maybe deflate_padding >> 16?)
- **Offset 34-35**: 2026 (close to 2027, constant)
- **Offset 36-37**: 7 (constant)
- **Offset 42-43**: 11 (constant)
- **File must be padded to exactly 12,582,986 bytes** (game expects this size)
- **Outer deflate can use ANY valid compressor** (Ionic.Zlib level 9 works)
- **No file-wide checksum** (changed padding byte still loads)

## Unknown Header Fields (vary between Community/Official)
| Offset | Type | Community | Official | Notes |
|--------|------|-----------|----------|-------|
| 26-27 | u16 LE | 0x5E7F (24191) | 0x78A7 (30887) | NOT CRC16 of any tested range; NOT any common hash |
| 28-29 | u16 LE | 0xDF42 (57154) | 0x2AEC (10988) | Same |
| 38-39 | u16 LE | 2 | 8 | NOT validated (TESTG passed) |
| 40-41 | u16 LE | 13 | 17 | Validated? |
| 44-45 | u16 LE | 47 | 52 | Validated? |

## Container Header (23 bytes)
Both differ at bytes 16-17 and 21-22.
C: `8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 8C 99 01 AB 01 A5 09`
O: `8A CB E2 03 8A CB E2 04 03 02 8A C8 AD 05 00 02 BA BD 01 AB 01 B6 0A`

## Hash Functions Tested (none matched)
- CRC32 (all standard init/xor combos)
- CRC32C (Castagnoli)
- Adler32
- JamCRC
- CRC16-CCITT, IBM, XMODEM (various inits)
- FNV-1a, FNV-1, DJB2, SDBM
- Sum, XOR, mul_sum, xor_rot
- SHA1 (truncated to 4 bytes)
- MD5 (truncated to 4 bytes)
- Data ranges: full inner, gzip only, header only, combined outer+inner, gzip streams only

## Next Steps
1. Determine if remaining unknown fields (26, 28, 40, 44) are checksums or structure metadata
2. Try CRC32 with endian-swapped bytes of inner payload
3. Try CRC32 of reversed inner payload
4. Try Fletcher-32 checksum
5. Create test files that zero individual fields (26 only, 28 only, 40 only, 44 only) to isolate which causes rejection
6. Consider reverse-engineering the game binary to find the checksum implementation

## Key Insight
The game validates SOME header fields against computed values from the inner payload. When the inner payload changes (even 1 gzip stream recompressed), the checksum changes and the file is rejected. The exact checksum algorithm is unknown but is NOT a standard CRC32.
