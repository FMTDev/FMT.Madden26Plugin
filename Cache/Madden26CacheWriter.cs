using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System.Text;

namespace Madden26Plugin.Cache
{
    internal class Madden26CacheWriter : ICacheWriter
    {
        public ILogger Logger { get; private set; }

        public void Write(ILogger logger)
        {
            Logger = logger;
            Madden26CacheHelpers cacheHelpers = new();
            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();

            var fs = SingletonService.GetInstance<IFileSystemService>();

            if (File.Exists(cacheHelpers.GetCachePath()))
                File.Delete(cacheHelpers.GetCachePath());

            MemoryStream msCache = new();

            using (NativeWriter nativeWriter = new(msCache, leaveOpen: true))
            {
                nativeWriter.WriteLengthPrefixedString("madden26");

                nativeWriter.Write(cacheHelpers.GetSystemIteration());

                nativeWriter.Write(cacheHelpers.GetExeWriteTime());

                nativeWriter.Write(assetManagementService.Bundles.Count);
                foreach (BundleEntry bundle in assetManagementService.Bundles)
                {
                    nativeWriter.WriteUInt16((ushort)bundle.Name.Length, Endian.Little);
                    nativeWriter.WriteBytes(Encoding.UTF8.GetBytes(bundle.Name));
                    nativeWriter.Write(bundle.SuperBundleId);
                }

                var ebx = assetManagementService.EnumerateEbx().ToList();
                nativeWriter.Write(ebx.Count());
                foreach (EbxAssetEntry ebxEntry in ebx)
                {
                    WriteEbxEntry(nativeWriter, ebxEntry);
                }

                var resources = assetManagementService.EnumerateRes().ToList();
                nativeWriter.Write(resources.Count);
                foreach (ResAssetEntry resEntry in resources)
                {
                    WriteResEntry(nativeWriter, resEntry);
                }

                var chunks = assetManagementService.EnumerateChunks().ToList();
                nativeWriter.Write(chunks.Count);
                foreach (ChunkAssetEntry chunkEntry in chunks)
                {
                    WriteChunkEntry(nativeWriter, chunkEntry);
                }

                nativeWriter.Write(assetManagementService.SuperBundleChunks.Count);
                foreach (ChunkAssetEntry chunkEntry in assetManagementService.SuperBundleChunks.Values)
                {

                    WriteChunkEntry(nativeWriter, chunkEntry);
                }
            }


            msCache.Position = 0;
            File.WriteAllBytes(cacheHelpers.GetCachePath(), msCache.ToArray());
            Logger.Log("Wrote Madden26 cache to " + cacheHelpers.GetCachePath());

        }

        private static bool DoesExtraDataExist(IAssetEntry assetEntry)
        {
            return assetEntry.ExtraData != null
                            && assetEntry.ExtraData.DataOffset > 0
                            && assetEntry.ExtraData.Catalog.HasValue
                            && assetEntry.ExtraData.Cas.HasValue;
        }

        public virtual void WriteEbxEntry(NativeWriter nativeWriter, IEbxAssetEntry ebxEntry)
        {
            nativeWriter.WriteLengthPrefixedString(ebxEntry.Name);
            nativeWriter.Write(ebxEntry.Sha1);
            nativeWriter.Write(ebxEntry.Size);
            nativeWriter.Write(ebxEntry.OriginalSize);
            nativeWriter.Write((byte)ebxEntry.Location);
            nativeWriter.WriteLengthPrefixedString((ebxEntry.Type != null) ? ebxEntry.Type : "");
            nativeWriter.Write(ebxEntry.Id);

            nativeWriter.Write(DoesExtraDataExist(ebxEntry));
            if (DoesExtraDataExist(ebxEntry))
            {
                nativeWriter.Write(ebxEntry.ExtraData.DataOffset);
                nativeWriter.Write(ebxEntry.ExtraData.Catalog.Value);
                nativeWriter.Write(ebxEntry.ExtraData.Cas.Value);
                nativeWriter.Write(ebxEntry.ExtraData.IsPatch);
            }

            nativeWriter.Write(ebxEntry.Bundles.Count);
            foreach (int bundle2 in ebxEntry.Bundles)
            {
                nativeWriter.Write(bundle2);
            }
        }

        public virtual void WriteResEntry(NativeWriter nativeWriter, IResourceAssetEntry resEntry)
        {
            nativeWriter.WriteLengthPrefixedString(resEntry.Name);
            nativeWriter.Write(resEntry.Sha1);
            nativeWriter.Write(resEntry.Size);
            nativeWriter.Write(resEntry.OriginalSize);
            nativeWriter.Write((byte)resEntry.Location);
            nativeWriter.Write(resEntry.IsInline);
            nativeWriter.Write(resEntry.ResRid);
            nativeWriter.Write(resEntry.ResType);
            nativeWriter.Write(resEntry.ResMeta.Length);
            nativeWriter.Write(resEntry.ResMeta);
            bool extraDataExists = DoesExtraDataExist(resEntry);
            nativeWriter.Write(extraDataExists);
            if (extraDataExists)
            {
                nativeWriter.Write(resEntry.ExtraData.DataOffset);
                nativeWriter.Write(resEntry.ExtraData.Catalog.Value);
                nativeWriter.Write(resEntry.ExtraData.Cas.Value);
                nativeWriter.Write(resEntry.ExtraData.IsPatch);
            }

            nativeWriter.Write(resEntry.Bundles.Count);
            foreach (int b in resEntry.Bundles)
            {
                nativeWriter.Write(b);
            }
        }



        public virtual void WriteChunkEntry(NativeWriter nativeWriter, IChunkAssetEntry chunkEntry)
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            nativeWriter.Write(chunkEntry.Id);
            nativeWriter.Write(chunkEntry.Sha1);
            nativeWriter.Write(chunkEntry.Size);
            nativeWriter.Write((byte)chunkEntry.Location);
            nativeWriter.Write(chunkEntry.IsInline);
            nativeWriter.Write(chunkEntry.BundledSize);
            nativeWriter.Write(chunkEntry.RangeStart);
            nativeWriter.Write(chunkEntry.RangeEnd);
            nativeWriter.Write(chunkEntry.LogicalOffset);
            nativeWriter.Write(chunkEntry.LogicalSize);
            nativeWriter.Write(chunkEntry.H32);
            nativeWriter.Write(chunkEntry.FirstMip);
            bool extraDataExists = DoesExtraDataExist(chunkEntry);
            nativeWriter.Write(extraDataExists);
            if (extraDataExists)
            {
                nativeWriter.Write(chunkEntry.ExtraData.DataOffset);
                nativeWriter.Write(chunkEntry.ExtraData.Catalog.Value);
                nativeWriter.Write(chunkEntry.ExtraData.Cas.Value);
                nativeWriter.Write(chunkEntry.ExtraData.IsPatch);
            }

            nativeWriter.Write(chunkEntry.Bundles.Count);
            foreach (int bundleId in chunkEntry.Bundles)
            {
                nativeWriter.Write(bundleId);
            }
        }
    }
}
