using System.Collections.ObjectModel;

namespace Madden26Plugin.Roster;

public class PlayerVisualRecipe
{
    public string FullId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string UniqueId { get; set; }
    public string BodyType { get; set; }
    public string Position { get; set; }
    public string ClassYear { get; set; }
    public string JerseyNumber { get; set; }
    public Dictionary<string, string> Equipment { get; set; } = new();
    public ObservableCollection<EquipmentEntry> EquipmentEntries { get; set; } = new();

    public byte[] RawRecordData { get; set; }
    public int StreamIndex { get; set; }

    public SkinToneGroup SkinTone { get; set; } = SkinToneGroup.Unknown;
    public int HairColorIndex { get; set; }
    public string HairColorRecipe { get; set; } = "";
    public string HairColorDescription { get; set; } = "";
    public string EyeColorRecipe { get; set; } = "";
    public string EyeColorDescription { get; set; } = "";
    public int? HeightInches { get; set; }
    public int? HeightByte { get; set; }
    public int? WeightOffset { get; set; }
    /// <summary>
    /// Weight is stored as offset from a base of 160.
    /// Actual weight = WeightOffset + 160.
    /// The game computes weight internally from body type/position/template;
    /// the roster file does not contain a direct weight field.
    /// </summary>
    public int? ActualWeight => WeightOffset.HasValue ? WeightOffset.Value + 160 : null;
    public bool IsGenericPlayer => UniqueId?.StartsWith("Generic_") == true;
    public string DisplayName => $"{FirstName} {LastName}";
    public string DisplayHeight => HeightInches.HasValue ? $"{HeightInches.Value / 12}'{HeightInches.Value % 12}\"" : "";
    public override string ToString() => DisplayName;
}

public class EquipmentEntry
{
    public string Slot { get; set; }
    public string Value { get; set; }
    public List<string> AvailableValues { get; set; } = new();
    public override string ToString() => $"{Slot}: {Value}";
}
