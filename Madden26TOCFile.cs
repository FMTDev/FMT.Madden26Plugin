using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.FileTools;
using FMT.Hash;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System.Text;

namespace Madden26Plugin
{
    public class Madden26TOCFile : TOCFile
    {
        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        /// <summary>
        /// Used if you want to Read TOC without the normal way
        /// </summary>
        public Madden26TOCFile(string nativeFilePath) : base(nativeFilePath)
        {

        }


        /// <summary>
        /// Reads the TOC file and process any data within it (Chunks) and its Bundles (In Cas files)
        /// </summary>
        /// <param name="nativeFilePath"></param>
        /// <param name="log"></param>
        /// <param name="process"></param>
        /// <param name="modDataPath"></param>
        /// <param name="sbIndex"></param>
        /// <param name="headerOnly">If true then do not read/process Cas Bundles</param>
        public Madden26TOCFile(string nativeFilePath, bool log = true, bool process = true, bool modDataPath = false, int sbIndex = -1, bool headerOnly = false)
            : base(nativeFilePath, log, process, modDataPath, sbIndex, headerOnly)
        {

        }

        public Madden26TOCFile(Stream tocStream, bool log = true, bool process = true, bool modDataPath = false, int sbIndex = -1, bool headerOnly = false)
            : base(tocStream, log, process, modDataPath, sbIndex, headerOnly)
        {

        }

        protected override (short Catalog, byte Cas, bool IsInPatch) FindCatalogCasPatch(NativeReader nativeReader)
        {
            var isInPatch = Convert.ToBoolean(nativeReader.ReadShort(Endian.Big)); // Patch
            var catalogIndex = nativeReader.ReadInt(Endian.Big); // Catalog PersistedIndex Identifier
            var cas = Convert.ToByte(nativeReader.ReadShort(Endian.Big)); // Cas number

            var catalogIndexInt = fss.CatalogObjects.ToList().FindIndex(x => x.PersistentIndex.HasValue && x.PersistentIndex.Value == catalogIndex);
            if (catalogIndexInt == -1)
                throw new IndexOutOfRangeException();

            var catalog = (short)catalogIndexInt;
            if ((byte)catalog == 255)
                throw new ArithmeticException();

            return (catalog, cas, isInPatch);
        }

        protected override void ReadChunkData(NativeReader nativeReader)
        {
            if (MetaData.ChunkCount == 0)
                return;

            nativeReader.Position = 556 + MetaData.ChunkFlagOffsetPosition;
            for (int chunkIndex = 0; chunkIndex < MetaData.ChunkCount; chunkIndex++)
            {
                ListTocChunkFlags.Add(nativeReader.ReadInt(Endian.Big));
            }
            nativeReader.Position = 556 + MetaData.ChunkGuidOffset;
            TocChunkGuids = new Guid[MetaData.ChunkCount];

            var TOCChunkByOffset = new Dictionary<uint, ChunkAssetEntry>();

            for (int chunkIndex = 0; chunkIndex < MetaData.ChunkCount; chunkIndex++)
            {
                Guid guidReverse = nativeReader.ReadGuidReverse();
                TocChunkGuids[chunkIndex] = guidReverse;

                uint decodeAndOffset = nativeReader.ReadUInt(Endian.Big);
                uint order = decodeAndOffset & 0xFFFFFFu;
                (Guid, uint) superBundleChunk = new(guidReverse, decodeAndOffset);
                while (TocChunks.Count <= order / 3u)
                {
                    TocChunks.Add(null);
                }
                TocChunks[(int)(order / 3u)] = new ChunkAssetEntry
                {
                    Id = guidReverse
                };
                TOCChunkByOffset.Add(order, TocChunks[(int)(order / 3u)]);
            }

            var expectedOffsetAfterGuid = 556 + MetaData.ChunkGuidOffset + (4 * MetaData.ChunkCount) + (16 * MetaData.ChunkCount);
            if (nativeReader.Position != expectedOffsetAfterGuid)
            {
                throw new Exception("We are not where we expected to be!");
            }

            nativeReader.Position = 556 + MetaData.ChunkEntryOffset;
            for (int chunkIndex = 0; chunkIndex < MetaData.ChunkCount; chunkIndex++)
            {
                uint chunkIdentificationOffset = (uint)(nativeReader.Position - 556 - MetaData.DataOffset) / 4u;
                ChunkAssetEntry tocChunk = TOCChunkByOffset[chunkIdentificationOffset];
                tocChunk.IsTocChunk = true;

                uint patchPosition = (uint)nativeReader.Position;
                var catcaspatch = FindCatalogCasPatch(nativeReader);
                TocChunkPatchPositions.Add(tocChunk.Id, patchPosition);

                tocChunk.SB_CAS_Offset_Position = (int)nativeReader.Position;
                var offset = nativeReader.ReadUInt(Endian.Big);
                tocChunk.SB_CAS_Size_Position = (int)nativeReader.Position;
                var size = nativeReader.ReadUInt(Endian.Big);
                tocChunk.Sha1 = Sha1.Create(Encoding.ASCII.GetBytes(tocChunk.Id.ToString()));

                tocChunk.LogicalOffset = offset;
                tocChunk.OriginalSize = (tocChunk.LogicalOffset & 0xFFFF) | size;
                tocChunk.Size = size;
                tocChunk.Location = AssetDataLocation.CasNonIndexed;
                tocChunk.ExtraData = new AssetExtraData();
                tocChunk.ExtraData.Unk = 0;
                tocChunk.ExtraData.Catalog = (ushort)catcaspatch.Catalog;
                tocChunk.ExtraData.Cas = catcaspatch.Cas;
                tocChunk.ExtraData.IsPatch = catcaspatch.IsInPatch;
                tocChunk.ExtraData.DataOffset = offset;
                tocChunk.Bundles.Add(ChunkDataBundleId);
                if (assetManagementService != null && ProcessData)
                    assetManagementService.AddChunk(tocChunk);
            }
        }

