using System.IO.Compression;

namespace Madden26Plugin.Roster;

public class CFB27RosterWriter
{
    public void WriteRosterFile(string outputPath, RosterData data, byte[] originalFileBytes)
    {
        var payload = BuildPayload(data, originalFileBytes);
        File.WriteAllBytes(outputPath, payload);
    }

    public byte[] BuildPayload(RosterData data, byte[] originalFileBytes)
    {
        var totalStreams = data.AllCompressedStreams.Count;
        var newCompressed = new byte[totalStreams][];

        for (var i = 0; i < totalStreams; i++)
            newCompressed[i] = data.AllCompressedStreams[i];

        foreach (var player in data.Players)
        {
            var idx = player.StreamIndex;
            if (idx < 0 || idx >= totalStreams)
                continue;

            var originalDecompressed = data.AllDecompressedStreams[idx];
            var modifiedData = player.RawRecordData;

            if (modifiedData == null)
                continue;

            var hasChanges = !modifiedData.AsSpan().SequenceEqual(originalDecompressed);

            if (!hasChanges && player.HeightByte.HasValue)
            {
                var existingByte = ReadHeightByte(originalDecompressed);
                if (!existingByte.HasValue || existingByte.Value != player.HeightByte.Value)
                {
                    modifiedData = (byte[])originalDecompressed.Clone();
                    if (CFB27RosterReader.ApplyHeightToRecord(modifiedData, player.HeightInches ?? (player.HeightByte.Value + 12) / 2))
                        hasChanges = true;
                }
            }

            if (hasChanges)
                newCompressed[idx] = CompressGzipToSize(modifiedData, data.AllCompressedStreams[idx].Length);
        }

        // Stats serializer doesn't reproduce original bytes exactly (padding, ordering).
        // Since no stats editing is exposed in the UI yet, skip re-compression to
        // preserve the inner payload for a no-edit round-trip.
        // (Uncomment when stats editing is implemented.)

        using var gzipConcat = new MemoryStream();
        foreach (var stream in newCompressed)
            gzipConcat.Write(stream);

        var gzipConcatData = gzipConcat.ToArray();

        // Build inner payload: 23-byte container header + gzip streams + C2 trailing data
        using var innerPayload = new MemoryStream();
        if (data.ContainerHeader != null)
            innerPayload.Write(data.ContainerHeader);
        innerPayload.Write(gzipConcatData);
        if (data.C2TrailingData != null)
            innerPayload.Write(data.C2TrailingData);

        var innerPayloadBytes = innerPayload.ToArray();

        byte[] deflatedData;
        int originalCompressedLen = originalFileBytes.Length - 0x4A;
        if (data.RawDeflatedPayload != null &&
            data.RawDeflatedPayload.AsSpan().SequenceEqual(innerPayloadBytes))
        {
            deflatedData = originalFileBytes[0x4A..];
        }
        else
        {
            deflatedData = DeflateCompress(innerPayloadBytes);
            if (deflatedData.Length < originalCompressedLen)
            {
                var padded = new byte[originalCompressedLen];
                Array.Copy(deflatedData, padded, deflatedData.Length);
                deflatedData = padded;
            }
        }

        // Use original FBCHUNKS header as-is (keep all fields unchanged)
        var fbChunksHeader = data.FbChunksHeader ?? (originalFileBytes.Length > 0x4A ? originalFileBytes[..0x4A] : originalFileBytes);

        using var result = new MemoryStream(fbChunksHeader.Length + deflatedData.Length);
        result.Write(fbChunksHeader);
        result.Write(deflatedData);
        return result.ToArray();
    }

    internal static byte? ReadHeightByte(byte[] record)
    {
        for (var i = 0; i < record.Length - 5; i++)
        {
            if (record[i] == 0xA2 && record[i + 1] == 0x9B && record[i + 2] == 0xA3 && record[i + 3] == 0x00)
                return record[i + 4];
        }
        return null;
    }

    private static byte[] CompressGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] IonicCompress(byte[] data, int level)
    {
        using var output = new MemoryStream();
        using (var gzip = new Ionic.Zlib.GZipStream(output, Ionic.Zlib.CompressionMode.Compress,
            (Ionic.Zlib.CompressionLevel)level))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] DotNetCompress(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, level))
            gzip.Write(data);
        return output.ToArray();
    }

    /// <summary>Compress to gzip, padding AFTER the trailer to match targetSize exactly.</summary>
    private static byte[] CompressGzipToSize(byte[] data, int targetSize)
    {
        // Collect candidates from all available compressors/levels
        var candidates = new List<byte[]>();

        // .NET levels
        candidates.Add(DotNetCompress(data, CompressionLevel.Optimal));
        candidates.Add(DotNetCompress(data, CompressionLevel.Fastest));
        try { candidates.Add(DotNetCompress(data, CompressionLevel.SmallestSize)); } catch { }

        // Ionic levels 1-9
        for (var l = 1; l <= 9; l++)
            candidates.Add(IonicCompress(data, l));

        // Check for exact match
        foreach (var c in candidates)
            if (c.Length == targetSize) return c;

        // Pick best undersized candidate (largest that fits)
        var best = candidates.Where(c => c.Length < targetSize).OrderByDescending(c => c.Length).FirstOrDefault();
        if (best == null)
        {
            // All oversized — return smallest oversize
            return candidates.OrderBy(c => c.Length).First();
        }

        var gap = targetSize - best.Length;

        // Pad AFTER the gzip trailer (between streams, not inside the stream).
        // The game uses the next "1F 8B 08" magic to find stream boundaries,
        // so padding bytes after the trailer are ignored by the gzip decompressor.
        if (gap > 0)
        {
            var result = new byte[targetSize];
            Array.Copy(best, result, best.Length);
            // Remaining bytes are already zero-initialized (null padding)
            return result;
        }

        // Shouldn't reach here
        return best;
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0xDA);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);

        // Compute and write proper Adler-32 checksum (big-endian)
        uint a = 1, b = 0;
        foreach (var v in data)
        {
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
}
