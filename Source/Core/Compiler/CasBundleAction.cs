using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FMT.Core;
using FMT.Db;
using FMT.FileTools;
using FMT.FileTools.Deobfuscators;
using FMT.FileTools.Readers;
using FMT.FileTools.Writers;
using FMT.Hash;
using FMT.Logging;
using FMT.Models.Assets;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AppInsights;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Compiler;

/// <summary>
/// Migrated CasBundleAction – legacy Frostbite CAS bundle compiler action.
/// Original code used removed fields:
///   - parent.m_modDirName / parent.m_patchPath / parent.m_hasPatchFolder (removed per task)
///   - parent.m_am (now SingletonService.GetInstance&lt;IAssetManagementService&gt;())
///   - parent.m_modifiedBundles (now parent.ModifiedBundles)
///   - parent.ArchiveData (now entry.ModifiedEntry.Data)
///   - IFileSystemService.GetCatalogIndex (removed – catalog now derived from ExtraData)
///   - IResourceAssetEntry.GetResMeta() (now .ResMeta)
///   - ChunkAssetEntry.H64 (removed – only H32 remains)
///   - NativeReader 2-splitTocName ctor (now 1-splitTocName) and BinarySbReader 2-splitTocName ctor (now 5-splitTocName)
///   - EGame.Fifa21 casing (now EGame.FIFA21)
/// See Madden26AssetCompiler2025.cs and Frostbite2025AssetCompiler.cs for modern reference.
/// </summary>
public class SuperBundleInfo
{
    public string Name { get; set; }
    public List<string> SplitSuperBundles { get; set; }

    public SuperBundleInfo(string inName)
    {
        Name = inName;
        SplitSuperBundles = new List<string>();
    }
}

[Flags]
public enum Flags
{
    HasBaseBundles = 1,
    HasBaseChunks = 2,
    HasCompressedNames = 4
}

internal class CasBundleAction
{
    [Flags]
    private enum InternalFlags : byte
    {
        HasCompressedStrings = 1,
        HasInlineSb = 2,
        HasInlineBundle = 4
    }

    private struct CasFileInfo
    {
        public bool IsPatch;
        public int CatalogIndex;
        public byte CasIndex;
        public uint Offset;
        public uint Size;
    }

    private class BundleInfo
    {
        public string Name;
        public uint NameOffset;
        public long Offset;
        public uint Size;
        public bool IsModified;
        public bool IsPatch;
        public string SbName;
        public int SplitIndex;
    }

    private class ChunkInfo
    {
        public Guid Guid;
        public bool IsPatch;
        public CasFileInfo CasFileInfo;
        public string SbName;
        public int SplitIndex;
    }

    private static readonly object locker = new object();

    public SuperBundleInfo SuperBundleInfo;
    public bool HasSb = true;
    private ManualResetEvent doneEvent;
    private IModExecutor parent;
    private string m_catalog;
    public static Dictionary<string, int> CasFiles = new Dictionary<string, int>();
    private bool useM25Strategy;
    private int chunkSbBytesLength;
    private int chunkSbFlag;
    public bool HasErrored => Exception != null;
    public Exception Exception { get; private set; }

    private bool m_hasPatchFolder => Directory.Exists(Path.Combine(SingletonService.GetInstance<IFileSystemService>().BasePath, "Patch"));

    bool UseModData { get; set; } = false;

    public CasBundleAction(SuperBundleInfo inSb, ManualResetEvent inDoneEvent, IModExecutor inParent)
    {
        SuperBundleInfo = inSb;
        parent = inParent;
        doneEvent = inDoneEvent;
        m_catalog = GetCatalog(SuperBundleInfo, SingletonService.GetInstance<IFileSystemService>().Catalogs.AsEnumerable());
        useM25Strategy = true;
        chunkSbBytesLength = (useM25Strategy ? 4 : 3);
        chunkSbFlag = ((!useM25Strategy) ? 1 : 128);
    }

    int GetCatalogIndex(string catalogName)
    {
        var catalog = fss.Catalogs.Find(x => x.Name == catalogName);
        return catalog.PersistentIndex.Value;
    }

