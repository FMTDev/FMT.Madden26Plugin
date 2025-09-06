using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Meshes;

namespace Madden26Plugin.Meshes.Readers
{
    internal class Madden26MeshSetSectionReader : IMeshSetSectionReader
    {

        public void Read(NativeReader nativeReader, MeshSetSection section, int index)
        {
            var startPosition = nativeReader.Position;
            nativeReader.Position = startPosition;

            section.SectionIndex = index;
            section.Offset1 = nativeReader.ReadInt64LittleEndian();
            if (section.Offset1 != 0)
                return;
            section.Name = nativeReader.ReadNullTerminatedString(offset: nativeReader.ReadInt64LittleEndian() + 16);
            long bonePositions = nativeReader.ReadInt64LittleEndian();
            section.BoneCount = nativeReader.ReadUInt16LittleEndian(); //438
            section.BonesPerVertex = (byte)nativeReader.ReadUShort(); // 8
            section.MaterialId = nativeReader.ReadUShort(); // 28
            section.VertexStride = nativeReader.ReadByte(); // 68
            section.PrimitiveType = (PrimitiveType)nativeReader.ReadByte(); // 3
            section.PrimitiveCount = (uint)nativeReader.ReadUInt32LittleEndian();
            section.StartIndex = nativeReader.ReadUInt32LittleEndian(); // 0
            section.VertexOffset = nativeReader.ReadUInt32LittleEndian(); // 0
            section.VertexCount = (uint)nativeReader.ReadUInt32LittleEndian(); // 3157
            section.UnknownBytes.Add(nativeReader.ReadBytes(28));

            section.TextureCoordinateRatios.Clear();
            for (int i = 0; i < 6; i++)
            {
                section.TextureCoordinateRatios.Add(nativeReader.ReadFloat());
            }

            var positionBeforeGeomDecl = nativeReader.Position;

            section.DeclCount = 2;
            section.ReadGeomDecl(nativeReader);

            var positionAfterGeomDecl = nativeReader.Position;
            var lengthOfGeomDecl = positionAfterGeomDecl - positionBeforeGeomDecl;

            _ = nativeReader.Position;
            section.UnknownBytes.Add(nativeReader.ReadBytes(68));
            section.ReadBones(nativeReader, bonePositions);
        }

        public void Read(NativeReader nativeReader, IMeshSetSection section, int index)
        {
            Read(nativeReader, (MeshSetSection)section, index);
        }

    }
}
