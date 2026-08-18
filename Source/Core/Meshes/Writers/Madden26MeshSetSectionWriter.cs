using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.PluginInterfaces;
using System.Collections.Generic;

namespace Madden26Plugin.Meshes.Writers
{
    public class Madden26MeshSetSectionWriter : IMeshSetSectionWriter
    {
        public List<(long, ulong, string)> StringPositions { get; internal set; } = new List<(long, ulong, string)>();
        public long BoneListPosition { get; internal set; } = 0L;

        public void Write(NativeWriter writer, MeshSetSection section, MeshContainer meshContainer)
        {
            var writerPositionBeforeStarting = writer.Position;

            // 0
            writer.Write(section.Offset1);

            // name
            var strIndexAndName = section.SectionIndex + ":" + section.Name;
            meshContainer.WriteRelocPtr("STR", strIndexAndName, writer);

            // bones
            if (section.BoneList.Count > 0)
                meshContainer.WriteRelocPtr("BONELIST", section.BoneList, writer);
            else
                writer.WriteUInt64LittleEndian(0uL);

            // bone count
            writer.WriteUInt16LittleEndian((ushort)section.BoneList.Count);
            //writer.WriteUInt16LittleEndian((ushort)section.BonesPerVertex);
            writer.WriteUInt16LittleEndian(section.BonesPerVertex16Bit);
            writer.WriteUInt16LittleEndian((ushort)section.MaterialId);
            writer.Write((byte)section.VertexStride);
            writer.Write((byte)section.PrimitiveType);
            writer.WriteUInt32LittleEndian(section.PrimitiveCount);
            writer.WriteUInt32LittleEndian(section.StartIndex);
            writer.WriteUInt32LittleEndian(section.VertexOffset);
            writer.WriteUInt32LittleEndian(section.VertexCount);
            writer.WriteBytes(section.UnknownBytes[0]);

            for (int coordIndex = 0; coordIndex < 6; coordIndex++)
            {
                writer.WriteSingleLittleEndian(section.TextureCoordinateRatios[coordIndex]);
            }

            for (int geomDeclIndex = 0; geomDeclIndex < 2; geomDeclIndex++)
            {
                for (int j = 0; j < section.GeometryDeclDesc[geomDeclIndex].Elements.Length; j++)
                {
                    writer.Write((byte)section.GeometryDeclDesc[geomDeclIndex].Elements[j].Usage);
                    writer.Write((byte)section.GeometryDeclDesc[geomDeclIndex].Elements[j].Format);
                    writer.Write(section.GeometryDeclDesc[geomDeclIndex].Elements[j].Offset);
                    writer.Write(section.GeometryDeclDesc[geomDeclIndex].Elements[j].StreamIndex);
                }
                for (int k = 0; k < section.GeometryDeclDesc[geomDeclIndex].Streams.Length; k++)
                {
                    writer.Write(section.GeometryDeclDesc[geomDeclIndex].Streams[k].VertexStride);
                    writer.Write((byte)section.GeometryDeclDesc[geomDeclIndex].Streams[k].Classification);
                }
                writer.Write(section.GeometryDeclDesc[geomDeclIndex].ElementCount);
                writer.Write(section.GeometryDeclDesc[geomDeclIndex].StreamCount);
                writer.WriteUInt16LittleEndian(0);
            }

            writer.WriteBytes(section.UnknownBytes[1]);
        }


        public void Write(NativeWriter writer, IMeshSetSection section, IMeshContainer meshContainer)
        {
            Write(writer, (MeshSetSection)section, (MeshContainer)meshContainer);
        }
    }
}
