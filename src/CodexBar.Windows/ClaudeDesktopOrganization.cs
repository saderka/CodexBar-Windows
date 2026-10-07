using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CodexBar.Core;

namespace CodexBar.Windows;

internal sealed class ClaudeDesktopCookieLockedException() : IOException("Claude Desktop organization cookie is exclusively locked.");

internal static class ClaudeDesktopOrganization
{
    // Read the active organization cookie only. Never enumerate or extract session cookies.
    public static string Read(string database, byte[] key, Action<string>? diagnostic = null)
    {
        IntPtr connection = IntPtr.Zero;
        try
        {
            var opened = sqlite3_open_v2(database, out connection, 1, IntPtr.Zero);
            diagnostic?.Invoke($"Organization database open: {opened}");
            if (opened != 0 && connection != IntPtr.Zero)
            {
                var systemError = sqlite3_system_errno(connection);
                diagnostic?.Invoke($"Database system error: {systemError}");
                if (systemError == 32) throw new ClaudeDesktopCookieLockedException();
            }
            if (opened != 0) throw Unavailable();
            _ = sqlite3_busy_timeout(connection, 300);
            Exec(connection, "BEGIN"); // One consistent read transaction, including the WAL.
            var version = 0;
            Query(connection, "SELECT value FROM meta WHERE key='version'", row =>
            {
                if (!int.TryParse(Text(row, 0), out version)) throw Unavailable();
            });
            diagnostic?.Invoke($"Organization cookie schema: {version}");
            var organizations = new HashSet<string>(StringComparer.Ordinal);
            var rows = 0;
            Query(connection, "SELECT host_key,value,encrypted_value,expires_utc FROM cookies WHERE name='lastActiveOrg' AND host_key IN ('.claude.ai','claude.ai') AND path='/' LIMIT 9", row =>
            {
                if (++rows > 8) throw Unavailable();
                var expires = sqlite3_column_int64(row, 3);
                var chromiumNow = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 11644473600000L) * 1000;
                diagnostic?.Invoke($"Organization cookie present; expired={expires != 0 && expires <= chromiumNow}; plain-value-present={sqlite3_column_bytes(row, 1) > 0}; encrypted-bytes={sqlite3_column_bytes(row, 2)}");
                if (expires != 0 && expires <= chromiumNow) return;
                var value = Text(row, 1);
                if (value.Length == 0)
                {
                    var encrypted = Bytes(row, 2);
                    byte[]? clear = null;
                    try
                    {
                        clear = ClaudeDesktopCipher.Decrypt(encrypted, key, NativeDpapi.Unprotect);
                        var host = Text(row, 0);
                        var payload = clear.AsSpan();
                        if (version >= 24)
                        {
                            if (payload.Length < 32 || !CryptographicOperations.FixedTimeEquals(payload[..32], SHA256.HashData(Encoding.UTF8.GetBytes(host))))
                                throw Unavailable();
                            payload = payload[32..];
                        }
                        value = Encoding.UTF8.GetString(payload);
                    }
                    finally { CryptographicOperations.ZeroMemory(encrypted); if (clear != null) CryptographicOperations.ZeroMemory(clear); }
                }
                organizations.Add(ClaudeDesktopSessionParser.Identity(value));
            });
            diagnostic?.Invoke($"Distinct active organizations: {organizations.Count}");
            if (organizations.Count != 1) throw Unavailable();
            return organizations.Single();
        }
        finally { if (connection != IntPtr.Zero) _ = sqlite3_close_v2(connection); }
    }

    private static ClaudeDesktopSessionException Unavailable() => new(
        "Claude Desktop's active organization is unavailable. Open Desktop and its Code tab, then refresh.");
    private static void Exec(IntPtr db, string sql)
    {
        var status = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error);
        if (status != 0) throw Unavailable();
    }
    private static void Query(IntPtr db, string sql, Action<IntPtr> row)
    {
        IntPtr statement = IntPtr.Zero;
        try
        {
            if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != 0) throw Unavailable();
            int status;
            while ((status = sqlite3_step(statement)) == 100) row(statement);
            if (status != 101) throw Unavailable();
        }
        finally { if (statement != IntPtr.Zero) _ = sqlite3_finalize(statement); }
    }
    private static string Text(IntPtr row, int column)
    {
        var count = sqlite3_column_bytes(row, column);
        if (count > 8192) throw Unavailable();
        return Marshal.PtrToStringUTF8(sqlite3_column_text(row, column), count) ?? "";
    }
    private static byte[] Bytes(IntPtr row, int column)
    {
        var count = sqlite3_column_bytes(row, column);
        if (count is <= 0 or > 8192) throw Unavailable();
        var result = new byte[count]; Marshal.Copy(sqlite3_column_blob(row, column), result, 0, count); return result;
    }
    private const string Sqlite = "winsqlite3.dll";
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_system_errno(IntPtr db);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, out IntPtr error);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr pointer);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
}
