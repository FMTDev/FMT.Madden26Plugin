using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Textures
{
    public class Madden26TextureResourceReader : ITextureResourceReader
    {
        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();

        public void ReadInStream(NativeReader nativeReader, ITexture texture)
        {
#if DEBUG
            nativeReader.Position = 0;
            var msCopy = new MemoryStream();
            nativeReader.BaseStream.CopyTo(msCopy);
            nativeReader.Position = 0;
            DebugBytesToFileLogger.Instance.WriteAllBytes("Texture.bin", msCopy.ToArray(), $"Texture/{ProfileManager.ProfileName}/Read", false);
#endif
            texture.UnknownBytes.Clear();

            texture.MipOffsets[0] = nativeReader.ReadUInt();
            texture.MipOffsets[1] = nativeReader.ReadUInt();
            texture.Type = (TextureType)nativeReader.ReadUInt();
            texture.PixelFormatNumber = nativeReader.ReadInt();
            texture.PoolId = nativeReader.ReadUInt();
            texture.Flags = (TextureFlags)nativeReader.ReadUShort();
            texture.Width = nativeReader.ReadUShort();
            texture.Height = nativeReader.ReadUShort();
            texture.Depth = nativeReader.ReadUShort();
            texture.SliceCount = nativeReader.ReadUShort();
            texture.MipCount = nativeReader.ReadByte();
            texture.FirstMip = nativeReader.ReadByte();
            texture.UnknownBytes.Add(nativeReader.ReadBytes(8));
            texture.ChunkId = nativeReader.ReadGuid();
            texture.MipSizes = (from _ in Enumerable.Range(0, 15)
                                select nativeReader.ReadUInt()).ToArray();

            texture.ChunkSize = nativeReader.ReadUInt();
            // Todo: this is a ulong
            texture.UnknownBytes.Add(nativeReader.ReadBytes(8));
            texture.AssetNameHash = (uint)BitConverter.ToUInt64(texture.UnknownBytes[1]);
            texture.TextureGroup = nativeReader.ReadNullTerminatedString();

            List<byte> lastBytes = new();
            while (nativeReader.Position != nativeReader.Length)
            {
                lastBytes.Add(nativeReader.ReadByte());
            }
            texture.UnknownBytes.Add(lastBytes.ToArray());

            if (assetManagementService.Logger != null)
                assetManagementService.Logger.Log($"Texture: Loading ChunkId: {texture.ChunkId}");

            texture.ChunkEntry = assetManagementService.GetChunkEntry(texture.ChunkId);
            if (texture.ChunkEntry == null)
            {
                if (M27ChunkResolver.IsPlaceholderChunkId(texture.ChunkId))
                {
                    if (assetManagementService.Logger != null)
                        assetManagementService.Logger.Log($"Texture: Chunk {texture.ChunkId} is a placeholder chunk id with no chunk data. Allocating empty data buffer for texture importer.");

                    int bufferSize = (int)texture.ChunkSize;
                    if (bufferSize <= 0)
                    {
                        for (int i = 0; i < texture.MipSizes.Length; i++)
                            bufferSize += (int)texture.MipSizes[i];
                    }
                    if (bufferSize <= 0)
                        bufferSize = 1;

                    texture.Data = new byte[bufferSize];
                    return;
                }

                if (assetManagementService.Logger != null)
                    assetManagementService.Logger.Log($"Texture: Chunk {texture.ChunkId} not found in Madden NFL 26, attempting to resolve from Madden NFL 27 game data.");
                if (!M27ChunkResolver.TryResolve(assetManagementService, texture.ChunkId, out ChunkAssetEntry resolvedChunkEntry, out string resolveError))
                    throw new Exception($"Unable to locate chunk {texture.ChunkId} in the Madden NFL 26 or Madden NFL 27 game data. {resolveError}");
                texture.ChunkEntry = resolvedChunkEntry;
                texture.Data = resolvedChunkEntry.ModifiedEntry.Data;
            }
            else
            {
                texture.Data = assetManagementService.GetChunk(texture.ChunkEntry).ToArray();
            }
        }

    }
}
