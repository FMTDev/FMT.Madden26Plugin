using FMT.Core.Meshes;
using FMT.FileTools;
using FMT.Logging;
using FMT.PluginInterfaces;
using FMT.ProfileSystem;
using System.Collections.Generic;
using System.IO;

namespace Madden26Plugin.Meshes.Writers
{
    public class Madden26MeshSetWriter : IMeshSetWriter
    {
        static Madden26MeshSetWriter()
        {
            MeshSet.MeshContainerType = typeof(Madden26MeshContainer);
        }

        public void Write(NativeWriter writer, MeshSet meshSet, MeshContainer meshContainer)
        {
            if (meshSet.Type == MeshType.MeshType_Rigid)
            {
                new Madden26RigidMeshSetWriter().Write(writer, meshSet, meshContainer);
                return;
            }

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
            //meshContainer.WriteRelocPtr("STR_PART", meshSet.fullName, writer);
            meshContainer.WriteRelocPtr("STR_PART", meshSet.Name, writer);
            writer.Write(meshSet.nameHash);
            writer.Write((byte)meshSet.Type);
            writer.Write(meshSet.UnknownBytes[0]);
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

            if (meshSet.Type == MeshType.MeshType_Skinned)
            {
                writer.Write(meshSet.UnknownBytes[1]);
                writer.WriteUInt16LittleEndian((ushort)meshSet.boneCount);
                writer.WriteUInt16LittleEndian((ushort)meshSet.CullBoxCount);
                if (meshSet.CullBoxCount > 0)
                {
                    meshContainer.WriteRelocPtr("BONEINDICES", meshSet.boneIndices, writer);
                    meshContainer.WriteRelocPtr("BONEBBOXES", meshSet.boneBoundingBoxes, writer);
                }
            }

            else if (meshSet.Type == MeshType.MeshType_Composite)
            {
                writer.WriteUInt16LittleEndian((ushort)meshSet.boneIndices.Count);
                writer.WriteUInt16LittleEndian(0);
                meshContainer.WriteRelocPtr("BONEINDICES", meshSet.boneIndices, writer);
                meshContainer.WriteRelocPtr("BONEBBOXES", meshSet.boneBoundingBoxes, writer);
            }
            writer.WritePadding(16);
            foreach (MeshSetLod lod in meshSet.Lods)
            {
                meshContainer.AddOffset("LOD", lod, writer);
                lod.Write(writer, meshContainer);
            }
            var sectionIndex = 0;
            foreach (MeshSetLod lod in meshSet.Lods)
            {
                meshContainer.AddOffset("SECTION", lod.Sections, writer);
                foreach (MeshSetSection section in lod.Sections)
                {
                    section.SectionIndex = sectionIndex;
                    section.Process(writer, meshContainer);
                    sectionIndex++;
                }
            }
            writer.WritePadding(16);
            foreach (MeshSetLod lod5 in meshSet.Lods)
            {
                foreach (MeshSetSection section2 in lod5.Sections)
                {
                    if (section2.BoneList.Count == 0)
                    {
                        continue;
                    }
                    meshContainer.AddOffset("BONELIST", section2.BoneList, writer);
                    foreach (ushort bone in section2.BoneList)
                    {
                        writer.WriteUInt16LittleEndian(bone);
                    }
                }
            }
            writer.WritePadding(16);
            meshContainer.WriteStrings(writer);
            writer.WritePadding(16);

            if (meshSet.Type == MeshType.MeshType_Skinned)
            {
                foreach (MeshSetLod lod6 in meshSet.Lods)
                {
                    foreach (List<byte> categorySubsetIndex in lod6.CategorySubsetIndices)
                    {
                        meshContainer.AddOffset("SUBSET", categorySubsetIndex, writer);
                        writer.WriteBytes(categorySubsetIndex.ToArray());
                    }
                }
            }
            else
            {
                foreach (MeshSetLod lod6 in meshSet.Lods)
                {
                    foreach (List<byte> categorySubsetIndex in lod6.CategorySubsetIndices)
                    {
                        meshContainer.AddOffset("SUBSET", categorySubsetIndex, writer);
                        writer.Write((byte)0x0);
                    }
                }
            }
            writer.WritePadding(16);
            if (meshSet.Type != MeshType.MeshType_Skinned)
            {
                return;
            }
            foreach (MeshSetLod lod4 in meshSet.Lods)
            {
                meshContainer.AddOffset("BONES", lod4.BoneIndexArray, writer);
                foreach (uint item in lod4.BoneIndexArray)
                {
                    writer.Write((uint)item);
                }
            }
            writer.WritePadding(16);
            meshContainer.AddOffset("BONEINDICES", meshSet.boneIndices, writer);
            //foreach (ushort boneIndex in CullBoxCount)
            for (var iCB = 0; iCB < meshSet.CullBoxCount; iCB++)
            {
                //writer.WriteUInt16LittleEndian(boneIndex);
                writer.WriteUInt16LittleEndian(meshSet.boneIndices[iCB]);
            }
            writer.WritePadding(16);
            meshContainer.AddOffset("BONEBBOXES", meshSet.boneBoundingBoxes, writer);
            for (var iBB = 0; iBB < meshSet.boneBoundingBoxes.Count; iBB++)
            {
                writer.WriteAxisAlignedBox(meshSet.boneBoundingBoxes[iBB]);
            }
            writer.WritePadding(16);
        }

        private byte[] WriteWithoutHeader(MeshSet meshSet, MeshContainer meshContainer)
        {
            MemoryStream memoryStream = new MemoryStream();
            using (var tempWriter = new NativeWriter(memoryStream))
            {
                Write(tempWriter, meshSet, meshContainer);
                return memoryStream.ToArray();
            }
        }

        public void Write(NativeWriter writer, IMeshSet meshSet, IMeshContainer meshContainer)
        {
            // We create a new meshContainer here because we need to process the meshSet to fill in the reloc ptrs
            //meshContainer = new Madden26MeshContainer();
            //((MeshSet)meshSet).PreProcess(meshContainer);

            // We need to write the meshSet without the header first so we can get the size for the header
            var meshDataWithoutHeader = WriteWithoutHeader((MeshSet)meshSet, (MeshContainer)meshContainer);

#if DEBUG
            DebugBytesToFileLogger.Instance.WriteAllBytes("_MeshSetWithoutHeader.dat", meshDataWithoutHeader, $"Mesh/{ProfileManager.Instance.Name}/Write");
#endif
            if (meshSet.Type == MeshType.MeshType_Rigid)
            {
                writer.WriteBytes(meshSet.UnknownBytes[0]);
            }
            else
                new Madden26MeshSetHeaderWriter().Write(writer, new Madden26MeshSetHeader() { });
            writer.Write(meshDataWithoutHeader);
        }
    }


}
