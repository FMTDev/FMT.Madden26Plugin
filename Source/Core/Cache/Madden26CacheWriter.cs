using FMT.FileTools;
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
    public class Madden26CacheWriter : ICacheWriter
    {
        public ILogger Logger { get; private set; }

        private EbxAssetEntryService ebxAssetEntryService { get; } = new EbxAssetEntryService();
        private ResourceAssetEntryService resourceAssetEntryService { get; } = new ResourceAssetEntryService();
        private ChunkAssetEntryService chunkAssetEntryService { get; } = new ChunkAssetEntryService();

        public void Write(ILogger logger)
        {
            Logger = logger;
            Madden26CacheHelpers cacheHelpers = new();
            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();

            if (File.Exists(cacheHelpers.GetCachePath()))
                File.Delete(cacheHelpers.GetCachePath());

            using MemoryStream msCacheHeader = new();

            using (NativeWriter nwHeader = new(msCacheHeader, leaveOpen: true))
            {
                nwHeader.Write(cacheHelpers.Version);

                nwHeader.WriteLengthPrefixedString(ProfileManager.Instance.Name);

                nwHeader.Write(cacheHelpers.GetSystemIteration());

                nwHeader.Write(cacheHelpers.GetExeWriteTime());
            }

            using MemoryStream msCacheBody = new();

            using (NativeWriter nwBody = new(msCacheBody, leaveOpen: true))
            { 

                var distinctBundles = assetManagementService.Bundles.DistinctBy(x => SingletonService.GetInstance<IBundleEntryService>().GetNameHashUIntForBundleEntry(x)).ToArray();
                nwBody.Write(distinctBundles.Length);
                foreach (BundleEntry bundle in distinctBundles)
                {
                    nwBody.WriteUInt16((ushort)bundle.Name.Length, Endian.Little);
                    nwBody.WriteBytes(Encoding.UTF8.GetBytes(bundle.Name));
                    nwBody.Write(bundle.SuperBundleId);
                }

                var ebx = assetManagementService.EnumerateEbx().ToList();
                //var paths = assetManagementService.EnumerateEbx().ToList().Select(x => x.FullPath.Contains('/') ? x.FullPath.Substring(0, x.FullPath.LastIndexOf('/')) : x.FullPath).Distinct().ToList();

                nwBody.Write(ebx.Count());
                foreach (EbxAssetEntry ebxEntry in ebx)
                {
                    WriteEbxEntry(nwBody, ebxEntry);
                }

                var resources = assetManagementService.EnumerateRes().ToList();
                nwBody.Write(resources.Count);
                foreach (ResAssetEntry resEntry in resources)
                {
                    WriteResEntry(nwBody, resEntry);
                }

                var chunks = assetManagementService.EnumerateChunks().ToList();
                nwBody.Write(chunks.Count);
                foreach (ChunkAssetEntry chunkEntry in chunks)
                {
                    WriteChunkEntry(nwBody, chunkEntry);
                }
            }


            msCacheHeader.Position = 0;
            msCacheBody.Position = 0;

            if (File.Exists(cacheHelpers.GetCachePath()))
                File.Delete(cacheHelpers.GetCachePath());

            using (FileStream fs = new(cacheHelpers.GetCachePath(), FileMode.CreateNew, FileAccess.Write))
            {
                msCacheHeader.CopyTo(fs);
            }

            if (File.Exists(cacheHelpers.GetCacheBodyPath()))
                File.Delete(cacheHelpers.GetCacheBodyPath());

            using (FileStream fs = new(cacheHelpers.GetCacheBodyPath(), FileMode.CreateNew, FileAccess.Write))
            {
                // Ensure the GZipStream is disposed so the gzip footer is written and the stream is not truncated.
                using (var gZipStream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Compress))
                {
                    msCacheBody.CopyTo(gZipStream);
                }
            }
            Logger.Log($"Wrote {ProfileManager.Instance.Name} cache");

        }

        public virtual void WriteEbxEntry(NativeWriter nativeWriter, IEbxAssetEntry ebxEntry)
        {
           nativeWriter.WriteLengthPrefixedBytes(ebxAssetEntryService.WriteAssetEntryInfo(ebxEntry));
        }

        public virtual void WriteResEntry(NativeWriter nativeWriter, IResourceAssetEntry resEntry)
        {
            nativeWriter.WriteLengthPrefixedBytes(resourceAssetEntryService.WriteAssetEntryInfo(resEntry));
        }

        public void WriteChunkEntry(NativeWriter nativeWriter, IChunkAssetEntry chunkEntry)
        {
            nativeWriter.WriteLengthPrefixedBytes(chunkAssetEntryService.WriteAssetEntryInfo(chunkEntry));
        }
    }
}
