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
            meshSet.FIFA23_Type2 = (MeshType)nativeReader.ReadByte();
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(10));

            for (int n = 0; n < MaxLodCount * 2; n++)
            {
                meshSet.LodFade.Add(nativeReader.ReadUInt16LittleEndian());
            }
            meshSet.MeshLayout = (EMeshLayout)nativeReader.ReadByte();
            nativeReader.Position -= 1;
            var meshLayoutFlags = (MeshSetLayoutFlags)nativeReader.ReadByte();
            meshSet.unknownUInts.Add(nativeReader.ReadUInt());
            meshSet.unknownUInts.Add(nativeReader.ReadUInt());
            nativeReader.Position -= 1;
            meshSet.ShaderDrawOrder = (ShaderDrawOrder)nativeReader.ReadByte();
            meshSet.ShaderDrawOrderUserSlot = (ShaderDrawOrderUserSlot)nativeReader.ReadByte();
            meshSet.ShaderDrawOrderSubOrder = (ShaderDrawOrderSubOrder)nativeReader.ReadUShort();
            var LodCount = nativeReader.ReadUShort();
            //meshSet.LodCount = nativeReader.ReadUShort();
            meshSet.MeshCount = nativeReader.ReadUShort();

            //for (var iL = 0; iL < LodCount; iL++)
            //{
            //    meshSet.PositionsOfLodMeshSet.Add(nativeReader.ReadUShort());
            //}
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(10));

            var sectionIndex = 0;
            for (int iL = 0; iL < LodCount; iL++)
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
