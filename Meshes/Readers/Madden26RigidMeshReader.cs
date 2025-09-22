using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Meshes;

namespace Madden26Plugin.Meshes.Readers
{
    internal class Madden26RigidMeshReader
    {
        public int MaxLodCount => 6;

        public void Read(NativeReader nativeReader, IMeshSet meshSet)
        {
            nativeReader.Position = 0;
            meshSet.UnknownBytes.Clear();

            new Madden26MeshHeaderReader().Read(nativeReader, meshSet);
            meshSet.BoundingBox = nativeReader.ReadAxisAlignedBox();
            long[] lodOffsets = new long[MaxLodCount];
            for (int i2 = 0; i2 < MaxLodCount; i2++)
            {
                lodOffsets[i2] = nativeReader.ReadLong();
            }
            meshSet.UnknownPostLODCount = nativeReader.ReadLong();
            long offsetNameLong = nativeReader.ReadLong();
            long offsetNameShort = nativeReader.ReadLong();
            meshSet.nameHash = nativeReader.ReadUInt();
            meshSet.Type = (MeshType)nativeReader.ReadByte();
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(11));
            for (int j = 0; j < MaxLodCount * 2; j++)
            {
                meshSet.LodFade.Add(nativeReader.ReadUInt16LittleEndian());
            }
            meshSet.MeshSetLayoutFlags = (MeshSetLayoutFlags)nativeReader.ReadUInt64();
            meshSet.ShaderDrawOrder = (ShaderDrawOrder)nativeReader.ReadByte();
            meshSet.ShaderDrawOrderUserSlot = (ShaderDrawOrderUserSlot)nativeReader.ReadByte();
            meshSet.ShaderDrawOrderSubOrder = (ShaderDrawOrderSubOrder)nativeReader.ReadUShort();
            var unk1 = nativeReader.ReadUInt16();
            ((MeshSet)meshSet).LodCount = nativeReader.ReadUInt16();
            meshSet.MeshCount = nativeReader.ReadUShort();
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(14));

            var sectionIndex = 0;
            for (int iL = 0; iL < ((MeshSet)meshSet).LodCount; iL++)
            {
                if (lodOffsets[iL] == 0)
                    continue;

                nativeReader.Position = lodOffsets[iL];
                IMeshSetLod lod = new Madden26MeshSetLodReader().Read(nativeReader, meshSet, ref sectionIndex);
                lod.SetParts(meshSet.partTransforms, meshSet.partBoundingBoxes);
                meshSet.Lods.Add(lod);
            }
        }
    }
}
