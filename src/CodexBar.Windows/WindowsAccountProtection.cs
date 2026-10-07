using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodexBar.Windows;

internal static class WindowsAccountProtection
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexBarWindows account vault v1");
    public static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = Allocate(bytes);
        var entropy = Allocate(Entropy);
        Blob output = default;
        try
        {
            var success = protect
                ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("This saved account cannot be opened by this Windows user. Sign in again to add it.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Clear(input); Marshal.FreeHGlobal(input.Data);
            Clear(entropy); Marshal.FreeHGlobal(entropy.Data);
            if (output.Data != IntPtr.Zero) { Clear(output); _ = LocalFree(output.Data); }
        }
    }

    private static Blob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new() { Length = bytes.Length, Data = data };
    }
    private static void Clear(Blob blob) { if (blob.Length > 0) Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length); }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
