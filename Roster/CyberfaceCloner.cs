using FMT.Core.Readers.Ebx;
using FMT.Core.Writers.Ebx;
using FMT.Models.Assets;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Roster;

public class CyberfaceCloner
{
    private const string RecipePathPrefix = "content/characters/hs/hs_playerrecipe/";
    private const string HairRecipePrefix = "ContentShared/content/characters/HS/HS_common/HS_hair/HS_hair_color/";
    private const string GenericPrefix = "generic/";
    private const string UniquePrefix = "unique/";
    private const string BrtSuffix = "_playerhead_brt";

    public bool CloneGenericToUnique(string genericName, string newUniqueName,
        string hairRecipeName = null, string eyeRecipeName = null)
    {
        var assetService = SingletonService.GetInstance<IAssetManagementService>();
        if (assetService == null) return false;

        var genericPath = $"{RecipePathPrefix}{GenericPrefix}{genericName}";
        var uniquePath = $"{RecipePathPrefix}{UniquePrefix}{newUniqueName}";

        var sourceEntry = FindEbxEntry(assetService, genericPath);
        if (sourceEntry == null) return false;

        try
        {
            var reader = EbxReader.GetEbxReader(new MemoryStream(sourceEntry.ModifiedEntry.Data));
            reader.InitialRead(reader.BaseStream, false);
            var sourceAsset = reader.ReadAsset();

            var newAsset = new EbxAsset();
            foreach (var obj in sourceAsset.Objects)
                newAsset.AddObject(obj, false);

            newAsset.SetRootObject(sourceAsset.RootObject);
            newAsset.SetFileGuid(Guid.NewGuid());

            ApplyHairColor(assetService, newAsset, hairRecipeName);
            ApplyEyeColor(assetService, newAsset, eyeRecipeName);

            newAsset.Update();

            var newBytes = EbxBaseWriter.GetEbxByteArrayDecompressed(newAsset, uniquePath, out var errors);

            RegisterNewEbxEntry(assetService, uniquePath, newBytes, sourceEntry);
            CloneBrtEntry(assetService, genericName, newUniqueName);

            return !errors.Any(e => e.StartsWith("Error"));
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyHairColor(IAssetManagementService service, EbxAsset asset, string recipeName)
    {
        if (string.IsNullOrEmpty(recipeName)) return;

        var (rootColor, tipColor) = ReadHairRecipeColors(service, recipeName);
        if (rootColor == null) return;

        var rootObj = asset.RootObject;
        var clonedRoot = ComplexionPresetMapper.CloneObject(rootColor);
        var clonedTip = tipColor != null ? ComplexionPresetMapper.CloneObject(tipColor) : null;

        ComplexionPresetMapper.SetFieldValue(rootObj, "HairRootColor", clonedRoot);
        if (clonedTip != null)
            ComplexionPresetMapper.SetFieldValue(rootObj, "HairTipColor", clonedTip);

        ComplexionPresetMapper.SetFieldValue(rootObj, "EyebrowRootColor", clonedRoot);
        if (clonedTip != null)
            ComplexionPresetMapper.SetFieldValue(rootObj, "EyebrowTipColor", clonedTip);

        ComplexionPresetMapper.SetFieldValue(rootObj, "BeardRootColor", clonedRoot);
        if (clonedTip != null)
            ComplexionPresetMapper.SetFieldValue(rootObj, "BeardTipColor", clonedTip);
    }

    private static void ApplyEyeColor(IAssetManagementService service, EbxAsset asset, string recipeName)
    {
        if (string.IsNullOrEmpty(recipeName)) return;

        // Eye recipes store MaterialPreset → ShaderPreset with iris_color_R/G/B VectorParameters.
        // The player recipe's EyeColorRecipe is an External reference (FileGuid + ClassGuid).
        // Changing it requires reading the target eye recipe's EBX, extracting its instance/class
        // GUIDs, and constructing a new External reference object via reflection.
        // Known eye recipes (18 total) at:
        //   ContentShared/content/characters/HS/HS_common/HS_eye_color/{recipe_name}
        // For now, the cloned recipe inherits the source template's eye color.
        // To re-enable: read target recipe EBX, get file/class GUIDs, create ref object,
        // set via ComplexionPresetMapper.SetFieldValue(rootObj, "EyeColorRecipe", newRef).
    }

    private static (object rootColor, object tipColor) ReadHairRecipeColors(IAssetManagementService service, string recipeName)
    {
        var recipePath = $"{HairRecipePrefix}{recipeName}";
        var entry = FindEbxEntry(service, recipePath);
        if (entry == null) return (null, null);

        try
        {
            var reader = EbxReader.GetEbxReader(new MemoryStream(entry.ModifiedEntry.Data));
            reader.InitialRead(reader.BaseStream, false);
            var asset = reader.ReadAsset();

            var rootColor = ComplexionPresetMapper.FindField(asset.RootObject, "RootColor");
            var tipColor = ComplexionPresetMapper.FindField(asset.RootObject, "TipColor");
            return (rootColor, tipColor);
        }
        catch
        {
            return (null, null);
        }
    }

    private static IEbxAssetEntry FindEbxEntry(IAssetManagementService service, string path)
    {
        return service.EnumerateEbx().FirstOrDefault(e =>
            string.Equals(e.Name, path, StringComparison.OrdinalIgnoreCase));
    }

    private static void RegisterNewEbxEntry(IAssetManagementService service, string path, byte[] data, IEbxAssetEntry sourceEntry)
    {
        var newEntry = new EbxAssetEntry
        {
            Name = path,
            Sha1 = sourceEntry.Sha1,
            Size = data.Length,
            OriginalSize = data.Length,
            Location = sourceEntry.Location,
            Type = sourceEntry.Type,
            Id = Guid.NewGuid(),
            Bundles = sourceEntry.Bundles.ToList(),
        };
        service.AddEbx(newEntry);
    }

    private static void CloneBrtEntry(IAssetManagementService service, string sourceName, string targetName)
    {
        var sourceBrtPath = $"{RecipePathPrefix}{GenericPrefix}{sourceName}{BrtSuffix}";
        var targetBrtPath = $"{RecipePathPrefix}{UniquePrefix}{targetName}{BrtSuffix}";

        var sourceBrt = FindEbxEntry(service, sourceBrtPath);
        if (sourceBrt == null) return;

        var reader = EbxReader.GetEbxReader(new MemoryStream(sourceBrt.ModifiedEntry.Data));
        reader.InitialRead(reader.BaseStream, false);
        var brtAsset = reader.ReadAsset();

        var newBrt = new EbxAsset();
        foreach (var obj in brtAsset.Objects)
            newBrt.AddObject(obj, false);
        newBrt.SetRootObject(brtAsset.RootObject);
        newBrt.SetFileGuid(Guid.NewGuid());
        newBrt.Update();

        var newBytes = EbxBaseWriter.GetEbxByteArrayDecompressed(newBrt, targetBrtPath, out _);

        var newEntry = new EbxAssetEntry
        {
            Name = targetBrtPath,
            Sha1 = sourceBrt.Sha1,
            Size = newBytes.Length,
            OriginalSize = newBytes.Length,
            Location = sourceBrt.Location,
            Type = sourceBrt.Type,
            Id = Guid.NewGuid(),
            Bundles = sourceBrt.Bundles.ToList(),
        };
        service.AddEbx(newEntry);
    }
}
