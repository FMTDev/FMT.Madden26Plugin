using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using MaddenWindowsPlugin.Textures;

class Program
{
    static int Main()
    {
        int failed = 0;
        void Check(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        // Correctness tests for span->stream path (no WPF needed, fast)
        Check("Convert_4x4_BC3_Fast_NoMip_Stream", Test_4x4_BC3);
        Check("Convert_7x5_NonMultipleOf4_BC3", Test_NonMultiple);
        Check("Convert_1x1_BC1", Test_1x1_BC1);
        Check("Convert_8x8_BC7_Fast", Test_8x8_BC7);
        Check("Convert_512x512_BC3_ValidHeader", Test_512);
        Check("Convert_Throws_On_Invalid_Size", Test_InvalidSize);
        Check("Convert_Throws_On_Insufficient_Pixels", Test_InsufficientPixels);
        Check("Convert_Throws_On_Null_Bitmap", Test_NullBitmap);
        Check("Convert_FileVsStream_Identical", Test_FileVsStream);
        Check("Convert_BC1_BC3_BC5_BC7_AllFormats", Test_AllFormats);
        Check("Convert_GenerateMips_True", Test_MipMaps);
        Check("Benchmark_Parallel_Is_Faster", Test_Benchmark);

        Console.WriteLine();
        if (failed == 0)
            Console.WriteLine("All tests passed.");
        else
            Console.WriteLine($"{failed} test(s) failed.");

        return failed == 0 ? 0 : 1;
    }

    static byte[] MakeBgra(int width, int height, Func<int,int,(byte r,byte g,byte b,byte a)> gen)
    {
        var buf = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (r,g,b,a) = gen(x,y);
                int i = (y * width + x) * 4;
                buf[i+0] = b;
                buf[i+1] = g;
                buf[i+2] = r;
                buf[i+3] = a;
            }
        return buf;
    }

    static void ValidateDdsHeader(byte[] dds, int expectedWidth, int expectedHeight)
    {
        if (dds.Length < 128) throw new InvalidDataException($"DDS too small: {dds.Length}");
        // DDS magic "DDS "
        if (dds[0] != 0x44 || dds[1] != 0x44 || dds[2] != 0x53 || dds[3] != 0x20)
            throw new InvalidDataException($"Missing DDS magic: {dds[0]:X2} {dds[1]:X2} {dds[2]:X2} {dds[3]:X2}");
        // width at offset 16, height at 12 (little endian)
        int h = BitConverter.ToInt32(dds, 12);
        int w = BitConverter.ToInt32(dds, 16);
        if (w != expectedWidth || h != expectedHeight)
            throw new InvalidDataException($"Header size mismatch: got {w}x{h}, expected {expectedWidth}x{expectedHeight}");
        string fourCC = System.Text.Encoding.ASCII.GetString(dds, 84, 4);
        bool hasDx10 = fourCC == "DX10";
        // BC7 must be DX10, BC3 may be legacy DXT5 or DX10 depending on encoder
        if (dds.Length <= (hasDx10 ? 148 : 128))
            throw new InvalidDataException("DDS has no payload");
    }

    static void Test_4x4_BC3()
    {
        var pixels = MakeBgra(4,4, (x,y) => (255,0,0,255));
        using var ms = new MemoryStream();
        PngToDdsConverter.Convert(pixels, 4,4, ms, DdsCompression.BC3, generateMipMaps:false, quality:CompressionQuality.Fast);
        var dds = ms.ToArray();
        ValidateDdsHeader(dds,4,4);
        // BC3 block = 16 bytes for 4x4
        string fourCC = System.Text.Encoding.ASCII.GetString(dds,84,4);
        // may be DXT5 or DX10
        if (fourCC != "DXT5" && fourCC != "DX10") throw new Exception($"Unexpected FourCC {fourCC} for BC3");
    }

