using FMT.Compilers;
using FMT.Core.Models.TOC;
using FMT.Core.Writers;
using FMT.Db;
using FMT.Ebx;
using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.Resources.Textures;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.ThirdParty;
using System.Runtime.InteropServices;

namespace Madden26Plugin.Compiler
{
    //public class Madden26AssetCompiler : FrostbiteNullCompiler, IAssetCompiler
    public class Madden26AssetCompiler : FrostbiteNullCompiler, IAssetCompiler
    {
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();

        //public override bool RequiresCacheToCompile => false;

        /// <summary>
        /// Ensures that the mod data directory exists, creating it if necessary.
        /// </summary>
        /// <remarks>This method verifies the existence of the mod data directory and creates it if it
        /// does not exist.  The directory path is determined based on the file system service's base path and the mod
        /// directory name.</remarks>
        /// <param name="logger">The logger used to record messages during the operation.</param>
        /// <param name="modExecutor">The mod executor providing context for the operation, including logging capabilities.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the
        /// operation completes successfully.</returns>
        public override async Task<bool> PreCompile(ILogger logger, IModExecutor modExecutor)
        {
            SingletonService.GetInstance<IFileSystemService>();
            await Task.Run(delegate
            {
                new FileSystemGameUpdateManager(logger).MakeGameVanilla();
            });

            //modExecutor.UseModData = true;

            //var fs = SingletonService.GetInstance<IFileSystemService>();
            //var modDataPath = Path.Combine(fs.BasePath, ModDirectory);
            //if (!Directory.Exists(modDataPath))
            //{
            //    modExecutor.Logger.Log("Creating ModData");

            //    // create mod path
            //    Directory.CreateDirectory(modDataPath);
            //}


            //// copy data across
            //CopyToModData(logger, modExecutor);

            Oodle.Bind(fss.BasePath);
            //RecompressAllDataIntoCompressMaddenExpects(logger, modExecutor);

            return true;
        }

        private void RecompressAllDataIntoCompressMaddenExpects(ILogger logger, IModExecutor modExecutor)
        {
            foreach (var entry in modExecutor.ModifiedEbx)
            {
                entry.Value.ModifiedEntry.Data = CompressFile(entry.Value.ModifiedEntry.Data, null);
            }
            foreach (var entry in modExecutor.ModifiedRes)
            {
                entry.Value.ModifiedEntry.Data = CompressFile(entry.Value.ModifiedEntry.Data, null);
            }
            foreach (var entry in modExecutor.ModifiedChunks)
            {
                entry.Value.ModifiedEntry.Data = CompressFile(entry.Value.ModifiedEntry.Data, null);
            }
        }

        protected void CopyToModData(ILogger logger, IModExecutor modExecutor)
        {
            if (logger == null)
                logger = modExecutor.Logger;

            var fs = SingletonService.GetInstance<IFileSystemService>();

            if (!Directory.Exists(fs.BasePath))
                throw new DirectoryNotFoundException($"Unable to find the correct base path directory of {fs.BasePath}");

            var modDataPath = Path.Combine(fs.BasePath, ModDirectory);
            var modDataDataPath = Path.Combine(modDataPath, "Data");
            //var modDataUpdatePatchPath = Path.Combine(modDataPath, "Update", "Patch");
            Directory.CreateDirectory(modDataDataPath);
            //Directory.CreateDirectory(modDataUpdatePatchPath);

            logger.Log("Copying files from Data to ModData/Data");
            CopyDataFolder(Path.Combine(fs.BasePath, "Data"), modDataDataPath, logger);

            //logger.Log("Copying files from Update\\Patch to ModData\\Update\\Patch");

            //CopyDataFolder(fs.BasePath + "\\Update\\Patch\\", fs.BasePath + ModDirectory + "\\Update\\Patch\\", logger);
        }

