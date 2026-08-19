#!/usr/bin/env dotnet-script
#r "System.IO.Compression"
using System.IO.Compression;

var gameFile = File.ReadAllBytes("ROSTER-Community.bin");
var defStart = 0x4A;
var zlibEnd = 7911918;

// Decompress game's zlib to get inner payload
using var ms = new MemoryStream(gameFile, defStart + 2, zlibEnd - 2, false);
using var ds = new DeflateStream(ms, CompressionMode.Decompress);
using var innerMs = new MemoryStream();
ds.CopyTo(innerMs);
var innerPayload = innerMs.ToArray();
Console.WriteLine($"Inner payload: {innerPayload.Length} bytes");

var gameDeflate = gameFile[defStart..(defStart + zlibEnd)];
Console.WriteLine($"Game deflate:  {gameDeflate.Length} bytes");

// Function to compress with deflate
byte[] Compress(byte[] data, CompressionLevel level) {
    using var mo = new MemoryStream();
    mo.WriteByte(0x78); mo.WriteByte(0xDA);
    using (var def = new DeflateStream(mo, level, true))
        def.Write(data);
    // Adler-32
    uint a = 1, b = 0;
    foreach (var v in data) {
        a = (a + v) % 65521;
        b = (b + a) % 65521;
    }
    uint adler = (b << 16) | a;
    mo.WriteByte((byte)(adler >> 24)); mo.WriteByte((byte)(adler >> 16));
    mo.WriteByte((byte)(adler >> 8));  mo.WriteByte((byte)adler);
    return mo.ToArray();
}

var dotNetOpt = Compress(innerPayload, CompressionLevel.Optimal);
var dotNetFast = Compress(innerPayload, CompressionLevel.Fastest);
var dotNetSmall = Compress(innerPayload, CompressionLevel.SmallestSize);

Console.WriteLine($"\n.NET Optimal: {dotNetOpt.Length} bytes");
Console.WriteLine($".NET Fastest:  {dotNetFast.Length} bytes");
Console.WriteLine($".NET Smallest: {dotNetSmall.Length} bytes");

// Compare sizes
Console.WriteLine($"\nGame vs .NET Optimal first bytes match: {gameDeflate.AsSpan().SequenceEqual(dotNetOpt.AsSpan(0, Math.Min(100, dotNetOpt.Length)))}");

// Count identical bytes at start
int same = 0;
int minLen = Math.Min(gameDeflate.Length, dotNetOpt.Length);
for (int i = 0; i < minLen; i++) {
    if (gameDeflate[i] != dotNetOpt[i]) break;
    same++;
}
Console.WriteLine($"Consecutive identical bytes from start: {same}");

// Print first 20 bytes of each
Console.WriteLine("\nFirst 20 bytes:");
Console.Write("Game:        ");
for (int i = 0; i < 20; i++) Console.Write($"{gameDeflate[i]:X2} ");
Console.Write("\n.NET Optimal: ");
for (int i = 0; i < 20; i++) Console.Write($"{dotNetOpt[i]:X2} ");
Console.WriteLine();

Console.WriteLine("\nLast 20 bytes:");
Console.Write("Game:        ");
for (int i = gameDeflate.Length - 20; i < gameDeflate.Length; i++) Console.Write($"{gameDeflate[i]:X2} ");
Console.Write("\n.NET Optimal: ");
for (int i = dotNetOpt.Length - 20; i < dotNetOpt.Length; i++) Console.Write($"{dotNetOpt[i]:X2} ");
Console.WriteLine();