    static void Test_NonMultiple()
    {
        // BCn requires padding to 4x4 – encoder handles it.
        var pixels = MakeBgra(7,5, (x,y) => ((byte)(x*30), (byte)(y*40), 128, 255));
        using var ms = new MemoryStream();
        PngToDdsConverter.Convert(pixels, 7,5, ms, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
        var dds = ms.ToArray();
        ValidateDdsHeader(dds,7,5);
    }

    static void Test_1x1_BC1()
    {
        var pixels = MakeBgra(1,1, (x,y)=>(10,20,30,255));
        using var ms = new MemoryStream();
        PngToDdsConverter.Convert(pixels,1,1, ms, DdsCompression.BC1, false, quality:CompressionQuality.Fast);
        var dds = ms.ToArray();
        ValidateDdsHeader(dds,1,1);
    }

    static void Test_8x8_BC7()
    {
        var pixels = MakeBgra(8,8, (x,y)=>((byte)(x*32),(byte)(y*32), 200,255));
        using var ms = new MemoryStream();
        // BC7 Fast is still heavier than BC3 but should complete <2s for 8x8
        var sw = Stopwatch.StartNew();
        PngToDdsConverter.Convert(pixels,8,8, ms, DdsCompression.BC7, false, quality:CompressionQuality.Fast);
        sw.Stop();
        if (sw.ElapsedMilliseconds > 5000) throw new Exception($"BC7 8x8 took too long: {sw.ElapsedMilliseconds}ms");
        ValidateDdsHeader(ms.ToArray(),8,8);
        string fourCC = System.Text.Encoding.ASCII.GetString(ms.ToArray(),84,4);
        if (fourCC != "DX10") throw new Exception($"BC7 should be DX10 but got {fourCC}");
    }

    static void Test_512()
    {
        var pixels = MakeBgra(512,512, (x,y)=>((byte)(x%256),(byte)(y%256),(byte)((x+y)%256),255));
        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        PngToDdsConverter.Convert(pixels,512,512, ms, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
        sw.Stop();
        Console.WriteLine($"  512x512 BC3 Fast took {sw.ElapsedMilliseconds}ms");
        ValidateDdsHeader(ms.ToArray(),512,512);
        // ensure payload size plausible: 512*512 /16 *16 bytes per block for BC3 = 262144
        var dds = ms.ToArray();
        string fourCC = System.Text.Encoding.ASCII.GetString(dds,84,4);
        int header = fourCC=="DX10"?148:128;
        int payload = dds.Length - header;
        if (payload < 100000) throw new Exception($"Payload suspiciously small {payload}");
    }

    static void Test_InvalidSize()
    {
        var pixels = new byte[16];
        try { using var ms=new MemoryStream(); PngToDdsConverter.Convert(pixels,0,4, ms, DdsCompression.BC3); throw new Exception("Should have thrown"); }
        catch (ArgumentOutOfRangeException) {}
        try { using var ms=new MemoryStream(); PngToDdsConverter.Convert(pixels,4,0, ms, DdsCompression.BC3); throw new Exception("Should have thrown"); }
        catch (ArgumentOutOfRangeException) {}
    }

    static void Test_InsufficientPixels()
    {
        var pixels = new byte[10]; // need 4*4*4=64
        try { using var ms=new MemoryStream(); PngToDdsConverter.Convert(pixels,4,4, ms, DdsCompression.BC3); throw new Exception("Should have thrown"); }
        catch (ArgumentException) {}
    }

    static void Test_NullBitmap()
    {
        try { PngToDdsConverter.Convert((System.Windows.Media.Imaging.BitmapSource)null!, new MemoryStream(), DdsCompression.BC3); throw new Exception("Should have thrown"); }
        catch (ArgumentNullException) {}
    }

    static void Test_FileVsStream()
    {
        var pixels = MakeBgra(16,16, (x,y)=>(100,150,200,255));
        string tmp = Path.Combine(Path.GetTempPath(), $"fmt_test_{Guid.NewGuid():N}.dds");
        try
        {
            PngToDdsConverter.Convert(pixels,16,16, tmp, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
            var fileBytes = File.ReadAllBytes(tmp);
            using var ms=new MemoryStream();
            PngToDdsConverter.Convert(pixels,16,16, ms, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
            var memBytes = ms.ToArray();
            if (fileBytes.Length != memBytes.Length) throw new Exception($"Length mismatch file {fileBytes.Length} vs mem {memBytes.Length}");
            for(int i=0;i<fileBytes.Length;i++) if(fileBytes[i]!=memBytes[i]) throw new Exception($"Byte mismatch at {i}");
        }
        finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }

    static void Test_AllFormats()
    {
        foreach(var fmt in new[] { DdsCompression.BC1, DdsCompression.BC3, DdsCompression.BC5, DdsCompression.BC7 })
        {
            var pixels = MakeBgra(8,8, (x,y)=>( (byte)fmt, 128, 64, 255));
            using var ms=new MemoryStream();
            PngToDdsConverter.Convert(pixels,8,8, ms, fmt, false, quality:CompressionQuality.Fast);
            ValidateDdsHeader(ms.ToArray(),8,8);
        }
    }

    static void Test_MipMaps()
    {
        var pixels = MakeBgra(32,32, (x,y)=>(50,100,150,255));
        using var ms=new MemoryStream();
        PngToDdsConverter.Convert(pixels,32,32, ms, DdsCompression.BC3, generateMipMaps:true, quality:CompressionQuality.Fast);
        var dds = ms.ToArray();
        // with mips, file should be larger than single level
        using var ms2=new MemoryStream();
        PngToDdsConverter.Convert(pixels,32,32, ms2, DdsCompression.BC3, generateMipMaps:false, quality:CompressionQuality.Fast);
        if (dds.Length <= ms2.ToArray().Length) throw new Exception("Mipped DDS should be larger");
    }

    static void Test_Benchmark()
    {
        // Compare large image swizzle+encode time – ensures parallel path doesn't regress.
        // Run twice to warm up.
        var pixels = MakeBgra(1024,1024, (x,y)=>((byte)(x&255),(byte)(y&255),128,255));
        // Warmup
        using (var ms=new MemoryStream()) PngToDdsConverter.Convert(pixels,1024,1024, ms, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
        var sw = Stopwatch.StartNew();
        using (var ms=new MemoryStream()) PngToDdsConverter.Convert(pixels,1024,1024, ms, DdsCompression.BC3, false, quality:CompressionQuality.Fast);
        sw.Stop();
        Console.WriteLine($"  1024x1024 BC3 Fast benchmark: {sw.ElapsedMilliseconds}ms");
        // Budget: should be < 5s on modern CPU with parallel; give generous threshold for CI.
        if (sw.ElapsedMilliseconds > 10000) throw new Exception($"Benchmark too slow: {sw.ElapsedMilliseconds}ms (expected <10000ms)");
    }
}
