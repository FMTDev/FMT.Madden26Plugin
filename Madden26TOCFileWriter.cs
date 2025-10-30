using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.Core.Writers;
using FMT.FileTools;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin
{
    public sealed class Madden26TOCFileWriter : TOCFileWriter
    {
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        public override byte CasIdentifier => 128;
        public override void WriteCasBundle(TOCFile tocFile, NativeWriter writer, CASBundle casBundle)
        {
            long casBundleOffsetPosition = writer.Position;
            long baseBundleOffset = casBundleOffsetPosition - 556;

            writer.Write(casBundle.unk1, Endian.Big);
            writer.Write(casBundle.unk2, Endian.Big);
            long FlagsOffsetLocation = writer.Position;
            writer.Write(casBundle.FlagsOffset, Endian.Big);
            writer.Write(casBundle.EntriesCount, Endian.Big);
            long EntriesOffsetLocation = writer.Position;
            writer.Write(casBundle.EntriesOffset, Endian.Big);
            writer.Write(casBundle.HeaderSize, Endian.Big);
            writer.Write(casBundle.unk4, Endian.Big);
            writer.Write(casBundle.unk5, Endian.Big);
            writer.Write(casBundle.unk6, Endian.Big);

            var currentCas = -1;
            var currentCatalog = -1;
            bool? currentPatch = null;
            var newFlags = new List<byte>();
            for (int entryIndex = 0; entryIndex < casBundle.EntriesCount; entryIndex++)
            {
                var entry = casBundle.Entries[entryIndex];

                bool hasCasIdentifier =
                    entryIndex == 0
                    || currentCas != entry.cas
                    || currentCatalog != entry.catalog
                    || !currentPatch.HasValue
                    || currentPatch.Value != entry.isInPatch;
                if (hasCasIdentifier)
                {
                    WriteCasIdentification(tocFile, writer, entry);
                }

                newFlags.Add(Convert.ToByte(hasCasIdentifier ? CasIdentifier : 0x0));
                writer.Write(entry.bundleOffsetInCas, Endian.Big);
                writer.Write(entry.bundleSizeInCas, Endian.Big);

                currentCas = entry.cas;
                currentCatalog = entry.catalog;
                currentPatch = entry.isInPatch;
            }
            casBundle.Flags = newFlags.ToArray();
            casBundle.FlagsOffset = (int)writer.Position - (int)casBundleOffsetPosition;
            writer.WriteBytes(casBundle.Flags);
            long endOfCasBundleOffsetPosition = writer.Position;
            writer.Position = FlagsOffsetLocation;
            writer.Write(casBundle.FlagsOffset, Endian.Big);
            writer.Position = endOfCasBundleOffsetPosition;

            casBundle.BaseBundle.ModifiedBundleInfo = new BundleReferenceTableItem.ModifiedBundleReferenceTableItem(baseBundleOffset);// BaseBundleInfo.ModifiedBundleInfoStruct(baseBundleOffset);
        }

        public override void WriteCasIdentification(TOCFile tocFile, NativeWriter writer, CASBundleEntry entry)
        {
            writer.Write((byte)0);
            writer.Write(entry.isInPatch);
            var catalogObjs = fss.CatalogObjects;
            var catalogObj = catalogObjs.ToArray()[entry.catalog];
            writer.Write((int)catalogObj.PersistentIndex, Endian.Big);
            writer.Write((short)entry.cas, Endian.Big);
        }
    }
}
