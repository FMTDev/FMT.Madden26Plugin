using FMT.Compilers.FC;
using FMT.Core.Models.Modding;
using FMT.Core.Models.TOC;
using FMT.Core.Writers;
using FMT.Db;
using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.AppInsights;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.TOC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Madden26Plugin.Compiler
{
    public class CFB27AssetCompiler : Madden26AssetCompiler2025, IAssetCompiler
    {
        public override Type TOCFileWriterType => typeof(Madden27TOCFileWriter);

        public override bool Compile(ILogger logger, IModExecutor modExecutor)
        {
            var result = true;
            //var result = base.Compile(logger, modExecutor);

            SingletonService.GetInstance<IAppInsightsService>()?.TrackEvent($"{nameof(CFB27AssetCompiler)} Completed");
            FileLogger.WriteLine($"{nameof(CFB27AssetCompiler)} Completed");

            return result;
        }

        CancellationTokenSource ctsInjectDllTask = new CancellationTokenSource();

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
            //DeployEmbeddedResource("Madden26Plugin.Launcher.CFB27.EAAntiCheat.GameServiceLauncher.exe", Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));
            DeployEmbeddedResource("FMT.Core.FMTLauncher.FMT.FrostbiteGameLoader.exe", Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));

            //GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

            return new FCSharedCompilerHelpers().LaunchFMTJavelinTask(logger, ProfileManager.Instance.ExecutableName, ctsInjectDllTask, false, false, out var InjectDllTask);
        }
    }
}
