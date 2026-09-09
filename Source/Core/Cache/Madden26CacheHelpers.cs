using FMT.Db;
using FMT.FileTools.Readers;
using FMT.ProfileSystem;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Cache
{
    internal class Madden26CacheHelpers
    {
        public short Version { get; set; } = 3;

        public string GetCachePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "_GameCaches", $"{ProfileManager.Instance.Name}.cache");
        }

        public string GetCacheBodyPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "_GameCaches", $"{ProfileManager.Instance.Name}_body.cache");
        }

        public ulong GetSystemIteration()
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            if (!Directory.Exists(fss.BasePath))
                return 0;

            var layoutFiles = Directory.GetFiles(fss.BasePath, "*layout.toc", new EnumerationOptions() { RecurseSubdirectories = true }).ToList();
            layoutFiles = layoutFiles.Where(x => !x.Contains("ModData")).ToList();

            string dataPath = fss.ResolvePath("native_data/layout.toc");

            DbObject dataLayoutTOC = null;
            using (DbReader dbReader = new(new FileStream(dataPath, FileMode.Open, FileAccess.Read), fss.CreateDeobfuscator()))
            {
                dataLayoutTOC = dbReader.ReadDbObject();
            }

            var baseNum = 0u;
            var headNum = 0u;
            baseNum = dataLayoutTOC.GetValue("base", 0u);
            headNum = dataLayoutTOC.GetValue("head", 0u);

            return baseNum + headNum;
        }

        public long GetExeWriteTime()
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            if (!Directory.Exists(fss.BasePath))
                return 0;

            var installLogText = Path.Combine(fss.BasePath, "__Installer", $"InstallLog.txt");
            if (File.Exists(installLogText))
            {
                return File.GetLastWriteTimeUtc(installLogText).ToFileTimeUtc();
            }

            var exePath = Path.Combine(fss.BasePath, $"{ProfileManager.Instance.ExecutableName}.exe");
            if (File.Exists(exePath))
            {
                return File.GetLastWriteTimeUtc(exePath).ToFileTimeUtc();
            }

            
            return 0;
        }
    }
}
