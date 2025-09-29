using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin
{

    public class Madden26AssetLoader : IAssetLoader
    {
        public void LoadData(IEnumerable<string> superBundles, string folder = "native_data")
        {
            var ffs = SingletonService.GetInstance<IFileSystemService>();
            foreach (var sbName in superBundles)
            {
                var tocFileRAW = $"{folder}/{sbName}.toc";
                string tocFileLocation = ffs.ResolvePath(tocFileRAW);
                if (!string.IsNullOrEmpty(tocFileLocation) && File.Exists(tocFileLocation))
                {
                    Madden26TOCFile tocFile = new(tocFileRAW, true, true, false, -1, false);
                    tocFile.Dispose();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    GC.WaitForFullGCComplete();
                }
            }
        }

        public IEnumerable<IAssetEntry> Load(IEnumerable<string> superBundles)
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            fss.TOCFileType = typeof(Madden26TOCFile);
            LoadData(superBundles);
            return null;
        }
    }


}
