using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Madden26Plugin
{
    public class Madden26CustomAssetEntryEnumerations : ICustomAssetEntryEnumerations
    {
        private IAssetManagementService assetManagementService => SingletonService.GetInstance<IAssetManagementService>();

        public Dictionary<string, IEnumerable<IAssetEntry>> GetCustomAssetEntriesEnumerations()
        {
            var jerseys = assetManagementService
                .EnumerateEbx()
                .Where(x => 
                
                    x.GetPath().Contains("content/characters/player/parts/uniforms", StringComparison.OrdinalIgnoreCase)
                    || x.GetPath().Contains("content/characters/player/parts/uniforms/jerseys", StringComparison.OrdinalIgnoreCase)
                    || x.GetPath().Contains("contentshared/characters/player/parts/uniforms", StringComparison.OrdinalIgnoreCase)

                )
                .OrderBy(x => x.GetPath())
                .Select(x => (IAssetEntry)x).ToList();


            Dictionary<string, IEnumerable<IAssetEntry>> assetCollections = new();
            assetCollections.Add("Jersey", jerseys);


            return assetCollections;
        }
    }
}
