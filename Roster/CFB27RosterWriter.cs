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

            if (!hasChanges && player.HeightInches.HasValue && player.HeightByte.HasValue)
            {
                var newByte = (byte)(player.HeightInches.Value * 2 - 12);
                if (player.HeightByte.Value != newByte)
                {
                    modifiedData = (byte[])originalDecompressed.Clone();
                    if (CFB27RosterReader.ApplyHeightToRecord(modifiedData, player.HeightInches))
                        hasChanges = true;
                }
            }

            if (hasChanges)
                newCompressed[idx] = CompressGzip(modifiedData);
        }

        foreach (var stat in data.StatsRecords)
        {
            var idx = stat.StreamIndex;
            if (idx < 0 || idx >= totalStreams)
                continue;

            var originalDecompressed = data.AllDecompressedStreams[idx];
            var modifiedData = stat.Serialize();

            if (!modifiedData.AsSpan().SequenceEqual(originalDecompressed))
                newCompressed[idx] = CompressGzip(modifiedData);
        }

        using var gzipConcat = new MemoryStream();
        foreach (var stream in newCompressed)
            gzipConcat.Write(stream);

        var gzipConcatData = gzipConcat.ToArray();

        // Build inner payload: 23-byte container header + gzip streams
        using var innerPayload = new MemoryStream();
        if (data.ContainerHeader != null)
            innerPayload.Write(data.ContainerHeader);
        innerPayload.Write(gzipConcatData);

        var innerPayloadBytes = innerPayload.ToArray();
        var deflatedData = DeflateCompress(innerPayloadBytes);

        // Build FBCHUNKS header with updated size fields
        var fbChunksHeader = data.FbChunksHeader ?? (originalFileBytes.Length > 0x4A ? originalFileBytes[..0x4A] : originalFileBytes);
        if (fbChunksHeader.Length >= 22)
        {
            var compressedSize = (uint)(deflatedData.Length);
            var decompressedSize = (uint)(innerPayloadBytes.Length);

            fbChunksHeader[14] = (byte)(compressedSize & 0xFF);
            fbChunksHeader[15] = (byte)((compressedSize >> 8) & 0xFF);
            fbChunksHeader[16] = (byte)((compressedSize >> 16) & 0xFF);
            fbChunksHeader[17] = (byte)((compressedSize >> 24) & 0xFF);

            fbChunksHeader[18] = (byte)(decompressedSize & 0xFF);
            fbChunksHeader[19] = (byte)((decompressedSize >> 8) & 0xFF);
            fbChunksHeader[20] = (byte)((decompressedSize >> 16) & 0xFF);
            fbChunksHeader[21] = (byte)((decompressedSize >> 24) & 0xFF);
        }

        using var result = new MemoryStream(fbChunksHeader.Length + deflatedData.Length);
        result.Write(fbChunksHeader);
        result.Write(deflatedData);
        return result.ToArray();
    }

    private static byte[] CompressGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0xDA);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);

        return output.ToArray();
    }
}
