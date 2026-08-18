using FMT.Core.Meshes;
using System.IO;

namespace Madden26Plugin.Meshes.Writers
{
    public sealed class Madden26MeshContainer : MeshContainer
    {
        static Madden26MeshContainer()
        {
            MeshSet.MeshContainerType = typeof(Madden26MeshContainer);
        }

        public override void AddOffset(string type, object data, BinaryWriter writer)
        {
            base.AddOffset(type, data, writer);
        }

        public override void AddRelocArray(string type, int count, object arrayObj)
        {
            base.AddRelocArray(type, count, arrayObj);
        }

        public override void AddRelocPtr(string type, object obj)
        {
            base.AddRelocPtr(type, obj);
        }

        public override void AddString(object obj, string data, bool ignoreNull = false)
        {
            base.AddString(obj, data, ignoreNull);
        }

        public override void WriteRelocArray(string type, object arrayObj, BinaryWriter writer)
        {
            base.WriteRelocArray(type, arrayObj, writer);
        }

        public override void WriteRelocPtr(string type, object obj, BinaryWriter writer)
        {
            base.WriteRelocPtr(type, obj, writer);
            //if (writer.BaseStream.Position == 0)
            //{

            //}
            //FindRelocPtr(type, obj).Offset = writer.BaseStream.Position + 16;
            //writer.Write(16045690984833335023uL);
        }

        public override void WriteRelocPtrs(BinaryWriter writer)
        {
            base.WriteRelocPtrs(writer);
        }

        public override void WriteRelocTable(BinaryWriter writer)
        {
            base.WriteRelocTable(writer);
        }

        public override void WriteStrings(BinaryWriter writer)
        {
            base.WriteStrings(writer);
        }
    }
}
