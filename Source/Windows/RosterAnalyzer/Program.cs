using Madden26Plugin.Roster;
using System.IO.Compression;

var filePath = @"C:\Users\Ninja\source\repos\FMT.Madden26Plugin\ROSTER-Community.bin";
var originalBytes = File.ReadAllBytes(filePath);
Console.WriteLine($"Original size: {originalBytes.Length}");

// Check the header size fields and outer deflate output
var hdrComp = BitConverter.ToUInt32(originalBytes, 16);
var hdrDecomp = BitConverter.ToUInt32(originalBytes, 20);
Console.WriteLine($"Header compressed size: {hdrComp} ({hdrComp:X8})");
Console.WriteLine($"Header decompressed size: {hdrDecomp} ({hdrDecomp:X8})");

var reader = new CFB27RosterReader();
var data = reader.ReadRoster(originalBytes);

Console.WriteLine($"Players: {data.Players.Count}");
Console.WriteLine($"Stats: {data.StatsRecords.Count}");
// Count position distribution in blocks of 100 players to detect team boundaries
Console.WriteLine("=== Position distribution by 100-player blocks ===");
var positions = new[] { "QB", "RB", "WR", "TE", "OL", "DL", "LB", "DB", "K", "P", "ATH", "RET" };
for (var block = 0; block < data.Players.Count; block += 100)
{
    var end = Math.Min(block + 100, data.Players.Count);
    var blockPlayers = data.Players.Skip(block).Take(100).ToList();
    var posCounts = positions.ToDictionary(p => p, p => blockPlayers.Count(bp => bp.Position == p));
    var counts = string.Join(" ", positions.Select(p => $"{p}={posCounts[p]}"));
    Console.WriteLine($"  players {block}-{end-1}: {counts}");
}
Console.WriteLine("=== FullId number distribution ===");
var numbers = data.Players
    .Select(p => { var idx = p.FullId.LastIndexOf('_'); return idx >= 0 && int.TryParse(p.FullId[(idx+1)..], out var n) ? n : -1; })
    .Where(n => n > 0)
    .ToList();
Console.WriteLine($"  Min={numbers.Min()}, Max={numbers.Max()}, Count={numbers.Count}");
// Check if FullId numbers increase roughly monotonically with player index
var jumps = new List<int>();
for (var i = 1; i < numbers.Count; i++)
    if (numbers[i] - numbers[i-1] > 500) jumps.Add(i);
Console.WriteLine($"  Monotonic jumps (>500): {jumps.Count}");
foreach (var j in jumps.Take(10))
    Console.WriteLine($"    player[{j}] = {numbers[j]} (prev={numbers[j-1]}, diff={numbers[j]-numbers[j-1]})");
Console.WriteLine($"C2 trailing data: {data.C2TrailingData?.Length ?? 0} bytes");
Console.WriteLine($"Compressed streams sum: {data.AllCompressedStreams.Sum(s => s.Length)}");

var writer = new CFB27RosterWriter();
var rebuilt = writer.BuildPayload(data, originalBytes);
Console.WriteLine($"Rebuilt size: {rebuilt.Length}");
Console.WriteLine($"Ratio: {(double)rebuilt.Length / originalBytes.Length * 100:F1}%");
File.WriteAllBytes("ROSTER-Rebuilt.bin", rebuilt);

var data2 = reader.ReadRoster(rebuilt);
Console.WriteLine($"Re-read: {data2.Players.Count} players, {data2.StatsRecords.Count} stats, {data2.C2TrailingData?.Length ?? 0} C2 bytes");

Console.WriteLine($"Orig inner payload len: {data.RawDeflatedPayload.Length}");
Console.WriteLine($"Rebuilt inner payload len: {data2.RawDeflatedPayload.Length}");
Console.WriteLine($"C2 start in orig (stream sum): {data.AllCompressedStreams.Sum(s => s.Length)}");
Console.WriteLine($"C2 start in rebuilt (stream sum): {data2.AllCompressedStreams.Sum(s => s.Length)}");

var innerOk = data.RawDeflatedPayload.AsSpan().SequenceEqual(data2.RawDeflatedPayload);
Console.WriteLine($"Inner payloads match: {innerOk}");
if (!innerOk)
{
    var min = Math.Min(data.RawDeflatedPayload.Length, data2.RawDeflatedPayload.Length);
    for (var i = 0; i < min; i++)
        if (data.RawDeflatedPayload[i] != data2.RawDeflatedPayload[i])
        {
            Console.WriteLine($"First diff at offset {i}: orig=0x{data.RawDeflatedPayload[i]:X2} rebuilt=0x{data2.RawDeflatedPayload[i]:X2}");
            Console.WriteLine($"  diff is {i - 23 - data.AllCompressedStreams.Sum(s => s.Length)} bytes after last gzip stream");
            break;
        }
}
var c2Ok = (data.C2TrailingData?.Length ?? 0) == (data2.C2TrailingData?.Length ?? 0) &&
           data.C2TrailingData.AsSpan().SequenceEqual(data2.C2TrailingData);
