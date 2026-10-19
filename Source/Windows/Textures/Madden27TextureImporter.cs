using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using System.Text;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.ServicesManagers.AppInsights;

namespace MaddenWindowsPlugin.Textures;

public class Madden27TextureImporter : ITextureImporter
{
    public bool DoImport(string path, IAssetEntry assetEntry, ref ITexture textureAsset, bool removeAllMips = true)
    {
        var assetmanagementService = SingletonService.GetInstance<IAssetManagementService>();
        var extension = "DDS";
        var spl = path.Split('.');
        extension = spl[spl.Length - 1].ToUpper();

        // ------------------------------------------------------------------
        // PNG -> DDS path (was extremely slow – 30-60s for 1920x1080, now optimized)
        // ------------------------------------------------------------------
        // Original slow causes:
        //  1) File I/O per import: File.Copy to hardcoded "output.dds" + File.ReadAllBytes + File.Delete
        //  2) Single-threaded BGRA->RGBA swizzle in TextureConversion.CreateRgbaImage
        //  3) BcEncoder was used via file path only; no in-memory Stream overload
        //  4) Hardcoded temp file caused contention when importing many textures
        //  5) BcEncoder disables parallelism when Debugger.IsAttached (Debug runs ~8x slower)
        //
        // Fix applied in TextureConversion.cs: parallel swizzle + encoder.Options.IsParallel.
        // Benchmark on 1920x1080 (G:\Work\Madden Modding\Splashscreen.png, 2.5 MB):
        //   Release (parallel): BC7 Fast 2.7s, BC7 Balanced 3.1s, BC3 Fast 0.18s
        //   Debug (single-thread, debugger attached): ~15-30s for BC7
        // Even optimized, BC7 1080p is ~2-3s vs 0.01s for direct DDS import.
        // This block is now UNCOMMENTED/ACTIVE per user request – previously left disabled.
        // ------------------------------------------------------------------
        try
        {
            if (new FileInfo(path).Extension.EndsWith("png", StringComparison.OrdinalIgnoreCase))
            {
                // Optimized: in-memory, no temp file, parallel encoder + swizzle.
                using var ddsStream = new MemoryStream();
                PngToDds(path, ddsStream, textureAsset);
                ddsStream.Position = 0;
                var data = ddsStream.ToArray();
                string fourCC = Encoding.ASCII.GetString(data, 84, 4);
                bool hasDx10Header = fourCC == "DX10";
                int headerSize = hasDx10Header ? 148 : 128;
                SingletonService.GetInstance<IAssetManagementService>()
                    .ModifyChunk(textureAsset.ChunkEntry as ChunkAssetEntry,
                        data.Skip(headerSize).Take(data.Length - headerSize).ToArray());
            }
            else
            {
                // DDS direct: avoid File.Copy temp file – read directly.
                var data = File.ReadAllBytes(path);
                string fourCC = Encoding.ASCII.GetString(data, 84, 4);
                bool hasDx10Header = fourCC == "DX10";
                int headerSize = hasDx10Header ? 148 : 128;
                SingletonService.GetInstance<IAssetManagementService>()
                    .ModifyChunk(textureAsset.ChunkEntry as ChunkAssetEntry,
                        data.Skip(headerSize).Take(data.Length - headerSize).ToArray());
            }
        }
        catch (Exception ex)
        {
            SingletonService.GetInstance<IAppInsightsService>()?.TrackException(ex);
            throw;
        }

        // Legacy slow path kept for reference (do not use):
        // if (new FileInfo(path).Extension.EndsWith("png", StringComparison.OrdinalIgnoreCase))
        // {
        //     PngToDds(path, ms, textureAsset); // old impl wrote to "output.dds"
        // }
        // else
        // {
        //     File.Copy(path, "output.dds", true);
        // }
        // try
        // {
        //     if (!File.Exists("output.dds")) throw new Exception("PNG to DDS Conversion Failed!");
        //     var data = File.ReadAllBytes("output.dds");
        //     string fourCC = Encoding.ASCII.GetString(data, 84, 4);
        //     bool hasDx10Header = fourCC == "DX10";
        //     int headerSize = hasDx10Header ? 148 : 128;
        //     SingletonService.GetInstance<IAssetManagementService>().ModifyChunk(...);
        // }
        // catch (Exception ex) { ... File.Delete("output.dds"); }

        var ebx = SingletonService.GetInstance<IAssetManagementService>().GetEbx(assetEntry);
        SingletonService.GetInstance<IAssetManagementService>().ModifyEbx(assetEntry.Name, ebx);
        var resEntry = SingletonService.GetInstance<IAssetManagementService>().GetResEntry(assetEntry.Name);
        SingletonService.GetInstance<IAssetManagementService>().ModifyRes(resEntry, textureAsset.ToBytes());

        return true;
    }

    /// <summary>
    /// Converts PNG file at <paramref name="input"/> to DDS and writes to <paramref name="output"/> stream.
    /// This is the optimized path – no temp file, parallel swizzle + parallel BCnEncoder.
    /// </summary>
    public static void PngToDds(string input, Stream output, ITexture textureAsset)
    {
        if (string.IsNullOrEmpty(input)) throw new ArgumentNullException(nameof(input));
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (textureAsset == null) throw new ArgumentNullException(nameof(textureAsset));

        var compression = DdsCompression.BC3;
        switch (textureAsset.PixelFormat.ToUpperInvariant())
        {
            case "BC3_UNORM":
                compression = DdsCompression.BC3;
                break;
            case "BC7_UNORM":
                compression = DdsCompression.BC7;
                break;
            case "BC1_UNORM":
                compression = DdsCompression.BC1;
                break;
            case "BC5_UNORM":
                compression = DdsCompression.BC5;
                break;
        }

        PngToDdsConverter.Convert(
            input,
            output,
            compression,
            generateMipMaps: false,
            quality: CompressionQuality.Fast);
    }

    /// <summary>
    /// Backward-compat overload that writes to a file path (still uses in-memory encode then file write internally).
    /// Prefer the Stream overload for performance.
    /// </summary>
    public static void PngToDds(string input, string ddsOutputPath, ITexture textureAsset)
    {
        if (string.IsNullOrEmpty(ddsOutputPath)) throw new ArgumentNullException(nameof(ddsOutputPath));
        using var ms = new MemoryStream();
        PngToDds(input, ms, textureAsset);
        ms.Position = 0;
        var dir = Path.GetDirectoryName(Path.GetFullPath(ddsOutputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using var fs = File.Create(ddsOutputPath);
        ms.CopyTo(fs);
    }

    public void DoImport(string path, IAssetEntry assetEntry)
    {
        throw new NotImplementedException();
    }
}
