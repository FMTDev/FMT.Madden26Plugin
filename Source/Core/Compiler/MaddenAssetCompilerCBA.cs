using FMT.Compilers;
using FMT.Compilers.FC;
using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.Core.Resources;
using FMT.Core.Writers;
using FMT.Db;
using FMT.Ebx;
using FMT.FileTools;
using FMT.FileTools.Readers;
using FMT.FileTools.Writers;
using FMT.Hash;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AppInsights;
using FMT.ServicesManagers.AssetEntryServicing;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.TOC;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Madden26Plugin.Compiler;

public static class DbWriterExtensions 
{
    public static void Write(this DbWriter writer, DbObject inObj)
    {
        //if (writer.writeHeader)
        {
            writer.Write(30331136);
            writer.Write(new byte[552]);
        }
        writer.Write(writer.WriteDbObject("", inObj));
    }
}
public class MaddenAssetCompilerCBA : FrostbiteNullCompiler, IAssetCompiler
{
    private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

    private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();
    private bool m_hasPatchFolder => Directory.Exists(Path.Combine(SingletonService.GetInstance<IFileSystemService>().BasePath, "Patch"));

    public override bool Compile(ILogger logger, IModExecutor modExecutor)
    {
        List<CasBundleAction> actions = new List<CasBundleAction>();
        var fs = SingletonService.GetInstance<IFileSystemService>();
        var doneEvent = new System.Threading.ManualResetEvent(false);
        var cancelToken = new System.Threading.CancellationToken();
        List<Task> actionTasks = new List<Task>();

        // Running in Async saves on compilation time but a nightmare to debug. Choose false to debug.
        bool runAsync = false;

        new LegacyModCompilerHelper().ProcessLegacyMods(modExecutor);

        CasBundleAction.CasFiles.Clear();
        foreach (var catalog in fs.Catalogs.Where(x => !string .IsNullOrEmpty(x.Name)))
        {
            int casIndex = 0;
            string path3 = Path.Combine(fs.BasePath, "Data", catalog.Name);
            if (Directory.Exists(path3))
            {
                var array2 = Directory.GetFiles(path3, "cas_*.cas");
                for (int i = 0; i < array2.Length; i++)
                {
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(array2[i]);
                    if (fileNameWithoutExtension.StartsWith("cas_") && int.TryParse(fileNameWithoutExtension.Substring(4), out var result) && result > casIndex)
                    {
                        casIndex = result;
                    }
                }
            }
            CasBundleAction.CasFiles.TryAdd(catalog.Name, casIndex + 1);
        }

        _ = CasBundleAction.CasFiles;

        foreach (string superBundle in fs.SuperBundles.Where(x => x.Contains("splash", StringComparison.OrdinalIgnoreCase)))
        {
            var resolvedPath = fs.ResolvePath(superBundle + ".toc");
            if (resolvedPath == "")
                continue;

            //CasBundleAction action = new CasBundleAction(new SuperBundleInfo(superBundle), doneEvent, modExecutor);
            //actions.Add(action);
            //if (runAsync)
            //    actionTasks.Add(Task.Run(() => action.Run()));
            Madden27TOCFile toc = new Madden27TOCFile(resolvedPath, false, false, false, -1, false);
            Madden27TOCFileWriter writer = new Madden27TOCFileWriter();
            writer.Write(toc, false);
        }

        if (runAsync)
            Task.WaitAll(actionTasks.ToArray(), cancelToken);
        else
        {
            foreach (CasBundleAction action in actions)
            {
                action.Run();
            }
        }

        foreach (CasBundleAction completedAction in actions)
        {
            if (completedAction.Exception != null)
                return false;
        }


        DbObject dbObject4 = null;
        using (DbReader dbReader2 = new DbReader(new FileStream(fs.ResolvePath("native_data/layout.toc"), FileMode.Open, FileAccess.Read), fs.CreateDeobfuscator()))
        {
            dbObject4 = dbReader2.ReadDbObject();
        }
        //if (modExecutor..Count > 0)
        //{
        //    foreach (string addedSuperBundle2 in m_addedSuperBundles)
        //    {
        //        DbObject dbObject5 = new DbObject();
        //        dbObject5.SetValue("name", addedSuperBundle2);
        //        dbObject4.GetValue<DbObject>("superBundles").Add(dbObject5);
        //        ((DbObject)dbObject4.GetValue<DbObject>("installManifest").GetValue<DbObject>("installChunks")[1]).GetValue<DbObject>("superbundles").Add(addedSuperBundle2);
        //    }
        //}
        //if (ProfilesLibrary.IsLoaded(ProfileVersion.Madden25, ProfileVersion.Madden26, ProfileVersion.CollegeFootball27, ProfileVersion.Madden27))
        {
            using NativeWriter nativeWriter2 = new NativeWriter(new MemoryStream());
            SortedDictionary<uint, int> sortedDictionary = new SortedDictionary<uint, int>();
            foreach (KeyValuePair<string, int> item11 in new SortedDictionary<string, int>(CasBundleAction.CasFiles))
            {
                int catalogIndex = fs.Catalogs.Find(x => x.Name == item11.Key).PersistentIndex.Value;
                sortedDictionary.Add((uint)catalogIndex, item11.Value);
            }
            foreach (KeyValuePair<uint, int> item12 in sortedDictionary)
            {
                for (int num8 = 1; num8 < item12.Value; num8++)
                {
                    //if (File.Exists(fs.GetFilePath((int)item12.Key, num8, m_hasPatchFolder).Replace(m_hasPatchFolder ? "native_patch/" : "native_data/", text + m_patchPath + "/")))
                    //{
                    nativeWriter2.Write((ushort)num8);
                    nativeWriter2.Write(item12.Key);
                    nativeWriter2.Write((ushort)0);
                    //}
                }
            }
            dbObject4.SetValue("layeredInstallChunkFiles", nativeWriter2.ToByteArray());
        }
        //string path4 = text + m_patchPath + "/layout.toc";
        ////if (ProfilesLibrary.IsLoaded(ProfileVersion.Madden25, ProfileVersion.Madden26, ProfileVersion.CollegeFootball27, ProfileVersion.Madden27))
        //{
        //    path4 = text + "Data/layout.toc";
        //}
        using DbWriter dbWriter2 = new DbWriter(new FileStream(fs.ResolvePath("native_data/layout.toc"), FileMode.Open, FileAccess.Write), inWriteHeader: true);
        dbWriter2.Write(dbObject4);

        return true;
    }

