using FMT.FileTools;
using Madden26Plugin.Meshes;

namespace Madden26Plugin.Meshes.Writers
{
    public class Madden26MeshSetHeaderWriter
    {
        public void Write(NativeWriter nativeWriter, Madden26Plugin.Meshes.Madden26MeshSetHeader meshSetHeader)
        {
            nativeWriter.Write(192);
            nativeWriter.Write(188);
            nativeWriter.Write(368);
            nativeWriter.Write(0);
        }
    }
}