        public override void ReadCasBundles(NativeReader nativeReader)
        {

            var remainingByteLength = nativeReader.Length - nativeReader.Position;
            if (remainingByteLength == 0)
                return;

            if (assetManagementService == null)
                return;

            if (assetManagementService != null && DoLogging)
                assetManagementService.Logger.Log("Searching for CAS Data from " + FileLocation);

            for (int i = 0; i < MetaData.BundleCount; i++)
            {
                nativeReader.Position = (Bundles[i].Offset + 556);

                CASBundle bundle = new();
                if (BundleEntries.Count == 0)
                    continue;

                bundle.BaseBundle = Bundles[i];
                bundle.BaseEntry = BundleEntries[i];

                long startPosition = nativeReader.Position;
                bundle.unk1 = nativeReader.ReadInt(Endian.Big); // 0-4
                bundle.unk2 = nativeReader.ReadInt(Endian.Big); // 4-8
                bundle.FlagsOffset = nativeReader.ReadInt(Endian.Big); // 8-12
                bundle.EntriesCount = nativeReader.ReadInt(Endian.Big); // 12-16
                bundle.EntriesOffset = nativeReader.ReadInt(Endian.Big); // 16-20
                bundle.HeaderSize = nativeReader.ReadInt(Endian.Big); // 20-24
                bundle.unk4 = nativeReader.ReadInt(Endian.Big); // 24-28
                bundle.unk5 = nativeReader.ReadInt(Endian.Big); // 28-32
                bundle.unk6 = nativeReader.ReadInt(Endian.Big); // 32-36

                var actualFlagsOffset = startPosition + bundle.FlagsOffset;
                nativeReader.Position = actualFlagsOffset;
                bundle.Flags = nativeReader.ReadBytes(bundle.EntriesCount);

                var actualEntriesOffset = startPosition + bundle.EntriesOffset;
                nativeReader.Position = actualEntriesOffset;

                byte unk = 0;
                bool isInPatch = false;
                byte catalog = 0;
                byte cas = 0;
                int catalogIndex = 0;

                for (int entryIndex = 0; entryIndex < bundle.EntriesCount; entryIndex++)
                {
                    bool hasCasIdentifier = bundle.Flags[entryIndex] == 128;
                    if (hasCasIdentifier)
                    {
                        isInPatch = Convert.ToBoolean(nativeReader.ReadShort(Endian.Big)); // Patch
                        catalogIndex = nativeReader.ReadInt(Endian.Big); // Catalog PersistedIndex Identifier
                        cas = Convert.ToByte(nativeReader.ReadShort(Endian.Big)); // Cas number

                        if (!fss.CatalogsIndexed.ContainsKey(catalogIndex))
                        {
                            continue;
                        }

                        var catalogIndexedKVP = fss.CatalogsIndexed[catalogIndex];
                        var catalogIndexInt = fss.CatalogObjects.ToList().FindIndex(x => x.PersistentIndex.HasValue && x.PersistentIndex.Value == catalogIndex);
                        if (catalogIndexInt == -1)
                        {
                            continue;
                        }
                        catalog = (byte)catalogIndexInt;// (byte)foundCatalog;
                    }

                    long locationOfOffset = nativeReader.Position;
                    uint bundleOffsetInCas = nativeReader.ReadUInt(Endian.Big);
                    long locationOfSize = nativeReader.Position;
                    uint bundleSizeInCas = nativeReader.ReadUInt(Endian.Big);

                    if (entryIndex == 0)
                    {
                        bundle.Unk = unk;
                        bundle.BundleOffset = bundleOffsetInCas;
                        bundle.BundleSize = bundleSizeInCas;
                        bundle.Cas = cas;
                        bundle.Catalog = catalog;
                        bundle.Patch = isInPatch;
                    }
                    else
                    {
                        bundle.TOCOffsets.Add(locationOfOffset);
                        bundle.Offsets.Add(bundleOffsetInCas);

                        bundle.TOCSizes.Add(locationOfSize);
                        bundle.Sizes.Add(bundleSizeInCas);

                        bundle.TOCCas.Add(cas);
                        bundle.TOCCatalog.Add(catalog);
                        bundle.TOCPatch.Add(isInPatch);
                    }

                    bundle.Entries.Add(
                        new CASBundleEntry()
                        {
                            unk = unk,
                            isInPatch = isInPatch,
                            catalog = catalog,
                            cas = cas,
                            bundleSizeInCas = bundleSizeInCas,
                            locationOfSize = locationOfSize,
                            bundleOffsetInCas = bundleOffsetInCas,
                            locationOfOffset = locationOfOffset
                        }
                    );
                }

                CasBundles[i] = bundle;
            }

        }

    }
}
