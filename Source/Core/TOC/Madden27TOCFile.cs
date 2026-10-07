using FMT.Core;
using FMT.Core.Models.TOC;
using FMT.FileTools;
using FMT.Hash;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System.IO;
using System.Text;

namespace Madden26Plugin.TOC
{
    public class Madden27TOCFile : Madden26TOCFile
    {
        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();
        private IFileSystemService fss => SingletonService.GetInstance<IFileSystemService>();

        /// <summary>
        /// Used if you want to Read TOC without the normal way
        /// </summary>
        public Madden27TOCFile(string nativeFilePath) : base(nativeFilePath)
        {

        }

        /// <summary>
        /// Reads the TOC file and process any data within it (Chunks) and its Bundles (In Cas files)
        /// </summary>
        /// <param name="nativeFilePath"></param>
        /// <param name="log"></param>
        /// <param name="process"></param>
        /// <param name="modDataPath"></param>
        /// <param name="sbIndex"></param>
        /// <param name="headerOnly">If true then do not read/process Cas Bundles</param>
        public Madden27TOCFile(string nativeFilePath, bool log = true, bool process = true, bool modDataPath = false, int sbIndex = -1, bool headerOnly = false)
            : base(nativeFilePath, log, process, modDataPath, sbIndex, headerOnly)
        {

        }

        public Madden27TOCFile(Stream tocStream, bool log = true, bool process = true, bool modDataPath = false, int sbIndex = -1, bool headerOnly = false)
            : base(tocStream, log, process, modDataPath, sbIndex, headerOnly)
        {

        }
    }
   
}
