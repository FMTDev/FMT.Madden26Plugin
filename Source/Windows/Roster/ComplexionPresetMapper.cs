using System.Text.Json;
using FMT.Core.Readers.Ebx;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Roster;

public enum SkinToneGroup
{
    Unknown,
    Light,
    Medium,
    Dark,
    Mannequin,
}

public class ComplexionPresetMapper
{
    private const string ItemPrefix = "content/FootballCharacter/Items/GenericHeads/Player/";

    private static readonly int[] ToneThresholds = { 1, 2 };

    private Dictionary<string, SkinToneGroup> _genericToGroup = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SkinToneGroup> GenericToGroup => _genericToGroup;

    public SkinToneGroup GetGroup(string genericId)
    {
        return _genericToGroup.TryGetValue(genericId, out var group) ? group : SkinToneGroup.Unknown;
    }

    public int MappedCount => _genericToGroup.Count;

    public void BuildFromFmt()
    {
        var assetService = SingletonService.GetInstance<IAssetManagementService>();
        if (assetService == null) return;

        var itemEntries = assetService.EnumerateEbx()
            .Where(e => e.Name.StartsWith(ItemPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in itemEntries)
        {
            try
            {
                var reader = EbxReader.GetEbxReader(new MemoryStream(entry.ModifiedEntry.Data));
                reader.InitialRead(reader.BaseStream, false);
                var asset = reader.ReadAsset();

                var recipeName = ReadRecipeName(asset);
                if (string.IsNullOrEmpty(recipeName))
                    continue;

                var toneValue = ReadSkinToneBaseValue(asset);
                var group = MapToneValue(toneValue);

                if (group != SkinToneGroup.Unknown)
                    _genericToGroup[recipeName] = group;
            }
            catch
            {
            }
        }
    }

    public string SerializeMapping()
    {
        var dict = _genericToGroup.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
    }

    public void DeserializeMapping(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (dict == null) return;

        _genericToGroup.Clear();
        foreach (var kv in dict)
        {
            if (Enum.TryParse<SkinToneGroup>(kv.Value, true, out var group))
                _genericToGroup[kv.Key] = group;
        }
    }

    private static string ReadRecipeName(FMT.Models.Assets.EbxAsset asset)
    {
        try
        {
            var root = asset.RootObject;
            var recipeField = FindField(root, "RecipeAssetName");
            if (recipeField == null)
            {
                var nameField = FindField(root, "Name");
                var nameStr = nameField?.ToString();
                if (!string.IsNullOrEmpty(nameStr) && nameStr.StartsWith(ItemPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var name = nameStr.Substring(ItemPrefix.Length);
                    if (name.EndsWith("_item", StringComparison.OrdinalIgnoreCase))
                        return name.Substring(0, name.Length - "_item".Length);
                }
                return null;
            }

            var strValue = FindField(recipeField, "StrValue");
            return strValue?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadSkinToneBaseValue(FMT.Models.Assets.EbxAsset asset)
    {
        try
        {
            var root = asset.RootObject;
            var val = FindField(root, "SkinToneBaseValue");
            if (val == null) return null;

            if (val is int i) return i;
            if (val is uint ui) return (int)ui;
            if (val is byte b) return b;
            if (val is sbyte sb) return sb;
            if (val is short s) return s;
            if (val is ushort us) return us;
            if (val is long l) return (int)l;
            if (val is ulong ul) return (int)ul;
            if (val is float f) return (int)f;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;

            return null;
        }
        catch
        {
            return null;
        }
    }

    internal static SkinToneGroup MapToneValue(int? value)
    {
        if (!value.HasValue)
            return SkinToneGroup.Unknown;

        return value.Value switch
        {
            0 => SkinToneGroup.Light,
            1 => SkinToneGroup.Medium,
            >= 2 => SkinToneGroup.Dark,
            _ => SkinToneGroup.Unknown,
        };
    }

    internal static object FindField(object obj, string fieldName)
    {
        if (obj == null) return null;

        var type = obj.GetType();

        var prop = type.GetProperty(fieldName);
        if (prop != null)
        {
            var val = prop.GetValue(obj);
            if (val != null) return val;
        }

        var field = type.GetField(fieldName);
        if (field != null)
        {
            var val = field.GetValue(obj);
            if (val != null) return val;
        }

        var indexer = type.GetProperty("Item", new[] { typeof(string) });
        if (indexer != null)
        {
            try { return indexer.GetValue(obj, new object[] { fieldName }); }
            catch { }
        }

        var getValue = type.GetMethod("GetValue", new[] { typeof(string) });
        if (getValue != null)
        {
            try { return getValue.Invoke(obj, new object[] { fieldName }); }
            catch { }
        }

        var getFieldValue = type.GetMethod("GetFieldValue", new[] { typeof(string) });
        if (getFieldValue != null)
        {
            try { return getFieldValue.Invoke(obj, new object[] { fieldName }); }
            catch { }
        }

        return null;
    }

    internal static void SetFieldValue(object obj, string fieldName, object value)
    {
        if (obj == null) return;
        var type = obj.GetType();
        var prop = type.GetProperty(fieldName);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
            return;
        }
        var field = type.GetField(fieldName);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }
    }

    internal static object CloneObject(object original)
    {
        if (original == null) return null;
        var t = original.GetType();
        try
        {
            var clone = Activator.CreateInstance(t);
            foreach (var prop in t.GetProperties())
            {
                if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                    prop.SetValue(clone, prop.GetValue(original));
            }
            foreach (var field in t.GetFields())
                field.SetValue(clone, field.GetValue(original));
            return clone;
        }
        catch
        {
            return original;
        }
    }

    public static int? ExtractSkinToneBaseValue(string genericId)
    {
        if (string.IsNullOrEmpty(genericId))
            return null;

        var parts = genericId.Split('_');
        if (parts.Length < 2)
            return null;

        if (int.TryParse(parts[^1], out var val))
            return val;

        return null;
    }

    public static string ToneFromTemplateId(string genericId)
    {
        var val = ExtractSkinToneBaseValue(genericId);
        return val.HasValue ? MapToneValue(val).ToString() : "";
    }
}
