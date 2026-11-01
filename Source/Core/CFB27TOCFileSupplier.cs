using FMT.PluginInterfaces;
using FMT.ProfileSystem;
using Madden26Plugin.TOC;
using System;
using System.Collections.Generic;
using System.Text;

namespace Madden26Plugin;

public class MaddenPluginTOCFileSupplier : ITOCFileSupplier
{
    public Type GetTOCFileType()
    {
        switch (ProfileManager.Instance.Name)
        {
            case "Madden26":
                return typeof(Madden26TOCFile);
            case "Madden27":
            case "CFB27":
                return typeof(Madden27TOCFile);
            default:
                throw new NotImplementedException($"TOCFileSupplier not implemented for profile: {ProfileManager.Instance.Name}");
        }
    }
}
