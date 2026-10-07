using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CodexBar.Windows;

// Desktop Electron uses Windows DPAPI without CodexBar vault entropy.
internal static class NativeDpapi
{
    public static byte[] Unprotect(byte[] bytes)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output))
                throw new CryptographicException("Claude Desktop session cannot be opened by this Windows user.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); _ = LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
