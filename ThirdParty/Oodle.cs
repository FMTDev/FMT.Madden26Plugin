using System.Buffers;
using System.Runtime.InteropServices;

namespace Madden26Plugin.ThirdParty
{
    public static class Oodle
    {
        public enum OodleCompressionLevel : ushort
        {
            NONE,
            SUPER_FAST,
            VERY_FAST,
            FAST,
            NORMAL,
            OPTIMAL1,
            OPTIMAL2,
            OPTIMAL3,
            OPTIMAL4,
            OPTIMAL5
        }

        public enum OodleFormat : uint
        {
            LZH = 0,
            LZHLW = 1,
            LZNIB = 2,
            None = 3,
            LZB16 = 4,
            LZBLW = 5,
            LZA = 6,
            LZNA = 7,
            Kraken = 8,
            Mermaid = 9,
            BitKnit = 10,
            Selkie = 11,
            Hydra = 12,
            Leviathan = 13
        }

        // https://www.zenhax.com/viewtopic.php?t=14842

        public delegate int DecompressFunc(IntPtr srcBuffer, long srcSize, IntPtr dstBuffer, long dstSize, int a5 = 0, int a6 = 0, long a7 = 0L, long a8 = 0L, long a9 = 0L, long a10 = 0L, long a11 = 0L, long a12 = 0L, long a13 = 0L, int a14 = 3);

        public delegate long CompressFuncWithCompLevel(OodleFormat cmpCode, IntPtr srcBuffer, long srcSize, IntPtr cmpBuffer, OodleCompressionLevel cmpLevel, IntPtr options = new IntPtr(), IntPtr dictionaryBase = new IntPtr(), IntPtr lrm = new IntPtr(), IntPtr scratch = new IntPtr(), long scratchSize = 0);

        public static DecompressFunc Decompress;

        public static CompressFuncWithCompLevel CompressWithCompLevel;

        public delegate IntPtr GetDefaultOptions(OodleFormat cmpCode, OodleCompressionLevel cmpLevel);
        public static GetDefaultOptions GetOptions;

        public delegate long MemorySizeNeededFunc(int a1, long a2);
        public static MemorySizeNeededFunc MemorySizeNeeded;

        internal static LoadLibraryHandle handle;

        public static bool IsBound { get; set; }

        public static void Bind(string basePath, int? specificVersion = null)
        {
            if (IsBound)
                return;

            string lib = Directory.EnumerateFiles(basePath, "oo2core_*").FirstOrDefault();
            if (lib == null)
            {
                if (specificVersion == null)
                    lib = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "ThirdParty"), "oo2core_*", enumerationOptions: new EnumerationOptions() { RecurseSubdirectories = true }).LastOrDefault();
                else
                    lib = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "ThirdParty"), $"oo2core_{specificVersion}*", enumerationOptions: new EnumerationOptions() { RecurseSubdirectories = true }).LastOrDefault(x => x.Contains(specificVersion.ToString()));

            }

            if (!string.IsNullOrEmpty(lib) && File.Exists(lib))
            {
                handle = new LoadLibraryHandle(lib);
                if (!(handle == IntPtr.Zero))
                {
                    Decompress = Marshal.GetDelegateForFunctionPointer<DecompressFunc>(NativeLibrary.GetExport(handle, "OodleLZ_Decompress"));
                    CompressWithCompLevel = Marshal.GetDelegateForFunctionPointer<CompressFuncWithCompLevel>(NativeLibrary.GetExport(handle, "OodleLZ_Compress"));
                    GetOptions = Marshal.GetDelegateForFunctionPointer<GetDefaultOptions>(NativeLibrary.GetExport(handle, "OodleLZ_CompressOptions_GetDefault"));
                    MemorySizeNeeded = Marshal.GetDelegateForFunctionPointer<MemorySizeNeededFunc>(NativeLibrary.GetExport(handle, "OodleLZDecoder_MemorySizeNeeded"));
                }
            }
            else
            {

            }

            IsBound = true;
        }

        public static ulong Compress(byte[] buffer, out byte[] compBuffer, out ushort compressCode, out bool uncompressed)
        {
            var result = CompressLeviathan(buffer, out compBuffer, out compressCode, out uncompressed);
            return result;
        }

        public static ulong CompressKraken(byte[] buffer, out byte[] compBuffer, out ushort compressCode, out bool uncompressed)
        {
            uncompressed = false;
            ArgumentNullException.ThrowIfNull(buffer, "buffer");
            compBuffer = ArrayPool<byte>.Shared.Rent(524288);
            compressCode = 6512;
            GCHandle gCHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                GCHandle gCHandle2 = GCHandle.Alloc(compBuffer, GCHandleType.Pinned);
                try
                {
                    ulong compressedSize = (ulong)Oodle.CompressWithCompLevel(Oodle.OodleFormat.Kraken, gCHandle.AddrOfPinnedObject(), buffer.Length, gCHandle2.AddrOfPinnedObject(), Oodle.OodleCompressionLevel.NORMAL);
                    if (compressedSize > (ulong)buffer.Length)
                    {
                        uncompressed = true;
                        compressedSize = 0uL;
                    }
                    return compressedSize;
                }
                finally
                {
                    gCHandle2.Free();
                }
            }
            finally
            {
                gCHandle.Free();
            }
        }

        public static ulong CompressLeviathan(byte[] buffer, out byte[] compBuffer, out ushort compressCode, out bool uncompressed)
        {
            uncompressed = false;
            ArgumentNullException.ThrowIfNull(buffer, "buffer");
            compBuffer = ArrayPool<byte>.Shared.Rent(524288);
            compressCode = 6512;
            GCHandle gCHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                GCHandle gCHandle2 = GCHandle.Alloc(compBuffer, GCHandleType.Pinned);
                try
                {
                    ulong compressedSize = (ulong)Oodle.CompressWithCompLevel(Oodle.OodleFormat.Leviathan, gCHandle.AddrOfPinnedObject(), buffer.Length, gCHandle2.AddrOfPinnedObject(), Oodle.OodleCompressionLevel.OPTIMAL5);
                    if (compressedSize > (ulong)buffer.Length)
                    {
                        uncompressed = true;
                        compressedSize = 0uL;
                    }
                    return compressedSize;
                }
                finally
                {
                    gCHandle2.Free();
                }
            }
            finally
            {
                gCHandle.Free();
            }
        }
    }
}
