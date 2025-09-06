using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Meshes;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System.Buffers.Binary;

namespace Madden26Plugin.Meshes.Readers
{
    public class Madden26SkinnedMeshReader
    {
        public int MaxLodCount => (int)MeshLimits.MaxMeshLodCount;

        public void Read(NativeReader nativeReader, IMeshSet meshSet)
        {
            nativeReader.Position = 0;

            var header = new Madden26MeshHeaderReader().Read(nativeReader, meshSet);
            meshSet.BoundingBox = nativeReader.ReadAxisAlignedBox();
            meshSet.LodOffsets.Clear();
            for (int i2 = 0; i2 < MaxLodCount; i2++)
            {
                meshSet.LodOffsets.Add(nativeReader.ReadLong());
            }
            meshSet.UnknownPostLODCount = nativeReader.ReadLong();
            long offsetNameLong = nativeReader.ReadLong() + 16;
            long offsetNameShort = nativeReader.ReadLong() + 16;
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
            nativeReader.ReadUInt16LittleEndian();
            var lodsCount = nativeReader.ReadUInt16LittleEndian();
            nativeReader.ReadUInt32LittleEndian();
            nativeReader.ReadUInt16LittleEndian();

            // useful for resetting when live debugging
            var positionBeforeMeshTypeRead = nativeReader.Position;
            nativeReader.Position = positionBeforeMeshTypeRead;
            meshSet.UnknownBytes.Add(nativeReader.ReadBytes(8));

            meshSet.boneCount = nativeReader.ReadUInt16LittleEndian();
            meshSet.CullBoxCount = nativeReader.ReadUInt16LittleEndian();
            nativeReader.ReadUInt16();
            if (meshSet.CullBoxCount != 0)
            {
                long cullBoxBoneIndicesOffset = nativeReader.ReadInt64LittleEndian();
                long cullBoxBoundingBoxOffset = nativeReader.ReadInt64LittleEndian();
                long position = nativeReader.Position;
                if (cullBoxBoneIndicesOffset != 0L)
                {
                    nativeReader.Position = cullBoxBoneIndicesOffset;
                    for (int m = 0; m < meshSet.CullBoxCount; m++)
                    {
                        meshSet.boneIndices.Add(nativeReader.ReadUInt16LittleEndian());
                    }
                }
                if (cullBoxBoundingBoxOffset != 0L)
                {
                    nativeReader.Position = cullBoxBoundingBoxOffset;
                    for (int l = 0; l < meshSet.CullBoxCount; l++)
                    {
                        meshSet.boneBoundingBoxes.Add(nativeReader.ReadAxisAlignedBox());
                    }
                }
                nativeReader.Position = position;
            }
            //}

            nativeReader.Pad(16);
            meshSet.headerSize = (uint)header.HeaderSize;// (uint)nativeReader.Position;
            var sectionIndex = 0;
            meshSet.Lods.Clear();
            for (int n = 0; n < lodsCount; n++)
            {
                nativeReader.Position = meshSet.LodOffsets[n];
                if (meshSet.LodOffsets[n] != 0)
                    meshSet.Lods.Add(new Madden26MeshSetLodReader().Read(nativeReader, meshSet, ref sectionIndex));
            }

            nativeReader.Position = offsetNameLong;
            meshSet.FullName = nativeReader.ReadNullTerminatedString();
            nativeReader.Position = offsetNameShort;
            meshSet.Name = nativeReader.ReadNullTerminatedString();

            // Get MeshSet Layout Size
            uint? meshSetLayoutSize = null;
            uint? meshSetVertexSize = null;

            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();
            var resEntry = assetManagementService.GetResEntry(meshSet.FullName);
            if (resEntry != null)
            {
                meshSetLayoutSize = BinaryPrimitives.ReadUInt32LittleEndian(resEntry.ResMeta);
                meshSetVertexSize = BinaryPrimitives.ReadUInt32LittleEndian(resEntry.ResMeta.AsSpan(4));
            }
            nativeReader.Pad(16);
            foreach (MeshSetLod lod in meshSet.Lods)
            {
                for (int l = 0; l < lod.CategorySubsetIndices.Count; l++)
                {
                    for (int j2 = 0; j2 < lod.CategorySubsetIndices[l].Count; j2++)
                    {
                        lod.CategorySubsetIndices[l][j2] = nativeReader.ReadByte();
                    }
                }
            }

            if (meshSetLayoutSize.HasValue)
            {
                nativeReader.Position = meshSetLayoutSize.Value;
                if (nativeReader.Position + 128 < nativeReader.Length)
                {
                    nativeReader.Position += 128;
                    foreach (MeshSetLod lod in meshSet.Lods)
                    {
                        if (lod.ChunkId == Guid.Empty && (lod.InlineData == null || lod.InlineData.Length == 0))
                        {
                            lod.SetInlineData(nativeReader.ReadBytes((int)(lod.VertexBufferSize + lod.IndexBufferSize)));
                            nativeReader.Pad(16);
                        }
                    }
                }
            }
        }
    }
}
