using FMT.Core.Models.TOC;
using FMT.Db;
using FMT.FileTools;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin
{
    public class CASDataReader
    {
        public Madden26TOCFile AssociatedTOCFile { get; set; }
        public string NativeFileLocation { get; set; }

        public CASDataReader(Madden26TOCFile inTOC)
        {
            AssociatedTOCFile = inTOC;
        }

        public IDboToAssetConversionService DboToAssetConversionService
        {
            get
            {
                if (!SingletonService.Instantiated<IDboToAssetConversionService>())
                    SingletonService.RegisterInstance<IDboToAssetConversionService, DboToAssetConversionService>(new DboToAssetConversionService());

                return SingletonService.GetInstance<IDboToAssetConversionService>();
            }
        }

        /// <summary>
        /// Loads all items of the CasBundle into the DbObject. Will return null if it cannot parse the path!
        /// </summary>
        /// <param name="path"></param>
        /// <param name="casBundles"></param>
        /// <returns></returns>
        public (Dictionary<CASBundle, DbObject> dboToCasBundle, List<IAssetEntry> assetEntries) Read(string path, List<CASBundle> casBundles)
        {
            var fs = SingletonService.GetInstance<IFileSystemService>();
            if (fs == null)
                throw new InvalidOperationException("FileSystemService is not initialized.");

            var assetManager = SingletonService.GetInstance<IAssetManagementService>();

            Dictionary<CASBundle, DbObject> dboToCasBundle = new();
            NativeFileLocation = path;
            path = fs.ResolvePath(NativeFileLocation);
            if (string.IsNullOrEmpty(path))
                return (null, null);

            using (NativeReader nr_cas = new(path))
            {
                nr_cas.Position = 0;
                int index = 0;
                foreach (CASBundle casBundle in casBundles.Where(x => x.TotalSize > 0))
                {
                    dboToCasBundle.Add(casBundle, new DbObject());

                    //if (assetManager != null && AssociatedTOCFile != null && AssociatedTOCFile.DoLogging)
                    //    assetManager.Logger.Log($"{path} [{Math.Round((double)index / casBundles.Count * 100).ToString()}%]");

                    index++;

                    // go back 4 from the magic
                    var actualPos = casBundle.BundleOffset;
                    nr_cas.Position = actualPos;

                    //var binaryReader = new BundleReader();
                    //var assetsByArea = new Dictionary<string, object>();

                    //if (binaryReader.Read(0, ref assetsByArea, nr_cas, false, false) == null)
                    //{
                    //    if (assetManager != null && AssociatedTOCFile != null && AssociatedTOCFile.DoLogging)
                    //        assetManager.Logger.LogError("Unable to find data in " + casBundle.ToString());

                    //    continue;
                    //}

                    //if (assetManager == null || AssociatedTOCFile == null)
                    //    continue;

                    //var EbxObjectList = assetsByArea["ebx"] as DbObject;
                    //var ResObjectList = assetsByArea["res"] as DbObject;
                    //var ChunkObjectList = assetsByArea["chunks"] as DbObject;
                    //var ChunkMetaList = assetsByArea.ContainsKey("chunkMeta") ? assetsByArea["chunkMeta"] as DbObject : null;

                    //var ebxCount = EbxObjectList.Count;
                    //var resCount = ResObjectList.Count;
                    //var chunkCount = ChunkObjectList.Count;
                    //var totalCount = ebxCount + resCount + chunkCount;

                    //var allObjectList = EbxObjectList.List.Union(ResObjectList.List).Union(ChunkObjectList.List).ToArray();
                    //var indexInList = 0;
                    //foreach (DbObject dbo in allObjectList)
                    //{
                    //    dbo.SetValue("offset", casBundle.Offsets[indexInList]);
                    //    dbo.SetValue("size", casBundle.Sizes[indexInList]);

                    //    dbo.SetValue("TOCOffsetPosition", casBundle.TOCOffsets[indexInList]);
                    //    dbo.SetValue("TOCSizePosition", casBundle.TOCSizes[indexInList]);

                    //    dbo.SetValue("CASFileLocation", NativeFileLocation);

                    //    dbo.SetValue("TOCFileLocation", AssociatedTOCFile.NativeFileLocation);
                    //    dbo.SetValue("SB_CAS_Offset_Position", casBundle.TOCOffsets[indexInList]);
                    //    dbo.SetValue("SB_CAS_Size_Position", casBundle.TOCSizes[indexInList]);
                    //    dbo.SetValue("ParentCASBundleLocation", NativeFileLocation);

                    //    dbo.SetValue("cas", casBundle.TOCCas[indexInList]);
                    //    dbo.SetValue("catalog", casBundle.TOCCatalog[indexInList]);
                    //    dbo.SetValue("patch", casBundle.TOCPatch[indexInList]);
                    //    //dbo.SetValue("BundleIndex", BaseBundleInfo.BundleItemIndex);
                    //    //dbo.SetValue("Bundle", casBundle.BaseEntry.Name);
                    //    dbo.SetValue("EntryIndex", indexInList + 1);
                    //    dbo.SetValue("BundleHash", casBundle.BaseEntry.NameHash);
                    //    dbo.SetValue("BundleReference", casBundle.BaseEntry.BundleReference);

                    //    indexInList++;
                    //}

                    //indexInList = 0;

                    //dboToCasBundle[casBundle].AddValue("ebx", assetsByArea["ebx"] as DbObject);
                    //dboToCasBundle[casBundle].AddValue("res", assetsByArea["res"] as DbObject);
                    //dboToCasBundle[casBundle].AddValue("chunks", assetsByArea["chunks"] as DbObject);
                    //if (assetsByArea.ContainsKey("chunkMeta"))
                    //    dboToCasBundle[casBundle].AddValue("chunkMeta", assetsByArea["chunkMeta"] as DbObject);

                    //if (assetsByArea.ContainsKey("AssetEbx"))
                    //{
                    //    var ebxObjectAssets = assetsByArea["AssetEbx"] as IAssetEntry[];
                    //    var resObjectAssets = assetsByArea["AssetRes"] as IAssetEntry[];
                    //    var chunkObjectAssets = assetsByArea["AssetChunk"] as IAssetEntry[];

                    //    indexInList = 0;
                    //    var allObjectAssets = ebxObjectAssets.Union(resObjectAssets).Union(chunkObjectAssets);
                    //    foreach (var obj in allObjectAssets)
                    //    {
                    //        var asset = ((AssetEntry)obj);
                    //        asset.ExtraData = new AssetExtraData()
                    //        {
                    //            Cas = (ushort)casBundle.TOCCas[indexInList],
                    //            Catalog = (ushort)casBundle.TOCCatalog[indexInList],
                    //            IsPatch = (bool)casBundle.TOCPatch[indexInList],
                    //            DataOffset = casBundle.Offsets[indexInList]
                    //        };
                    //        asset.Size = casBundle.Sizes[indexInList];

                    //        indexInList++;
                    //    }
                    //}


                    //if (AssociatedTOCFile.ProcessData)
                    //{
                    //    var allData = EbxObjectList.List
                    //        .Union(ResObjectList.List)
                    //        .Union(ChunkObjectList.List).ToArray();

                    //    foreach (DbObject item in allData)
                    //    {
                    //        IAssetEntry asset = null;
                    //        if (item.HasValue("ebx"))
                    //            asset = new EbxAssetEntry();
                    //        else if (item.HasValue("res"))
                    //            asset = new ResAssetEntry();
                    //        else if (item.HasValue("chunk"))
                    //            asset = new ChunkAssetEntry();

                    //        asset = DboToAssetConversionService.ConvertDbObjectToAssetEntry(item, asset);
                    //        asset.TOCFileLocation = AssociatedTOCFile.NativeFileLocation;
                    //        if (AssociatedTOCFile.ProcessData)
                    //        {
                    //            if (asset is EbxAssetEntry ebxAssetEntry)
                    //                assetManager.AddEbx(ebxAssetEntry);
                    //            else if (asset is ResAssetEntry resAssetEntry)
                    //                assetManager.AddRes(resAssetEntry);
                    //            else if (asset is ChunkAssetEntry chunkAssetEntry)
                    //                assetManager.AddChunk(chunkAssetEntry);
                    //        }
                    //    }
                    //}


                    //BaseBundleInfo.BundleItemIndex++;

                }
            }

            List<IAssetEntry> allAssets = new();
            return (dboToCasBundle, allAssets);
        }




    }


}
