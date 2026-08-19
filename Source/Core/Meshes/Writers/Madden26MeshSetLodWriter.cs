using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.PluginInterfaces;
using System.Collections.Generic;

namespace Madden26Plugin.Meshes.Writers
{
    public class Madden26MeshSetLodWriter : IMeshSetLodWriter
    {
        public long SectionOffset { get; private set; }
        public List<(long, ulong, string)> StringPositions { get; internal set; } = new List<(long, ulong, string)>();
        public List<long> SubsetCategoryOffsets { get; internal set; } = new List<long>();

        public void Write(NativeWriter writer, IMeshContainer meshContainer, IMeshSetLod meshSetLod)
        {
            _ = writer.Position;

            writer.Write((int)meshSetLod.Type);
            writer.Write(meshSetLod.maxInstances);
            meshContainer.WriteRelocArray("SECTION", meshSetLod.Sections, writer);
            foreach (List<byte> subsetCategory in meshSetLod.CategorySubsetIndices)
            {
                meshContainer.WriteRelocArray("SUBSET", subsetCategory, writer);
            }
            writer.Write(((MeshSetLod)meshSetLod).FlagsNumber);
            writer.Write(((MeshSetLod)meshSetLod).indexBufferFormat.format);
            writer.Write(meshSetLod.IndexBufferSize);
            writer.Write(meshSetLod.VertexBufferSize);
            if (meshSetLod.HasAdjacencyInMesh)
            {
                writer.Write(0);
            }
            writer.Write(meshSetLod.UnknownChunkPad);
            writer.WriteGuid(meshSetLod.ChunkId);

            _ = meshSetLod.inlineDataOffset;
            writer.Write(meshSetLod.inlineDataOffset); // 8 byte in 26, looks like its always FF FF FF FF FF FF FF FF
            writer.Write((int)-1); // FF FF FF FF
            if (meshSetLod.HasAdjacencyInMesh)
            {
                if (meshSetLod.inlineDataOffset != uint.MaxValue)
                {
                    meshContainer.WriteRelocPtr("ADJACENCY", ((MeshSetLod)meshSetLod).adjacencyData, writer);
                }
                else
                {
                    writer.WriteUInt64LittleEndian(0uL);
                }
            }
            meshContainer.WriteRelocPtr("STR", meshSetLod.shaderDebugName, writer);
            // Madden26 has this to be the same as the shaderDebugName position + 5 (removing the Mesh:)
            meshContainer.WriteRelocPtr("STR_PART", meshSetLod.Name, writer);
            meshContainer.WriteRelocPtr("STR_PART", meshSetLod.shortName, writer);
            writer.Write(meshSetLod.nameHash);
            writer.WriteInt64LittleEndian(meshSetLod.UnknownLongAfterNameHash);
            if (meshSetLod.Type == MeshType.MeshType_Skinned)
            {
                writer.Write(meshSetLod.BoneIndexArray.Count);
                meshContainer.WriteRelocPtr("BONES", meshSetLod.BoneIndexArray, writer);
            }
            else if (meshSetLod.Type == MeshType.MeshType_Composite)
            {
            }
            writer.WritePadding(16);
        }
    }
}
