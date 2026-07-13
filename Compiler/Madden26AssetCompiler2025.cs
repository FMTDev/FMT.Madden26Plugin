using FMT.Compilers;
using FMT.Core;
using FMT.Core.Models.Modding;
using FMT.Core.Models.TOC;
using FMT.Core.Resources;
using FMT.Core.Writers;
using FMT.Db;
using FMT.Ebx;
using FMT.FileTools;
using FMT.Hash;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AppInsights;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.ThirdParty;
using Madden26Plugin.TOC;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Madden26Plugin.Compiler
{
    public class Madden26AssetCompiler2025 : Frostbite2025AssetCompiler, IAssetCompiler
    {
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();

        public override bool RequiresCacheToCompile => false;

        /// <summary>
        /// Ensures that the mod data directory exists, creating it if necessary.
        /// </summary>
        /// <remarks>This method verifies the existence of the mod data directory and creates it if it
        /// does not exist.  The directory path is determined based on the file system service's base path and the mod
        /// directory name.</remarks>
        /// <param name="logger">The logger used to record messages during the operation.</param>
        /// <param name="modExecutor">The mod executor providing context for the operation, including logging capabilities.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the
        /// operation completes successfully.</returns>
        public override async Task<bool> PreCompile(ILogger logger, IModExecutor modExecutor)
        {
            new Madden26ModsCleanupFunctions().CleanUp();
            return true;
        }

        public override bool Compile(ILogger logger, IModExecutor modExecutor)
        {
            this.ModExecutor = modExecutor;
            logger.Log($"{nameof(Madden26AssetCompiler2025)} started");

            // Change TOC File Type to Madden26TOCFile
            fss.TOCFileType = typeof(Madden26TOCFile);
            // Initialize SDK
            TypeLibrary.Initialize();
            FixModifiedAssetsWithoutBundles(logger, modExecutor);

            var fs = SingletonService.GetInstance<IFileSystemService>();

            // Make the game vanilla before compiling
            new FileSystemGameUpdateManager(logger).MakeGameVanilla();
            var result = base.Compile(logger, modExecutor);

            SingletonService.GetInstance<IAppInsightsService>()?.TrackEvent($"{nameof(Madden26AssetCompiler2025)}.{nameof(Compile)} Completed");

            return result;
        }

        /// <summary>
        /// This is used to resolve issues from other mod tools where the mods come without bundle references.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="modExecutor"></param>
        private void FixModifiedAssetsWithoutBundles(ILogger logger, IModExecutor modExecutor)
        {
            var modsWithNoBundles = modExecutor
                                        .ModifiedAssets
                                        .Where(x => x.Bundles.Count == 0)
                                        .ToArray();


            var folder = "native_data/";
            var superBundles = fss.SuperBundles;
            // ------------------------------
            // For some reason, MMC mods do not ship with modified bundles indexes, we are going to have to fix that here
            // This takes a lot of resources and time.
            // @todo: find a way to fix this
            if (!modsWithNoBundles.Any())
                return;


            logger.Log($"Detected {modsWithNoBundles.Count()} modified files without Bundle Hash");

            foreach (var sbName in superBundles)
            {
                var tocFileRAW = $"{folder}{sbName}.toc";
                string tocFileLocation = fss.ResolvePath(tocFileRAW);
                if (string.IsNullOrEmpty(tocFileLocation) || !File.Exists(tocFileLocation))
                    continue;

                {
                    logger.Log($"Checking {tocFileRAW} for Bundles");

                    // Load the Toc file in its entirity to check for names or sha1s
                    Madden26TOCFile tocFile = new(tocFileRAW, false, false, false, -1, true);
                    _ = tocFile;

                    List<int> bundles = new List<int>();

                    foreach (var asset in modsWithNoBundles)
                    {
                        if (Guid.TryParse(asset.Name, out _))
                            continue;

                        var bundleToSearchFor = asset.Name.Substring(0, asset.Name.LastIndexOf('/'));
                        var tocBundleNames = tocFile.BundleEntries.Where(x => x.Value.Name.Count(x => x == '/') >= 2).Select(x => x.Value.Name.Substring(6, x.Value.Name.LastIndexOf('/') - 6)).ToArray();
                        var findIndexOfEbx = Array.IndexOf(tocBundleNames, bundleToSearchFor);
                        if (findIndexOfEbx != -1)
                        {
                            if (!modExecutor.ModifiedBundles.ContainsKey(tocFile.Bundles[findIndexOfEbx].GetNameHash()))
                                modExecutor.ModifiedBundles.Add(tocFile.Bundles[findIndexOfEbx].GetNameHash(), new ModBundleInfo() { Name = tocFile.Bundles[findIndexOfEbx].GetNameHash() });

                            var bundleId = tocFile.Bundles[findIndexOfEbx].GetNameHash();
                            bundles.Add(bundleId);
                            asset.Bundles.Add(bundleId);

                            logger.Log($"Found bundles in {tocFileRAW}");
                        }
                    }

                    // --- Assign all bundles to chunks. We have no other way? 
                    foreach (var asset in modsWithNoBundles)
                    {
                        if (Guid.TryParse(asset.Name, out _))
                        {
                            foreach (var bundleId in bundles)
                            {
                                asset.Bundles.Add(bundleId);
                            }
                        }
                    }

                    bundles = bundles.Distinct().ToList();

                    // likely to be chunks
                    if (bundles.Any())
                    {
                        foreach (var unassigned in modExecutor
                            .ModifiedAssets
                            .Where(x => x.Bundles.Count == 0))
                        {
                            foreach (var bundleId in bundles.Distinct())
                                unassigned.Bundles.Add(bundleId);
                        }
                    }

                    tocFile.Dispose();
                    tocFile = null;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                }
            }

            foreach (var modifiedChunk in modExecutor.ModifiedChunks)
            {
                modifiedChunk.Value.Bundles.Add(Fnv1.HashString("TocChunks"));
            }
        }

        public override bool PostCompile(ILogger logger, IModExecutor modExecutor)
        {
            // --------------------------------------------------------------------------------------------------------
            // Apply Anti-Cheat bypass
            // Deploy CryptBase.dll to the output folder
            DeployEmbeddedResource("CryptBase.dll", Path.Combine(fss.BasePath, "CryptBase.dll"));
            // Deploy dpapi.dll to the output folder
            DeployEmbeddedResource("dpapi.dll", Path.Combine(fss.BasePath, "dpapi.dll"));

            // Backup the original EAAntiCheat.GameServiceLauncher.exe if it exists and a backup does not already exist
            if (File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe")) && !File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup")))
                File.Move(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"), Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup"));

            // Deploy the modified EAAntiCheat.GameServiceLauncher.exe to the output folder
            DeployEmbeddedResource("EAAntiCheat.GameServiceLauncher.exe", Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

            return base.PostCompile(logger, modExecutor);
        }

        private byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            var resourceStream = EmbeddedResourceHelper.GetEmbeddedResourceByName(resourceName);
            if (resourceStream != null)
            {
                using (var ms = new MemoryStream())
                {
                    resourceStream.CopyTo(ms);
                    ms.Position = 0;
                    return ms.ToArray();
                }
            }
            return null;
        }

        private void DeployEmbeddedResource(string resourceName, string outputPath)
        {
            File.WriteAllBytes(outputPath, GetEmbeddedResourceBytes(resourceName));
        }

        protected override bool WriteNewDataChangesToSuperBundles(ref Dictionary<IAssetEntry, bool> listOfModifiedAssets, string directory = "native_patch")
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            if (fss == null)
                throw new NullReferenceException("FileSystemService has not been instantiated");

            if (listOfModifiedAssets == null)
            {
                ModExecutor.Logger.LogError($"Unable to find any entries to process");
                return false;
            }

            if (!listOfModifiedAssets.Any())
                return true;

            var tfs = this.ProcessedTOCFiles.Where(x => x.NativeFileLocation.Contains(directory)).Distinct().ToHashSet();
            if (tfs == null || tfs.Count() == 0)
            {
                var _uneededCasRef = new Dictionary<string, HashSet<ModdedFile>>();
                CompilerHelpers
                    .FindModdedFiles(
                    ref _uneededCasRef
                    , ref tfs
                    , this.ModExecutor.Logger
                    , listOfModifiedAssets.Keys.Select(x => (IAssetEntry)x).ToHashSet()
                    , directory
                    , false);
            }

            var assetBundleToCAS = new Dictionary<string, List<(IAssetEntry, DbObject)>>();

            // ------------------------------------------------------------------------------
            // Step 2. Apply bundle changes to TOC Files
            //
            foreach (var toc in tfs)
            {
                //var tocPath = toc.NativeFileLocation;

                GetModifyableObjectsInSuperBundle(
                    toc
                    , out var dictOfObjects
                    , out var unionObjectsToModify
                    );

                //var resolvedTocPath = fss.ResolvePath(tocPath, ModExecutor.UseModData && UseModDirectory, modDirectory: ModDirectory);

                //NativeWriter nw_toc = new(new FileStream(resolvedTocPath, FileMode.Open));

                var modifiedCasBundles = new HashSet<CASBundle>();
                var modifiedBundles = new HashSet<int>();
                foreach (var @object in unionObjectsToModify)
                {
                    IAssetEntry entry = null;

                    // Detect Resource first (has same name as Ebx)
                    if (@object.Key.StartsWith("RES") && ModExecutor.ModifiedRes.TryGetValue(@object.Value.ToString(), out IResourceAssetEntry resEntry))
                    {
                        entry = resEntry;
                    }
                    // Detect Ebx
                    else if (ModExecutor.ModifiedEbx.TryGetValue(@object.Value.ToString(), out IEbxAssetEntry ebxEntry))
                    {
                        entry = ebxEntry;
                    }
                    else if (ModExecutor.ModifiedChunks.TryGetValue(Guid.Parse(@object.Key), out IChunkAssetEntry chunkEntry))
                        entry = chunkEntry;

                    if (@object.Value.Dictionary.ContainsKey("BundleHash") && !modifiedBundles.Contains(@object.Value.GetValue<int>("BundleHash")))
                        modifiedBundles.Add(@object.Value.GetValue<int>("BundleHash"));

                    var casBundle = toc.CasBundles.FirstOrDefault(x => x.BaseEntry.NameHash == @object.Value.GetValue<int>("BundleHash"));
                    if (!modifiedCasBundles.Contains(casBundle))
                        modifiedCasBundles.Add(casBundle);

                    if (entry == null)
                    {
                        FileLogger.WriteLine($"{nameof(WriteNewDataChangesToSuperBundles)}: Unable to find entry {@object.Key}");
                        continue;
                    }

                    if (entry.ExtraData == null)
                    {
                        var entryExtraDataErrorMessage = $"{nameof(WriteNewDataChangesToSuperBundles)}: Unable to find ExtraData for entry {@object.Key}. This means it was not written to a Cas.";
                        Debug.WriteLine(entryExtraDataErrorMessage);
                        FileLogger.WriteLine(entryExtraDataErrorMessage);

                        if (entry is EbxAssetEntry)
                            ErrorCounts[ModType.EBX]++;
                        else if (entry is ResAssetEntry)
                            ErrorCounts[ModType.RES]++;
                        else if (entry is ChunkAssetEntry)
                            ErrorCounts[ModType.CHUNK]++;

                        continue;
                    }

                    var positionOfNewData = entry.ExtraData.DataOffset;
                    @object.Value.SetValue("offset", entry.ExtraData.DataOffset);

                    var sizeOfData = entry.Size;
                    @object.Value.SetValue("size", entry.Size);

                    var casBundleEntry = casBundle.Entries[@object.Value.GetValue<int>("EntryIndex")];
                    casBundleEntry.bundleSizeInCas = (uint)sizeOfData;
                    casBundleEntry.bundleOffsetInCas = positionOfNewData;

                    var casPath = @object.Value.GetValue<string>("ParentCASBundleLocation");
                    @object.Value.SetValue("originalSize", entry.OriginalSize);
                    @object.Value.SetValue("sha1", entry.Sha1.ToByteArray());

                    if (@object.Value.Dictionary.ContainsKey("resMeta") && entry is ResAssetEntry resAssetEntry)
                        @object.Value.SetValue("resMeta", resAssetEntry.ResMeta);

                    if (@object.Value.Dictionary.ContainsKey("logicalOffset") && entry is ChunkAssetEntry chunkAssetEntry)
                    {
                        @object.Value.SetValue("logicalOffset", chunkAssetEntry.LogicalOffset);
                        @object.Value.SetValue("logicalSize", chunkAssetEntry.LogicalSize);
                    }

#if DEBUG
                    @object.Value.SetValue("modifiedByFMT", true);
#endif

                    if (!assetBundleToCAS.TryGetValue(casPath, out var list))
                        assetBundleToCAS[casPath] = list = new List<(IAssetEntry, DbObject)>();

                    list.Add((entry, @object.Value));


                    listOfModifiedAssets[entry] = true;

                }

                foreach (var casBundle in modifiedCasBundles)
                {
                    BundleWriter bundleWriter = new();
                    var bundleObjects = toc.TOCObjectsByCasBundle[casBundle];
                    _ = bundleObjects;

                    var casPathRaw = fss.GetFilePath(casBundle.Catalog, casBundle.Cas, casBundle.Patch);
                    var resolvedPathCas = fss.ResolvePath(casPathRaw, checkModData: ModExecutor.UseModData);

#if DEBUG
                    using (var nrCas = new NativeReader(new FileStream(resolvedPathCas, FileMode.Open, FileAccess.Read)))
                    {
                        var entry = casBundle.Entries[0];
                        nrCas.Position = entry.bundleOffsetInCas;
                        var casBytes = nrCas.ReadBytes((int)entry.bundleSizeInCas);
                        DebugBytesToFileLogger.Instance.WriteAllBytes($"Bundle_{casBundle.BaseBundle.GetNameHash()}_Decompressed.bin", casBytes, "Bundles/Read", false);
                    }
#endif


                    using (var nwCas = new NativeWriter(new FileStream(resolvedPathCas, FileMode.Open, FileAccess.Write, FileShare.Write)))
                    {
                        var msNewBundle = new MemoryStream();
                        bundleWriter.Write(msNewBundle, bundleObjects);
                        _ = msNewBundle;
                        nwCas.Position = nwCas.Length;
                        var entry = casBundle.Entries[0];
#if DEBUG
                        DebugBytesToFileLogger.Instance.WriteAllBytes($"Bundle_{casBundle.BaseBundle.GetNameHash()}_Decompressed.bin", msNewBundle.ToArray(), "Bundles/Write", false);
#endif
                        //entry.bundleOffsetInCas = (uint)nwCas.Position;
                        //nwCas.Write(msNewBundle.ToArray());
                        //entry.bundleSizeInCas = (uint)msNewBundle.Length;
                    }
                }

                Madden26TOCFileWriter writer = new();
                writer.Write(toc, ModExecutor.UseModData);

#if DEBUG
                Madden26TOCFile tocFileDebug = new Madden26TOCFile(toc.FileLocation, false, false, false, -1, false);
#endif


                ModExecutor.Logger.Log($"Processing Complete: {toc.NativeFileLocation}");
                if (dictOfObjects != null)
                {
                    dictOfObjects = null;
                }

            }
            //
            // ------------------------------------------------------------------------------

            return true;
        }

        protected override List<Guid> ModifyTOCChunks(string directory = "native_patch")
        {
            //return new List<Guid>();
            return base.ModifyTOCChunks(directory);
        }

        public override Task<bool> OnProcessEnded(ILogger logger, IModExecutor modExecutor)
        {
            new Madden26ModsCleanupFunctions().CleanUp();

            return Task.FromResult(true);
        }

    }
}
