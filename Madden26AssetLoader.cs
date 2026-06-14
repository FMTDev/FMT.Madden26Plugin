using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.FileTools;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace Madden26Plugin
{

    public class Madden26AssetLoader : IAssetLoader
    {
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
                            if (!casBundles.ContainsKey(filePath))
                            {
                                casBundles.Add(filePath, new CasBundlesWithTocIndex(tocFiles.Count, new List<CASBundle>()));
                            }

                            casBundles[filePath].Bundles.Add(casBundle);
                        }
                    }

                    tocFiles.Add(tocFile);
                }
            }
            

            foreach (var casBundle in casBundles)
            {
                CASDataReader casDataLoader = new(tocFiles[casBundle.Value.Index]);
                var filePath = SingletonService.GetInstance<IFileSystemService>().ResolvePath(casBundle.Key);
                if (File.Exists(filePath))
                {
                    using (var nr = new NativeReader(filePath))
                    {
                        casDataLoader.ReadFromReader(casBundle.Key, casBundle.Value.Bundles, null, nr);
                    }
                }
            }

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
