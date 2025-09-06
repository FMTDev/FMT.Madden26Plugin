using FMT.FileTools;
using FMT.Logging;
using FMT.PluginInterfaces;

namespace Madden26Plugin.Textures
{
    public class Madden26TextureResourceWriter : ITextureResourceWriter
    {
        public byte[] ToBytes(ITexture texture)
        {
            // Define new memory stream with a capacity of 132 bytes
            MemoryStream memoryStream = new(132);
            using (var nw = new NativeWriter(memoryStream))
            {
                nw.Write((uint)texture.MipOffsets[0]);
                nw.Write((uint)texture.MipOffsets[1]);
                nw.Write((uint)texture.Type);
                nw.Write((int)texture.PixelFormatNumber);
                nw.Write((uint)texture.PoolId);
                nw.Write((ushort)texture.Flags);
                nw.Write((ushort)texture.Width);
                nw.Write((ushort)texture.Height);
                nw.Write((ushort)texture.Depth);
                nw.Write((ushort)texture.SliceCount);
                nw.Write((byte)texture.MipCount);
                nw.Write((byte)texture.FirstMip);
                nw.Write(texture.UnknownBytes[0]);
                nw.Write(texture.ChunkId);
                for (int i = 0; i < 15; i++)
                    nw.Write((uint)texture.MipSizes[i]);

                nw.Write((uint)texture.ChunkSize);
                nw.Write((uint)texture.AssetNameHash);
                nw.WriteNullTerminatedString(texture.TextureGroup);
                nw.Write(texture.UnknownBytes[1]);
            }

            var arrayOfBytes = memoryStream.ToArray();
#if DEBUG
            DebugBytesToFileLogger.Instance.WriteAllBytes("Texture_Write.bin", arrayOfBytes, "Texture/FC26/Write");
#endif

            return arrayOfBytes;
        }
    }
}
