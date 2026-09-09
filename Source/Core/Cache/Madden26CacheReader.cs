using FMT.FileTools;
using FMT.Hash;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AssetEntryServicing;
using FMT.ServicesManagers.Interfaces;
using System.Text;

namespace Madden26Plugin.Cache
{
    public class Madden26CacheReader : ICacheReader
    {
        protected ILogger Logger { get; set; }
        private EbxAssetEntryService ebxAssetEntryService { get; } = new EbxAssetEntryService();
        private ResourceAssetEntryService resourceAssetEntryService { get; } = new ResourceAssetEntryService();
        private ChunkAssetEntryService chunkAssetEntryService { get; } = new ChunkAssetEntryService();

        // NOT NEEDED
        public ulong EbxDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong ResDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong ChunkDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong NameToPositionOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// Reads and processes data from the Madden 26 cache file, populating asset management services with the
        /// extracted data.
        /// </summary>
        /// <remarks>This method reads various asset entries, including bundles, EBX assets, resources,
        /// and chunks, from the cache file. The extracted data is added to the asset management service if available.
        /// Progress updates are logged periodically.</remarks>
        /// <param name="logger">The logger used to record progress and status messages during the read operation.</param>
        /// <returns><see langword="true"/> if the cache file matches the expected system iteration and no patching is required;
        /// otherwise, <see langword="false"/>.</returns>
        public bool Read(ILogger logger)
        {
            Logger = logger;
            var fss = SingletonService.GetInstance<IFileSystemService>();
            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();
            var cacheHelpers = new Madden26CacheHelpers();

            // If no cache, nothing to read
            if (!File.Exists(cacheHelpers.GetCachePath()))
                return false;

            if (!File.Exists(cacheHelpers.GetCacheBodyPath()))
                return false;

            using (NativeReader nativeReader = new NativeReader(new FileStream(cacheHelpers.GetCachePath(), FileMode.Open, FileAccess.Read)))
            {
                if (nativeReader.ReadUInt16() != cacheHelpers.Version)
                    return false;

                if (nativeReader.ReadLengthPrefixedString() != ProfileManager.Instance.Name)
                    return false;

                var cacheHead = nativeReader.ReadULong();
                if (cacheHead != cacheHelpers.GetSystemIteration())
                    return false;

                var exeTime = cacheHelpers.GetExeWriteTime();
                var cacheTime = nativeReader.ReadLong();
                if (exeTime != cacheTime)
                    return false; // Patching required, so ignore cache
            }

            using var fs = new FileStream(cacheHelpers.GetCacheBodyPath(), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var msBody = new MemoryStream();
            try
            {
                using var gZipStream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                gZipStream.CopyTo(msBody);
            }
            catch (System.IO.InvalidDataException ex)
            {
                Logger?.Log($"Cache body decompression failed: {ex.Message}");
                return false;
            }
            catch (System.IO.EndOfStreamException ex)
            {
                Logger?.Log($"Cache body truncated: {ex.Message}");
                return false;
            }

            msBody.Position = 0;
            using (NativeReader nativeReader = new NativeReader(msBody))
            {

                logger.Log("Cache: Reading bundles");

                var readBundles = new Dictionary<ulong, (string name, int sbId)>();

                int count = 0;
                // bundle count
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading bundles [{pct}%]");
                    }

                    BundleEntry bE = new();
                    var nameLength = nativeReader.ReadUInt16();
                    bE.Name = Encoding.UTF8.GetString(nativeReader.ReadBytes(nameLength));
                    bE.SuperBundleId = nativeReader.ReadInt();

                    var hash = Fnv64.FNV64_String8_Lower(bE.Name);
                    if (!readBundles.ContainsKey(hash))
                    {
                        readBundles.Add(hash, (bE.Name, bE.SuperBundleId));
                    }
                }

                foreach (var kvp in readBundles)
                {
                    if (assetManagementService != null)
                        assetManagementService.Bundles.Add(new BundleEntry() { Name = kvp.Value.name, SuperBundleId = kvp.Value.sbId });
                }

                logger.Log("Cache: Reading Ebx");
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Ebx [{pct}%]");
                    }

