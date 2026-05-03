using FMT.Core.Meshes;
using FMT.FileTools;
using System.Collections.Generic;

namespace Madden26Plugin.Meshes.Writers
{
    public class Madden26RigidMeshSetWriter
    {
        internal void Write(NativeWriter writer, MeshSet meshSet, MeshContainer meshContainer)
        {
            writer.WriteAxisAlignedBox(meshSet.BoundingBox);
            for (int i = 0; i < meshSet.MaxLodCount; i++)
            {
                if (i < meshSet.LodCount)
                {
                    meshContainer.WriteRelocPtr("LOD", meshSet.Lods[i], writer);
                }
                else
                {
                    writer.WriteUInt64LittleEndian(0uL);
                }
            }
            writer.Write(meshSet.UnknownPostLODCount);
            meshContainer.WriteRelocPtr("STR", meshSet.fullName, writer);
            meshContainer.WriteRelocPtr("STR", meshSet.Name, writer);
            writer.Write(meshSet.nameHash);
            writer.Write((byte)meshSet.Type);
            writer.Write(meshSet.UnknownBytes[1]);
            for (int n = 0; n < meshSet.MaxLodCount * 2; n++)
            {
                writer.Write((ushort)meshSet.LodFade[n]);
            }
            writer.Write((long)meshSet.MeshSetLayoutFlags);
            writer.Write((byte)meshSet.ShaderDrawOrder);
            writer.Write((byte)meshSet.ShaderDrawOrderUserSlot);
            writer.Write((ushort)meshSet.ShaderDrawOrderSubOrder);
            writer.WriteUInt16LittleEndian(meshSet.LodCount);
            writer.WriteUInt16LittleEndian(meshSet.MeshCount);
            writer.Write(meshSet.UnknownBytes[2]);
            writer.WritePadding(16);
            foreach (var lod in meshSet.Lods)
            {
                meshContainer.AddOffset("LOD", lod, writer);
                lod.Write(writer, meshContainer);
            }
            var sectionIndex = 0;
            foreach (var lod in meshSet.Lods)
            {
                meshContainer.AddOffset("SECTION", lod.Sections, writer);
                foreach (var section in lod.Sections)
                {
                    section.SectionIndex = sectionIndex;
                    section.Process(writer, meshContainer);
                    sectionIndex++;
                }
            }
            writer.WritePadding(16);
            meshContainer.WriteStrings(writer);
            writer.WritePadding(16);
            foreach (var lod in meshSet.Lods)
            {
                foreach (List<byte> categorySubsetIndex in lod.CategorySubsetIndices)
                {
                    meshContainer.AddOffset("SUBSET", categorySubsetIndex, writer);
                    writer.WriteBytes(categorySubsetIndex.ToArray());
                }
            }
            writer.WritePadding(16);
        }
    }
}