    public override bool PostCompile(ILogger logger, IModExecutor modExecutor)
    {
        var fss = SingletonService.GetInstance<IFileSystemService>();
        if (fss == null)
        {
            throw new NullReferenceException($"{nameof(fss)} cannot be null.");
        }
        // --------------------------------------------------------------------------------------------------------
        // Apply Anti-Cheat bypass
        // Deploy CryptBase.dll to the output folder
        DeployEmbeddedResource("FMT.Core.FMTLauncher.CryptBase.dll", Path.Combine(fss.BasePath, "CryptBase.dll"));
        // Deploy dpapi.dll to the output folder
        DeployEmbeddedResource("FMT.Core.FMTLauncher.dpapi.dll", Path.Combine(fss.BasePath, "dpapi.dll"));

        // Backup the original EAAntiCheat.GameServiceLauncher.exe if it exists and a backup does not already exist
        if (File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe")) && !File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup")))
            File.Move(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"), Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup"));

        // Deploy the modified EAAntiCheat.GameServiceLauncher.exe to the output folder
        DeployEmbeddedResource("FMT.Core.FMTLauncher.FMT.FrostbiteGameLoader.exe", Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));

        return new FCSharedCompilerHelpers().LaunchFMTJavelinTask(logger, ProfileManager.Instance.ExecutableName, ctsInjectDllTask, false, false, out var InjectDllTask);

    }

    private Task InjectDllTask;
    CancellationTokenSource ctsInjectDllTask = new CancellationTokenSource();

    private void LaunchFMTJavelinTask(ILogger logger)
    {
        logger.Log("Starting task to fix Madden27 to load mods...");
        InjectDllTask = Task.Run(async () =>
        {
            if (await new FrostbiteInjection(logger)
                .InjectDLLIntoProcessName("G:\\Work\\FIFA Modding\\FC26Javelin\\JavelinBypass2\\build64\\JavelinBypass2\\Release\\JavelinBypass2.dll"
                , "Madden27"
                , maxProcesses: 1
                , cancellationToken: ctsInjectDllTask.Token))
            {
                logger.Log("Succesfully injected FMT.Javelin.dll into Madden27!");
            }

        }, ctsInjectDllTask.Token);
    }

    private byte[] GetEmbeddedResourceBytes(string resourceName)
    {
        var resourceStream = EmbeddedResourceHelper.GetEmbeddedResourceByName(resourceName);
        if (resourceStream != null)
        {
            using (var ms = new MemoryStream())
            {
                resourceStream.CopyTo(ms);
                ms.Position = 0;
                return ms.ToArray();
            }
        }
        return null;
    }

    private void DeployEmbeddedResource(string resourceName, string outputPath)
    {
        File.WriteAllBytes(outputPath, GetEmbeddedResourceBytes(resourceName));
    }

}