        protected static void CopyDataFolder(string from_datafolderpath, string to_datafolderpath, ILogger logger)
        {
            // Cannot copy from itself to itself
            if (from_datafolderpath == to_datafolderpath)
                return;

            CopyDirectory(from_datafolderpath, to_datafolderpath, true, logger);
        }

        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive, ILogger logger)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            var filesToCopy = new List<(FileInfo, FileInfo)>();

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                if (file.Extension.Contains("bak", StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetFile = new FileInfo(targetFilePath);
                if (!targetFile.Exists)
                {
                    filesToCopy.Add((file, targetFile));
                    continue;
                }

                if (targetFile.Length != file.Length)
                {
                    filesToCopy.Add((file, targetFile));
                    continue;
                }

                if (targetFile.LastWriteTime != file.LastWriteTime)
                {
                    filesToCopy.Add((file, targetFile));
                    continue;
                }
            }

            //var index = 1;
            logger.Log($"Data Setup - Copying {sourceDir}");
            foreach (var ftc in filesToCopy)
            {
                ftc.Item1.CopyTo(ftc.Item2.FullName, true);
                //logger.Log($"Data Setup - Copied ({index}/{filesToCopy.Count}) - {ftc.Item1.FullName}");
                //index++;
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true, logger);
                }
            }
        }


        public override bool Compile(ILogger logger, IModExecutor modExecutor)
        {
            logger.Log($"{nameof(Madden26AssetCompiler)} started");

            fss.TOCFileType = typeof(Madden26TOCFile);
            TypeLibrary.Initialize();

            var folder = "native_data/";
            var superBundles = fss.SuperBundles;

            foreach (var sbName in superBundles)
            {
                var tocFileRAW = $"{folder}{sbName}.toc";
                string tocFileLocation = fss.ResolvePath(tocFileRAW);
                if (!string.IsNullOrEmpty(tocFileLocation) && File.Exists(tocFileLocation))
                {
                    Madden26TOCFile tocFile = new(tocFileRAW, false, false, false, -1, true);
                    _ = tocFile;

                    Dictionary<string, List<DbObject>> modifiedCas = new();
                    var modifiedCasBundles = new HashSet<CASBundle>();

                    foreach (int bundleHash in modExecutor.ModifiedBundles.Keys)
                    {
                        var indexOfBundle = Array.FindIndex(tocFile.Bundles, b => b.NameHash == bundleHash);
                        if (indexOfBundle != -1)
                        {
                            // Identify the Bundle being modified
                            var bundle = tocFile.Bundles[indexOfBundle];
                            _ = bundle;
                            var casBundle = tocFile.CasBundles.First(cb => cb.BaseBundle == bundle);
                            _ = casBundle;
                            // Add the identified bundle to the modifiedCasBundles hashset. To be used for Writing the Binary Info later
                            if (!modifiedCasBundles.Contains(casBundle))
                                modifiedCasBundles.Add(casBundle);

                            tocFile.ShouldReadCASBundles = true;
                            tocFile.ReadCasBundlesFromCasFiles(new[] { casBundle.BaseBundle.NameHash });
                            foreach (var t in tocFile.TOCObjectsByCasBundle)
                            {
                                foreach (DbObject ebx in t.Value.GetValue<DbObject>("ebx"))
                                {
                                    if (modExecutor.ModifiedEbx.ContainsKey(ebx.GetValue<string>("name")))
                                    {
                                        IEbxAssetEntry entry = modExecutor.ModifiedEbx[ebx.GetValue<string>("name")];
                                        byte[] data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        if (entry.ExtraData == null)
                                            entry.ExtraData = new AssetExtraData() { Cas = ebx.GetValue<ushort>("cas"), Catalog = ebx.GetValue<ushort>("catalog"), DataOffset = ebx.GetValue<uint>("offset") };

                                        var casPath = fss.GetCasPath(entry.ExtraData);
                                        if (modifiedCas.ContainsKey(casPath))
                                        {
                                            modifiedCas[casPath].Add(ebx);
                                        }
                                        else
                                        {
                                            modifiedCas[casPath] = new List<DbObject>();
                                            modifiedCas[casPath].Add(ebx);
                                        }
                                    }
                                }

                                foreach (DbObject res in t.Value.GetValue<DbObject>("res"))
                                {
                                    if (modExecutor.ModifiedEbx.ContainsKey(res.GetValue<string>("name")))
                                    {
                                        var entry = modExecutor.ModifiedRes[res.GetValue<string>("name")];
                                        byte[] data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        if (entry.ExtraData == null)
                                            entry.ExtraData = new AssetExtraData() { Cas = res.GetValue<ushort>("cas"), Catalog = res.GetValue<ushort>("catalog"), DataOffset = res.GetValue<uint>("offset") };

                                        var casPath = fss.GetCasPath(entry.ExtraData);
                                        if (modifiedCas.ContainsKey(casPath))
                                        {
                                            modifiedCas[casPath].Add(res);
                                        }
                                        else
                                        {
                                            modifiedCas[casPath] = new List<DbObject>();
                                            modifiedCas[casPath].Add(res);
                                        }
                                    }
                                }

                                foreach (DbObject chunk in t.Value.GetValue<DbObject>("chunks"))
                                {
                                    if (modExecutor.ModifiedChunks.ContainsKey(chunk.GetValue<Guid>("id")))
                                    {
                                        IChunkAssetEntry entry = modExecutor.ModifiedChunks[chunk.GetValue<Guid>("id")];
                                        byte[] data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        if (entry.ExtraData == null)
                                            entry.ExtraData = new AssetExtraData() { Cas = chunk.GetValue<ushort>("cas"), Catalog = chunk.GetValue<ushort>("catalog"), DataOffset = chunk.GetValue<uint>("offset") };

                                        var casPath = fss.GetCasPath(entry.ExtraData);
                                        if (modifiedCas.ContainsKey(casPath))
                                        {
                                            modifiedCas[casPath].Add(chunk);
                                        }
                                        else
                                        {
                                            modifiedCas[casPath] = new List<DbObject>();
                                            modifiedCas[casPath].Add(chunk);
                                        }

                                    }
                                }
                            }
                        }
                    }

                    if (modExecutor.ModifiedEbx.Count == 0 && modExecutor.ModifiedRes.Count == 0 && modExecutor.ModifiedChunks.Count == 0)
                    {
                        throw new Exception("No mods were able to compile");
                    }

                    Dictionary<string, List<DbObject>> newBundleChanges = new();
                    foreach (var modified in modifiedCas)
                    {
                        var casPath = modified.Key;
                        var fullCasPath = fss.ResolvePath(casPath);
                        if (!string.IsNullOrEmpty(fullCasPath) && File.Exists(fullCasPath))
                        {
                            using (var bw = new BinaryWriter(new FileStream(fullCasPath, FileMode.Open, FileAccess.ReadWrite)))
                            {
                                foreach (var obj in modified.Value)
                                {
                                    byte[] data = null;
                                    int originalSize = -1;
                                    IAssetEntry entry = null;
                                    if (obj.HasValue("name"))
                                    {
                                        var name = obj.GetValue<string>("name");
                                        if (modExecutor.ModifiedRes.ContainsKey(name) && obj.HasValue("res"))
                                        {
                                            entry = modExecutor.ModifiedRes[name];
                                            data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        }
                                        else if (modExecutor.ModifiedEbx.ContainsKey(name) && obj.HasValue("ebx"))
                                        {
                                            entry = modExecutor.ModifiedEbx[name];
                                            data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        }
                                    }
                                    else if (obj.HasValue("id"))
                                    {
                                        var id = obj.GetValue<Guid>("id");
                                        if (modExecutor.ModifiedChunks.ContainsKey(id))
                                        {
                                            entry = modExecutor.ModifiedChunks[id];
                                            data = entry.ModifiedEntry != null && entry.ModifiedEntry.Data != null ? entry.ModifiedEntry.Data : null;
                                        }
                                    }

                                    if (entry == null)
                                        continue;

                                    originalSize = (int)entry.OriginalSize;
                                    if (data != null)
                                    {
                                        var newOffset = bw.BaseStream.Length;
                                        bw.BaseStream.Position = bw.BaseStream.Length;
                                        bw.Write(data, 0, data.Length);
                                        if (!newBundleChanges.ContainsKey(obj.GetValue<string>("ParentCASBundleLocation")))
                                            newBundleChanges.Add(obj.GetValue<string>("ParentCASBundleLocation"), new List<DbObject>());

                                        obj.SetValue("offset", newOffset);
                                        obj.SetValue("size", data.Length);
                                        obj.SetValue("originalSize", originalSize);
                                        obj.SetValue("sha1", entry.Sha1);

                                        if (obj.HasValue("resMeta") && entry is ResAssetEntry resAssetEntry)
                                            obj.SetValue("resMeta", resAssetEntry.ResMeta);

                                        if (obj.HasValue("logicalOffset") && entry is ChunkAssetEntry chunkAssetEntry)
                                        {
                                            obj.SetValue("logicalOffset", chunkAssetEntry.LogicalOffset);
                                            obj.SetValue("logicalSize", chunkAssetEntry.LogicalSize);
                                        }

                                        newBundleChanges[obj.GetValue<string>("ParentCASBundleLocation")].Add(obj);

                                        obj.SetValue("ModifiedByFMT", true);
                                    }
                                }
                            }
                        }
                    }

                    foreach (var objL in newBundleChanges.Values)
                    {
                        foreach (var obj in objL)
                        {
                            foreach (var casBundle in modifiedCasBundles)
                            {
                                var casBundleEntry = casBundle.Entries[obj.GetValue<int>("EntryIndex")];
                                casBundleEntry.bundleSizeInCas = (uint)obj.GetValue<uint>("size");
                                casBundleEntry.bundleOffsetInCas = (uint)obj.GetValue<uint>("offset");
                            }
                        }
                    }

                    foreach (var casBundle in modifiedCasBundles)
                    {
                        BundleWriter bundleWriter = new();
                        var bundleObjects = tocFile.TOCObjectsByCasBundle[casBundle];
                        _ = bundleObjects;
                        foreach (var modified in modifiedCas)
                        {
                            foreach (var bundleObj in modified.Value)
                            {

                            }
                        }

                        var casPathRaw = fss.GetFilePath(casBundle.Catalog, casBundle.Cas, casBundle.Patch);
                        var resolvedPathCas = fss.ResolvePath(casPathRaw, false);

#if DEBUG
                        using (var nrCas = new NativeReader(new FileStream(resolvedPathCas, FileMode.Open, FileAccess.Read)))
                        {
                            var entry = casBundle.Entries[0];
                            nrCas.Position = entry.bundleOffsetInCas;
                            var casBytes = nrCas.ReadBytes((int)entry.bundleSizeInCas);
                            DebugBytesToFileLogger.Instance.WriteAllBytes($"Bundle_{casBundle.BaseBundle.NameHash}_Decompressed.bin", casBytes, "Bundles/Read", false);
                        }
#endif

                        using (var nwCasBundle = new NativeWriter(new FileStream(resolvedPathCas, FileMode.Open, FileAccess.Write, FileShare.Write)))
                        {
                            var msNewBundle = new MemoryStream();
                            bundleWriter.Write(msNewBundle, bundleObjects);
                            _ = msNewBundle;
                            nwCasBundle.Position = nwCasBundle.Length;
                            var entry = casBundle.Entries[0];
                            entry.bundleOffsetInCas = (uint)nwCasBundle.Position;
                            nwCasBundle.Write(msNewBundle.ToArray());
                            entry.bundleSizeInCas = (uint)msNewBundle.Length;
                        }
                    }

                    if (modifiedCasBundles.Count > 0)
                    {
                        Madden26TOCFileWriter tOCFileWriter = new Madden26TOCFileWriter();

                        if (tocFile.CasBundles == null)
                            tocFile.CasBundles = new CASBundle[0];

                        tOCFileWriter.Write(tocFile, false);
                    }

                    if (true)
                    {

                    }
                }
            }

            return true;
        }

        public override bool PostCompile(ILogger logger, IModExecutor modExecutor)
        {
            // --------------------------------------------------------------------------------------------------------
            // Apply Anti-Cheat bypass

            var dpApi = EmbeddedResourceHelper.GetEmbeddedResourceByName("dpapi.dll");
            var ac = EmbeddedResourceHelper.GetEmbeddedResourceByName("EAAntiCheat.GameServiceLauncher.exe");

            var msdpapi = new MemoryStream();
            var msAC = new MemoryStream();
            dpApi.CopyTo(msdpapi);
            ac.CopyTo(msAC);

            File.WriteAllBytes(Path.Combine(modExecutor.GamePath, "dpapi.dll"), msdpapi.ToArray());
            File.WriteAllBytes(Path.Combine(modExecutor.GamePath, "EAAntiCheat.GameServiceLauncher.exe"), msAC.ToArray());

            return base.PostCompile(logger, modExecutor);
        }

        public static ulong OodleCompress(byte[] buffer, Oodle.OodleFormat format, Oodle.OodleCompressionLevel compressionLevel, out byte[] compBuffer, out ushort compressCode, out bool uncompressed, int bufferSize = 262144)
        {
            uncompressed = false;
            ArgumentNullException.ThrowIfNull(buffer, "buffer");
            compBuffer = new byte[bufferSize];// ArrayPool<byte>.Shared.Rent(bufferSize);
            compressCode = 6512;
            switch (format)
            {
                case Oodle.OodleFormat.Kraken:
                    compressCode = 4464;
                    break;
                case Oodle.OodleFormat.Leviathan:
                    compressCode = 6512;
                    break;
                case Oodle.OodleFormat.Selkie:
                    compressCode = 5488;
                    break;
            }
            GCHandle gCHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                GCHandle gCHandle2 = GCHandle.Alloc(compBuffer, GCHandleType.Pinned);
                try
                {
                    ulong compressedSize = (ulong)Oodle.CompressWithCompLevel(format, gCHandle.AddrOfPinnedObject(), buffer.Length, gCHandle2.AddrOfPinnedObject(), compressionLevel);//, Oodle.GetOptions(format, compressionLevel));
                    if (compressedSize > (ulong)buffer.Length)
                    {
                        uncompressed = true;
                        compressedSize = 0uL;
                    }

                    return compressedSize;
                }
                finally
                {
                    gCHandle2.Free();
                }
            }
            finally
            {
                gCHandle.Free();
            }
        }

        public static byte[] CompressFile(
            byte[] inData
            , Texture texture = null
            , ResourceType resType = ResourceType.Invalid
            , Oodle.OodleFormat oodleFormat = Oodle.OodleFormat.Kraken
            , Oodle.OodleCompressionLevel compressionLevel = Oodle.OodleCompressionLevel.OPTIMAL3
            , uint offset = 0u
            )
        {
            int maxBufferSize = 262144;
            MemoryStream memoryStream = new();
            FileWriter outputWriter = new(memoryStream);
            FileReader inputReader = new(new MemoryStream(inData));
            long remainingByteCount = inputReader.Length - inputReader.Position;
            long totalBytesRead = 0L;
            long totalBytesWritten = 0L;
            while (remainingByteCount > 0)
            {
                int bufferSize = (int)((remainingByteCount > maxBufferSize) ? maxBufferSize : remainingByteCount);
                byte[] bufferArray = inputReader.ReadBytes(bufferSize);
                ushort compressCode = 0;
                ulong compressedSize = 0uL;
                byte[] compBuffer = null;
                bool pooledBuffer = false;
                try
                {
                    compressedSize = OodleCompress(bufferArray, oodleFormat, compressionLevel, out compBuffer, out compressCode, out _);
                    compressCode |= (ushort)((compressedSize & 0xF0000) >> 16);
                    switch (oodleFormat)
                    {
                        case Oodle.OodleFormat.Kraken:
                            compressCode = 4464;
                            break;
                        case Oodle.OodleFormat.Leviathan:
                            compressCode = 6512;
                            break;
                        case Oodle.OodleFormat.Selkie:
                            compressCode = 5488;
                            break;
                    }
                    outputWriter.WriteInt32BigEndian(bufferSize);
                    outputWriter.WriteUInt16BigEndian(compressCode);
                    outputWriter.WriteUInt16BigEndian((ushort)compressedSize);
                    outputWriter.Write(compBuffer, 0, (int)compressedSize);
                    remainingByteCount -= bufferSize;
                    totalBytesRead += bufferSize;
                    if (texture != null && texture.MipCount > 1)
                    {
                        if (totalBytesRead + offset == texture.MipSizes[0])
                        {
                            uint secondMipOffset = (texture.MipOffsets[2] = (uint)totalBytesWritten);
                            texture.MipOffsets[1] = secondMipOffset;
                        }
                        else if (totalBytesRead + offset == texture.MipSizes[0] + texture.MipSizes[1])
                        {
                            texture.MipOffsets[2] = (uint)totalBytesWritten;
                        }
                    }
                }
                finally
                {
                    if (compBuffer != null && pooledBuffer)
                    {
                        //ArrayPool<byte>.Shared.Return(compBuffer);
                        compBuffer = null;
                    }
                }
            }
            return memoryStream.ToArray();
        }
    }



}
