using System.IO.Compression;

namespace Madden26Plugin.Roster;

public class CFB27RosterReader
{
    private const int SmallRecordThreshold = 100;

    private static readonly uint[] Crc32Lookup = BuildCrc32Table();
    private static uint[] BuildCrc32Table()
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
    internal static uint ComputeCrc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Crc32Lookup[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public RosterData ReadRosterFile(string filePath)
    {
        var allBytes = File.ReadAllBytes(filePath);
        return ReadRoster(allBytes);
    }

    public RosterData ReadRoster(byte[] fileBytes)
    {
        if (fileBytes.Length < 8 || System.Text.Encoding.ASCII.GetString(fileBytes, 0, 8) != "FBCHUNKS")
            throw new InvalidDataException("Not a valid Frostbite save file (missing FBCHUNKS magic).");

        var deflateData = DecompressOuterDeflate(fileBytes);
        var rawStreams = ExtractGzipRecords(deflateData);

        var fbChunksHeader = fileBytes.Length >= 0x4A ? fileBytes[..0x4A] : null;

        var result = new RosterData
        {
            RawDeflatedPayload = deflateData,
            AllDecompressedStreams = rawStreams,
            ContainerHeader = deflateData.Length >= 23 ? deflateData[..23] : null,
            FbChunksHeader = fbChunksHeader,
        };

        var compressedStreams = ExtractRawGzipStreams(deflateData);

        // Capture C2 trailing data after the last real gzip stream
        var headerLen = result.ContainerHeader?.Length ?? 0;
        var c2Start = compressedStreams.Count > 0
            ? headerLen + compressedStreams.Sum(s => s.Length)
            : GetC2DataStart(deflateData);
        if (c2Start > 0 && c2Start < deflateData.Length)
            result.C2TrailingData = deflateData[c2Start..];

        for (var i = 0; i < rawStreams.Count; i++)
        {
            var record = rawStreams[i];
            if (i < compressedStreams.Count)
                result.AllCompressedStreams.Add(compressedStreams[i]);

            if (record.Length < SmallRecordThreshold)
            {
                var stats = RosterPlayerStats.Deserialize(record, i);
                stats.StreamIndex = i;
                result.StatsRecords.Add(stats);
            }
            else
            {
                var player = ParsePlayerRecord(record);
                if (player != null)
                {
                    player.RawRecordData = record;
                    player.StreamIndex = i;
                    result.Players.Add(player);
                }
            }
        }

        return result;
    }

    private static byte[] DecompressOuterDeflate(byte[] fileBytes)
    {
        var zlibHeader = 2;
        var deflateStart = 0x4A;
        var deflateData = fileBytes[deflateStart..];

        using var outputStream = new MemoryStream();
        using var inputStream = new MemoryStream(deflateData[zlibHeader..]);
        using var deflate = new DeflateStream(inputStream, CompressionMode.Decompress);
        deflate.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    internal static List<byte[]> ExtractRawGzipStreams(byte[] data)
    {
        var streams = new List<byte[]>();
        for (var i = 0; i < data.Length - 3; i++)
        {
            if (data[i] != 0x1F || data[i + 1] != 0x8B || data[i + 2] != 0x08)
                continue;

            var nextGzip = data.Length;
            for (var j = i + 3; j < data.Length - 3; j++)
            {
                if (data[j] == 0x1F && data[j + 1] == 0x8B && data[j + 2] == 0x08)
                {
                    nextGzip = j;
                    break;
                }
            }

            // Verify this is a real gzip stream
            byte[] decompressed;
            try
            {
                using var msOut = new MemoryStream();
                using var msIn = new MemoryStream(data[i..nextGzip]);
                using var gzip = new GZipStream(msIn, CompressionMode.Decompress);
                gzip.CopyTo(msOut);
                decompressed = msOut.ToArray();
            }
            catch
            {
                // First false match in C2 data — stop here
                return streams;
            }

            // Check if nextGzip starts another valid gzip or is a C2 false match
            var nextIsValid = nextGzip < data.Length - 3 &&
                data[nextGzip] == 0x1F && data[nextGzip + 1] == 0x8B && data[nextGzip + 2] == 0x08 &&
                IsValidGzip(data, nextGzip);

            // If next is NOT a valid gzip, we're at the last real stream — clip it
            if (!nextIsValid)
            {
                var gzipEnd = FindGzipEnd(data, i, nextGzip, decompressed);
                streams.Add(data[i..gzipEnd]);
                return streams;
            }

            streams.Add(data[i..nextGzip]);
            i = nextGzip - 1;
        }
        return streams;
    }

    internal static List<byte[]> ExtractGzipRecords(byte[] data)
    {
        var records = new List<byte[]>();
        for (var i = 0; i < data.Length - 3; i++)
        {
            if (data[i] != 0x1F || data[i + 1] != 0x8B || data[i + 2] != 0x08)
                continue;

            var nextGzip = data.Length;
            for (var j = i + 3; j < data.Length - 3; j++)
            {
                if (data[j] == 0x1F && data[j + 1] == 0x8B && data[j + 2] == 0x08)
                {
                    nextGzip = j;
                    break;
                }
            }

            byte[] decompressed;
            try
            {
                using var msOut = new MemoryStream();
                using var msIn = new MemoryStream(data[i..nextGzip]);
                using var gzip = new GZipStream(msIn, CompressionMode.Decompress);
                gzip.CopyTo(msOut);
                decompressed = msOut.ToArray();
            }
            catch
            {
                return records;
            }

            records.Add(decompressed);

            var nextIsValid = nextGzip < data.Length - 3 &&
                data[nextGzip] == 0x1F && data[nextGzip + 1] == 0x8B && data[nextGzip + 2] == 0x08 &&
                IsValidGzip(data, nextGzip);

            if (!nextIsValid)
            {
                var gzipEnd = FindGzipEnd(data, i, nextGzip, decompressed);
                i = gzipEnd - 1;
                return records;
            }

            i = nextGzip - 1;
        }
        return records;
    }

    internal static bool IsValidGzip(byte[] data, int offset)
    {
        if (offset > data.Length - 10) return false;
        var nextGzip = data.Length;
        for (var j = offset + 3; j < data.Length - 3; j++)
        {
            if (data[j] == 0x1F && data[j + 1] == 0x8B && data[j + 2] == 0x08)
            {
                nextGzip = j;
                break;
            }
        }
        try
        {
            using var msOut = new MemoryStream();
            using var msIn = new MemoryStream(data[offset..nextGzip]);
            using var gzip = new GZipStream(msIn, CompressionMode.Decompress);
            gzip.CopyTo(msOut);
            return msOut.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    internal static int FindGzipEnd(byte[] data, int streamStart, int searchEnd, byte[] decompressed)
    {
        var expectedCrc = ComputeCrc32(decompressed);
        var expectedIsize = (uint)decompressed.Length;
        for (var scan = searchEnd - 8; scan >= streamStart + 10; scan--)
        {
            var crc = (uint)(data[scan] | (data[scan + 1] << 8) | (data[scan + 2] << 16) | (data[scan + 3] << 24));
            if (crc != expectedCrc) continue;
            var isize = (uint)(data[scan + 4] | (data[scan + 5] << 8) | (data[scan + 6] << 16) | (data[scan + 7] << 24));
            if (isize == expectedIsize)
                return scan + 8;
        }
        return searchEnd;
    }

    internal static int GetC2DataStart(byte[] data)
    {
        for (var i = 23; i < data.Length - 3; i++)
        {
            if (data[i] != 0x1F || data[i + 1] != 0x8B || data[i + 2] != 0x08)
                continue;

            if (!IsValidGzip(data, i))
                return i;
        }
        return data.Length;
    }

    private static readonly HashSet<string> KnownPositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "QB","RB","FB","WR","TE","OT","OG","C","OL","LT","LG","RG","RT",
        "DL","DE","DT","NT","EDGE","LB","ILB","OLB","MLB","CB","DB","FS","SS","S","NB",
        "K","PK","P","LS","KR","PR","ATH","RET","ST","KO","PUNTER"
    };

    private static readonly HashSet<string> KnownClassYears = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fr","RFr","So","RSo","Jr","RJr","Sr","RSr","GS","Gr","Grad",
        "Freshman","Sophomore","Junior","Senior","Graduate"
    };

    internal static PlayerVisualRecipe ParsePlayerRecord(byte[] record)
    {
        if (record.Length < 20)
            return null;

        var strings = ExtractStrings(record);
        if (strings.Count < 3)
            return null;

        var player = new PlayerVisualRecipe();

        for (var i = 0; i < strings.Count; i++)
        {
            var s = strings[i];

            if (i == 0)
                player.FullId = s;
            else if (i == 1)
                player.FirstName = s;
            else if (i == 2)
                player.LastName = s;
            else if (s.StartsWith("Unique_") || s.StartsWith("Generic_"))
                player.UniqueId = s;
            else if (s.EndsWith("_BodyType"))
                player.BodyType = s;
            else if (KnownPositions.Contains(s))
                player.Position = s;
            else if (KnownClassYears.Contains(s))
                player.ClassYear = s;
            else if (string.IsNullOrEmpty(player.JerseyNumber) && s.Length <= 2 && s.All(char.IsDigit) && i >= 3 && i <= 6)
                player.JerseyNumber = s;
            else if (s.StartsWith("Gear") || s.StartsWith("Face") || s.StartsWith("Calf") || s.StartsWith("Arm") ||
                     s.StartsWith("Elbow") || s.StartsWith("Backplate") || s.StartsWith("Knee") ||
                     s.StartsWith("Thigh") || s.StartsWith("Towel") || s.StartsWith("Handwarmer") ||
                     s.StartsWith("Spats") || s.StartsWith("Undershirt") || s.StartsWith("Flakjacket") ||
                     s.StartsWith("Small_") || s == "Spats_None")
            {
                var slot = DeriveSlotName(s);
                player.Equipment[slot] = s;
                player.EquipmentEntries.Add(new EquipmentEntry { Slot = slot, Value = s });
            }
        }

        ParseHeightFromRecord(record, player);

        return player;
    }

    internal static bool ApplyHeightToRecord(byte[] record, int? newHeightInches)
    {
        if (!newHeightInches.HasValue)
            return false;

        var heightByte = (byte)(newHeightInches.Value * 2 - 12);

        for (var i = 0; i < record.Length - 5; i++)
        {
            if (record[i] == 0xA2 && record[i + 1] == 0x9B && record[i + 2] == 0xA3 && record[i + 3] == 0x00)
            {
                record[i + 4] = heightByte;
                return true;
            }
        }
        return false;
    }

    internal static void ParseHeightFromRecord(byte[] record, PlayerVisualRecipe player)
    {
        for (var i = 0; i < record.Length - 5; i++)
        {
            if (record[i] == 0xA2 && record[i + 1] == 0x9B && record[i + 2] == 0xA3 && record[i + 3] == 0x00)
            {
                player.HeightByte = record[i + 4];
                player.HeightInches = (record[i + 4] + 12) / 2;
                break;
            }
        }
    }

    internal static List<string> ExtractStrings(byte[] data)
    {
        var strings = new List<string>();
        var current = new List<byte>();
        var inString = false;

        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] >= 32 && data[i] <= 126)
            {
                current.Add(data[i]);
                inString = true;
            }
            else
            {
                if (inString && current.Count >= 2)
                {
                    strings.Add(System.Text.Encoding.ASCII.GetString(current.ToArray()));
                }
                current.Clear();
                inString = false;
            }
        }

