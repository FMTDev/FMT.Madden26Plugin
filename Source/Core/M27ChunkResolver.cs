using System.Runtime.InteropServices;
using System.Text.Json;
using FMT.Db;
using FMT.FileTools;
using FMT.FileTools.Deobfuscators;
using FMT.FileTools.Readers;
using FMT.Hash;
using FMT.Models.Assets;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin
{
    public static class M27ChunkResolver
    {
        private struct ChunkRef
        {
            public Guid Id;
            public int CatalogIndex;
            public short Cas;
            public uint Offset;
            public uint Size;
        }

        private static readonly object SyncRoot = new();
        private static readonly Dictionary<Guid, ChunkAssetEntry> ResolvedEntries = new();
        private static Dictionary<Guid, ChunkRef> chunkIndex;
        private static Dictionary<int, string> catalogBundles;
        private static string m27DataDir;

        public static bool IsPlaceholderChunkId(Guid chunkId)
        {
            if (chunkId == Guid.Empty)
                return true;

            byte[] b = chunkId.ToByteArray();
            for (int i = 4; i < 8; i++)
            {
                if (b[i] != 0)
                    return false;
            }
            for (int i = 9; i < 16; i++)
            {
                if (b[i] != 0)
                    return false;
            }
            return true;
        }

        public static bool TryResolve(IAssetManagementService assetManagementService, Guid chunkId, out ChunkAssetEntry chunkEntry, out string error)
        {
            chunkEntry = null;
            error = null;
            try
            {
                EnsureIndex(out error);

                if (chunkIndex == null || !chunkIndex.TryGetValue(chunkId, out ChunkRef chunkRef))
                {
                    error = $"Chunk {chunkId} was not found in any Madden NFL 27 TOC.";
                    return false;
                }

                if (catalogBundles == null || !catalogBundles.TryGetValue(chunkRef.CatalogIndex, out string bundlePath))
                {
                    error = $"Catalog PersistentIndex {chunkRef.CatalogIndex} for chunk {chunkId} has no matching install bundle in Madden NFL 27 layout.toc.";
                    return false;
                }

                lock (ResolvedEntries)
                {
                    if (ResolvedEntries.TryGetValue(chunkId, out chunkEntry))
                        return true;
                }

                string casPath = Path.Combine(m27DataDir, bundlePath.Replace('/', Path.DirectorySeparatorChar), $"cas_{chunkRef.Cas:D2}.cas");
                if (!File.Exists(casPath))
                {
                    error = $"CAS file not found: {casPath}";
                    return false;
                }

                byte[] container = ReadRange(casPath, chunkRef.Offset, chunkRef.Size);
                byte[] data = DecompressChunk(container);

                var entry = new ChunkAssetEntry
                {
                    Id = chunkId,
                    Name = chunkId.ToString(),
                    IsTocChunk = true,
                    IsAdded = true,
                    Size = data.Length,
                    BundledSize = (uint)data.Length,
                    ModifiedEntry = new ModifiedAssetEntry
                    {
                        Data = data,
                        Sha1 = Sha1.Create(data),
                        Size = data.Length,
                        OriginalSize = data.Length,
                        LogicalSize = (uint)data.Length
                    }
                };

                lock (ResolvedEntries)
                {
                    assetManagementService.AddChunk(entry);
                    ResolvedEntries[chunkId] = entry;
                }

                if (assetManagementService.Logger != null)
                    assetManagementService.Logger.Log($"Chunk {chunkId} was resolved from Madden NFL 27 game data.");

                chunkEntry = entry;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }

        private static void EnsureIndex(out string error)
        {
            error = null;
            if (chunkIndex != null && catalogBundles != null)
                return;

            lock (SyncRoot)
            {
                if (chunkIndex != null && catalogBundles != null)
                    return;

                m27DataDir = FindMadden27DataDir();
                if (string.IsNullOrEmpty(m27DataDir))
                {
                    error = "Unable to locate the Madden NFL 27 game data folder. Create M27ChunkResolver.json next to the plugin with {\"GameDataPath\": \"...\"} pointing to the Madden NFL 27 Data folder.";
                    throw new InvalidOperationException(error);
                }

                catalogBundles = ReadCatalogBundles(m27DataDir);

                chunkIndex = new Dictionary<Guid, ChunkRef>();
                string win32Dir = Path.Combine(m27DataDir, "Win32");
                foreach (string tocPath in Directory.GetFiles(win32Dir, "*.toc"))
                {
                    foreach (ChunkRef chunkRef in ReadTocChunks(tocPath))
                        chunkIndex.TryAdd(chunkRef.Id, chunkRef);
                }
            }
        }

        private static Dictionary<int, string> ReadCatalogBundles(string dataDir)
        {
            var result = new Dictionary<int, string>();
            string layoutPath = Path.Combine(dataDir, "layout.toc");
            if (!File.Exists(layoutPath))
                return result;

            using var dbReader = new DbReader(new MemoryStream(File.ReadAllBytes(layoutPath)), new NullDeobfuscator());
            DbObject layout = dbReader.ReadDbObject();
            DbObject installManifest = layout.GetValue<DbObject>("installManifest");
            if (installManifest == null)
                return result;

            foreach (DbObject installChunk in installManifest.GetValue<DbObject>("installChunks"))
            {
                if (installChunk.GetValue("testDLC", defaultValue: false))
                    continue;

                string bundle = installChunk.HasValue("installBundle")
                    ? installChunk.GetValue<string>("installBundle")
                    : "win32/" + installChunk.GetValue<string>("name");

                if (!installChunk.HasValue("PersistentIndex"))
                    continue;

                int persistentIndex = installChunk.GetValue("PersistentIndex", 0);
                result[persistentIndex] = bundle.Trim();
            }

            return result;
        }

        private static IEnumerable<ChunkRef> ReadTocChunks(string tocPath)
        {
            var result = new List<ChunkRef>();
            byte[] bytes = File.ReadAllBytes(tocPath);
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);

            ms.Position = 556;
            ReadInt32BE(br);
            ReadInt32BE(br);
            ReadInt32BE(br);
            ReadInt32BE(br);
            int chunkGuidOffset = ReadInt32BE(br);
            int chunkCount = ReadInt32BE(br);
            int chunkEntryOffset = ReadInt32BE(br);

            if (chunkCount <= 0)
                return result;

            var guids = new Guid[chunkCount];
            ms.Position = 556 + chunkGuidOffset;
            for (int i = 0; i < chunkCount; i++)
            {
                byte[] guidBytes = br.ReadBytes(16);
                Array.Reverse(guidBytes);
                guids[i] = new Guid(guidBytes);
                ReadInt32BE(br);
            }

            ms.Position = 556 + chunkEntryOffset;
            for (int i = 0; i < chunkCount; i++)
            {
                ReadUInt16BE(br);
                int catalogIndex = ReadInt32BE(br);
                short cas = (short)ReadUInt16BE(br);
                uint offset = ReadUInt32BE(br);
                uint size = ReadUInt32BE(br);
                result.Add(new ChunkRef
                {
                    Id = guids[i],
                    CatalogIndex = catalogIndex,
                    Cas = cas,
                    Offset = offset,
                    Size = size
                });
            }

            return result;
        }

        private static byte[] DecompressChunk(byte[] container)
        {
            using var ms = new MemoryStream(container);
            using var br = new BinaryReader(ms);
            using var output = new MemoryStream();

            while (ms.Position < ms.Length)
            {
                if (ms.Length - ms.Position < 8)
                    throw new InvalidDataException("Truncated chunk block header.");

                uint uncompressedLen = ReadUInt32BE(br);
                ushort code = ReadUInt16BE(br);
                ushort sizeLow = ReadUInt16BE(br);
                uint fullSize = sizeLow | ((uint)(code & 0x000F) << 16);

                if (fullSize > ms.Length - ms.Position)
                    throw new InvalidDataException("Chunk block payload exceeds container size.");

                byte[] payload = br.ReadBytes((int)fullSize);

                byte[] block;
                if (code == 0x0070)
                {
                    block = payload;
                }
                else if ((code & 0xFFF0) == 0x1170 || (code & 0xFFF0) == 0x1570 || (code & 0xFFF0) == 0x1970)
                {
                    EnsureOodle();
                    block = FMT.FileTools.Compression.ThirdParty.Oodle.DecompressOodle(payload, (int)uncompressedLen, false);
                }
                else if ((code & 0xFFF0) == 0x0F70)
                {
                    block = DecompressZstd(payload, (int)uncompressedLen);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported chunk compression code 0x{code:X4}.");
                }

                output.Write(block, 0, Math.Min(block.Length, (int)uncompressedLen));
            }

            return output.ToArray();
        }

        private static void EnsureOodle()
        {
            if (FMT.FileTools.Compression.ThirdParty.Oodle.IsBound)
                return;

            try
            {
                FMT.FileTools.Compression.ThirdParty.Oodle.Bind(AppContext.BaseDirectory, 9);
            }
            catch (Exception bindEx)
            {
                throw new InvalidOperationException("Unable to bind the Oodle library (oo2core). " + bindEx.Message);
            }
        }

        private static byte[] DecompressZstd(byte[] payload, int uncompressedLen)
        {
            if (FMT.FileTools.Compression.ThirdParty.ZStd.Decompress == null)
                throw new NotSupportedException("ZStd is not available.");

            byte[] output = new byte[uncompressedLen];
            GCHandle pinIn = GCHandle.Alloc(payload, GCHandleType.Pinned);
            GCHandle pinOut = GCHandle.Alloc(output, GCHandleType.Pinned);
            try
            {
                ulong result = FMT.FileTools.Compression.ThirdParty.ZStd.Decompress(
                    pinOut.AddrOfPinnedObject(), (ulong)output.Length,
                    pinIn.AddrOfPinnedObject(), (ulong)payload.Length);

                if (FMT.FileTools.Compression.ThirdParty.ZStd.IsError != null && FMT.FileTools.Compression.ThirdParty.ZStd.IsError(result))
                    throw new InvalidDataException("ZSTD decompression failed.");

                if (result != (ulong)uncompressedLen)
                    throw new InvalidDataException("ZSTD decompression size mismatch.");
            }
            finally
            {
                pinIn.Free();
                pinOut.Free();
            }

            return output;
        }

        private static byte[] ReadRange(string path, uint offset, uint size)
        {
            using var fs = File.OpenRead(path);
            fs.Position = offset;
            var buffer = new byte[size];
            int read = 0;
            while (read < size)
            {
                int n = fs.Read(buffer, read, (int)(size - read));
                if (n <= 0)
                    break;
                read += n;
            }
            if (read != size)
                throw new EndOfStreamException("Unexpected end of CAS file.");
            return buffer;
        }

        private static string FindMadden27DataDir()
        {
            string configPath = Path.Combine(Path.GetDirectoryName(typeof(M27ChunkResolver).Assembly.Location), "M27ChunkResolver.json");
            if (File.Exists(configPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (doc.RootElement.TryGetProperty("GameDataPath", out JsonElement element))
                    {
                        string configured = element.GetString();
                        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "layout.toc")))
                            return configured;
                    }
                }
                catch
                {
                }
            }

            string[] roots =
            {
                @"C:\Program Files\EA Games",
                @"C:\Program Files (x86)\EA Games",
                @"D:\Program Files\EA Games",
                @"D:\EA Games"
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.Contains("Madden 27", StringComparison.OrdinalIgnoreCase) && !name.Contains("MADDEN 27", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dataDir = Path.Combine(dir, "Data");
                    if (File.Exists(Path.Combine(dataDir, "layout.toc")))
                        return dataDir;
                }
            }

            return null;
        }

        private static int ReadInt32BE(BinaryReader br) => (int)(((uint)br.ReadByte() << 24) | ((uint)br.ReadByte() << 16) | ((uint)br.ReadByte() << 8) | br.ReadByte());

        private static uint ReadUInt32BE(BinaryReader br) => ((uint)br.ReadByte() << 24) | ((uint)br.ReadByte() << 16) | ((uint)br.ReadByte() << 8) | br.ReadByte();

        private static ushort ReadUInt16BE(BinaryReader br) => (ushort)(((uint)br.ReadByte() << 8) | br.ReadByte());
    }
}
