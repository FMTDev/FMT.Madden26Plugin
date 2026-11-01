using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.FileTools;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using Madden26Plugin.TOC;

namespace Madden26Plugin
{

    public class Madden26AssetLoader : IAssetLoader
    {
        static Madden26AssetLoader()
        {
            SingletonService.GetInstance<IFileSystemService>().TOCFileType = typeof(Madden26TOCFile);
        }
        private struct CasBundlesWithTocIndex
        {
            public int Index { get; set; }
            public List<CASBundle> Bundles { get; set; }

            public CasBundlesWithTocIndex(int index, List<CASBundle> bundles)
            {
                Index = index;
                Bundles = bundles;
            }
        }

        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();
        
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        public void LoadData(IEnumerable<string> superBundles, string folder = "native_data/")
        {
            Dictionary<string, CasBundlesWithTocIndex> casBundles = new();
            List<Madden26TOCFile> tocFiles = new List<Madden26TOCFile>();

            foreach (string bundle in superBundles)
            {
                var tocFileRAW = $"{folder}{bundle}.toc";
                string tocFileLocation = fss.ResolvePath(tocFileRAW);
                if (!string.IsNullOrEmpty(tocFileLocation) && File.Exists(tocFileLocation))
                {
                    Madden26TOCFile tocFile = new(tocFileRAW, true, true, false, -1, true);
                    if (tocFile.CasBundles != null)
                    {
                        foreach (var casBundle in tocFile.CasBundles)
                        {
                            var filePath = SingletonService.GetInstance<IFileSystemService>().GetFilePath(casBundle.Catalog, casBundle.Cas, casBundle.Patch);
                            _ = filePath;
                            if (!casBundles.TryGetValue(filePath, out var bundleEntry))
                            {
                                casBundles[filePath] = bundleEntry = new CasBundlesWithTocIndex(tocFiles.Count, new List<CASBundle>());
                            }

                            bundleEntry.Bundles.Add(casBundle);
                        }
                    }

                    tocFiles.Add(tocFile);
                }
            }


            int numCompletedBundles = 0;
            var logUpdate = new Action(() =>
            {
                if (assetManagementService != null)
                {
                    assetManagementService.Logger.Log($"Loading data from Cas [{Math.Round((double)numCompletedBundles / casBundles.Count * 100).ToString()}%]");
                }
            });

            var allTasks = new List<Task>();

            foreach (var casBundle in casBundles)
            {
                tocFiles[casBundle.Value.Index].DoLogging = false;
                CASDataReader casDataLoader = new(tocFiles[casBundle.Value.Index]);
                var filePath = SingletonService.GetInstance<IFileSystemService>().ResolvePath(casBundle.Key);
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                {
                    // Check if the file size exceeds 1 GB (1024 * 1024 * 1024 bytes)
                    if (fileInfo.Length > 1024 * 1024 * 1024)
                    {
                        using (var nr = new NativeReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                        {
                            casDataLoader.ReadFromReader(casBundle.Key, casBundle.Value.Bundles, null, nr);
                            numCompletedBundles++; logUpdate();
                        }
                    }
                    else
                    {
                        var ms = new MemoryStream(File.ReadAllBytes(filePath));
                        using (var nr = new NativeReader(ms))
                        {
                            casDataLoader.ReadFromReader(casBundle.Key, casBundle.Value.Bundles, null, nr);
                            numCompletedBundles++; logUpdate();
                        }
                    }
                }
                else
                {
                    numCompletedBundles++; logUpdate();
                }
            }

            Task.WaitAll(allTasks);

            foreach (var tocFile in tocFiles)
            {
                tocFile.Dispose();
            }

            tocFiles.Clear();
        }



        public IEnumerable<IAssetEntry> Load(IEnumerable<string> superBundles)
        {
            fss.TOCFileType = typeof(Madden26TOCFile);
            LoadData(superBundles);
            return null;
        }
    }


}
