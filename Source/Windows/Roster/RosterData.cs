namespace Madden26Plugin.Roster;

public class RosterData
{
    public List<PlayerVisualRecipe> Players { get; set; } = new();
    public List<RosterPlayerStats> StatsRecords { get; set; } = new();
    public List<byte[]> AllDecompressedStreams { get; set; } = new();
    public List<byte[]> AllCompressedStreams { get; set; } = new();
    public byte[] RawDeflatedPayload { get; set; }
    public byte[] ContainerHeader { get; set; }
    public byte[] FbChunksHeader { get; set; }
    public byte[] C2TrailingData { get; set; }
}
