using FMT.PluginInterfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Madden26Plugin
{
    public class Madden26ModsCleanupFunctions : ICleanupFunction
    {
        public void CleanUp()
        {
            var fss = FMT.ServicesManagers.SingletonService.GetInstance<FMT.ServicesManagers.Interfaces.IFileSystemService>();

            // Cleanup the Injected DLLs
            if (File.Exists(Path.Combine(fss.BasePath, "CryptBase.dll")))
                File.Delete(Path.Combine(fss.BasePath, "CryptBase.dll"));

            if (File.Exists(Path.Combine(fss.BasePath, "dpapi.dll")))
                File.Delete(Path.Combine(fss.BasePath, "dpapi.dll"));

            Task.Run(async () =>
            {
                int attempts = 90;
                while ((Process.GetProcessesByName("FMT.FrostbiteGameLoader").Any() || Process.GetProcessesByName("EAAntiCheat.GameServiceLauncher").Any()) && attempts-- > 0)
                {
                    await Task.Delay(1000);
                }
                if (File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe")) && File.Exists(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup")))
                {
                    File.Delete(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));
                    File.Move(Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe.backup"), Path.Combine(fss.BasePath, "EAAntiCheat.GameServiceLauncher.exe"));
                }
            });
          
        }
    }
}
