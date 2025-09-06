using FMT.FileTools;
using FMT.PluginInterfaces;

namespace Madden26Plugin.Meshes.Readers
{
    internal class Madden26MeshSetReader : IMeshSetReader
    {
        private Madden26SkinnedMeshReader SkinnedMeshReader { get; } = new Madden26SkinnedMeshReader();
        //private Madden26CompositeMeshReader CompositeMeshReader { get; } = new Madden26CompositeMeshReader();
        private Madden26RigidMeshReader RigidMeshReader { get; } = new Madden26RigidMeshReader();

        public int MaxLodCount => 7;

        public void Read(NativeReader nativeReader, IMeshSet meshSet)
        {
            nativeReader.Position = 0;
            var unk1 = nativeReader.ReadInt32();
            var unk2 = nativeReader.ReadInt32();
            var unk3 = nativeReader.ReadInt32();
            var unk4 = nativeReader.ReadInt32();

            meshSet.BoundingBox = nativeReader.ReadAxisAlignedBox();
            meshSet.LodOffsets.Clear();
            for (int i2 = 0; i2 < MaxLodCount; i2++)
            {
                meshSet.LodOffsets.Add(nativeReader.ReadLong());
            }
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(16)); // This used to be PostLodUnknown thingy
            meshSet.nameHash = nativeReader.ReadUInt();
            var meshTypeByte = nativeReader.ReadByte();
            meshSet.Type = (MeshType)meshTypeByte;

            switch (meshSet.Type)
            {
                case MeshType.MeshType_Rigid:
                    RigidMeshReader.Read(nativeReader, meshSet);
                    break;
                case MeshType.MeshType_Skinned:
                    SkinnedMeshReader.Read(nativeReader, meshSet);
                    break;
                    //case MeshType.MeshType_Composite:
                    //    CompositeMeshReader.Read(nativeReader, meshSet);
                    //    break;
            }
        }

    }
}
