using FMT.Db;
using FMT.FileTools.Readers;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin.Cache
{
    internal class Madden26CacheHelpers
    {
        public string GetCachePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "_GameCaches", "Madden26.cache");
        }

        public ulong GetSystemIteration()
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            var layoutFiles = Directory.GetFiles(fss.BasePath, "*layout.toc", new EnumerationOptions() { RecurseSubdirectories = true }).ToList();
            layoutFiles = layoutFiles.Where(x => !x.Contains("ModData")).ToList();

            string dataPath = fss.ResolvePath("native_data/layout.toc");
            string patchPath = fss.ResolvePath("native_patch/layout.toc");

            DbObject dataLayoutTOC = null;
            using (DbReader dbReader = new(new FileStream(dataPath, FileMode.Open, FileAccess.Read), fss.CreateDeobfuscator()))
            {
                dataLayoutTOC = dbReader.ReadDbObject();
            }

            var baseNum = 0u;
            var headNum = 0u;
            if (patchPath != "")
            {
                DbObject patchLayoutTOC = null;
                using (DbReader dbReader2 = new(new FileStream(patchPath, FileMode.Open, FileAccess.Read), fss.CreateDeobfuscator()))
                {
                    patchLayoutTOC = dbReader2.ReadDbObject();
                }
                baseNum = patchLayoutTOC.GetValue("base", 0u);
                headNum = patchLayoutTOC.GetValue("head", 0u);
            }
            else
            {
                baseNum = dataLayoutTOC.GetValue("base", 0u);
                headNum = dataLayoutTOC.GetValue("head", 0u);
            }

            return baseNum + headNum;
        }
    }
}