        if (inString && current.Count >= 2)
            strings.Add(System.Text.Encoding.ASCII.GetString(current.ToArray()));

        return strings;
    }

    internal static string DeriveSlotName(string gearValue)
    {
        if (gearValue.StartsWith("GearFaceMask_")) return "FaceMask";
        if (gearValue.StartsWith("GearVisor_")) return "Visor";
        if (gearValue.StartsWith("GearHelmet_")) return "Helmet";
        if (gearValue.StartsWith("Gear_JerseyStyle_")) return "JerseyStyle";
        if (gearValue.StartsWith("Gear_Socks_")) return "Socks";
        if (gearValue.StartsWith("GearSpats_")) return "Spats";
        if (gearValue.StartsWith("GearFootwear_")) return "Footwear";
        if (gearValue.StartsWith("GearArmSleeve_") || gearValue.StartsWith("ArmSleeve_")) return "ArmSleeve";
        if (gearValue.StartsWith("CalfGear_")) return "CalfGear";
        if (gearValue.StartsWith("ElbowGear_")) return "ElbowGear";
        if (gearValue.StartsWith("GearWrist_") || gearValue.StartsWith("Wrist_")) return "Wrist";
        if (gearValue.StartsWith("GearHand_")) return "HandGlove";
        if (gearValue.StartsWith("GearMouthpiece_")) return "Mouthpiece";
        if (gearValue.StartsWith("Backplate_")) return "Backplate";
        if (gearValue.StartsWith("Small_") || gearValue == "Small_Pads") return "PadSize";
        if (gearValue.StartsWith("Towel_")) return "Towel";
        if (gearValue.StartsWith("Handwarmer_")) return "Handwarmer";
        if (gearValue.StartsWith("HandwarmerStyle_")) return "HandwarmerStyle";
        if (gearValue.StartsWith("GearNeckpad_")) return "Neckpad";
        if (gearValue.StartsWith("Undershirt_")) return "Undershirt";
        if (gearValue.StartsWith("FaceMarks_")) return "FaceMarks";
        if (gearValue.StartsWith("KneePad_") || gearValue.StartsWith("GearKneeBrace_")) return "KneeBrace";
        if (gearValue.StartsWith("ThighPad_")) return "ThighPad";
        if (gearValue.StartsWith("GearPants_")) return "Pants";
        if (gearValue.StartsWith("GearLegsBase_")) return "LegsBase";
        if (gearValue.StartsWith("Flakjacket_")) return "Flakjacket";
        if (gearValue.StartsWith("Spats_")) return "Spats";
        return "Other";
    }
}