Console.WriteLine($"C2 data matches: {c2Ok}");
var c2Ok2 = (data.C2TrailingData?.Length ?? 0) == (data2.C2TrailingData?.Length ?? 0);
Console.WriteLine(c2Ok2 ? "OK" : $"MISMATCH: {data.C2TrailingData?.Length} vs {data2.C2TrailingData?.Length}");

// === Player[0] details ===
var p0 = data.Players[0];
Console.WriteLine($"\n=== Player[0] ===");
Console.WriteLine($"  Name: '{p0.FirstName} {p0.LastName}'");
Console.WriteLine($"  Position: {p0.Position}");
Console.WriteLine($"  FullId: {p0.FullId}");
Console.WriteLine($"  Record bytes: {p0.RawRecordData.Length}");
// Search record for printable ASCII strings
var p0rec = p0.RawRecordData;
for (var i = 0; i < p0rec.Length - 4; i++)
{
    if (p0rec[i] >= 0x20 && p0rec[i] <= 0x7E && p0rec[i+1] >= 0x20 && p0rec[i+1] <= 0x7E && p0rec[i+2] >= 0x20 && p0rec[i+2] <= 0x7E)
    {
        var end = i;
        while (end < p0rec.Length && p0rec[end] >= 0x20 && p0rec[end] <= 0x7E) end++;
        var s = System.Text.Encoding.ASCII.GetString(p0rec[i..end]);
        if (s.Length >= 4) Console.WriteLine($"  string at offset {i}: '{s}'");
        i = end;
    }
}

// === EDIT TEST ===
var data3 = reader.ReadRoster(originalBytes);
var data3b = reader.ReadRoster(originalBytes); // second read for different-length edit

// Test 1: same-length edit
var editPlayer = data3.Players[0];
var oldFirst = editPlayer.FirstName;
var sameLenName = new string('X', oldFirst.Length);
editPlayer.FirstName = sameLenName;
BinaryRecordHelper.ReplaceFieldValue(editPlayer, oldFirst, sameLenName);

// Test 2: different-length edit
var editPlayer2 = data3b.Players[0];
var oldFirst2 = editPlayer2.FirstName;
var newName = "EDITED"; // 6 chars vs original's length
editPlayer2.FirstName = newName;
BinaryRecordHelper.ReplaceFieldValue(editPlayer2, oldFirst2, newName);

// Debug: check what the writer does internally
// Build both edited versions
var writerDebug = new CFB27RosterWriter();
var editedSameLen = writerDebug.BuildPayload(data3, originalBytes);
var editedDiffLen = writerDebug.BuildPayload(data3b, originalBytes);
// Build both inner payloads
var editSameLenInner = reader.ReadRoster(editedSameLen).RawDeflatedPayload;
var editDiffLenInner = reader.ReadRoster(editedDiffLen).RawDeflatedPayload;
var origInnerLen = data.RawDeflatedPayload.Length;
Console.WriteLine($"\n=== EDIT TEST ===");
Console.WriteLine($"Same-length: '{oldFirst}'({oldFirst.Length}) -> '{sameLenName}'({sameLenName.Length}) inner={editSameLenInner.Length} (origInner={origInnerLen})");
Console.WriteLine($"Diff-length: '{oldFirst2}'({oldFirst2.Length}) -> '{newName}'({newName.Length}) inner={editDiffLenInner.Length} (origInner={origInnerLen})");
Console.WriteLine($"Same-length file: {editedSameLen.Length}  Diff-length file: {editedDiffLen.Length}  (orig: {originalBytes.Length})");
Console.WriteLine($"Container header: {data3.ContainerHeader?.Length ?? 0} bytes");
Console.WriteLine($"Compressed streams sum: {data3.AllCompressedStreams.Sum(s => s.Length)}");
File.WriteAllBytes("ROSTER-Edited-SameLen.bin", editedSameLen);
File.WriteAllBytes("ROSTER-Edited-DiffLen.bin", editedDiffLen);

// Re-read same-length
var data4 = reader.ReadRoster(editedSameLen);
Console.WriteLine($"\nSame-length re-read: {data4.Players.Count} players, {data4.C2TrailingData?.Length ?? 0} C2 bytes");
var reloaded = data4.Players[0];
Console.WriteLine($"Player[0] name after re-read: '{reloaded.FirstName}'");

// Debug: compare original vs same-length stream[0] sizes
var origStream0 = data.AllCompressedStreams[0];
Console.WriteLine($"\n=== STREAM SIZE CHECK ===");
Console.WriteLine($"Orig stream[0]: {origStream0.Length} bytes");
Console.WriteLine($"SameLen stream[0]: {data4.AllCompressedStreams[0].Length} bytes");
Console.WriteLine($"Diff: {data4.AllCompressedStreams[0].Length - origStream0.Length} bytes");
// Also decompress sameLen stream[0] to verify
using (var ms = new MemoryStream(data4.AllCompressedStreams[0]))
using (var gz = new GZipStream(ms, CompressionMode.Decompress))
using (var result = new MemoryStream())
{
    gz.CopyTo(result);
    var text = System.Text.Encoding.ASCII.GetString(result.ToArray());
    var firstName = "";
    foreach (var s in CFB27RosterReader.ExtractStrings(result.ToArray()))
    { firstName = s; break; }
    Console.WriteLine($"  Decompressed first string: '{firstName}' ({result.Length} bytes)");
}
var innerOk2 = data3.RawDeflatedPayload.AsSpan().SequenceEqual(data4.RawDeflatedPayload);
Console.WriteLine($"Inner payloads match (edit vs re-read): {innerOk2}");

