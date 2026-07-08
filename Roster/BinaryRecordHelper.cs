namespace Madden26Plugin.Roster;

internal static class BinaryRecordHelper
{
    public static void RebuildPlayerRecord(PlayerVisualRecipe player, string newFirstName, string newLastName, string newUniqueId)
    {
        var data = player.RawRecordData;
        if (data == null || data.Length == 0) return;

        var strings = CFB27RosterReader.ExtractStrings(data);
        if (strings.Count < 3) return;

        var offsets = FindStringOffsets(data, strings);
        using var ms = new MemoryStream(data.Length + 64);
        ms.Write(data, 0, data.Length);

        if (offsets.Count >= 2)
            ReplaceStringAt(ms, offsets[1], strings[1], newFirstName);
        if (offsets.Count >= 3)
            ReplaceStringAt(ms, offsets[2], strings[2], newLastName);

        var uniqueIdx = strings.FindIndex(s => s.StartsWith("Unique_") || s.StartsWith("Generic_"));
        if (uniqueIdx >= 0 && uniqueIdx < offsets.Count)
            ReplaceStringAt(ms, offsets[uniqueIdx], strings[uniqueIdx], newUniqueId);

        player.RawRecordData = ms.ToArray();
        player.FirstName = newFirstName;
        player.LastName = newLastName;
        player.UniqueId = newUniqueId;
    }

    public static void ReplaceUniqueId(PlayerVisualRecipe player, string newUniqueId)
    {
        var data = player.RawRecordData;
        if (data == null || data.Length == 0) return;

        var strings = CFB27RosterReader.ExtractStrings(data);
        var offsets = FindStringOffsets(data, strings);
        var uniqueIdx = strings.FindIndex(s => s.StartsWith("Unique_") || s.StartsWith("Generic_"));
        if (uniqueIdx < 0 || uniqueIdx >= offsets.Count || offsets[uniqueIdx] < 0) return;

        using var ms = new MemoryStream(data.Length + 64);
        ms.Write(data, 0, data.Length);
        ReplaceStringAt(ms, offsets[uniqueIdx], strings[uniqueIdx], newUniqueId);
        player.RawRecordData = ms.ToArray();
        player.UniqueId = newUniqueId;
    }

    public static List<int> FindStringOffsets(byte[] data, List<string> strings)
    {
        var offsets = new List<int>();
        var searchStart = 0;
        foreach (var s in strings)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(s);
            var idx = IndexOfSequence(data, bytes, searchStart);
            offsets.Add(idx >= 0 ? idx : -1);
            if (idx >= 0)
                searchStart = idx + bytes.Length;
        }
        return offsets;
    }

    public static int IndexOfSequence(byte[] data, byte[] pattern, int startIndex)
    {
        for (var i = startIndex; i <= data.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    public static void ReplaceStringAt(MemoryStream ms, int offset, string oldString, string newString)
    {
        if (offset < 0) return;
        var oldBytes = System.Text.Encoding.ASCII.GetBytes(oldString);
        var newBytes = System.Text.Encoding.ASCII.GetBytes(newString ?? "");
        var data = ms.GetBuffer();
        var lenDiff = newBytes.Length - oldBytes.Length;
        if (lenDiff == 0)
            Array.Copy(newBytes, 0, data, offset, newBytes.Length);
        else if (lenDiff < 0)
        {
            Array.Copy(newBytes, 0, data, offset, newBytes.Length);
            Array.Copy(data, offset + oldBytes.Length, data, offset + newBytes.Length, ms.Length - offset - oldBytes.Length);
            ms.SetLength(ms.Length + lenDiff);
        }
        else
        {
            ms.SetLength(ms.Length + lenDiff);
            var movedData = ms.GetBuffer();
            Array.Copy(movedData, offset + oldBytes.Length, movedData, offset + newBytes.Length, ms.Length - offset - newBytes.Length);
            Array.Copy(newBytes, 0, movedData, offset, newBytes.Length);
        }
    }

    public static void ReplaceFieldValue(PlayerVisualRecipe player, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue) || oldValue == newValue) return;
        var data = player.RawRecordData;
        if (data == null || data.Length == 0) return;

        var strings = CFB27RosterReader.ExtractStrings(data);
        var offsets = FindStringOffsets(data, strings);
        var idx = strings.FindIndex(s => s == oldValue);
        if (idx < 0 || idx >= offsets.Count || offsets[idx] < 0) return;

        using var ms = new MemoryStream(data.Length + 64);
        ms.Write(data, 0, data.Length);
        ReplaceStringAt(ms, offsets[idx], oldValue, newValue);
        player.RawRecordData = ms.ToArray();
    }

    public static void ReplaceBodyType(PlayerVisualRecipe player, string newBodyType)
    {
        var oldBt = player.BodyType;
        if (string.IsNullOrEmpty(oldBt)) return;
        ReplaceFieldValue(player, oldBt, newBodyType);
        player.BodyType = newBodyType;
    }

    public static void ReplacePosition(PlayerVisualRecipe player, string newPos)
    {
        var oldPos = player.Position;
        if (oldPos == newPos) return;
        if (!string.IsNullOrEmpty(oldPos))
            ReplaceFieldValue(player, oldPos, newPos);
        player.Position = newPos;
    }

    public static void ReplaceClassYear(PlayerVisualRecipe player, string newYear)
    {
        var oldYear = player.ClassYear;
        if (oldYear == newYear) return;
        if (!string.IsNullOrEmpty(oldYear))
            ReplaceFieldValue(player, oldYear, newYear);
        player.ClassYear = newYear;
    }

    public static void ReplaceEquipmentValue(PlayerVisualRecipe player, string slot, string newValue)
    {
        if (!player.Equipment.TryGetValue(slot, out var oldValue) || oldValue == newValue) return;
        ReplaceFieldValue(player, oldValue, newValue);
        player.Equipment[slot] = newValue;
        var entry = player.EquipmentEntries.FirstOrDefault(e => e.Slot == slot);
        if (entry != null) entry.Value = newValue;
    }

    public static void ReplaceJerseyNumber(PlayerVisualRecipe player, string newNumber)
    {
        var oldNum = player.JerseyNumber;
        if (oldNum == newNumber) return;
        if (!string.IsNullOrEmpty(oldNum))
            ReplaceFieldValue(player, oldNum, newNumber);
        player.JerseyNumber = newNumber;
    }
}
