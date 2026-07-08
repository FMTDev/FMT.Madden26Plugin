using System.Text.Json;

namespace Madden26Plugin.Roster;

public class HairColorMapper
{
    public record HairRecipe(string Name, string Description);

    public record EyeRecipe(string Name, string Description);

    public static readonly List<HairRecipe> KnownHairRecipes = new()
    {
        new("hs_hair_color_black", "Black"),
        new("hs_hair_color_blonde_ash", "Blonde (Ash)"),
        new("hs_hair_color_blonde_dirty", "Blonde (Dirty)"),
        new("hs_hair_color_blonde_light", "Blonde (Light)"),
        new("hs_hair_color_blonde_st1_to_st4", "Blonde (ST1-ST4)"),
        new("hs_hair_color_blonde_st5_to_st8", "Blonde (ST5-ST8)"),
        new("hs_hair_color_brown_dark", "Brown (Dark)"),
        new("hs_hair_color_brown", "Brown"),
        new("hs_hair_color_brown_light", "Brown (Light)"),
        new("hs_hair_color_gray_dark", "Gray (Dark)"),
        new("hs_hair_color_gray", "Gray"),
        new("hs_hair_color_gray_light", "Gray (Light)"),
        new("hs_hair_color_gray_white", "Gray-White"),
        new("hs_hair_color_red_dark", "Red (Dark)"),
        new("hs_hair_color_red", "Red"),
        new("hs_hair_color_red_light", "Red (Light)"),
        new("hs_hair_color_white", "White"),
    };

    public static readonly List<EyeRecipe> KnownEyeRecipes = new()
    {
        new("freemanmarcus_eye_color_recipe", "Brown (Freeman)"),
        new("frostscott_eye_color_recipe", "Blue (Scott)"),
        new("blue_dark_eye_color_recipe", "Blue (Dark)"),
        new("blue_eye_color_recipe", "Blue"),
        new("blue_light_eye_color_recipe", "Blue (Light)"),
        new("brown_dark_eye_color_recipe", "Brown (Dark)"),
        new("brown_eye_color_recipe", "Brown"),
        new("brown_light_eye_color_recipe", "Brown (Light)"),
        new("green_dark_eye_color_recipe", "Green (Dark)"),
        new("green_eye_color_recipe", "Green"),
        new("green_light_eye_color_recipe", "Green (Light)"),
        new("grey_dark_eye_color_recipe", "Grey (Dark)"),
        new("grey_eye_color_recipe", "Grey"),
        new("grey_light_eye_color_recipe", "Grey (Light)"),
        new("hazel_dark_eye_color_recipe", "Hazel (Dark)"),
        new("hazel_eye_color_recipe", "Hazel"),
        new("hazel_light_eye_color_recipe", "Hazel (Light)"),
        new("mannequinn_eye_color_recipe", "Mannequin"),
    };

    private const string EyeRecipePathPrefix = "ContentShared/content/characters/HS/HS_common/HS_eye_color/";

    public static string GetEyeRecipePath(string recipeName) => $"{EyeRecipePathPrefix}{recipeName}";

    public static string GetEyeColorDescription(string recipeName)
    {
        var recipe = KnownEyeRecipes.FirstOrDefault(r =>
            string.Equals(r.Name, recipeName, StringComparison.OrdinalIgnoreCase));
        return recipe?.Description ?? recipeName;
    }

    // Analysis of 3,143 Generic_ IDs shows hair color indices 1-4 only
    // Exact index→recipe mapping requires runtime EBX analysis.
    // These defaults are reasonable guesses based on frequency.
    public static Dictionary<int, HairRecipe> HairColorByIndex { get; } = new()
    {
        [1] = KnownHairRecipes[0],  // Black
        [2] = KnownHairRecipes[7],  // Brown
        [3] = KnownHairRecipes[8],  // Brown (Light)
        [4] = KnownHairRecipes[3],  // Blonde (Light)
    };

    public static string GetHairColorDescription(int index)
    {
        return HairColorByIndex.TryGetValue(index, out var recipe) ? recipe.Description : $"Index {index}";
    }

    public static string GetHairColorRecipeName(int index)
    {
        return HairColorByIndex.TryGetValue(index, out var recipe) ? recipe.Name : "hs_hair_color_black";
    }

    public static int ExtractHairColorIndex(string genericId)
    {
        if (string.IsNullOrEmpty(genericId)) return 0;
        var parts = genericId.Split('_');
        if (parts.Length >= 8 && int.TryParse(parts[^1], out var hc))
            return hc;
        return 0;
    }

    public static int ExtractSkinToneDigit(string genericId)
    {
        if (string.IsNullOrEmpty(genericId)) return 0;
        var parts = genericId.Split('_');
        if (parts.Length >= 8 && int.TryParse(parts[^2], out var st))
            return st;
        return 0;
    }

    public static List<string> AllHairColorDescriptions
        => KnownHairRecipes.Select(r => r.Description).ToList();

    public static List<string> AllEyeColorDescriptions
        => KnownEyeRecipes.Select(r => r.Description).ToList();

    public static string GetHairRecipeNameByDescription(string description)
    {
        var recipe = KnownHairRecipes.FirstOrDefault(r => r.Description == description);
        return recipe?.Name ?? "hs_hair_color_black";
    }

    public static string GetEyeRecipeNameByDescription(string description)
    {
        var recipe = KnownEyeRecipes.FirstOrDefault(r => r.Description == description);
        return recipe?.Name ?? "freemanmarcus_eye_color_recipe";
    }

    public static string SerializeMapping()
    {
        var dict = HairColorByIndex.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void DeserializeMapping(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
        if (dict == null) return;
        HairColorByIndex.Clear();
        foreach (var kv in dict)
        {
            var recipe = KnownHairRecipes.FirstOrDefault(r =>
                string.Equals(r.Name, kv.Value, StringComparison.OrdinalIgnoreCase));
            if (recipe != null)
                HairColorByIndex[kv.Key] = recipe;
        }
    }
}