// === CRC CHECK ===
var origDeflated = originalBytes[0x4A..];
var editDeflated = editedSameLen[0x4A..];
var origCrc = CFB27RosterReader.ComputeCrc32(origDeflated);
var editCrc = CFB27RosterReader.ComputeCrc32(editDeflated);
var hdrCrc = BitConverter.ToUInt32(originalBytes, 22);
Console.WriteLine($"\n=== CRC CHECK ===");
Console.WriteLine($"Header bytes 22-25 (as stored): 0x{hdrCrc:X8}");
Console.WriteLine($"CRC32 of orig deflated: 0x{origCrc:X8} match={origCrc == hdrCrc}");
Console.WriteLine($"CRC32 of edit deflated: 0x{editCrc:X8} match={editCrc == hdrCrc}");
Console.WriteLine($"Header bytes 34-35 (0x22=34): {originalBytes[34]:X2} {originalBytes[35]:X2}");

// Check if FBChunks offset 24 is a CRC32 of the inner payload
Console.WriteLine($"\n=== INNER PAYLOAD CHECKSUM CHECK ===");
var originalInnerPayload = data.RawDeflatedPayload;
var innerCrc = CFB27RosterReader.ComputeCrc32(originalInnerPayload);
var fbField24 = BitConverter.ToUInt32(originalBytes, 24);
Console.WriteLine($"CRC32 of inner payload: 0x{innerCrc:X8}");
Console.WriteLine($"FBChunks offset 24 u32: 0x{fbField24:X8}");
Console.WriteLine($"Match: {innerCrc == fbField24}");

// Also compute CRC of gzip streams only (skip container header and C2)
var c2Len = data.C2TrailingData?.Length ?? 0;
var gzipEnd = originalInnerPayload.Length - c2Len;
var gzipStreamsOnly = originalInnerPayload[23..gzipEnd];
var gzipCrc = CFB27RosterReader.ComputeCrc32(gzipStreamsOnly);
Console.WriteLine($"CRC32 of gzip streams only: 0x{gzipCrc:X8}");
Console.WriteLine($"Match fb offset 24: {gzipCrc == fbField24}");

// CRC of streams + container header (no C2)
var noC2Payload = originalInnerPayload[..gzipEnd];
var noC2Crc = CFB27RosterReader.ComputeCrc32(noC2Payload);
Console.WriteLine($"CRC32 of inner (no C2): 0x{noC2Crc:X8}");
Console.WriteLine($"Match fb offset 24: {noC2Crc == fbField24}");

// === RE-COMPRESS ALL TEST: force re-compress every gzip stream with no data changes ===
Console.WriteLine($"\n=== RE-COMPRESS ALL TEST ===");
var data5 = reader.ReadRoster(originalBytes);
data5.RawDeflatedPayload = [];
var recompAll = writerDebug.BuildPayload(data5, originalBytes);
Console.WriteLine($"Re-compressed all size: {recompAll.Length} (original: {originalBytes.Length})");
Console.WriteLine($"ZLib header: {recompAll[0x4A]:X2} {recompAll[0x4B]:X2} (orig: 78 DA)");

// Find the ACTUAL Adler-32 in the original file by computing it from decompressed data
uint Adler32(byte[] data)
{
    uint a = 1, b = 0;
    foreach (var v in data)
    {
        a = (a + v) % 65521;
        b = (b + a) % 65521;
    }
    return (b << 16) | a;
}

// Compute Adler-32 of the uncompressed inner payload
var origInnerPayload = data.RawDeflatedPayload;
var expectedAdler = Adler32(origInnerPayload);
Console.WriteLine($"Expected Adler-32 of inner payload: 0x{expectedAdler:X8}");

// Search for Adler-32 in the original deflated data - scan by first byte
var origDeflatedBytes = originalBytes[0x4A..];
// Adler-32 = 0x9F3A42C4, stored in zlib BIG-ENDIAN as 9F 3A 42 C4
byte b0 = 0x9F, b1 = 0x3A, b2 = 0x42, b3 = 0xC4;
Console.WriteLine($"Searching for Adler-32 [{b0:X2} {b1:X2} {b2:X2} {b3:X2}] in orig deflated (9700000..end)...");
for (var i = 9700000; i < origDeflatedBytes.Length - 4; i++)
{
    if (origDeflatedBytes[i] == b0 && origDeflatedBytes[i+1] == b1 && origDeflatedBytes[i+2] == b2 && origDeflatedBytes[i+3] == b3)
    {
        Console.WriteLine($"Found Adler-32 at offset 0x4A+{i} (0x{0x4A + i:X})");
        Console.WriteLine($"  Bytes before: {origDeflatedBytes[i-4]:X2} {origDeflatedBytes[i-3]:X2} {origDeflatedBytes[i-2]:X2} {origDeflatedBytes[i-1]:X2}");
        Console.WriteLine($"  Bytes after: {origDeflatedBytes[i+4]:X2} {origDeflatedBytes[i+5]:X2} {origDeflatedBytes[i+6]:X2} {origDeflatedBytes[i+7]:X2}");
        break;
    }
    if (i % 1000000 == 0) Console.WriteLine($"  searched up to {i}...");
}

