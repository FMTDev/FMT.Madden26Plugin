using System;
using System.Diagnostics;
using System.IO;

namespace ACBypass;

class Program
{
    static void Main(string[] args)
    {
        string gameDir = @"C:\Program Files\EA Games\EA SPORTS College Football 27";
        string launcherExe = Path.Combine(gameDir, "EAAntiCheat.GameServiceLauncher.exe");

        if (!File.Exists(launcherExe))
        {
            Console.Error.WriteLine("Launcher not found at " + launcherExe);
            Console.ReadKey();
            return;
        }

        Console.Title = "AC Bypass (offline mod)";
        Console.WriteLine("Starting: " + Path.GetFileName(launcherExe));

        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = launcherExe,
                WorkingDirectory = gameDir,
                UseShellExecute = false
            });

            if (proc != null)
            {
                Console.WriteLine("Launcher running (PID: " + proc.Id + ")");
                Console.WriteLine("The game will start shortly.");
                Console.WriteLine("This window will stay open until you close the game.");
                proc.WaitForExit();
                Console.WriteLine("Game exited. You may close this window.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            Console.ReadKey();
        }
    }
}
