using FMT.Compilers;
using FMT.Compilers.FC;
using FMT.Core;
using FMT.Core.CustomExceptions;
using FMT.Core.Models.Modding;
using FMT.Core.Models.TOC;
using FMT.Core.Writers;
using FMT.Db;
using FMT.Ebx;
using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AppInsights;
using FMT.ServicesManagers.AssetEntryServicing;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.TOC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Madden26Plugin;

public class Madden26AssetCompiler : Frostbite2025AssetCompiler, IAssetCompiler, IDisposable
{
    private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

    private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();

    private Task FMTDllInjectTask = null;
    private Task LiveEditorDllInjectTask = null;

    CancellationTokenSource ctsInjectDllTask = new CancellationTokenSource();

    public override bool RequiresCacheToCompile => false;

    private ILogger Logger;

    public Madden26AssetCompiler()
    {
        FileLogger.WriteLine($"{nameof(Madden26AssetCompiler)}.{nameof(Madden26AssetCompiler)} started");
        TocFileWriter = new Madden26TOCFileWriter();
        //UseFullTOCRebuild = true;
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

    public override async Task<bool> PreCompile(ILogger logger, IModExecutor modExecutor)
    {
        FileLogger.WriteLine($"{nameof(Madden26AssetCompiler)}.{nameof(PreCompile)} started");
        Logger = logger;

        // Deploy CryptBase.dll to the output folder
        DeployEmbeddedResource("FMT.Core.FMTLauncher.CryptBase.dll", Path.Combine(fss.BasePath, "CryptBase.dll"));

        // Backup the original EAAntiCheat.GameServiceLauncher.exe if it exists and a backup does not already exist
        if (File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe")) && !File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup")))
            File.Move(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"), Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup"));

        DeployEmbeddedResource("FMT.Core.FMTLauncher.FMT.FrostbiteGameLoader.exe", Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));

        //return await base.PreCompile(logger, modExecutor);
        return true;
    }

    public override bool PostCompile(ILogger logger, IModExecutor modExecutor)
    {
        bool result = true;
        result = LaunchFMTJavelinTask(logger);
        return result;
    }

    private bool LaunchFMTJavelinTask(ILogger logger)
    {
        return new FCSharedCompilerHelpers().LaunchFMTJavelinTask(logger, "CollegeFB27", ctsInjectDllTask, false, false, out FMTDllInjectTask);
    }

    /// <summary>
    /// This is run AFTER the compilation of the fbmod into resource files ready for the Actions to TOC/SB/CAS to be taken
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="logger"></param>
    /// <param name="frostyModExecuter">Frosty Mod Executer object</param>
    /// <returns></returns>
    public override bool Compile(ILogger logger, IModExecutor modExecutor)
    {
        this.ModExecutor = modExecutor;
        logger.Log($"{nameof(Madden26AssetCompiler)} started");

        

        // Change TOC File Type to Madden26TOCFile
        fss.TOCFileType = typeof(Madden26TOCFile);
        // Initialize SDK
        TypeLibrary.Initialize();

        var fs = SingletonService.GetInstance<IFileSystemService>();

        var result = base.Compile(logger, modExecutor);
        return result;
    }
    public override async Task<bool> OnProcessEnded(ILogger logger, IModExecutor modExecutor)
    {
        // Stop the (possibly still polling/waiting) injection task from injecting into a process we are about to clean up.
        ctsInjectDllTask?.Cancel();

        // Wait for it to actually unwind before cleanup: CleanUp() deletes %AppData%\FMT\Javelin, so anything still
        // waiting to inject would either load a deleted file or never get the chance to run.
        await WaitForInjectTaskToFinish();

        AppContext.SetSwitch("IsInCleanup", true);
        var cleanFunc = new Madden26ModsCleanupFunctions();
        cleanFunc.CleanUp();

        while (cleanFunc == null)
            await Task.Delay(1000);

        while (cleanFunc.CleanupTask?.Status == TaskStatus.Running)
            await Task.Delay(1000);

        AppContext.SetSwitch("IsInCleanup", false);
        return true;
    }

    private async Task WaitForInjectTaskToFinish(TimeSpan? timeout = null)
    {
        Task injectTask = FMTDllInjectTask;
        if (injectTask == null)
            return;

        try
        {
            await injectTask.WaitAsync(timeout ?? TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            FileLogger.WriteException(ex);
        }
    }

    public void Dispose()
    {
        ModExecutor?.Logger?.Log($"{nameof(Madden26AssetCompiler)}.{nameof(Dispose)}");

        // ModExecutor.Run() calls this immediately after ExecuteProcess() - i.e. the game has only just been launched
        // and the process does not exist yet. The injection task started in PostCompile is a *waiting* task that is
        // normally still polling for that process right now, so cancelling first kills the injection before it ever
        // sees the game. Wait for the task to finish its own budget (that wait is what makes injection happen at all),
        // and only cancel whatever is left afterwards.
        try
        {
            if (FMTDllInjectTask != null && !FMTDllInjectTask.Wait(TimeSpan.FromMinutes(4)))
                FileLogger.WriteLine($"{nameof(Madden26AssetCompiler)}.{nameof(Dispose)}: injection task still running after 4 minutes, cancelling it.");
        }
        catch (Exception ex)
        {
            FileLogger.WriteException(ex);
        }

        ctsInjectDllTask?.Cancel();

        try
        {
            LiveEditorDllInjectTask?.Wait(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            FileLogger.WriteException(ex);
        }

        ctsInjectDllTask?.Dispose();
        FMTDllInjectTask?.Dispose();
        LiveEditorDllInjectTask?.Dispose();

        ctsInjectDllTask = null;
        LiveEditorDllInjectTask = null;
        FMTDllInjectTask = null;
    }
}