// Test Ionic.Zlib on both
foreach (var (label, bytes) in new[] { ("Original", originalBytes), ("Recomp", recompAll) })
{
    var deflated = bytes[0x4A..];
    try
    {
        using (var ms = new MemoryStream(deflated))
        using (var zlib = new Ionic.Zlib.ZlibStream(ms, Ionic.Zlib.CompressionMode.Decompress))
        {
            var result = new MemoryStream();
            zlib.CopyTo(result);
            Console.WriteLine($"  Ionic.Zlib({label}): OK, {result.Length} bytes");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Ionic.Zlib({label}): FAILED - {ex.Message}");
    }
    try
    {
        using (var ms = new MemoryStream(deflated[2..]))
        using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
        {
            var result = new MemoryStream();
            deflate.CopyTo(result);
            Console.WriteLine($"  S.DC Deflate({label}): OK, {result.Length} bytes");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  S.DC Deflate({label}): FAILED - {ex.Message}");
    }
}

// Verify it re-reads correctly
var data6 = reader.ReadRoster(recompAll);
Console.WriteLine($"Re-read: {data6.Players.Count} players, {data6.C2TrailingData?.Length ?? 0} C2 bytes");
Console.WriteLine($"Player[0] name: '{data6.Players[0].FirstName}'");

// Dump container headers for comparison
var origInner = data.RawDeflatedPayload;
var recompInner = data6.RawDeflatedPayload;
var editInner = reader.ReadRoster(editedSameLen).RawDeflatedPayload;
Console.WriteLine($"\n=== CONTAINER HEADER DUMP ===");
void DumpHeader(string label, byte[] inner)
{
    Console.Write($"  {label} (len={inner.Length}): ");
    for (var i = 0; i < Math.Min(23, inner.Length); i++) Console.Write($"{inner[i]:X2} ");
    Console.WriteLine();
        Console.Write($"    {label} container bytes: ");
        for (var i = 0; i < Math.Min(23, inner.Length); i++) Console.Write($"{(char)(inner[i] >= 32 && inner[i] < 127 ? (int)inner[i] : 46)}");
    Console.WriteLine();
    // Parse as potential count + offsets
    if (inner.Length >= 8)
    {
        var u1 = BitConverter.ToUInt32(inner, 0);
        var u2 = BitConverter.ToUInt32(inner, 4);
        var u3 = BitConverter.ToUInt32(inner, 8);
        var u4 = BitConverter.ToUInt32(inner, 12);
        var u5 = BitConverter.ToUInt32(inner, 16);
        Console.WriteLine($"    u32[0..3]: {u1} {u2} {u3}  u32[4..5]: {u4} {u5}");
    }
}
DumpHeader("Orig", origInner);
DumpHeader("Recomp", recompInner);
DumpHeader("Edited", editInner);

// Compare original vs edited inner payload first bytes
var diffStart = 0;
var minLen = Math.Min(origInner.Length, editInner.Length);
for (var i = 23; i < minLen; i++) // skip container header
{
    if (origInner[i] != editInner[i])
    {
        diffStart = i;
        break;
    }
}
Console.WriteLine($"\n=== INNER PAYLOAD COMPARISON (Orig vs Edited) ===");
Console.WriteLine($"First diff at {diffStart} (byte 0x{diffStart:X}) - this is {diffStart - 23} bytes into gzip streams");

// Also check: where is the first 1F 8B 08 magic in the inner payload?
for (var i = 0; i < Math.Min(100, origInner.Length); i++)
{
    if (origInner[i] == 0x1F && origInner[i+1] == 0x8B && origInner[i+2] == 0x08)
    {
        Console.WriteLine($"First gzip magic at inner payload offset {i} (0x{i:X})");
        break;
    }
}
// Dump first 60 bytes of all inner payloads for inspection
Console.WriteLine("\nFirst 60 bytes of inner payloads:");
foreach (var (label, inner) in new[] { ("Orig", origInner), ("SameLen", editInner), ("DiffLen", editDiffLenInner) })
{
    Console.Write($"  {label}: ");
    for (var i = 0; i < Math.Min(60, inner.Length); i++)
    {
        var c = inner[i];
        Console.Write(c >= 0x20 && c < 0x7F ? $"'{chr(c)}'" : $"{c:X2}");
        if (i < Math.Min(60, inner.Length) - 1) Console.Write(" ");
    }
    Console.WriteLine();
}
static char chr(byte b) => (char)b;

// Check: in the original inner payload, are there bytes between container header (23) and first gzip magic?
Console.WriteLine($"\nBytes between header (23) and first gzip magic in Orig:");
for (var i = 23; i < 30 && i < origInner.Length; i++)
{
    if (origInner[i] == 0x1F) break;
    Console.Write($"{origInner[i]:X2} ");
}
Console.WriteLine();
if (diffStart > 0)
{
    Console.WriteLine("  Context around diff (Orig):");
    Console.Write($"    ");
    for (var i = diffStart - 4; i < diffStart + 8 && i < origInner.Length; i++) Console.Write($"{origInner[i]:X2} ");
    Console.WriteLine();
    Console.WriteLine("  Context around diff (Edited):");
    Console.Write($"    ");
    for (var i = diffStart - 4; i < diffStart + 8 && i < editInner.Length; i++) Console.Write($"{editInner[i]:X2} ");
    Console.WriteLine();
}

Console.WriteLine($"\n=== FBCHUNKS HEADER DUMP ===");
for (var off = 0; off < 0x4A; off += 4)
{
    var u32 = BitConverter.ToUInt32(originalBytes, off);
    Console.WriteLine($"  offset {off,3} (0x{off:X2}): u32 = {u32,12} (0x{u32:X8})  bytes: {originalBytes[off]:X2} {originalBytes[off+1]:X2} {originalBytes[off+2]:X2} {originalBytes[off+3]:X2}");
}

// Also compare FBCHUNKS between orig and edited
Console.WriteLine($"\n=== FBCHUNKS COMPARISON Orig vs Edited ===");
var fbOrig = originalBytes[..0x4A];
var fbEdit = editedSameLen[..0x4A];
var fbDiff = false;
for (var i = 0; i < 0x4A; i++)
{
    if (fbOrig[i] != fbEdit[i])
    {
        Console.WriteLine($"  Diff at offset {i} (0x{i:X2}): orig={fbOrig[i]:X2} edit={fbEdit[i]:X2}");
        fbDiff = true;
    }
}
if (!fbDiff) Console.WriteLine("  FBCHUNKS headers are IDENTICAL (as expected)");

File.WriteAllBytes("ROSTER-RECOMPRESSED.bin", recompAll);

// === RE-COMPRESS SINGLE GZIP STREAM TEST (innocuous byte change) ===
Console.WriteLine($"\n=== RE-COMPRESS STREAM[0] INNOCUOUS CHANGE TEST ===");
var dataTweak = reader.ReadRoster(originalBytes);
var tweakPlayer = dataTweak.Players[0];
if (tweakPlayer.RawRecordData.Length > 10)
{
    var cloned = (byte[])tweakPlayer.RawRecordData.Clone();
    cloned[cloned.Length - 1] ^= 0x01;
    tweakPlayer.RawRecordData = cloned;
}
var tweakWriter = new CFB27RosterWriter();
var tweakBytes = tweakWriter.BuildPayload(dataTweak, originalBytes);
var dataTweakReread = reader.ReadRoster(tweakBytes);
Console.WriteLine($"Stream[0] re-compressed: {dataTweakReread.AllCompressedStreams[0].Length}B (orig: {data.AllCompressedStreams[0].Length}B)");
Console.WriteLine($"Inner payloads match (tweak): {data.RawDeflatedPayload.AsSpan().SequenceEqual(dataTweakReread.RawDeflatedPayload)}");
Console.WriteLine($"Player[0] name: '{dataTweakReread.Players[0].FirstName}'");
File.WriteAllBytes("ROSTER-Tweak.bin", tweakBytes);

// === RE-COMPRESS STREAM[0] WITH ORIGINAL DATA ===
Console.WriteLine($"\n=== RE-COMPRESS ORIGINAL DATA (NO EDIT) ===");
var dataRecomp = reader.ReadRoster(originalBytes);
var recompPlayer = dataRecomp.Players[0];
// Clone data so change detection works
var recompCloned = (byte[])recompPlayer.RawRecordData.Clone();
recompPlayer.RawRecordData = recompCloned;
// Re-compress unchanged data -> see what size we get
var recompWriter = new CFB27RosterWriter();
var recompBytes = recompWriter.BuildPayload(dataRecomp, originalBytes);
var dataRecompReread = reader.ReadRoster(recompBytes);
Console.WriteLine($"Stream[0] re-compressed (no edit): {dataRecompReread.AllCompressedStreams[0].Length}B (orig: {data.AllCompressedStreams[0].Length}B)");
var innerRecompOk = data.RawDeflatedPayload.AsSpan().SequenceEqual(dataRecompReread.RawDeflatedPayload);
Console.WriteLine($"Inner payloads match: {innerRecompOk}");
File.WriteAllBytes("ROSTER-RawRecomp.bin", recompBytes);

// === VERIFY EMPTY GZIP MEMBER PADDING ===
Console.WriteLine($"\n=== EMPTY GZIP MEMBER TEST ===");
// Create a padded gzip stream: compress "Hello" + pad with empty member to 100 bytes
var realData = System.Text.Encoding.ASCII.GetBytes("Hello");
var realGzip = CompressGzip(realData);
Console.WriteLine($"Real gzip size: {realGzip.Length}");

// Create empty gzip member
var emptyMember = new byte[23];
emptyMember[0] = 0x1F; emptyMember[1] = 0x8B; emptyMember[2] = 0x08; emptyMember[3] = 0x00;
emptyMember[8] = 0x00; emptyMember[9] = 0x0A;
emptyMember[10] = 0x01; emptyMember[11] = 0x00; emptyMember[12] = 0x00;
emptyMember[13] = 0xFF; emptyMember[14] = 0xFF;
// CRC32=0, ISIZE=0 (already 0)

// Combine: real gzip + empty members to reach 100 bytes
var padded = new byte[100];
Array.Copy(realGzip, padded, realGzip.Length);
for (var pos = realGzip.Length; pos + 23 <= 100; pos += 23)
    Array.Copy(emptyMember, 0, padded, pos, 23);

// Decompress with .NET and Ionic
foreach (var (label, bytes) in new[] { ("Real", realGzip), ("Padded", padded) })
{
    // .NET GZipStream
    try
    {
        using var ms = new MemoryStream(bytes);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var result = new MemoryStream();
        gz.CopyTo(result);
        Console.WriteLine($"  .NET({label}): OK, '{System.Text.Encoding.ASCII.GetString(result.ToArray())}' ({result.Length} bytes)");
    }
    catch (Exception ex) { Console.WriteLine($"  .NET({label}): FAILED - {ex.Message}"); }
    
    // Ionic Zlib
    try
    {
        using var ms = new MemoryStream(bytes);
        using var gz = new Ionic.Zlib.GZipStream(ms, Ionic.Zlib.CompressionMode.Decompress);
        using var result = new MemoryStream();
        gz.CopyTo(result);
        Console.WriteLine($"  Ionic({label}): OK, '{System.Text.Encoding.ASCII.GetString(result.ToArray())}' ({result.Length} bytes)");
    }
    catch (Exception ex) { Console.WriteLine($"  Ionic({label}): FAILED - {ex.Message}"); }
}

// === FNAME GZIP TEST ===
Console.WriteLine($"\n=== FNAME GZIP TEST ===");
// Test: create gzip + pad with FNAME (1 byte)
var fnameData = System.Text.Encoding.ASCII.GetBytes("HelloFNAME");
var fnameGzip = CompressGzip(fnameData);
var fnamePadded = new byte[fnameGzip.Length + 1];
Array.Copy(fnameGzip, fnamePadded, fnameGzip.Length);
// Set FLG bit 3 (FNAME), insert null byte after OS byte (pos 9)
fnamePadded[3] |= 0x08;
// Shift deflate data + trailer right by 1 to make room
Array.Copy(fnamePadded, 10, fnamePadded, 11, fnameGzip.Length - 10);
fnamePadded[10] = 0x00;
Console.WriteLine($"FNAME gzip original: {fnameGzip.Length}B  padded: {fnamePadded.Length}B");
foreach (var (label, stream) in new[] { ("Original", fnameGzip), ("Padded+FNAME", fnamePadded) })
{
    try
    {
        using var ms = new MemoryStream(stream);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var r = new MemoryStream();
        gz.CopyTo(r);
        Console.WriteLine($"  .NET({label}): OK, '{System.Text.Encoding.ASCII.GetString(r.ToArray())}' ({r.Length} bytes)");
    }
    catch (Exception ex) { Console.WriteLine($"  .NET({label}): FAILED - {ex.Message}"); }
    try
    {
        using var ms = new MemoryStream(stream);
        using var gz = new Ionic.Zlib.GZipStream(ms, Ionic.Zlib.CompressionMode.Decompress);
        using var r = new MemoryStream();
        gz.CopyTo(r);
        Console.WriteLine($"  Ionic({label}): OK, '{System.Text.Encoding.ASCII.GetString(r.ToArray())}' ({r.Length} bytes)");
    }
    catch (Exception ex) { Console.WriteLine($"  Ionic({label}): FAILED - {ex.Message}"); }
}

// Helper function
static byte[] CompressGzip(byte[] data)
{
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        gzip.Write(data);
    return output.ToArray();
}

// === C2 SEARCH ===
Console.WriteLine($"\n\n=== C2 SEARCH ===");
var c2SearchInner = data.RawDeflatedPayload;
var c2SearchStart = data.ContainerHeader?.Length ?? 0;
if (data.AllCompressedStreams.Count > 0)
    c2SearchStart += data.AllCompressedStreams.Sum(s => s.Length);
var c2SearchData = c2SearchInner[c2SearchStart..];
Console.WriteLine($"C2 data: {c2SearchData.Length} bytes (starts at offset {c2SearchStart})");

// Get stream[0] CRC from gzip trailer
var stream0 = data.AllCompressedStreams[0];
var stream0Crc = BitConverter.ToUInt32(stream0, stream0.Length - 8);
var stream0Isize = BitConverter.ToUInt32(stream0, stream0.Length - 4);
Console.WriteLine($"Stream[0]: compressed={stream0.Length}B CRC=0x{stream0Crc:X8} ISIZE={stream0Isize}");
Console.WriteLine($"  Expected CRC=0x{CFB27RosterReader.ComputeCrc32(data.AllDecompressedStreams[0]):X8} ISIZE={data.AllDecompressedStreams[0].Length}");

// Find actual gzip end by decompressing and finding matching CRC
int FindGzipEnd(byte[] inner, int gzStart)
{
    var msOut = new MemoryStream();
    using (var msIn = new MemoryStream(inner, gzStart, inner.Length - gzStart))
    using (var gzip = new GZipStream(msIn, CompressionMode.Decompress))
        gzip.CopyTo(msOut);
    var decompressed = msOut.ToArray();
    msOut.Dispose();
    var expectedCrc = ComputeCrc32(decompressed);
    var expectedIsize = (uint)decompressed.Length;
    var searchEnd = Math.Min(gzStart + 2000, inner.Length);
    for (var scan = searchEnd - 8; scan >= gzStart + 10; scan--)
    {
        var crc = BitConverter.ToUInt32(inner, scan);
        if (crc != expectedCrc) continue;
        var isize = BitConverter.ToUInt32(inner, scan + 4);
        if (isize == expectedIsize)
            return scan + 8;
    }
    return searchEnd;
}
var stream0ActualEnd = FindGzipEnd(c2SearchInner, 23);
var stream0ActualLen = stream0ActualEnd - 23;
Console.WriteLine($"  Actual gzip end: {stream0ActualEnd}, actual compressed len: {stream0ActualLen}B");
Console.WriteLine($"  Diff: {stream0.Length - stream0ActualLen}B padding in extracted stream");

// Dump the last 24 bytes of extracted stream[0] and actual gzip
Console.WriteLine($"\n  Last 24 bytes of extracted stream[0]:");
for (var i = Math.Max(0, stream0.Length - 24); i < stream0.Length; i++)
    Console.Write($"{stream0[i]:X2} ");
Console.WriteLine();
Console.WriteLine($"  Actual gzip trailer (at pos {stream0ActualEnd - 8}):");
for (var i = stream0ActualEnd - 8; i < stream0ActualEnd; i++)
    Console.Write($"{c2SearchInner[i]:X2} ");
Console.WriteLine();

// Search for CRC in C2
var crcB = BitConverter.GetBytes(stream0Crc);
var crcHits = 0;
for (var i = 0; i < c2SearchData.Length - 4; i++)
{
    if (c2SearchData[i] == crcB[0] && c2SearchData[i+1] == crcB[1] && 
        c2SearchData[i+2] == crcB[2] && c2SearchData[i+3] == crcB[3])
    {
        Console.Write($"  CRC found at C2 offset {i}: ");
        for (var j = Math.Max(0,i-4); j < Math.Min(c2SearchData.Length, i+12); j++)
            Console.Write($"{c2SearchData[j]:X2} ");
        Console.WriteLine();
        crcHits++;
        if (crcHits >= 3) break;
    }
}
if (crcHits == 0) Console.WriteLine("  CRC not found in C2 data");

// Search for ISIZE
var isizeB = BitConverter.GetBytes(stream0Isize);
var isizeHits = 0;
for (var i = 0; i < c2SearchData.Length - 4; i++)
{
    if (c2SearchData[i] == isizeB[0] && c2SearchData[i+1] == isizeB[1] && 
        c2SearchData[i+2] == isizeB[2] && c2SearchData[i+3] == isizeB[3])
    {
        Console.Write($"  ISIZE found at C2 offset {i}: ");
        for (var j = Math.Max(0,i-4); j < Math.Min(c2SearchData.Length, i+12); j++)
            Console.Write($"{c2SearchData[j]:X2} ");
        Console.WriteLine();
        isizeHits++;
        if (isizeHits >= 3) break;
    }
}
if (isizeHits == 0) Console.WriteLine("  ISIZE not found in C2 data");

// CRC of first 5 player records
Console.WriteLine($"\nCRCs of player records:");
for (var s = 0; s < Math.Min(5, data.AllDecompressedStreams.Count); s++)
{
    var rec = data.AllDecompressedStreams[s];
    if (rec.Length < 100) continue;
    Console.WriteLine($"  Stream[{s}]: CRC=0x{ComputeCrc32(rec):X8} ISIZE={rec.Length}");
}

// Compute various hashes of the deflate data to find header field matches
Console.WriteLine($"\n=== HEADER CHECKSUM SEARCH ===");
var hdrDeflated = originalBytes[0x4A..];
var hdrInner = data.RawDeflatedPayload;

// Try CRC32 of deflate with different variants
Console.WriteLine("Hash of deflated data vs header fields:");
Console.WriteLine($"  CRC32:      0x{CFB27RosterReader.ComputeCrc32(hdrDeflated):X8}");
Console.WriteLine($"  CRC32-C:    0x{ComputeCrc32C(hdrDeflated):X8}");

// CRC32 of header itself
Console.WriteLine($"  Header CRC: 0x{CFB27RosterReader.ComputeCrc32(originalBytes[..0x4A]):X8}");

// Check if offset 16-23 is a 64-bit value
var hdrU64 = BitConverter.ToUInt64(originalBytes, 16);
Console.WriteLine($"  u64 at off16: 0x{hdrU64:X16}");

// Check if these are actually TWO crc32 values (big-endian)
var be32_16 = (uint)((originalBytes[16] << 24) | (originalBytes[17] << 16) | (originalBytes[18] << 8) | originalBytes[19]);
var be32_20 = (uint)((originalBytes[20] << 24) | (originalBytes[21] << 16) | (originalBytes[22] << 8) | originalBytes[23]);
Console.WriteLine($"  BE u32 off16: 0x{be32_16:X8}");
Console.WriteLine($"  BE u32 off20: 0x{be32_20:X8}");

// Compare header field at offset 24 against hashes
var cmp24 = BitConverter.ToUInt32(originalBytes, 24);
Console.WriteLine($"\nHeader offset 24: 0x{cmp24:X8}");
Console.WriteLine($"  CRC32 deflated:          0x{CFB27RosterReader.ComputeCrc32(hdrDeflated):X8} match={(CFB27RosterReader.ComputeCrc32(hdrDeflated) == cmp24)}");
Console.WriteLine($"  CRC32 inner payload:     0x{CFB27RosterReader.ComputeCrc32(hdrInner):X8} match={(CFB27RosterReader.ComputeCrc32(hdrInner) == cmp24)}");
Console.WriteLine($"  CRC32 inner noC2:        {ComputeCrc32(hdrInner[..^c2SearchData.Length]):X8}");

// CRC32C of various parts
Console.WriteLine($"\nCRC32C variants:");
Console.WriteLine($"  deflated:    0x{ComputeCrc32C(hdrDeflated):X8}");
Console.WriteLine($"  inner:       0x{ComputeCrc32C(hdrInner):X8}");

// Try JamCRC (CRC32 with 0 init, not ~0)
Console.WriteLine($"\nJamCRC variants:");
Console.WriteLine($"  deflated:    0x{ComputeJamCrc(hdrDeflated):X8}");
Console.WriteLine($"  inner:       0x{ComputeJamCrc(hdrInner):X8}");

// MD5
using (var md5 = System.Security.Cryptography.MD5.Create())
{
    var hash = md5.ComputeHash(hdrDeflated);
    Console.WriteLine($"\nMD5 of deflated: {BitConverter.ToString(hash).Replace("-","")}");
}

// SHA-1
using (var sha1 = System.Security.Cryptography.SHA1.Create())
{
    var hash = sha1.ComputeHash(hdrDeflated);
    Console.WriteLine($"SHA-1 of deflated: {BitConverter.ToString(hash).Replace("-","")}");
}

// Check if the C2 data contains a hash that references the deflate data
Console.WriteLine("\n=== C2 HASH CHECK ===");
// Search for any of these values in C2
var knownVals = new uint[] {
    CFB27RosterReader.ComputeCrc32(hdrDeflated),
    ComputeCrc32C(hdrDeflated),
    ComputeJamCrc(hdrDeflated),
    BitConverter.ToUInt32(originalBytes, 16),
    BitConverter.ToUInt32(originalBytes, 20),
    BitConverter.ToUInt32(originalBytes, 24),
};
foreach (var val in knownVals)
{
    var vb = BitConverter.GetBytes(val);
    for (var i = 0; i < c2SearchData.Length - 4; i++)
    {
        if (c2SearchData[i] == vb[0] && c2SearchData[i+1] == vb[1] && 
            c2SearchData[i+2] == vb[2] && c2SearchData[i+3] == vb[3])
        {
            Console.WriteLine($"  Value 0x{val:X8} found at C2 offset {i}");
            break;
        }
    }
}

static uint ComputeCrc32C(byte[] data)
{
    // CRC-32C (Castagnoli) polynomial: 0x82F63B78
    var crc = 0xFFFFFFFFu;
    for (var i = 0; i < data.Length; i++)
    {
        crc ^= data[i];
        for (var j = 0; j < 8; j++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78u : crc >> 1;
    }
    return crc ^ 0xFFFFFFFFu;
}
static uint ComputeJamCrc(byte[] data)
{
    var crc = 0u;
    for (var i = 0; i < data.Length; i++)
    {
        crc ^= data[i];
        for (var j = 0; j < 8; j++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
    }
    return crc;
}

// First 64 bytes of C2
Console.WriteLine($"\nFirst 64 bytes of C2:");
for (var i = 0; i < Math.Min(64, c2SearchData.Length); i++)
    Console.Write($"{c2SearchData[i]:X2} ");
Console.WriteLine();

static uint ComputeCrc32(byte[] data)
{
    var crc = 0xFFFFFFFFu;
    var table = BuildCrc32Table();
    foreach (var b in data)
        crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
    return crc ^ 0xFFFFFFFFu;
}
static uint[] BuildCrc32Table()
{
    var table = new uint[256];
    for (uint i = 0; i < 256; i++)
    {
        var c = i;
        for (var j = 0; j < 8; j++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        table[i] = c;
    }
    return table;
}
