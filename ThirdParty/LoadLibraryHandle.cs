using System.Runtime.InteropServices;

namespace Madden26Plugin.ThirdParty
{
    internal class LoadLibraryHandle
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private IntPtr handle;

        public LoadLibraryHandle(string lib)
        {
            handle = LoadLibraryEx(lib, IntPtr.Zero, 0u);
        }

        public static implicit operator IntPtr(LoadLibraryHandle value)
        {
            return value.handle;
        }

        ~LoadLibraryHandle()
        {
            FreeLibrary(handle);
        }
    }
}