                    var asset = ReadEbxAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddEbx(asset as EbxAssetEntry);

                }

                logger.Log("Cache: Reading Resources");
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Resources [{pct}%]");
                    }
                    var asset = ReadResAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddRes(asset as ResAssetEntry);
                }

                // ------------------------------------------------------------------------
                // Chunks
                logger.Log("Cache: Reading Chunks");
                count = nativeReader.ReadInt();
                for (int chunkIndex = 0; chunkIndex < count; chunkIndex++)
                {
                    if (chunkIndex % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)chunkIndex / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Chunks [{pct}%]");
                    }

                    var asset = ReadChunkAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddChunk(asset as ChunkAssetEntry);
                }
            }
            return true;
        }

        public virtual IEbxAssetEntry ReadEbxAssetEntry(NativeReader nativeReader)
        {
            return ebxAssetEntryService.ReadAssetEntryInfo(nativeReader.ReadLengthPrefixedBytes()) as EbxAssetEntry;
        }

        public virtual IResourceAssetEntry ReadResAssetEntry(NativeReader nativeReader)
        {
            return resourceAssetEntryService.ReadAssetEntryInfo(nativeReader.ReadLengthPrefixedBytes()) as ResAssetEntry;
        }

        public virtual IChunkAssetEntry ReadChunkAssetEntry(NativeReader nativeReader)
        {
            return chunkAssetEntryService.ReadAssetEntryInfo(nativeReader.ReadLengthPrefixedBytes()) as ChunkAssetEntry;
        }

        public bool DoesCacheNeedRebuilding(ILogger logger)
        {
            var cacheHelpers = new Madden26CacheHelpers();

            if (!File.Exists(cacheHelpers.GetCachePath()))
                return true; // file doesn't exist, rebuild required

            using (NativeReader nativeReader = new NativeReader(new FileStream(cacheHelpers.GetCachePath(), FileMode.Open, FileAccess.Read)))
            {
                if (nativeReader.ReadUInt16() != cacheHelpers.Version)
                    return true;

                if (nativeReader.ReadLengthPrefixedString() != ProfileManager.Instance.Name)
                    return true; // rebuild required

                var cacheHead = nativeReader.ReadULong();
                if (cacheHead != cacheHelpers.GetSystemIteration())
                    return true; // rebuild required

                var exeTime = cacheHelpers.GetExeWriteTime();
                var cacheTime = nativeReader.ReadLong();
                if (exeTime != cacheTime)
                    return true;  // rebuild required

                //var installTime = cacheHelpers.GetInstallWriteTime();
                //var cachedInstallTime = nativeReader.ReadLong();
                //if (installTime != cachedInstallTime)
                //    return true; // rebuild required
            }

            return false; // rebuild NOT required
        }

        public bool ReadIntoLists(ILogger logger, out List<IEbxAssetEntry> ebxAssetEntries, out List<IResourceAssetEntry> resourceAssetEntries, out List<IChunkAssetEntry> chunkAssetEntries)
        {
            ebxAssetEntries = new List<IEbxAssetEntry>();
            resourceAssetEntries = new List<IResourceAssetEntry>();
            chunkAssetEntries = new List<IChunkAssetEntry>();

            var fss = SingletonService.GetInstance<IFileSystemService>();

            if (!SingletonService.Instantiated<IAssetManagementService>())
                return false;

            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();
            var cacheHelpers = new Madden26CacheHelpers();

            // If no cache, nothing to read
            if (!File.Exists(cacheHelpers.GetCachePath()))
                return false;

            using (NativeReader nativeReader = new NativeReader(new FileStream(cacheHelpers.GetCachePath(), FileMode.Open, FileAccess.Read)))
            {
                // get past header
                _ = nativeReader.ReadUInt16();

                _ = nativeReader.ReadLengthPrefixedString();

                _ = nativeReader.ReadULong();
                
                _ = nativeReader.ReadLong();
            }


            logger.Log("Madden26 Caching does not support this function. Returning no items.");
            return true;
        }

    }
}
