using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;

namespace MaddenWindowsPlugin.Textures;

public enum DdsCompression
{
    BC1,
    BC3,
    BC5,
    BC7
}

public static class PngToDdsConverter
{
    /// <summary>
    /// Converts a PNG file to a DDS file on disk.
    /// </summary>
    public static void Convert(
        string pngPath,
        string ddsPath,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException("PNG file not found.", pngPath);

        using FileStream input = File.OpenRead(pngPath);

        BitmapDecoder decoder = new PngBitmapDecoder(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The PNG contains no image frames.");

        BitmapSource bitmap = decoder.Frames[0];

        Convert(
            bitmap,
            ddsPath,
            compression,
            generateMipMaps,
            maxMipMapLevel,
            quality);
    }

    /// <summary>
    /// Converts a PNG file to a DDS via <see cref="Stream"/> (no temp file).
    /// </summary>
    public static void Convert(
        string pngPath,
        Stream outputStream,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException("PNG file not found.", pngPath);
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        using FileStream input = File.OpenRead(pngPath);

        BitmapDecoder decoder = new PngBitmapDecoder(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The PNG contains no image frames.");

        BitmapSource bitmap = decoder.Frames[0];

        Convert(
            bitmap,
            outputStream,
            compression,
            generateMipMaps,
            maxMipMapLevel,
            quality);
    }

    /// <summary>
    /// Converts a PNG byte array to a DDS file on disk.
    /// </summary>
    public static void Convert(
        byte[] pngData,
        string ddsPath,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        using MemoryStream stream = new(pngData);

        BitmapDecoder decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The PNG contains no image frames.");

        BitmapSource bitmap = decoder.Frames[0];

        Convert(
            bitmap,
            ddsPath,
            compression,
            generateMipMaps,
            maxMipMapLevel,
            quality);
    }

    /// <summary>
    /// Converts a PNG byte array to a DDS <see cref="Stream"/>.
    /// </summary>
    public static void Convert(
        byte[] pngData,
        Stream outputStream,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
        using MemoryStream stream = new(pngData);

        BitmapDecoder decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The PNG contains no image frames.");

        BitmapSource bitmap = decoder.Frames[0];

        Convert(
            bitmap,
            outputStream,
            compression,
            generateMipMaps,
            maxMipMapLevel,
            quality);
    }

    /// <summary>
    /// Converts a WPF BitmapSource to a DDS file on disk.
    /// </summary>
    public static void Convert(
        BitmapSource bitmap,
        string ddsPath,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (ddsPath == null) throw new ArgumentNullException(nameof(ddsPath));
        var dir = Path.GetDirectoryName(Path.GetFullPath(ddsPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using FileStream output = File.Create(ddsPath);
        Convert(bitmap, output, compression, generateMipMaps, maxMipMapLevel, quality);
    }

    /// <summary>
    /// Converts a WPF BitmapSource to a DDS <see cref="Stream"/>.
    /// This is the preferred overload – it avoids the temporary file that the
    /// legacy <c>PngToDds(string, string)</c> path used (see importer comments about perf).
    /// </summary>
    public static void Convert(
        BitmapSource bitmap,
        Stream outputStream,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));
        if (outputStream == null)
            throw new ArgumentNullException(nameof(outputStream));

        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            throw new ArgumentException("Bitmap has an invalid size.", nameof(bitmap));

        BitmapSource rgbaBitmap = new FormatConvertedBitmap(
            bitmap,
            PixelFormats.Bgra32,
            null,
            0);

        int width = rgbaBitmap.PixelWidth;
        int height = rgbaBitmap.PixelHeight;

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        rgbaBitmap.CopyPixels(
            pixels,
            stride,
            0);

        Convert(
            (ReadOnlySpan<byte>)pixels,
            width,
            height,
            outputStream,
            compression,
            generateMipMaps,
            maxMipMapLevel,
            quality);
    }

    /// <summary>
    /// Converts raw BGRA8 pixel data (as produced by WPF's Bgra32) to a DDS file on disk.
    /// The span is expected to be BGRA order (WPF native) – converted to RGBA for the encoder.
    /// </summary>
    public static void Convert(
        ReadOnlySpan<byte> bgraPixels,
        int width,
        int height,
        string ddsPath,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (ddsPath == null) throw new ArgumentNullException(nameof(ddsPath));
        var dir = Path.GetDirectoryName(Path.GetFullPath(ddsPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using FileStream output = File.Create(ddsPath);
        Convert(bgraPixels, width, height, output, compression, generateMipMaps, maxMipMapLevel, quality);
    }

    /// <summary>
    /// Converts raw BGRA8 pixel data to a DDS <see cref="Stream"/>.
    /// This is the hot path: encoder + pixel swizzle. Both are now parallelized.
    /// </summary>
    public static void Convert(
        ReadOnlySpan<byte> bgraPixels,
        int width,
        int height,
        Stream outputStream,
        DdsCompression compression,
        bool generateMipMaps = true,
        int maxMipMapLevel = 0,
        CompressionQuality quality = CompressionQuality.Balanced)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        if (outputStream == null)
            throw new ArgumentNullException(nameof(outputStream));

        int expectedSize = checked(width * height * 4);

        if (bgraPixels.Length < expectedSize)
        {
            throw new ArgumentException(
                $"Expected at least {expectedSize:N0} bytes of RGBA data, " +
                $"but received {bgraPixels.Length:N0}.",
                nameof(bgraPixels));
        }

        BcEncoder encoder = new();

        encoder.OutputOptions.GenerateMipMaps = generateMipMaps;
        encoder.OutputOptions.Quality = quality;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

        if (maxMipMapLevel > 0)
            encoder.OutputOptions.MaxMipMapLevel = maxMipMapLevel;

        encoder.OutputOptions.Format = compression switch
        {
            DdsCompression.BC1 => CompressionFormat.Bc1,
            DdsCompression.BC3 => CompressionFormat.Bc3,
            DdsCompression.BC5 => CompressionFormat.Bc5,
            DdsCompression.BC7 => CompressionFormat.Bc7,

            _ => throw new ArgumentOutOfRangeException(nameof(compression))
        };

        // Ensure BCnEncoder itself runs in parallel (disabled automatically when debugger attached).
        // Forcing IsParallel and using all cores gives ~2-4x speedup on BC7/BC3.
        encoder.Options.IsParallel = true;
        // 0 lets the library pick ProcessorCount; set explicitly if you want to cap.
        // encoder.Options.TaskCount = Environment.ProcessorCount;

        // BCnEncoder's core API works with Memory2D<ColorRgba32>.
        // CreateRgbaImage is the second hot spot after the encoder – now parallelized.
        // For very large images this avoids the ~100ms+ single-threaded BGRA->RGBA swizzle.
        Memory2D<ColorRgba32> image = CreateRgbaImageParallel(
            bgraPixels,
            width,
            height);

        encoder.EncodeToStream(image, outputStream);
    }

    /// <summary>
    /// Fast BGRA (WPF) -> RGBA (encoder) conversion.
    /// Original code was a single-threaded nested loop over width*height.
    /// This version is parallelized for height >= threshold. We copy the span to an array
    /// to allow capturing in Parallel.For (ref structs cannot be captured in lambdas).
    /// The extra copy is ~1-2ms for 1024x1024 and is dwarfed by BC7 encode time (seconds).
    /// </summary>
    private static Memory2D<ColorRgba32> CreateRgbaImageParallel(
        ReadOnlySpan<byte> pixels,
        int width,
        int height)
    {
        ColorRgba32[] rgba = new ColorRgba32[width * height];

        const int ParallelHeightThreshold = 64;

        // Copy once so we can capture a reference type in Parallel.For.
        // For the common WPF path pixels already comes from a byte[], so this is one extra 4MB copy for 1024x1024.
        byte[] src = pixels.ToArray();

        if (height >= ParallelHeightThreshold)
        {
            Parallel.For(0, height, y =>
            {
                int srcBase = y * width * 4;
                int dstBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    int si = srcBase + x * 4;
                    // BGRA -> RGBA
                    rgba[dstBase + x] = new ColorRgba32(
                        src[si + 2],
                        src[si + 1],
                        src[si + 0],
                        src[si + 3]);
                }
            });
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                int srcBase = y * width * 4;
                int dstBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    int si = srcBase + x * 4;
                    rgba[dstBase + x] = new ColorRgba32(
                        src[si + 2],
                        src[si + 1],
                        src[si + 0],
                        src[si + 3]);
                }
            }
        }

        return new Memory2D<ColorRgba32>(rgba, height, width);
    }

    // Kept for backward compat / tests that may call via reflection.
    private static Memory2D<ColorRgba32> CreateRgbaImage(
        ReadOnlySpan<byte> pixels,
        int width,
        int height) => CreateRgbaImageParallel(pixels, width, height);
}
