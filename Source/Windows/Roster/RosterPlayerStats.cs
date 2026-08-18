using System.Collections.ObjectModel;

namespace Madden26Plugin.Roster;

public class RosterPlayerStats
{
    public int StreamIndex { get; set; }
    public Dictionary<uint, byte> FieldTags { get; set; } = new();
    public ObservableCollection<FieldTagEntry> TagEntries { get; set; } = new();

    public int? JerseyNumber
    {
        get => FieldTags.TryGetValue(0xC628F300, out var v) ? (int)v : null;
        set
        {
            if (value.HasValue)
                SetTag(0xC628F300, (byte)value.Value);
            else
                FieldTags.Remove(0xC628F300);
        }
    }

    public byte? OverallRating
    {
        get => FieldTags.TryGetValue(0xC62CF300, out var v) ? v : null;
        set => SetTag(0xC62CF300, value);
    }

    public byte? Speed
    {
        get => FieldTags.TryGetValue(0x8A3B3300, out var v) ? v : null;
        set => SetTag(0x8A3B3300, value);
    }

    public byte? Strength
    {
        get => FieldTags.TryGetValue(0xDF2CF400, out var v) ? v : null;
        set => SetTag(0xDF2CF400, value);
    }

    public byte? Awareness
    {
        get => FieldTags.TryGetValue(0xA22CF400, out var v) ? v : null;
        set => SetTag(0xA22CF400, value);
    }

    public byte? Agility
    {
        get => FieldTags.TryGetValue(0x8E2CF400, out var v) ? v : null;
        set => SetTag(0x8E2CF400, value);
    }

    public byte? Acceleration
    {
        get => FieldTags.TryGetValue(0xC62D3300, out var v) ? v : null;
        set => SetTag(0xC62D3300, value);
    }

    public void SetTag(uint tag, byte? value)
    {
        if (value.HasValue)
        {
            FieldTags[tag] = value.Value;
            SyncEntries();
        }
    }

    public byte[] Serialize()
    {
        var ms = new MemoryStream();
        var ordered = FieldTags.OrderBy(kv => kv.Key).ToList();
        foreach (var kv in ordered)
        {
            ms.Write(BitConverter.GetBytes(kv.Key), 0, 4);
            ms.WriteByte(kv.Value);
        }
        return ms.ToArray();
    }

    public static RosterPlayerStats Deserialize(byte[] data, int streamIndex)
    {
        var stats = new RosterPlayerStats { StreamIndex = streamIndex };
        var i = 0;
        while (i + 4 < data.Length)
        {
            var tag = BitConverter.ToUInt32(data, i);
            var value = data[i + 4];
            stats.FieldTags[tag] = value;
            i += 5;
        }
        stats.SyncEntries();
        return stats;
    }

    private void SyncEntries()
    {
        TagEntries.Clear();
        foreach (var kv in FieldTags.OrderBy(k => k.Key))
            TagEntries.Add(new FieldTagEntry { Tag = $"0x{kv.Key:X8}", Value = kv.Value });
    }

    public override string ToString() =>
        $"#{JerseyNumber?.ToString() ?? "?"} OVR={OverallRating?.ToString() ?? "?"}";
}

public class FieldTagEntry
{
    public string Tag { get; set; }
    public byte Value { get; set; }
    public override string ToString() => $"{Tag}: {Value}";
}
