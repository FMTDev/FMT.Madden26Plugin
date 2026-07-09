using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

var community = File.ReadAllBytes("ROSTER-Community.bin");
var official = File.ReadAllBytes("ROSTER-Official.bin");

int defStart = 0x4A;
int zlibEnd = 7911918;

byte[] DecompressZlib(byte[] file, int offset, int len) {
    using var ms = new MemoryStream(file, offset + 2, len - 2, false);
    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
    using var result = new MemoryStream();
    ds.CopyTo(result);
    return result.ToArray();
}

var inner = DecompressZlib(community, defStart, zlibEnd);
Console.WriteLine($"Inner payload size: {inner.Length}");

// Find all gzip streams
var gzipOffsets = new List<int>();
for (int i = 0; i < inner.Length - 2; i++) {
    if (inner[i] == 0x1F && inner[i+1] == 0x8B && inner[i+2] == 0x08)
        gzipOffsets.Add(i);
}
Console.WriteLine($"Found {gzipOffsets.Count} gzip streams");
Console.WriteLine($"Container header size: {gzipOffsets[0]} bytes");

// For first 3 gzip streams, decompress and find player records
for (int i = 0; i < Math.Min(3, gzipOffsets.Count); i++) {
    int startOff = gzipOffsets[i];
    int endOff = (i + 1 < gzipOffsets.Count) ? gzipOffsets[i + 1] : inner.Length;
    int gzipLen = endOff - startOff;
    
    using var gms = new MemoryStream(inner, startOff, gzipLen, false);
    using var gz = new GZipStream(gms, CompressionMode.Decompress);
    using var decomp = new MemoryStream();
    gz.CopyTo(decomp);
    var decompressed = decomp.ToArray();
    
    int lastCount = 0;
    for (int j = 0; j < decompressed.Length - 4; j++) {
        if (decompressed[j] == 'L' && decompressed[j+1] == 'A' && decompressed[j+2] == 'S' && decompressed[j+3] == 'T') {
            lastCount++;
            if (lastCount <= 2) {
                var ctx = Encoding.ASCII.GetString(decompressed, Math.Max(0, j-8), Math.Min(50, decompressed.Length - j + 8));
                Console.WriteLine($"Gzip[{i}] 'LAST' at offset {j}: ...{ctx}...");
            }
        }
    }
    Console.WriteLine($"Gzip[{i}]: compressed={gzipLen}B, decompressed={decompressed.Length}B, 'LAST' count={lastCount}");
}

// Search for 'LAST' in the RAW deflate data (before decompression)
var deflateData = community[defStart..(defStart + zlibEnd)];
Console.WriteLine("\n=== 'LAST' in RAW deflate ===");
int literalLastCount = 0;
for (int i = 2; i < deflateData.Length - 4; i++) {
    if (deflateData[i] == 'L' && deflateData[i+1] == 'A' && deflateData[i+2] == 'S' && deflateData[i+3] == 'T') {
        literalLastCount++;
        if (literalLastCount <= 3)
            Console.WriteLine($"  at deflate offset {i}");
    }
}
Console.WriteLine($"Total literal 'LAST': {literalLastCount}");

// Search for player names in raw deflate
string[] names = { "COLEMAN", "MARTIN", "WILLIAMS", "JOHNSON", "BROWN", "DAVIS", "MILLER", "WILSON", "MOORE", "TAYLOR" };
Console.WriteLine("\n=== Player names in RAW deflate ===");
foreach (var name in names) {
    var bytes = Encoding.ASCII.GetBytes(name);
    int count = 0;
    for (int i = 2; i < deflateData.Length - bytes.Length; i++) {
        bool match = true;
        for (int j = 0; j < bytes.Length; j++)
            if (deflateData[i + j] != bytes[j]) { match = false; break; }
        if (match) count++;
    }
    if (count > 0) Console.WriteLine($"  '{name}': {count} times");
}

// Test: can we decompress our re-compressed file?
Console.WriteLine("\n=== Testing ROSTER-RECOMPRESSED ===");
try {
    var rc = File.ReadAllBytes("ROSTER-RECOMPRESSED.bin");
    var rcDeflate = rc[defStart..];
    // Find where the zlib stream ends (search for adler-32)
    var innerFromRc = DecompressZlib(rc, defStart, rc.Length - defStart - 4); // approximate
    
    // Find the actual zlib end by decompressing
    using var rcMs = new MemoryStream(rc, defStart + 2, rc.Length - defStart - 2, false);
    using var rcDs = new DeflateStream(rcMs, CompressionMode.Decompress);
    using var rcResult = new MemoryStream();
    rcDs.CopyTo(rcResult);
    var rcInner = rcResult.ToArray();
    Console.WriteLine($"ROSTER-RECOMPRESSED inner: {rcInner.Length} bytes");
    Console.WriteLine($"Inner matches original: {inner.AsSpan().SequenceEqual(rcInner)}");
    Console.WriteLine($"Deflate stream consumed: {rcMs.Position} bytes (of {rc.Length - defStart - 2})");
} catch (Exception ex) {
    Console.WriteLine($"Error: {ex.Message}");
}

// Test .NET vs game deflate comparison
Console.WriteLine("\n=== .NET Deflate vs Game Deflate ===");
var dnOptimal = DeflateCompress(inner);
Console.WriteLine($"Game deflate: {deflateData.Length} bytes");
Console.WriteLine($".NET Optimal: {dnOptimal.Length} bytes");

int common = Math.Min(deflateData.Length, dnOptimal.Length);
int sameCount = 0;
for (int i = 0; i < common; i++)
    if (deflateData[i] == dnOptimal[i]) sameCount++;
Console.WriteLine($"First {common} bytes: {sameCount} identical");

// Show first mismatch
for (int i = 0; i < common; i++) {
    if (deflateData[i] != dnOptimal[i]) {
        Console.WriteLine($"First mismatch at offset {i}: game=0x{deflateData[i]:X2} .NET=0x{dnOptimal[i]:X2}");
        break;
    }
}

byte[] DeflateCompress(byte[] data) {
    using var output = new MemoryStream();
    output.WriteByte(0x78);
    output.WriteByte(0xDA);
    using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        deflate.Write(data);
    uint a = 1, b = 0;
    foreach (var v in data) {
        a = (a + v) % 65521;
        b = (b + a) % 65521;
    }
    uint adler = (b << 16) | a;
    output.WriteByte((byte)((adler >> 24) & 0xFF));
    output.WriteByte((byte)((adler >> 16) & 0xFF));
    output.WriteByte((byte)((adler >> 8) & 0xFF));
    output.WriteByte((byte)(adler & 0xFF));
    return output.ToArray();
}

Console.WriteLine("\nDone.");
