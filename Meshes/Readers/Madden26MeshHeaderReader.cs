using FMT.FileTools;
using FMT.PluginInterfaces;

namespace Madden26Plugin.Meshes.Readers
{
    public class Madden26MeshHeaderReader
    {
        public int HeaderSize { get; set; }

        public Madden26MeshHeaderReader Read(NativeReader nativeReader, IMeshSet meshSet)
        {
            HeaderSize = nativeReader.ReadInt32();
            nativeReader.ReadInt32();
            nativeReader.ReadInt32();
            nativeReader.ReadInt32();
            return this;
        }
    }
}