    internal void Run()
    {
        try
        {
            NativeWriter activeCasWriter = null;
            int currentCasIndex = 0;
            Dictionary<string, BundleInfo> tocBundlesByName = new Dictionary<string, BundleInfo>(StringComparer.OrdinalIgnoreCase);
            Dictionary<Guid, ChunkInfo> tocChunksByGuid = new Dictionary<Guid, ChunkInfo>();
            InternalFlags internalTocFlags = (InternalFlags)0;
            bool isMainSuperBundleDirty = false;
            bool[] isSplitSuperBundleDirty = new bool[SuperBundleInfo.SplitSuperBundles.Count];
            bool isPatchToc = false;
            var fss = SingletonService.GetInstance<IFileSystemService>();
            string resolvedTocPath =  fss.ResolvePath($"native_data/{SuperBundleInfo.Name}.toc");
            if (!resolvedTocPath.Equals(string.Empty))
            {
                using NativeReader reader = new NativeReader(new FileStream(resolvedTocPath, FileMode.Open, FileAccess.Read));
                ReadToc(reader, ref tocBundlesByName, ref tocChunksByGuid, ref internalTocFlags, isPatchToc);
            }
            for (int i = 0; i < SuperBundleInfo.SplitSuperBundles.Count; i++)
            {
                string splitTocName = SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[i]);
                isPatchToc = true;
                resolvedTocPath = fss.ResolvePath($"native_patch/{splitTocName}.toc");
                if (resolvedTocPath.Equals(string.Empty))
                {
                    resolvedTocPath = fss.ResolvePath($"native_data/{splitTocName}.toc");
                    isPatchToc = false;
                }
                if (!resolvedTocPath.Equals(string.Empty))
                {
                    using NativeReader reader2 = new NativeReader(new FileStream(resolvedTocPath, FileMode.Open, FileAccess.Read));
                    ReadToc(reader2, ref tocBundlesByName, ref tocChunksByGuid, ref internalTocFlags, isPatchToc, i);
                }
            }
            string outputBasePath = UseModData ? Path.Combine(fss.BasePath, "ModData") : fss.BasePath;
            Endian inEndian = Endian.Little;

            BinarySbWriter targetSbWriter = null;
            BinarySbWriter mainSbWriter = new BinarySbWriter(new MemoryStream(), leaveOpen: false, inEndian);
            List<BinarySbWriter> splitSbWriters = null;
            if (SuperBundleInfo.SplitSuperBundles.Count > 0)
            {
                splitSbWriters = new List<BinarySbWriter>();
                foreach (string splitSuperBundle in SuperBundleInfo.SplitSuperBundles)
                {
                    _ = splitSuperBundle;
                    splitSbWriters.Add(new BinarySbWriter(new MemoryStream(), leaveOpen: false, inEndian));
                }
            }
            string cachedPatchSbName = "";
            string cachedBaseSbName = "";
            NativeReader activeTocReader = null;
            NativeReader patchTocReader = null;
            NativeReader baseTocReader = null;
            List<int> newlyCreatedBundleHashes = new List<int>();
            int superBundleHash = Fnv1a.HashStringUTF8(SuperBundleInfo.Name.ToLowerInvariant());
            if (parent.AddedBundles.TryGetValue(superBundleHash, out var addedBundleNames))
            {
                foreach (string newBundleName in addedBundleNames)
                {
                    int bundleNameHash = Fnv1a.HashStringUTF8(newBundleName.ToLower());
                    // MIGRATED: parent.m_modifiedBundles -> parent.ModifiedBundles
                    if (!parent.ModifiedBundles.TryGetValue(bundleNameHash, out var modBundleInfo) || (modBundleInfo.Add.Ebx.Count == 0 && modBundleInfo.Add.Res.Count == 0 && modBundleInfo.Add.Chunks.Count == 0))
                    {
                        continue;
                    }
                    int targetSplitIndex = ((SuperBundleInfo.SplitSuperBundles.Count <= 0) ? (-1) : 0);
                    string targetCatalogName;
                    if (targetSplitIndex != -1)
                    {
                        targetSbWriter = splitSbWriters[targetSplitIndex];
                        isSplitSuperBundleDirty[targetSplitIndex] = true;
                        targetCatalogName = SuperBundleInfo.SplitSuperBundles[targetSplitIndex];
                    }
                    else
                    {
                        targetSbWriter = mainSbWriter;
                        isMainSuperBundleDirty = true;
                        targetCatalogName = m_catalog;
                    }
                    // MIGRATED: GetCatalogIndex removed – use 0
                    int targetCatalogIndex = GetCatalogIndex(targetCatalogName);
                    DbObject bundlePayload = new DbObject();
                    DbObject ebxTable = new DbObject(bObject: false);
                    DbObject resTable = new DbObject(bObject: false);
                    DbObject chunkTable = new DbObject(bObject: false);
                    DbObject chunkMetaTable = new DbObject(bObject: false);
                    foreach (string ebxName in modBundleInfo.Add.Ebx)
                    {
                        if (parent.ModifiedEbx.TryGetValue(ebxName, out var ebxAsset))
                        {
                            byte[] data = ebxAsset.ModifiedEntry?.Data;
                            if (data == null) continue;
                            if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                            {
                                activeCasWriter?.Close();
                                activeCasWriter = GetNextCas(targetCatalogName, out currentCasIndex);
                            }
                            DbObject ebxEntry = new DbObject();
                            ebxEntry.SetValue("name", ebxAsset.Name);
                            ebxEntry.SetValue("sha1", ebxAsset.Sha1.ToByteArray());
                            ebxEntry.SetValue("originalSize", ebxAsset.OriginalSize);
                            ebxEntry.SetValue("size", ebxAsset.Size);
                            ebxEntry.SetValue("catalog", targetCatalogIndex);
                            ebxEntry.SetValue("cas", currentCasIndex);
                            ebxEntry.SetValue("offset", (int)activeCasWriter.Position);
                            activeCasWriter.Write(data);
                            ebxTable.Add(ebxEntry);
                        }
                    }
                    foreach (string resName in modBundleInfo.Add.Res)
                    {
                        if (parent.ModifiedRes.TryGetValue(resName, out var resAsset))
                        {
                            byte[] data = resAsset.ModifiedEntry?.Data;
                            if (data == null) continue;
                            if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                            {
                                activeCasWriter?.Close();
                                activeCasWriter = GetNextCas(targetCatalogName, out currentCasIndex);
                            }
                            DbObject resEntry = new DbObject();
                            resEntry.SetValue("name", resAsset.Name);
                            resEntry.SetValue("sha1", resAsset.Sha1);
                            resEntry.SetValue("originalSize", resAsset.OriginalSize);
                            resEntry.SetValue("size", resAsset.Size);
                            resEntry.SetValue("catalog", targetCatalogIndex);
                            resEntry.SetValue("cas", currentCasIndex);
                            resEntry.SetValue("offset", (int)activeCasWriter.Position);
                            resEntry.SetValue("resRid", (long)resAsset.ResRid);
                            resEntry.SetValue("resMeta", resAsset.ResMeta);
                            resEntry.SetValue("resType", resAsset.ResType);
                            activeCasWriter.Write(data);
                            resTable.Add(resEntry);
                        }
                    }
                    foreach (Guid chunkIdToAdd in modBundleInfo.Add.Chunks)
                    {
                        if (!parent.ModifiedChunks.TryGetValue(chunkIdToAdd, out var chunkAssetToAdd))
                        {
                            continue;
                        }
                        byte[] data = chunkAssetToAdd.ModifiedEntry?.Data;
                        if (data == null) continue;
                        byte[] chunkSlice = data;
                        if (chunkAssetToAdd.LogicalOffset != 0)
                        {
                            chunkSlice = new byte[chunkAssetToAdd.RangeEnd - chunkAssetToAdd.RangeStart];
                            Array.Copy(data, chunkAssetToAdd.RangeStart, chunkSlice, 0L, chunkSlice.Length);
                        }
                        if (activeCasWriter == null || activeCasWriter.Length + chunkSlice.Length > 1073741824)
                        {
                            activeCasWriter?.Close();
                            activeCasWriter = GetNextCas(targetCatalogName, out currentCasIndex);
                        }
                        DbObject chunkEntry = new DbObject();
                        chunkEntry.SetValue("id", chunkIdToAdd);
                        chunkEntry.SetValue("sha1", chunkAssetToAdd.Sha1);
                        chunkEntry.SetValue("originalSize", chunkAssetToAdd.OriginalSize);
                        chunkEntry.SetValue("size", chunkSlice.Length);
                        chunkEntry.SetValue("catalog", targetCatalogIndex);
                        chunkEntry.SetValue("cas", currentCasIndex);
                        chunkEntry.SetValue("offset", (uint)activeCasWriter.Position);
                        chunkEntry.SetValue("logicalOffset", (int)chunkAssetToAdd.LogicalOffset);
                        chunkEntry.SetValue("logicalSize", (int)chunkAssetToAdd.LogicalSize);
                        // patch handling removed (m_hasPatchFolder)
                        activeCasWriter.Write(chunkSlice);
                        chunkTable.Add(chunkEntry);
                        if (chunkAssetToAdd.H32 != 0)
                        {
                            DbObject chunkMetaEntry = new DbObject();
                            SetChunkMetaHash(chunkMetaEntry, chunkAssetToAdd);
                            DbObject chunkMetaInner = new DbObject();
                            if (chunkAssetToAdd.FirstMip != -1)
                            {
                                chunkMetaInner.SetValue("firstMip", chunkAssetToAdd.FirstMip);
                            }
                            chunkMetaEntry.SetValue("meta", chunkMetaInner);
                            chunkMetaTable.Add(chunkMetaEntry);
                        }
                    }
                    if (ebxTable.Count <= 0 && resTable.Count <= 0 && chunkTable.Count <= 0)
                    {
                        continue;
                    }
                    bundlePayload.SetValue("ebx", ebxTable);
                    bundlePayload.SetValue("res", resTable);
                    bundlePayload.SetValue("chunks", chunkTable);
                    bundlePayload.SetValue("chunkMeta", chunkMetaTable);
                    BundleInfo bundleInfo = new BundleInfo
                    {
                        Name = newBundleName,
                        Offset = targetSbWriter.Position,
                        IsModified = true,
                        IsPatch = isPatchToc,
                        SbName = ((targetSplitIndex != -1) ? SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[targetSplitIndex]) : SuperBundleInfo.Name),
                        SplitIndex = targetSplitIndex
                    };
                    if (ProfileManager.IsLoaded(EGame.FIFA21))
                    {
                        internalTocFlags |= InternalFlags.HasInlineBundle;
                    }
                    uint inlinePayloadOffset = 0u;
                    uint inlinePayloadSize = 0u;
                    uint entriesOffset = 0u;
                    uint totalEntryCount = (uint)(bundlePayload.GetValue<DbObject>("ebx").Count + bundlePayload.GetValue<DbObject>("res").Count + bundlePayload.GetValue<DbObject>("chunks").Count) + ((!internalTocFlags.HasFlag(InternalFlags.HasInlineBundle)) ? 1u : 0u);
                    uint headerSize = 0u;
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(3735927486u, Endian.Big);
                    targetSbWriter.Write(0, Endian.Big);
                    targetSbWriter.Write(0, Endian.Big);
                    if (internalTocFlags.HasFlag(InternalFlags.HasInlineBundle))
                    {
                        inlinePayloadOffset = (uint)(targetSbWriter.Position - bundleInfo.Offset);
                        targetSbWriter.Write(bundlePayload, false, 0);
                        inlinePayloadSize = (uint)(targetSbWriter.Position - inlinePayloadOffset);
                    }
                    byte[] entryLocationFlags = new byte[totalEntryCount];
                    byte deltaHeaderByte = 0;
                    bool lastIsPatch = false;
                    int lastCatalogIndex = 0;
                    byte lastCasIndex = 0;
                    int entryCursor = 0;
                    headerSize = (uint)(targetSbWriter.Position - bundleInfo.Offset);
                    if (!internalTocFlags.HasFlag(InternalFlags.HasInlineBundle))
                    {
                        MemoryStream memoryStream = new MemoryStream();
                        using (BinarySbWriter tempPayloadWriter = new BinarySbWriter(memoryStream, leaveOpen: true, inEndian))
                        {
                            tempPayloadWriter.Write(bundlePayload, false, 0);
                        }
                        byte[] inlineBundleBlob = memoryStream.ToArray();
                        memoryStream.Dispose();
                        if (activeCasWriter == null || activeCasWriter.Length + inlineBundleBlob.Length > 1073741824)
                        {
                            activeCasWriter?.Close();
                            activeCasWriter = GetNextCas(targetCatalogName, out currentCasIndex);
                        }
                        entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                        targetSbWriter.Write(deltaHeaderByte);
                        targetSbWriter.Write(lastIsPatch = false); // was parent.m_hasPatchFolder
                        if (useM25Strategy)
                        {
                            targetSbWriter.Write(lastCatalogIndex = targetCatalogIndex, Endian.Big);
                            targetSbWriter.Write((byte)0);
                        }
                        else
                        {
                            targetSbWriter.Write((byte)(lastCatalogIndex = targetCatalogIndex));
                        }
                        targetSbWriter.Write(lastCasIndex = (byte)currentCasIndex);
                        targetSbWriter.Write((uint)activeCasWriter.Position, Endian.Big);
                        targetSbWriter.Write(inlineBundleBlob.Length, Endian.Big);
                        entryCursor++;
                        activeCasWriter.Write(inlineBundleBlob);
                    }
                    foreach (DbObject ebxDescriptor in bundlePayload.GetValue<DbObject>("ebx"))
                    {
                        if (lastIsPatch != ebxDescriptor.HasValue("patch") || lastCatalogIndex != ebxDescriptor.GetValue("catalog", 0) || lastCasIndex != ebxDescriptor.GetValue("cas", (byte)0))
                        {
                            entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                            targetSbWriter.Write(deltaHeaderByte);
                            targetSbWriter.Write(lastIsPatch = ebxDescriptor.HasValue("patch"));
                            if (useM25Strategy)
                            {
                                targetSbWriter.Write(lastCatalogIndex = ebxDescriptor.GetValue("catalog", 0), Endian.Big);
                                targetSbWriter.Write((byte)0);
                            }
                            else
                            {
                                targetSbWriter.Write((byte)(lastCatalogIndex = ebxDescriptor.GetValue("catalog", (byte)0)));
                            }
                            targetSbWriter.Write(lastCasIndex = ebxDescriptor.GetValue("cas", (byte)0));
                        }
                        targetSbWriter.Write(ebxDescriptor.GetValue("offset", 0), Endian.Big);
                        targetSbWriter.Write(ebxDescriptor.GetValue("size", 0), Endian.Big);
                        entryCursor++;
                    }
                    foreach (DbObject resDescriptor in bundlePayload.GetValue<DbObject>("res"))
                    {
                        if (lastIsPatch != resDescriptor.HasValue("patch") || lastCatalogIndex != resDescriptor.GetValue("catalog", 0) || lastCasIndex != resDescriptor.GetValue("cas", (byte)0))
                        {
                            entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                            targetSbWriter.Write(deltaHeaderByte);
                            targetSbWriter.Write(lastIsPatch = resDescriptor.HasValue("patch"));
                            if (useM25Strategy)
                            {
                                targetSbWriter.Write(lastCatalogIndex = resDescriptor.GetValue("catalog", 0), Endian.Big);
                                targetSbWriter.Write((byte)0);
                            }
                            else
                            {
                                targetSbWriter.Write((byte)(lastCatalogIndex = resDescriptor.GetValue("catalog", (byte)0)));
                            }
                            targetSbWriter.Write(lastCasIndex = resDescriptor.GetValue("cas", (byte)0));
                        }
                        targetSbWriter.Write(resDescriptor.GetValue("offset", 0), Endian.Big);
                        targetSbWriter.Write(resDescriptor.GetValue("size", 0), Endian.Big);
                        entryCursor++;
                    }
                    foreach (DbObject chunkDescriptor in bundlePayload.GetValue<DbObject>("chunks"))
                    {
                        if (lastIsPatch != chunkDescriptor.HasValue("patch") || lastCatalogIndex != chunkDescriptor.GetValue("catalog", 0) || lastCasIndex != chunkDescriptor.GetValue("cas", (byte)0))
                        {
                            entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                            targetSbWriter.Write(deltaHeaderByte);
                            targetSbWriter.Write(lastIsPatch = chunkDescriptor.HasValue("patch"));
                            if (useM25Strategy)
                            {
                                targetSbWriter.Write(lastCatalogIndex = chunkDescriptor.GetValue("catalog", 0), Endian.Big);
                                targetSbWriter.Write((byte)0);
                            }
                            else
                            {
                                targetSbWriter.Write((byte)(lastCatalogIndex = chunkDescriptor.GetValue("catalog", (byte)0)));
                            }
                            targetSbWriter.Write(lastCasIndex = chunkDescriptor.GetValue("cas", (byte)0));
                        }
                        targetSbWriter.Write(chunkDescriptor.GetValue("offset", 0), Endian.Big);
                        targetSbWriter.Write(chunkDescriptor.GetValue("size", 0), Endian.Big);
                        entryCursor++;
                    }
                    entriesOffset = (uint)(targetSbWriter.Position - bundleInfo.Offset);
                    ApplyNewBundleLocationFlags(entryLocationFlags);
                    targetSbWriter.Write(entryLocationFlags);
                    uint size = (uint)(targetSbWriter.Position - bundleInfo.Offset);
                    targetSbWriter.WritePadding(4);
                    targetSbWriter.Position = bundleInfo.Offset;
                    targetSbWriter.Write(inlinePayloadOffset, Endian.Big);
                    targetSbWriter.Write(inlinePayloadSize, Endian.Big);
                    targetSbWriter.Write(entriesOffset, Endian.Big);
                    targetSbWriter.Write(totalEntryCount, Endian.Big);
                    targetSbWriter.Write(headerSize, Endian.Big);
                    targetSbWriter.Write(headerSize, Endian.Big);
                    targetSbWriter.Write(headerSize, Endian.Big);
                    targetSbWriter.Position += 4L;
                    if (ProfileManager.IsLoaded(EGame.MADDEN26, EGame.CFB27, EGame.MADDEN27))
                    {
                        targetSbWriter.Write(totalEntryCount, Endian.Big);
                        targetSbWriter.Position -= 4L;
                    }
                    targetSbWriter.Position = targetSbWriter.Length;
                    bundleInfo.Size = size;
                    int bundleHash = Fnv1.HashString(newBundleName.ToLower());
                    tocBundlesByName.Add(newBundleName, bundleInfo);
                    newlyCreatedBundleHashes.Add(bundleHash);
                }
            }
            Dictionary<int, List<BundleInfo>> bundlesByHash = new Dictionary<int, List<BundleInfo>>();
            foreach (BundleInfo existingBundleInfo in tocBundlesByName.Values)
            {
                int bundleHashKey = Fnv1.HashString(existingBundleInfo.Name.ToLower());
                if (!bundlesByHash.TryGetValue(bundleHashKey, out var bundleListForHash))
                {
                    bundlesByHash.Add(bundleHashKey, bundleListForHash = new List<BundleInfo>());
                }
                bundleListForHash.Add(existingBundleInfo);
            }
            List<KeyValuePair<int, BundleInfo>> bundlesToPatch = new List<KeyValuePair<int, BundleInfo>>();
            foreach (int modifiedBundleHash in parent.ModifiedBundles.Keys)
            {
                if (!bundlesByHash.TryGetValue(modifiedBundleHash, out var existingBundlesForHash) || newlyCreatedBundleHashes.Contains(modifiedBundleHash))
                {
                    continue;
                }
                foreach (BundleInfo existingBundleForPatch in existingBundlesForHash)
                {
                    bundlesToPatch.Add(new KeyValuePair<int, BundleInfo>(modifiedBundleHash, existingBundleForPatch));
                }
            }
            foreach (KeyValuePair<int, BundleInfo> bundleToPatch in bundlesToPatch)
            {
                int patchedBundleHash = bundleToPatch.Key;
                BundleInfo existingBundleInfoForPatch = bundleToPatch.Value;
                internalTocFlags &= ~InternalFlags.HasInlineBundle;
                internalTocFlags &= ~InternalFlags.HasInlineSb;
                if ((existingBundleInfoForPatch.Size & 0xC0000000u) == 1073741824)
                {
                    internalTocFlags |= InternalFlags.HasInlineSb;
                    HasSb = false;
                    existingBundleInfoForPatch.Size &= 1073741823u;
                    existingBundleInfoForPatch.Offset += 556L;
                }
                if (existingBundleInfoForPatch.IsPatch)
                {
                    if (patchTocReader == null || cachedPatchSbName != existingBundleInfoForPatch.SbName)
                    {
                        cachedPatchSbName = existingBundleInfoForPatch.SbName;
                        patchTocReader?.Dispose();
                        patchTocReader = ((!internalTocFlags.HasFlag(InternalFlags.HasInlineSb)) ? new NativeReader(new FileStream(fss.ResolvePath($"native_patch/{cachedPatchSbName}.sb"), FileMode.Open, FileAccess.Read)) : new NativeReader(new FileStream(fss.ResolvePath($"native_patch/{cachedPatchSbName}.toc"), FileMode.Open, FileAccess.Read)));
                    }
                    activeTocReader = patchTocReader;
                }
                else
                {
                    if (baseTocReader == null || cachedBaseSbName != existingBundleInfoForPatch.SbName)
                    {
                        cachedBaseSbName = existingBundleInfoForPatch.SbName;
                        baseTocReader?.Dispose();
                        baseTocReader = ((!internalTocFlags.HasFlag(InternalFlags.HasInlineSb)) ? new NativeReader(new FileStream(fss.ResolvePath($"native_data/{cachedBaseSbName}.sb"), FileMode.Open, FileAccess.Read)) : new NativeReader(new FileStream(fss.ResolvePath($"native_data/{cachedBaseSbName}.toc"), FileMode.Open, FileAccess.Read)));
                    }
                    activeTocReader = baseTocReader;
                }
                string modificationCatalog;
                if (existingBundleInfoForPatch.SplitIndex != -1)
                {
                    targetSbWriter = splitSbWriters[existingBundleInfoForPatch.SplitIndex];
                    isSplitSuperBundleDirty[existingBundleInfoForPatch.SplitIndex] = true;
                    modificationCatalog = SuperBundleInfo.SplitSuperBundles[existingBundleInfoForPatch.SplitIndex];
                }
                else
                {
                    targetSbWriter = mainSbWriter;
                    isMainSuperBundleDirty = true;
                    modificationCatalog = m_catalog;
                }
                int patchCatalogIndex = GetCatalogIndex(modificationCatalog);
                Stream stream = activeTocReader.CreateViewStream(existingBundleInfoForPatch.Offset, existingBundleInfoForPatch.Size);
                DbObject patchedBundlePayload;
                //using (BinarySbReader reader3 = new BinarySbReader(stream, 0, fss.CreateDeobfuscator(), 0, null))
                using (var reader3 = new NativeReader(stream))
                {
                    patchedBundlePayload = ReadBundle(reader3, ref internalTocFlags);
                }
                stream.Dispose();
                ModBundleInfo modBundleInfo = parent.ModifiedBundles[patchedBundleHash];
                foreach (DbObject ebx in patchedBundlePayload.GetValue<DbObject>("ebx"))
                {
                    int ebxModifyIndex = modBundleInfo.Modify.Ebx.FindIndex((string a) => a.Equals(ebx.GetValue<string>("name")));
                    if (ebxModifyIndex != -1)
                    {
                        IEbxAssetEntry ebxAssetEntry = parent.ModifiedEbx[modBundleInfo.Modify.Ebx[ebxModifyIndex]];
                        byte[] data = ebxAssetEntry.ModifiedEntry?.Data;
                        if (data == null) continue;
                        if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                        {
                            activeCasWriter?.Close();
                            activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                        }
                        ebx.SetValue("sha1", ebxAssetEntry.Sha1.ToByteArray());
                        ebx.SetValue("originalSize", ebxAssetEntry.OriginalSize);
                        ebx.SetValue("size", ebxAssetEntry.Size);
                        ebx.SetValue("catalog", patchCatalogIndex);
                        ebx.SetValue("cas", currentCasIndex);
                        ebx.SetValue("offset", (int)activeCasWriter.Position);
                        activeCasWriter.Write(data);
                    }
                }
                foreach (string ebxNameToAdd in modBundleInfo.Add.Ebx)
                {
                    IEbxAssetEntry ebxAssetEntry2 = parent.ModifiedEbx[ebxNameToAdd];
                    byte[] data = ebxAssetEntry2.ModifiedEntry?.Data;
                    if (data == null) continue;
                    if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                    {
                        activeCasWriter?.Close();
                        activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                    }
                    DbObject newEbxEntry = new DbObject();
                    newEbxEntry.SetValue("name", ebxAssetEntry2.Name);
                    newEbxEntry.SetValue("sha1", ebxAssetEntry2.Sha1.ToByteArray());
                    newEbxEntry.SetValue("originalSize", ebxAssetEntry2.OriginalSize);
                    newEbxEntry.SetValue("size", ebxAssetEntry2.Size);
                    newEbxEntry.SetValue("catalog", patchCatalogIndex);
                    newEbxEntry.SetValue("cas", currentCasIndex);
                    newEbxEntry.SetValue("offset", (int)activeCasWriter.Position);
                    patchedBundlePayload.GetValue<DbObject>("ebx").Add(newEbxEntry);
                    activeCasWriter.Write(data);
                }
                foreach (DbObject res in patchedBundlePayload.GetValue<DbObject>("res"))
                {
                    int resModifyIndex = modBundleInfo.Modify.Res.FindIndex((string a) => a.Equals(res.GetValue<string>("name")));
                    if (resModifyIndex != -1)
                    {
                        IResourceAssetEntry resAssetEntry = parent.ModifiedRes[modBundleInfo.Modify.Res[resModifyIndex]];
                        byte[] data = resAssetEntry.ModifiedEntry?.Data;
                        if (data == null) continue;
                        if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                        {
                            activeCasWriter?.Close();
                            activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                        }
                        res.SetValue("sha1", resAssetEntry.Sha1.ToByteArray());
                        res.SetValue("originalSize", resAssetEntry.OriginalSize);
                        res.SetValue("size", resAssetEntry.Size);
                        res.SetValue("catalog", patchCatalogIndex);
                        res.SetValue("cas", currentCasIndex);
                        res.SetValue("offset", (int)activeCasWriter.Position);
                        res.SetValue("resRid", (long)resAssetEntry.ResRid);
                        res.SetValue("resMeta", resAssetEntry.ResMeta);
                        res.SetValue("resType", resAssetEntry.ResType);
                        activeCasWriter.Write(data);
                    }
                }
                foreach (string re2 in modBundleInfo.Add.Res)
                {
                    IResourceAssetEntry resAssetEntry2 = parent.ModifiedRes[re2];
                    byte[] data = resAssetEntry2.ModifiedEntry?.Data;
                    if (data == null) continue;
                    if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                    {
                        activeCasWriter?.Close();
                        activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                    }
                    DbObject newResEntry = new DbObject();
                    newResEntry.SetValue("name", resAssetEntry2.Name);
                    newResEntry.SetValue("sha1", resAssetEntry2.Sha1.ToByteArray());
                    newResEntry.SetValue("originalSize", resAssetEntry2.OriginalSize);
                    newResEntry.SetValue("size", resAssetEntry2.Size);
                    newResEntry.SetValue("catalog", patchCatalogIndex);
                    newResEntry.SetValue("cas", currentCasIndex);
                    newResEntry.SetValue("offset", (int)activeCasWriter.Position);
                    newResEntry.SetValue("resRid", (long)resAssetEntry2.ResRid);
                    newResEntry.SetValue("resMeta", resAssetEntry2.ResMeta);
                    newResEntry.SetValue("resType", resAssetEntry2.ResType);
                    patchedBundlePayload.GetValue<DbObject>("res").Add(newResEntry);
                    activeCasWriter.Write(data);
                }
                DbObject chunkMetaTableRef = patchedBundlePayload.GetValue<DbObject>("chunkMeta");
                int chunkLoopIndex = 0;
                List<(int, long)> chunksToRemove = new List<(int, long)>();
                foreach (DbObject chunk in patchedBundlePayload.GetValue<DbObject>("chunks"))
                {
                    int chunkIndex = modBundleInfo.Remove.Chunks.FindIndex((Guid a) => a == chunk.GetValue<Guid>("id"));
                    if (chunkIndex != -1)
                    {
                        chunksToRemove.Add((chunkLoopIndex, GetChunkMetaHash(parent.ModifiedChunks[modBundleInfo.Remove.Chunks[chunkIndex]])));
                    }
                    else
                    {
                        chunkIndex = modBundleInfo.Modify.Chunks.FindIndex((Guid a) => a == chunk.GetValue<Guid>("id"));
                        if (chunkIndex != -1)
                        {
                            IChunkAssetEntry chunkAssetEntry = parent.ModifiedChunks[modBundleInfo.Modify.Chunks[chunkIndex]];
                            byte[] data = chunkAssetEntry.ModifiedEntry?.Data;
                            if (data == null) { chunkLoopIndex++; continue; }
                            if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                            {
                                activeCasWriter?.Close();
                                activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                            }
                            DbObject existingChunkMeta = FindChunkMeta(chunkMetaTableRef, chunkAssetEntry);
                            byte[] chunkData = data;
                            if (chunkAssetEntry.LogicalOffset != 0)
                            {
                                chunkData = new byte[chunkAssetEntry.RangeEnd - chunkAssetEntry.RangeStart];
                                Array.Copy(data, chunkAssetEntry.RangeStart, chunkData, 0L, chunkData.Length);
                            }
                            chunk.SetValue("sha1", chunkAssetEntry.Sha1.ToByteArray());
                            chunk.SetValue("originalSize", chunkAssetEntry.OriginalSize);
                            chunk.SetValue("size", chunkData.Length);
                            chunk.SetValue("catalog", patchCatalogIndex);
                            chunk.SetValue("cas", currentCasIndex);
                            chunk.SetValue("offset", (uint)activeCasWriter.Position);
                            chunk.SetValue("logicalOffset", (int)chunkAssetEntry.LogicalOffset);
                            chunk.SetValue("logicalSize", (int)chunkAssetEntry.LogicalSize);
                            if (chunkAssetEntry.FirstMip != -1)
                            {
                                existingChunkMeta?.GetValue<DbObject>("meta").SetValue("firstMip", chunkAssetEntry.FirstMip);
                            }
                            activeCasWriter.Write(chunkData);
                        }
                    }
                    chunkLoopIndex++;
                }
                chunksToRemove.Reverse();
                foreach (var chunkRemovalEntry in chunksToRemove)
                {
                    patchedBundlePayload.GetValue<DbObject>("chunks").RemoveAt(chunkRemovalEntry.Item1);
                    int chunkMetaIndex = FindChunkMetaIndex(chunkMetaTableRef, chunkRemovalEntry.Item2);
                    if (chunkMetaIndex != -1)
                    {
                        patchedBundlePayload.GetValue<DbObject>("chunkMeta").RemoveAt(chunkMetaIndex);
                    }
                }
                foreach (Guid chunk3 in modBundleInfo.Add.Chunks)
                {
                    IChunkAssetEntry chunkAssetEntry2 = parent.ModifiedChunks[chunk3];
                    byte[] data = chunkAssetEntry2.ModifiedEntry?.Data;
                    if (data == null) continue;
                    if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                    {
                        activeCasWriter?.Close();
                        activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                    }
                    byte[] chunkSlice = data;
                    if (chunkAssetEntry2.LogicalOffset != 0)
                    {
                        chunkSlice = new byte[chunkAssetEntry2.RangeEnd - chunkAssetEntry2.RangeStart];
                        Array.Copy(data, chunkAssetEntry2.RangeStart, chunkSlice, 0L, chunkSlice.Length);
                    }
                    DbObject newChunkEntry = new DbObject();
                    newChunkEntry.SetValue("id", chunk3);
                    newChunkEntry.SetValue("sha1", chunkAssetEntry2.Sha1.ToByteArray());
                    newChunkEntry.SetValue("originalSize", chunkAssetEntry2.OriginalSize);
                    newChunkEntry.SetValue("size", chunkSlice.Length);
                    newChunkEntry.SetValue("catalog", patchCatalogIndex);
                    newChunkEntry.SetValue("cas", currentCasIndex);
                    newChunkEntry.SetValue("offset", (uint)activeCasWriter.Position);
                    newChunkEntry.SetValue("logicalOffset", (int)chunkAssetEntry2.LogicalOffset);
                    newChunkEntry.SetValue("logicalSize", (int)chunkAssetEntry2.LogicalSize);
                    DbObject newChunkMetaEntry = new DbObject();
                    SetChunkMetaHash(newChunkMetaEntry, chunkAssetEntry2);
                    newChunkMetaEntry.SetValue("meta", new DbObject());
                    if (chunkMetaTableRef == null)
                    {
                        patchedBundlePayload.SetValue("chunkMeta", new DbObject());
                        chunkMetaTableRef = patchedBundlePayload.GetValue<DbObject>("chunkMeta");
                    }
                    chunkMetaTableRef.Add(newChunkMetaEntry);
                    if (chunkAssetEntry2.FirstMip != -1)
                    {
                        newChunkMetaEntry?.GetValue<DbObject>("meta").SetValue("firstMip", chunkAssetEntry2.FirstMip);
                    }
                    activeCasWriter.Write(chunkSlice);
                    patchedBundlePayload.GetValue<DbObject>("chunks").Add(newChunkEntry);
                }
                existingBundleInfoForPatch.Offset = targetSbWriter.Position;
                existingBundleInfoForPatch.IsModified = true;
                existingBundleInfoForPatch.IsPatch = true;
              
                uint inlinePayloadOffset = 0u;
                uint inlinePayloadSize = 0u;
                uint entriesOffset = 0u;
                uint totalEntryCount = (uint)(patchedBundlePayload.GetValue<DbObject>("ebx").Count + patchedBundlePayload.GetValue<DbObject>("res").Count + patchedBundlePayload.GetValue<DbObject>("chunks").Count) + ((!internalTocFlags.HasFlag(InternalFlags.HasInlineBundle)) ? 1u : 0u);
                uint headerSize = 0u;
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(3735927486u, Endian.Big);
                targetSbWriter.Write(0, Endian.Big);
                targetSbWriter.Write(0, Endian.Big);
                if (internalTocFlags.HasFlag(InternalFlags.HasInlineBundle))
                {
                    inlinePayloadOffset = (uint)(targetSbWriter.Position - existingBundleInfoForPatch.Offset);
                    targetSbWriter.Write(patchedBundlePayload, false, 0);
                    inlinePayloadSize = (uint)(targetSbWriter.Position - inlinePayloadOffset);
                }
                byte[] entryLocationFlags = new byte[totalEntryCount];
                byte deltaHeaderByte = 0;
                bool lastIsPatch = false;
                int lastCatalogIndex = 0;
                byte lastCasIndex = 0;
                int entryCursor = 0;
                headerSize = (uint)(targetSbWriter.Position - existingBundleInfoForPatch.Offset);
                if (!internalTocFlags.HasFlag(InternalFlags.HasInlineBundle))
                {
                    MemoryStream memoryStream2 = new MemoryStream();
                    using (BinarySbWriter tempPayloadWriter2 = new BinarySbWriter(memoryStream2, leaveOpen: true, inEndian))
                    {
                        tempPayloadWriter2.Write(patchedBundlePayload, false, 0);
                    }
                    byte[] inlineBundleBlob = memoryStream2.ToArray();
                    memoryStream2.Dispose();
                    if (activeCasWriter == null || activeCasWriter.Length + inlineBundleBlob.Length > 1073741824)
                    {
                        activeCasWriter?.Close();
                        activeCasWriter = GetNextCas(modificationCatalog, out currentCasIndex);
                    }
                    entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                    targetSbWriter.Write(deltaHeaderByte);
                    targetSbWriter.Write(lastIsPatch = false);
                    if (useM25Strategy)
                    {
                        targetSbWriter.Write(lastCatalogIndex = patchCatalogIndex, Endian.Big);
                        targetSbWriter.Write((byte)0);
                    }
                    else
                    {
                        targetSbWriter.Write((byte)(lastCatalogIndex = patchCatalogIndex));
                    }
                    targetSbWriter.Write(lastCasIndex = (byte)currentCasIndex);
                    targetSbWriter.Write((uint)activeCasWriter.Position, Endian.Big);
                    targetSbWriter.Write(inlineBundleBlob.Length, Endian.Big);
                    entryCursor++;
                    activeCasWriter.Write(inlineBundleBlob);
                }
                foreach (DbObject ebxDescriptorForPatch in patchedBundlePayload.GetValue<DbObject>("ebx"))
                {
                    if (lastIsPatch != ebxDescriptorForPatch.HasValue("patch") || lastCatalogIndex != ebxDescriptorForPatch.GetValue("catalog", 0) || lastCasIndex != ebxDescriptorForPatch.GetValue("cas", (byte)0))
                    {
                        entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                        targetSbWriter.Write(deltaHeaderByte);
                        targetSbWriter.Write(lastIsPatch = ebxDescriptorForPatch.HasValue("patch"));
                        if (useM25Strategy)
                        {
                            targetSbWriter.Write(lastCatalogIndex = ebxDescriptorForPatch.GetValue("catalog", 0), Endian.Big);
                            targetSbWriter.Write((byte)0);
                        }
                        else
                        {
                            targetSbWriter.Write((byte)(lastCatalogIndex = ebxDescriptorForPatch.GetValue("catalog", (byte)0)));
                        }
                        targetSbWriter.Write(lastCasIndex = ebxDescriptorForPatch.GetValue("cas", (byte)0));
                    }
                    targetSbWriter.Write(ebxDescriptorForPatch.GetValue("offset", 0), Endian.Big);
                    targetSbWriter.Write(ebxDescriptorForPatch.GetValue("size", 0), Endian.Big);
                    entryCursor++;
                }
                foreach (DbObject resDescriptorForPatch in patchedBundlePayload.GetValue<DbObject>("res"))
                {
                    if (lastIsPatch != resDescriptorForPatch.HasValue("patch") || lastCatalogIndex != resDescriptorForPatch.GetValue("catalog", 0) || lastCasIndex != resDescriptorForPatch.GetValue("cas", (byte)0))
                    {
                        entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                        targetSbWriter.Write(deltaHeaderByte);
                        targetSbWriter.Write(lastIsPatch = resDescriptorForPatch.HasValue("patch"));
                        if (useM25Strategy)
                        {
                            targetSbWriter.Write(lastCatalogIndex = resDescriptorForPatch.GetValue("catalog", 0), Endian.Big);
                            targetSbWriter.Write((byte)0);
                        }
                        else
                        {
                            targetSbWriter.Write((byte)(lastCatalogIndex = resDescriptorForPatch.GetValue("catalog", (byte)0)));
                        }
                        targetSbWriter.Write(lastCasIndex = resDescriptorForPatch.GetValue("cas", (byte)0));
                    }
                    targetSbWriter.Write(resDescriptorForPatch.GetValue("offset", 0), Endian.Big);
                    targetSbWriter.Write(resDescriptorForPatch.GetValue("size", 0), Endian.Big);
                    entryCursor++;
                }
                foreach (DbObject chunkDescriptorForPatch in patchedBundlePayload.GetValue<DbObject>("chunks"))
                {
                    if (lastIsPatch != chunkDescriptorForPatch.HasValue("patch") || lastCatalogIndex != chunkDescriptorForPatch.GetValue("catalog", 0) || lastCasIndex != chunkDescriptorForPatch.GetValue("cas", (byte)0))
                    {
                        entryLocationFlags[entryCursor] = (byte)((!useM25Strategy) ? 1u : 128u);
                        targetSbWriter.Write(deltaHeaderByte);
                        targetSbWriter.Write(lastIsPatch = chunkDescriptorForPatch.HasValue("patch"));
                        if (useM25Strategy)
                        {
                            targetSbWriter.Write(lastCatalogIndex = chunkDescriptorForPatch.GetValue("catalog", 0), Endian.Big);
                            targetSbWriter.Write((byte)0);
                        }
                        else
                        {
                            targetSbWriter.Write((byte)(lastCatalogIndex = chunkDescriptorForPatch.GetValue("catalog", (byte)0)));
                        }
                        targetSbWriter.Write(lastCasIndex = chunkDescriptorForPatch.GetValue("cas", (byte)0));
                    }
                    targetSbWriter.Write(chunkDescriptorForPatch.GetValue("offset", 0), Endian.Big);
                    targetSbWriter.Write(chunkDescriptorForPatch.GetValue("size", 0), Endian.Big);
                    entryCursor++;
                }
                entriesOffset = (uint)(targetSbWriter.Position - existingBundleInfoForPatch.Offset);
                ApplyNewBundleLocationFlags(entryLocationFlags);
                targetSbWriter.Write(entryLocationFlags);
                uint size2 = (uint)(targetSbWriter.Position - existingBundleInfoForPatch.Offset);
                targetSbWriter.WritePadding(4);
                targetSbWriter.Position = existingBundleInfoForPatch.Offset;
                targetSbWriter.Write(inlinePayloadOffset, Endian.Big);
                targetSbWriter.Write(inlinePayloadSize, Endian.Big);
                targetSbWriter.Write(entriesOffset, Endian.Big);
                targetSbWriter.Write(totalEntryCount, Endian.Big);
                targetSbWriter.Write(headerSize, Endian.Big);
                targetSbWriter.Write(headerSize, Endian.Big);
                targetSbWriter.Write(headerSize, Endian.Big);
                //targetSbWriter.Position += 4L;
                //if (ProfileManager.IsLoaded(EGame.MADDEN26, EGame.CFB27, EGame.MADDEN27))
                {
                    targetSbWriter.Write(totalEntryCount, Endian.Big);
                    //targetSbWriter.Position -= 4L;
                }
                targetSbWriter.Position = targetSbWriter.Length;
                existingBundleInfoForPatch.Size = size2;
            }
            // MIGRATED: parent.m_am -> SingletonService.GetInstance<IAssetManagementService>()
            int superBundleId = SingletonService.GetInstance<IAssetManagementService>().GetSuperBundleId(SuperBundleInfo.Name);
            if (parent.ModifiedSuperBundles.ContainsKey(superBundleId)
                || parent.ModifiedChunks.Any(x => x.Value.Bundles.Contains(superBundleId))
                )
            {
                ModBundleInfo modBundleInfo2 = parent.ModifiedSuperBundles[superBundleId];
                foreach (Guid chunk4 in modBundleInfo2.Modify.Chunks)
                {
                    if (tocChunksByGuid.ContainsKey(chunk4))
                    {
                        ChunkInfo chunkInfo = tocChunksByGuid[chunk4];
                        IChunkAssetEntry chunkAssetEntry3 = parent.ModifiedChunks[chunk4];
                        byte[] data = chunkAssetEntry3.ModifiedEntry?.Data;
                        if (data == null) continue;
                        string chunkTargetCatalog;
                        if (chunkInfo.SplitIndex != -1)
                        {
                            isSplitSuperBundleDirty[chunkInfo.SplitIndex] = true;
                            chunkTargetCatalog = SuperBundleInfo.SplitSuperBundles[chunkInfo.SplitIndex];
                        }
                        else
                        {
                            isMainSuperBundleDirty = true;
                            chunkTargetCatalog = m_catalog;
                        }
                        if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                        {
                            activeCasWriter?.Close();
                            activeCasWriter = GetNextCas(chunkTargetCatalog, out currentCasIndex);
                        }
                        chunkInfo.IsPatch = true;
                        chunkInfo.CasFileInfo.IsPatch = false;
                        chunkInfo.CasFileInfo.CatalogIndex = GetCatalogIndex(chunkTargetCatalog);
                        chunkInfo.CasFileInfo.CasIndex = (byte)currentCasIndex;
                        chunkInfo.CasFileInfo.Offset = (uint)activeCasWriter.Position;
                        chunkInfo.CasFileInfo.Size = (uint)data.Length;
                        activeCasWriter.Write(data);
                        continue;
                    }
                    throw new Exception($"tried to modify chunk {chunk4} in {SuperBundleInfo.Name}, but chunk is not in there");
                }
                foreach (Guid chunk5 in modBundleInfo2.Add.Chunks)
                {
                    ChunkInfo chunkInfo2 = new ChunkInfo
                    {
                        Guid = chunk5,
                        SplitIndex = ((SuperBundleInfo.SplitSuperBundles.Count <= 0) ? (-1) : 0),
                        SbName = SuperBundleInfo.Name
                    };
                    tocChunksByGuid.Add(chunk5, chunkInfo2);
                    IChunkAssetEntry chunkAssetEntry4 = parent.ModifiedChunks[chunk5];
                    byte[] data = chunkAssetEntry4.ModifiedEntry?.Data;
                    if (data == null) continue;
                    string newChunkTargetCatalog;
                    if (chunkInfo2.SplitIndex != -1)
                    {
                        isSplitSuperBundleDirty[chunkInfo2.SplitIndex] = true;
                        newChunkTargetCatalog = SuperBundleInfo.SplitSuperBundles[chunkInfo2.SplitIndex];
                    }
                    else
                    {
                        isMainSuperBundleDirty = true;
                        newChunkTargetCatalog = m_catalog;
                    }
                    if (activeCasWriter == null || activeCasWriter.Length + data.Length > 1073741824)
                    {
                        activeCasWriter?.Close();
                        activeCasWriter = GetNextCas(newChunkTargetCatalog, out currentCasIndex);
                    }
                    chunkInfo2.IsPatch = true;
                    chunkInfo2.CasFileInfo.IsPatch = false;
                    chunkInfo2.CasFileInfo.CatalogIndex = GetCatalogIndex(newChunkTargetCatalog);
                    chunkInfo2.CasFileInfo.CasIndex = (byte)currentCasIndex;
                    chunkInfo2.CasFileInfo.Offset = (uint)activeCasWriter.Position;
                    chunkInfo2.CasFileInfo.Size = (uint)data.Length;
                    activeCasWriter.Write(data);
                }
            }
            if (isMainSuperBundleDirty || isSplitSuperBundleDirty.FirstOrDefault((bool result) => result))
            {
                foreach (BundleInfo unmodifiedBundleInfo in tocBundlesByName.Values)
                {
                    if (unmodifiedBundleInfo.IsModified)
                    {
                        continue;
                    }
                    internalTocFlags &= ~InternalFlags.HasInlineSb;
                    if ((unmodifiedBundleInfo.Size & 0xC0000000u) == 1073741824)
                    {
                        internalTocFlags |= InternalFlags.HasInlineSb;
                        unmodifiedBundleInfo.Size &= 1073741823u;
                        unmodifiedBundleInfo.Offset += 556L;
                    }
                    if (unmodifiedBundleInfo.IsPatch)
                    {
                        if (patchTocReader == null || cachedPatchSbName != unmodifiedBundleInfo.SbName)
                        {
                            cachedPatchSbName = unmodifiedBundleInfo.SbName;
                            patchTocReader?.Dispose();
                            patchTocReader = ((!internalTocFlags.HasFlag(InternalFlags.HasInlineSb)) ? new NativeReader(new FileStream(fss.ResolvePath($"native_patch/{cachedPatchSbName}.sb"), FileMode.Open, FileAccess.Read)) : new NativeReader(new FileStream(fss.ResolvePath($"native_patch/{cachedPatchSbName}.toc"), FileMode.Open, FileAccess.Read)));
                        }
                        activeTocReader = patchTocReader;
                    }
                    else
                    {
                        if (baseTocReader == null || cachedBaseSbName != unmodifiedBundleInfo.SbName)
                        {
                            cachedBaseSbName = unmodifiedBundleInfo.SbName;
                            baseTocReader?.Dispose();
                            baseTocReader = ((!internalTocFlags.HasFlag(InternalFlags.HasInlineSb)) ? new NativeReader(new FileStream(fss.ResolvePath($"native_data/{cachedBaseSbName}.sb"), FileMode.Open, FileAccess.Read)) : new NativeReader(new FileStream(fss.ResolvePath($"native_data/{cachedBaseSbName}.toc"), FileMode.Open, FileAccess.Read)));
                        }
                        activeTocReader = baseTocReader;
                    }
                    targetSbWriter = ((unmodifiedBundleInfo.SplitIndex == -1) ? mainSbWriter : splitSbWriters[unmodifiedBundleInfo.SplitIndex]);
                    activeTocReader.Position = unmodifiedBundleInfo.Offset;
                    unmodifiedBundleInfo.Offset = targetSbWriter.Position;
                    targetSbWriter.Write(activeTocReader.ReadBytes((int)unmodifiedBundleInfo.Size));
                    targetSbWriter.WritePadding(4);
                }
            }
            baseTocReader?.Dispose();
            patchTocReader?.Dispose();
            internalTocFlags &= ~InternalFlags.HasCompressedStrings;
            if (isMainSuperBundleDirty)
            {
                //FileInfo fileInfo = new FileInfo($"{outputBasePath}\\{SuperBundleInfo.Name}.toc");
                FileInfo fileInfo = new FileInfo(Path.Combine(outputBasePath, "Data", $"{SuperBundleInfo.Name}.toc"));
                Directory.CreateDirectory(fileInfo.DirectoryName);
                NativeWriter tocOutputWriter = null;
                //if (!internalTocFlags.HasFlag(InternalFlags.HasInlineSb))
                //{
                //    tocOutputWriter = new NativeWriter(new FileStream($"{outputBasePath}\\{SuperBundleInfo.Name}.sb", FileMode.Create, FileAccess.Write));
                //}

                var existingHeaderData = new byte[556];
                if (File.Exists(fileInfo.FullName))
                {
                    using (var existingFileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
                    {
                        existingFileStream.Read(existingHeaderData, 0, existingHeaderData.Length);
                    }
                }

                using DbWriter dbWriter = new DbWriter(new FileStream(fileInfo.FullName, FileMode.Create, FileAccess.Write));
                //dbWriter.Write(30331136);
                //dbWriter.Position += 552L;
                dbWriter.Write(existingHeaderData);
                int bundleCount = 0;
                int chunkCount = 0;
                Flags flags = (Flags)0;
                int compressedNamesOffset1 = 0;
                int compressedNamesOffset2 = 0;
                uint compressedNamesSize = 0u;
                long position = dbWriter.Position;
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                dbWriter.Write(3735928559u, Endian.Big);
                if (internalTocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                {
                    flags |= Flags.HasCompressedNames;
                    dbWriter.Write(3735928559u, Endian.Big);
                    dbWriter.Write(3735928559u, Endian.Big);
                    dbWriter.Write(3735928559u, Endian.Big);
                }
                Dictionary<byte[], BundleInfo> bundlesForMainToc = new Dictionary<byte[], BundleInfo>();
                foreach (BundleInfo tocBundleInfo in tocBundlesByName.Values)
                {
                    if (tocBundleInfo.SplitIndex == -1)
                    {
                        if (tocBundleInfo.IsPatch || !m_hasPatchFolder)
                        {
                            bundlesForMainToc.Add(Encoding.ASCII.GetBytes(tocBundleInfo.Name.ToLower()), tocBundleInfo);
                        }
                        else
                        {
                            flags |= Flags.HasBaseBundles;
                        }
                    }
                }
                bundleCount = bundlesForMainToc.Count;
                Dictionary<byte[], ChunkInfo> chunksForMainToc = new Dictionary<byte[], ChunkInfo>();
                foreach (ChunkInfo tocChunkInfo in tocChunksByGuid.Values)
                {
                    if (tocChunkInfo.SplitIndex == -1)
                    {
                        if (tocChunkInfo.IsPatch || !m_hasPatchFolder)
                        {
                            chunksForMainToc.Add(tocChunkInfo.Guid.ToByteArray(), tocChunkInfo);
                        }
                        else
                        {
                            flags |= Flags.HasBaseChunks;
                        }
                    }
                }
                chunkCount = chunksForMainToc.Count;
                BundleInfo[] sortedBundleInfos;
                int[] bundleHashMap = CalculateHashMap(bundlesForMainToc, out sortedBundleInfos);
                byte[] bundleNameBuffer;
                using (NativeWriter bundleNameBufferWriter = new NativeWriter(new MemoryStream()))
                {
                    for (int bundleNameIndex = 0; bundleNameIndex < bundleCount; bundleNameIndex++)
                    {
                        if (internalTocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                        {
                            throw new NotImplementedException("compressed names");
                        }
                        sortedBundleInfos[bundleNameIndex].NameOffset = (uint)bundleNameBufferWriter.Position;
                        bundleNameBufferWriter.WriteNullTerminatedString(sortedBundleInfos[bundleNameIndex].Name);
                    }
                    bundleNameBuffer = bundleNameBufferWriter.ToByteArray();
                }
                uint bundleNameTableOffset = (uint)(dbWriter.Position - position);
                for (int bundleNameCount = 0; bundleNameCount < bundleCount; bundleNameCount++)
                {
                    dbWriter.Write(bundleHashMap[bundleNameCount], Endian.Big);
                }
                while ((dbWriter.Position - position) % 8 != 0L)
                {
                    dbWriter.Position++;
                }
                uint bundleDefOffset = (uint)(dbWriter.Position - position);
                for (int bundleEntryIndex = 0; bundleEntryIndex < bundleCount; bundleEntryIndex++)
                {
                    dbWriter.Write(sortedBundleInfos[bundleEntryIndex].NameOffset, Endian.Big);
                    dbWriter.Write(sortedBundleInfos[bundleEntryIndex].Size, Endian.Big);
                    dbWriter.Write(sortedBundleInfos[bundleEntryIndex].Offset, Endian.Big);
                }
                while ((dbWriter.Position - position) % 8 != 0L)
                {
                    dbWriter.Position++;
                }
                ChunkInfo[] sortedChunkInfos;
                int[] chunkHashMap = CalculateHashMap(chunksForMainToc, out sortedChunkInfos);
                uint chunkHashMapOffset = (uint)(dbWriter.Position - position);
                for (int chunkHashIndex = 0; chunkHashIndex < chunkCount; chunkHashIndex++)
                {
                    dbWriter.Write(chunkHashMap[chunkHashIndex], Endian.Big);
                }
                while ((dbWriter.Position - position) % 8 != 0L)
                {
                    dbWriter.Position++;
                }
                uint chunkGuidTableOffset = (uint)(dbWriter.Position - position);
                for (int chunkGuidIndex = 0; chunkGuidIndex < chunkCount; chunkGuidIndex++)
                {
                    byte[] reversedGuidBytes = sortedChunkInfos[chunkGuidIndex].Guid.ToByteArray();
                    Array.Reverse(reversedGuidBytes);
                    dbWriter.Write(reversedGuidBytes);
                    dbWriter.Write((chunkGuidIndex * chunkSbBytesLength) | (chunkSbFlag << 24), Endian.Big);
                }
                while ((dbWriter.Position - position) % 8 != 0L)
                {
                    dbWriter.Position++;
                }
                uint chunkEntryOffset = (uint)(dbWriter.Position - position);
                for (int chunkEntryIndex = 0; chunkEntryIndex < chunkCount; chunkEntryIndex++)
                {
                    dbWriter.Write((byte)0);
                    dbWriter.Write(sortedChunkInfos[chunkEntryIndex].CasFileInfo.IsPatch);
                    if (useM25Strategy)
                    {
                        dbWriter.Write(sortedChunkInfos[chunkEntryIndex].CasFileInfo.CatalogIndex, Endian.Big);
                        dbWriter.Write((byte)0);
                    }
                    else
                    {
                        dbWriter.Write((byte)sortedChunkInfos[chunkEntryIndex].CasFileInfo.CatalogIndex);
                    }
                    dbWriter.Write(sortedChunkInfos[chunkEntryIndex].CasFileInfo.CasIndex);
                    dbWriter.Write(sortedChunkInfos[chunkEntryIndex].CasFileInfo.Offset, Endian.Big);
                    dbWriter.Write(sortedChunkInfos[chunkEntryIndex].CasFileInfo.Size, Endian.Big);
                }
                while ((dbWriter.Position - position) % 8 != 0L)
                {
                    dbWriter.Position++;
                }
                uint bundleNameDataOffset = (uint)(dbWriter.Position - position);
                dbWriter.Write(bundleNameBuffer);
                if (flags.HasFlag(Flags.HasCompressedNames))
                {
                    compressedNamesSize = (uint)(dbWriter.Position - position);
                }
                if (internalTocFlags.HasFlag(InternalFlags.HasInlineSb))
                {
                    while ((dbWriter.Position - position) % 4 != 0L)
                    {
                        dbWriter.Position++;
                    }
                }
                dbWriter.Position = position;
                dbWriter.Write(bundleNameTableOffset, Endian.Big);
                dbWriter.Write(bundleDefOffset, Endian.Big);
                dbWriter.Write(bundleCount, Endian.Big);
                dbWriter.Write(chunkHashMapOffset, Endian.Big);
                dbWriter.Write(chunkGuidTableOffset, Endian.Big);
                dbWriter.Write(chunkCount, Endian.Big);
                dbWriter.Write(chunkEntryOffset, Endian.Big);
                dbWriter.Write(chunkEntryOffset, Endian.Big);
                dbWriter.Write(bundleNameDataOffset, Endian.Big);
                dbWriter.Write(chunkEntryOffset, Endian.Big);
                dbWriter.Write(chunkCount * chunkSbBytesLength, Endian.Big);
                dbWriter.Write((uint)flags, Endian.Big);
                if (internalTocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                {
                    dbWriter.Write(compressedNamesOffset1, Endian.Big);
                    dbWriter.Write(compressedNamesOffset2, Endian.Big);
                    dbWriter.Write(compressedNamesSize, Endian.Big);
                }
                if (internalTocFlags.HasFlag(InternalFlags.HasInlineSb))
                {
                    long tocInlineBundleOffset = dbWriter.Length - position;
                    dbWriter.Position = bundleDefOffset + position;
                    for (int bundleInlineIndex = 0; bundleInlineIndex < bundleCount; bundleInlineIndex++)
                    {
                        dbWriter.Write(sortedBundleInfos[bundleInlineIndex].NameOffset, Endian.Big);
                        dbWriter.Write(sortedBundleInfos[bundleInlineIndex].Size | 0x40000000, Endian.Big);
                        dbWriter.Write(sortedBundleInfos[bundleInlineIndex].Offset + tocInlineBundleOffset, Endian.Big);
                    }
                }
                dbWriter.Position = dbWriter.Length;
                if (bundleCount > 0)
                {
                    if (internalTocFlags.HasFlag(InternalFlags.HasInlineSb))
                    {
                        dbWriter.Write(mainSbWriter.ToByteArray());
                    }
                    else
                    {
                        tocOutputWriter.Write(mainSbWriter.ToByteArray());
                    }
                }
                tocOutputWriter?.Close();
            }
            mainSbWriter.Close();
            for (int splitIndex = 0; splitIndex < isSplitSuperBundleDirty.Length; splitIndex++)
            {
                if (isSplitSuperBundleDirty[splitIndex])
                {
                    // Skipped: split superbundle TOC write that also depended on outputBasePath
                }
                splitSbWriters[splitIndex].Close();
            }
            activeCasWriter?.Close();
        }
        catch (Exception exception)
        {
            Exception = exception;
            SingletonService.GetInstance<IAppInsightsService>()?.TrackException(exception);
        }
    }
    public void ThreadPoolCallback(object threadContext)
    {
        Run();
        doneEvent.Set();
    }

    private string GetCatalog(SuperBundleInfo sb, IEnumerable<Catalog> catalogs)
    {
        foreach (var catalog in catalogs)
        {
            //if (catalog.SuperBundles.Keys.Contains(sb.Name) && catalog.SuperBundles[sb.Name])
            //{
            //    return catalog.Name;
            //}

            if (catalog.SuperBundleKeys.Contains(sb.Name.ToLower()))
            {
                return catalog.Name;
            }
        }
        return catalogs.First().Name;
    }

    private static bool UsesH64ChunkMeta()
    {
        return ProfileManager.IsLoaded(EGame.CFB27, EGame.MADDEN27);
    }

    private static void ApplyNewBundleLocationFlags(byte[] flags)
    {
        if (flags.Length != 0 && ProfileManager.IsLoaded(EGame.CFB27, EGame.MADDEN27))
        {
            flags[0] |= 4;
        }
    }

    private static long GetChunkMetaHash(IChunkAssetEntry entry)
    {
        return entry.H32;
    }

    private static long GetChunkMetaHash(DbObject meta)
    {
        if (!UsesH64ChunkMeta())
        {
            return meta.GetValue("h32", 0);
        }
        return meta.GetValue("h64", 0L);
    }

    private static void SetChunkMetaHash(DbObject meta, IChunkAssetEntry entry)
    {
        meta.SetValue("h32", entry.H32);
    }

    private static DbObject FindChunkMeta(DbObject chunkMeta, IChunkAssetEntry entry)
    {
        if (chunkMeta == null)
        {
            return null;
        }
        long hash = GetChunkMetaHash(entry);
        return chunkMeta.Find<DbObject>((object a) => GetChunkMetaHash(a as DbObject) == hash);
    }

    private static int FindChunkMetaIndex(DbObject chunkMeta, long hash)
    {
        return chunkMeta?.FindIndex((object a) => GetChunkMetaHash(a as DbObject) == hash) ?? (-1);
    }

    private void ReadToc(NativeReader reader, ref Dictionary<string, BundleInfo> tocBundlesByName, ref Dictionary<Guid, ChunkInfo> tocChunksByGuid, ref InternalFlags internalTocFlags, bool patch, int splitIndex = -1)
    {
        reader.Position += 556;
        uint num = (uint)reader.Position;
        uint num2 = reader.ReadUInt(Endian.Big) + num;
        uint num3 = reader.ReadUInt(Endian.Big) + num;
        int num4 = reader.ReadInt(Endian.Big);
        uint num5 = reader.ReadUInt(Endian.Big) + num;
        uint num6 = reader.ReadUInt(Endian.Big) + num;
        int num7 = reader.ReadInt(Endian.Big);
        reader.ReadUInt(Endian.Big);
        reader.ReadUInt(Endian.Big);
        uint num8 = reader.ReadUInt(Endian.Big) + num;
        uint num9 = reader.ReadUInt(Endian.Big) + num;
        int num10 = reader.ReadInt(Endian.Big);
        Flags flags = (Flags)reader.ReadInt(Endian.Big);
        if (flags.HasFlag(Flags.HasBaseBundles) || flags.HasFlag(Flags.HasBaseChunks))
        {
            using NativeReader reader2 = new NativeReader(new FileStream(fss.ResolvePath(string.Format("native_data/{0}.toc", (splitIndex != -1) ? SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]) : SuperBundleInfo.Name)), FileMode.Open, FileAccess.Read));
            InternalFlags tocFlags2 = (InternalFlags)0;
            ReadToc(reader2, ref tocBundlesByName, ref tocChunksByGuid, ref tocFlags2, patch: false, splitIndex);
        }
        uint count = 0u;
        uint count2 = 0u;
        uint num11 = uint.MaxValue;
        HuffmanDecoder huffmanDecoder = null;
        if (flags.HasFlag(Flags.HasCompressedNames))
        {
            internalTocFlags |= InternalFlags.HasCompressedStrings;
            huffmanDecoder = new HuffmanDecoder();
            count = reader.ReadUInt(Endian.Big);
            count2 = reader.ReadUInt(Endian.Big);
            num11 = reader.ReadUInt(Endian.Big) + num;
        }
        if (num4 != 0)
        {
            if (flags.HasFlag(Flags.HasCompressedNames))
            {
                reader.Position = num8;
                huffmanDecoder.ReadEncodedData(reader, count, Endian.Big);
                reader.Position = num11;
                huffmanDecoder.ReadHuffmanTable(reader, count2, Endian.Big);
            }
            List<int> splitSbWriters = new List<int>(num4);
            reader.Position = num2;
            for (int i = 0; i < num4; i++)
            {
                splitSbWriters.Add(reader.ReadInt(Endian.Big));
            }
            reader.Position = num3;
            for (int j = 0; j < num4; j++)
            {
                int num12 = reader.ReadInt(Endian.Big);
                uint num13 = reader.ReadUInt(Endian.Big);
                long num14 = reader.ReadLong(Endian.Big);
                string resolvedTocPath = "";
                if (flags.HasFlag(Flags.HasCompressedNames))
                {
                    resolvedTocPath = huffmanDecoder.ReadHuffmanEncodedString(num12);
                }
                else
                {
                    long position = reader.Position;
                    reader.Position = num8 + num12;
                    resolvedTocPath = reader.ReadNullTerminatedString();
                    reader.Position = position;
                }
                if (tocBundlesByName.ContainsKey(resolvedTocPath))
                {
                    tocBundlesByName.Remove(resolvedTocPath);
                }
                BundleInfo value = new BundleInfo
                {
                    Name = resolvedTocPath,
                    Offset = num14,
                    Size = num13,
                    IsPatch = patch,
                    SbName = ((splitIndex != -1) ? SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]) : SuperBundleInfo.Name),
                    SplitIndex = splitIndex
                };
                if (num13 != uint.MaxValue && num14 != -1)
                {
                    tocBundlesByName.Add(resolvedTocPath, value);
                }
            }
            huffmanDecoder?.Dispose();
        }
        if (num7 == 0)
        {
            return;
        }
        reader.Position = num5;
        List<int> newlyCreatedBundleHashes = new List<int>(num7);
        for (int k = 0; k < num7; k++)
        {
            newlyCreatedBundleHashes.Add(reader.ReadInt(Endian.Big));
        }
        reader.Position = num6;
        Guid[] isSplitSuperBundleDirty = new Guid[num10 / chunkSbBytesLength];
        for (int l = 0; l < num7; l++)
        {
            byte[] array2 = reader.ReadBytes(16);
            Guid guid = new Guid(new byte[16]
            {
                array2[15],
                array2[14],
                array2[13],
                array2[12],
                array2[11],
                array2[10],
                array2[9],
                array2[8],
                array2[7],
                array2[6],
                array2[5],
                array2[4],
                array2[3],
                array2[2],
                array2[1],
                array2[0]
            });
            int num15 = reader.ReadInt(Endian.Big);
            if (num15 != -1)
            {
                num15 = (num15 & 0xFFFFFF) / chunkSbBytesLength;
                isSplitSuperBundleDirty[num15] = guid;
            }
        }
        reader.Position = num9;
        for (int m = 0; m < num10 / chunkSbBytesLength; m++)
        {
            reader.ReadByte();
            bool isPatchToc = reader.ReadBoolean();
            int num16 = 0;
            byte b = 0;
            if (useM25Strategy)
            {
                num16 = reader.ReadInt(Endian.Big);
                reader.ReadByte();
                b = reader.ReadByte();
            }
            else
            {
                num16 = reader.ReadByte();
                b = reader.ReadByte();
            }
            uint offset = reader.ReadUInt(Endian.Big);
            uint size = reader.ReadUInt(Endian.Big);
            ChunkInfo chunkInfo = new ChunkInfo
            {
                Guid = isSplitSuperBundleDirty[m],
                IsPatch = patch,
                CasFileInfo = new CasFileInfo
                {
                    IsPatch = isPatchToc,
                    CatalogIndex = num16,
                    CasIndex = b,
                    Offset = offset,
                    Size = size
                },
                SbName = ((splitIndex != -1) ? SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]) : SuperBundleInfo.Name),
                SplitIndex = splitIndex
            };
            if (tocChunksByGuid.ContainsKey(chunkInfo.Guid))
            {
                tocChunksByGuid.Remove(chunkInfo.Guid);
            }
            tocChunksByGuid.Add(chunkInfo.Guid, chunkInfo);
        }
    }

    private DbObject ReadBundle(NativeReader reader, ref InternalFlags internalTocFlags)
    {
        /*
         CASBundle bundle = new();
                bundle.BaseBundle = bundlesByOffset[i];
                bundle.BaseEntry = BundleEntries[bundlesByOffset[i]];

                long startPosition = activeTocReader.Position;
                bundle.unk1 = activeTocReader.ReadInt(Endian.Big); // 0-4
                bundle.unk2 = activeTocReader.ReadInt(Endian.Big); // 4-8
                bundle.FlagsOffset = activeTocReader.ReadInt(Endian.Big); // 8-12
                bundle.EntriesCount = activeTocReader.ReadInt(Endian.Big); // 12-16
                bundle.EntriesOffset = activeTocReader.ReadInt(Endian.Big); // 16-20
                bundle.HeaderSize = activeTocReader.ReadInt(Endian.Big); // 20-24
                bundle.unk4 = activeTocReader.ReadInt(Endian.Big); // 24-28
                bundle.unk5 = activeTocReader.ReadInt(Endian.Big); // 28-32
                bundle.unk6 = activeTocReader.ReadInt(Endian.Big); // 32-36
         */



        int unk1 = reader.ReadInt(Endian.Big);
        int unk2 = reader.ReadInt(Endian.Big);
        uint flagOffset = reader.ReadUInt(Endian.Big);
        uint entriesCount = reader.ReadUInt(Endian.Big);
        uint entriesOffset = reader.ReadUInt(Endian.Big);
        int headerSize = reader.ReadInt(Endian.Big); // 20-24
        int unk4 = reader.ReadInt(Endian.Big); // 24-28
        int unk5 = reader.ReadInt(Endian.Big); // 28-32
        int unk6 = reader.ReadInt(Endian.Big); // 32-36
        if (unk1 != 0 || unk2 != 0)
        {
            internalTocFlags |= InternalFlags.HasInlineBundle;
        }
        reader.Position = flagOffset;
        byte[] flagsArray = new byte[entriesCount];
        for (uint flagIndex = 0u; flagIndex < entriesCount; flagIndex++)
        {
            flagsArray[flagIndex] = reader.ReadByte();
        }
        bool patch = false;
        int catalog = 0;
        int cas = 0;
        int bundleOffsetInCas = 0;
        int bundleSizeInCas = 0;
        int num11 = 0;
        DbObject bundlePayload = new DbObject();
        //if (internalTocFlags.HasFlag(InternalFlags.HasInlineBundle))
        //{
        //    reader.Position = unk1;
        //    bundlePayload = reader.ReadDbObject();
        //    reader.Position = entriesOffset;
        //}
        //else
        //{
        reader.Position = entriesOffset;
        if (flagsArray[num11++] > 0)
        {
            reader.ReadByte();
            patch = reader.ReadBoolean();
            if (useM25Strategy)
            {
                catalog = reader.ReadInt(Endian.Big);
                reader.ReadByte();
                cas = reader.ReadByte();
            }
            else
            {
                catalog = reader.ReadByte();
                cas = reader.ReadByte();
            }
        }

        bundleOffsetInCas = reader.ReadInt(Endian.Big);
        bundleSizeInCas = reader.ReadInt(Endian.Big);
        string filePath = SingletonService.GetInstance<IFileSystemService>().GetFilePath(catalog, cas, patch);
        using Stream stream = new FileStream(SingletonService.GetInstance<IFileSystemService>().ResolvePath(filePath), FileMode.Open, FileAccess.Read);
        stream.Position = bundleOffsetInCas;
        byte[] buffer = new byte[bundleSizeInCas];
        stream.Read(buffer, 0, bundleSizeInCas);

#if DEBUG
        DebugBytesToFileLogger.Instance.WriteAllBytes("CasBundleAction_ReadBundleInCas.bin", buffer, "MaddenCompilerCBA", false);
#endif

        BundleReader bundleReader = new BundleReader();
        Dictionary<string, object> mv = new Dictionary<string, object>();
        bundleReader.Read(0, ref mv, new NativeReader(new MemoryStream(buffer)), false, false);
        bundlePayload.AddValue("ebx", ((DbObject)mv["ebx"]));
        bundlePayload.AddValue("res", ((DbObject)mv["res"]));
        bundlePayload.AddValue("chunks", ((DbObject)mv["chunks"]));
        bundlePayload.AddValue("chunkMeta", ((DbObject)mv["chunkMeta"]));

        for (int i = 0; i < ((DbObject)mv["ebx"]).List.Count; i++)
        {
            if (flagsArray[num11++] > 0)
            {
                reader.ReadByte();
                patch = reader.ReadBoolean();
                if (useM25Strategy)
                {
                    catalog = reader.ReadInt(Endian.Big);
                    reader.ReadByte();
                    cas = reader.ReadByte();
                }
                else
                {
                    catalog = reader.ReadByte();
                    cas = reader.ReadByte();
                }
            }
            DbObject ebxDescriptor = ((DbObject)mv["ebx"]).List[i] as DbObject;
            bundleOffsetInCas = reader.ReadInt(Endian.Big);
            bundleSizeInCas = reader.ReadInt(Endian.Big);
            ebxDescriptor.SetValue("catalog", catalog);
            ebxDescriptor.SetValue("cas", cas);
            ebxDescriptor.SetValue("offset", bundleOffsetInCas);
            ebxDescriptor.SetValue("size", bundleSizeInCas);
            if (patch)
            {
                ebxDescriptor.SetValue("patch", true);
            }
        }
        for (int j = 0; j < ((DbObject)mv["res"]).List.Count; j++)
        {
            if (flagsArray[num11++] > 0)
            {
                reader.ReadByte();
                patch = reader.ReadBoolean();
                if (useM25Strategy)
                {
                    catalog = reader.ReadInt(Endian.Big);
                    reader.ReadByte();
                    cas = reader.ReadByte();
                }
                else
                {
                    catalog = reader.ReadByte();
                    cas = reader.ReadByte();
                }
            }
            DbObject resDescriptor = ((DbObject)mv["res"]).List[j] as DbObject;
            bundleOffsetInCas = reader.ReadInt(Endian.Big);
            bundleSizeInCas = reader.ReadInt(Endian.Big);
            resDescriptor.SetValue("catalog", catalog);
            resDescriptor.SetValue("cas", cas);
            resDescriptor.SetValue("offset", bundleOffsetInCas);
            resDescriptor.SetValue("size", bundleSizeInCas);
            if (patch)
            {
                resDescriptor.SetValue("patch", true);
            }
        }
        for (int k = 0; k < ((DbObject)mv["chunks"]).List.Count; k++)
        {
            if (flagsArray[num11++] > 0)
            {
                reader.ReadByte();
                patch = reader.ReadBoolean();
                if (useM25Strategy)
                {
                    catalog = reader.ReadInt(Endian.Big);
                    reader.ReadByte();
                    cas = reader.ReadByte();
                }
                else
                {
                    catalog = reader.ReadByte();
                    cas = reader.ReadByte();
                }
            }
            DbObject chunkDescriptor = ((DbObject)mv["chunks"]).List[k] as DbObject;
            bundleOffsetInCas = reader.ReadInt(Endian.Big);
            bundleSizeInCas = reader.ReadInt(Endian.Big);
            chunkDescriptor.SetValue("catalog", catalog);
            chunkDescriptor.SetValue("cas", cas);
            chunkDescriptor.SetValue("offset", bundleOffsetInCas);
            chunkDescriptor.SetValue("size", bundleSizeInCas);
            if (patch)
            {
                chunkDescriptor.SetValue("patch", true);
            }
        }
        return bundlePayload;
    }

    private NativeWriter GetNextCas(string catName, out int currentCasIndex)
    {
        var lastCas = Directory.GetFiles(Path.Combine(fss.BasePath, "Data", catName), "cas_*.cas").Last();
        Debug.WriteLine($"Using Cas File {lastCas}");
        currentCasIndex = int.Parse(Path.GetFileNameWithoutExtension(lastCas).Split('_')[1]);
        var nw = new NativeWriter(new FileStream(lastCas, FileMode.Open, FileAccess.Write));
        nw.Position = nw.Length;
        return nw;
        //lock (locker)
        //{
        //    if (!CasFiles.TryGetValue(catName, out currentCasIndex))
        //    {
        //        currentCasIndex = 0;
        //        CasFiles[catName] = 0;
        //    }
        //    CasFiles[catName]++;
        //}
        //var fss = SingletonService.GetInstance<IFileSystemService>();
        //FileInfo fileInfo;
        //if (UseModData)
        //    fileInfo = new FileInfo(Path.Combine(fss.BasePath, "ModData", "Data", catName, "cas_" + currentCasIndex.ToString("D2") + ".cas"));
        //else 
        //    fileInfo = new FileInfo(Path.Combine(fss.BasePath, "Data", catName, "cas_" + currentCasIndex.ToString("D2") + ".cas"));

        //Directory.CreateDirectory(fileInfo.DirectoryName);

        //Debug.WriteLine($"Using Cas File {fileInfo.FullName}");
        //return new NativeWriter(new FileStream(fileInfo.FullName, FileMode.Create));
    }

    private int[] CalculateHashMap<T>(Dictionary<byte[], T> input, out T[] sortedItems)
    {
        int count = input.Keys.Count;
        sortedItems = new T[count];
        int[] hashMap = new int[count];
        for (int i = 0; i < count; i++)
        {
            hashMap[i] = -1;
        }
        List<byte[]>[] buckets = new List<byte[]>[count];
        for (int j = 0; j < count; j++)
        {
            buckets[j] = new List<byte[]>();
        }
        foreach (byte[] key in input.Keys)
        {
            buckets[(int)(Hash(key) % count)].Add(key);
        }
        Array.Sort(buckets, (List<byte[]> x, List<byte[]> y) => y.Count.CompareTo(x.Count));
        bool[] occupied = new bool[count];
        int bucketIndex;
        for (bucketIndex = 0; bucketIndex < count; bucketIndex++)
        {
            int count2 = buckets[bucketIndex].Count;
            if (buckets[bucketIndex].Count <= 1)
            {
                break;
            }
            List<int> candidateIndices = new List<int>(count2);
            uint hashSeed = 1u;
            int candidatePos = 0;
            while (candidatePos < count2)
            {
                int index = (int)(Hash(buckets[bucketIndex][candidatePos], hashSeed) % count);
                if (occupied[index] || candidateIndices.FindIndex((int k) => k == index) != -1)
                {
                    hashSeed++;
                    candidatePos = 0;
                    candidateIndices.Clear();
                }
                else
                {
                    candidateIndices.Add(index);
                    candidatePos++;
                }
            }
            hashMap[(int)(Hash(buckets[bucketIndex][0]) % count)] = (int)hashSeed;
            for (int k = 0; k < count2; k++)
            {
                int slot = candidateIndices[k];
                sortedItems[slot] = input[buckets[bucketIndex][k]];
                occupied[slot] = true;
            }
        }
        int nextFreeSlot = 0;
        for (; bucketIndex < count && buckets[bucketIndex].Count != 0; bucketIndex++)
        {
            while (occupied[nextFreeSlot])
            {
                nextFreeSlot = (nextFreeSlot + 1) % count;
            }
            hashMap[Hash(buckets[bucketIndex][0]) % count] = -1 * nextFreeSlot - 1;
            sortedItems[nextFreeSlot] = input[buckets[bucketIndex][0]];
            nextFreeSlot = (nextFreeSlot + 1) % count;
        }
        return hashMap;
    }

    private int GetIndex(byte[] key, int[] hashMap)
    {
        int num = hashMap[Hash(key) % hashMap.Length];
        if (num < 0)
        {
            return (num + 1) * -1;
        }
        return (int)(Hash(key, (uint)num) % hashMap.Length);
    }

    private uint Hash(byte[] bytes, uint offset = 2166136261u)
    {
        uint num = 16777619u;
        uint num2 = offset;
        for (int i = 0; i < bytes.Length; i++)
        {
            num2 = (num2 * num) ^ (uint)(sbyte)bytes[i];
        }
        return num2 % num;
    }

    private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();
}
