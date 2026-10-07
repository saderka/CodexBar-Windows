using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBar.Core;
using CodexBar.Windows;

internal static class ClaudeDesktopWindowsTests
{
    private const string Account = "11111111-1111-4111-8111-111111111111";
    private const string Org = "33333333-3333-4333-8333-333333333333";
    private const string OtherOrg = "44444444-4444-4444-8444-444444444444";
    private const string Token = "synthetic-native-desktop-token";
    private static string CacheKey(string org = Org) => $"acct:{Account}|{ClaudeDesktopSessionParser.CodeClient}:{org}:https://api.anthropic.com:user:inference user:file_upload user:profile user:sessions:claude_code";
    private static object Entry() => new { token = Token, expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() };

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "codexbar-desktop-check-" + Guid.NewGuid().ToString("N"));
        var database = Path.Combine(root, "Network", "Cookies");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = "DPAPI"u8.ToArray().Concat(Protect(key)).ToArray();
            File.WriteAllText(Path.Combine(root, "Local State"), JsonSerializer.Serialize(new { os_crypt = new { encrypted_key = Convert.ToBase64String(wrapped) } }));
            var map = new Dictionary<string, object?> { [CacheKey()] = Entry() };
            WriteConfig(root, key, map);
            Execute(database, "CREATE TABLE meta(key TEXT,value TEXT); INSERT INTO meta VALUES('version','24'); " +
                "CREATE TABLE cookies(host_key TEXT,name TEXT,path TEXT,value TEXT,encrypted_value BLOB,expires_utc INTEGER); " +
                $"INSERT INTO cookies VALUES('.claude.ai','lastActiveOrg','/','{Org}',X'',0); " +
                "INSERT INTO cookies VALUES('.claude.ai','sessionKey','/','',X'BADBAD',0); " +
                "INSERT INTO cookies VALUES('.example.test','lastActiveOrg','/','unrelated',X'',0);");
            var paths = new[] { Path.Combine(root, "config.json"), Path.Combine(root, "Local State"), database };
            var before = paths.Select(File.ReadAllBytes).ToArray();
            var session = await ClaudeDesktopSessionReader.ReadAsync(CancellationToken.None, root);
            check(session.Token == Token && session.ActiveOrganizationVerified && session.Organization == Org,
                "Windows reader decrypts DPAPI/AES session and reads only Claude active-org cookie");
            check(paths.Select((p, i) => File.ReadAllBytes(p).AsSpan().SequenceEqual(before[i])).All(x => x),
                "Windows session reading leaves config, Local State and cookie database byte-for-byte unchanged");

            var payload = SHA256.HashData(Encoding.UTF8.GetBytes(".claude.ai")).Concat(Encoding.UTF8.GetBytes(Org)).ToArray();
            Execute(database, $"UPDATE cookies SET value='',encrypted_value=X'{Convert.ToHexString(Encrypt(payload, key))}' WHERE name='lastActiveOrg' AND host_key='.claude.ai';");
            session = await ClaudeDesktopSessionReader.ReadAsync(CancellationToken.None, root);
            check(session.ActiveOrganizationVerified && session.Organization == Org, "Windows Chromium v24 cookie host hash and encrypted organization decoded");
            payload[0] ^= 1;
            Execute(database, $"UPDATE cookies SET encrypted_value=X'{Convert.ToHexString(Encrypt(payload, key))}' WHERE name='lastActiveOrg' AND host_key='.claude.ai';");
            await Deny(root, "Wrong encrypted cookie host hash fails closed", check);
            Execute(database, $"UPDATE cookies SET value='{Org}',encrypted_value=X'',expires_utc=1 WHERE name='lastActiveOrg' AND host_key='.claude.ai';");
            await Deny(root, "Expired active-org cookie does not select a historical Code organization", check);
            Execute(database, "UPDATE cookies SET expires_utc=0 WHERE name='lastActiveOrg';");

            WriteConfig(root, key, new() { [CacheKey()] = null }, map);
            await Deny(root, "Desktop V2 logout tombstone cannot fall back to valid V1 credentials", check);
            WriteConfig(root, key, map);
            using (var locked = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                session = await ClaudeDesktopSessionReader.ReadAsync(CancellationToken.None, root);
                check(!session.ActiveOrganizationVerified && session.Token == Token,
                    "Actual Windows exclusive cookie lock uses qualified unique Code session fallback");
                WriteConfig(root, key, new() { [CacheKey()] = Entry(), [CacheKey(OtherOrg)] = Entry() });
                await Deny(root, "Locked cookie plus several Code organizations cannot silently pick one", check);
            }
            WriteConfig(root, key, map);
            File.Delete(database);
            await Deny(root, "Absent organization database does not masquerade as a file-lock fallback", check);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await ClaudeDesktopSessionReader.ReadAsync(cancellation.Token, root); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(true, "Desktop native/file reader preserves cancellation"); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            // Fixture root is created above, never a discovered or user-supplied profile.
            Directory.Delete(root, true);
        }
    }

    private static void WriteConfig(string root, byte[] key, Dictionary<string, object?> v2, Dictionary<string, object?>? v1 = null)
    {
        var config = new Dictionary<string, object?> { ["lastKnownAccountUuid"] = Account,
            ["oauth:tokenCacheV2"] = Convert.ToBase64String(Encrypt(JsonSerializer.SerializeToUtf8Bytes(v2), key)) };
        if (v1 != null) config["oauth:tokenCache"] = Convert.ToBase64String(Encrypt(JsonSerializer.SerializeToUtf8Bytes(v1), key));
        File.WriteAllText(Path.Combine(root, "config.json"), JsonSerializer.Serialize(config));
    }
    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        var encrypted = new byte[plain.Length + 31]; "v10"u8.CopyTo(encrypted); RandomNumberGenerator.Fill(encrypted.AsSpan(3, 12));
        using var aes = new AesGcm(key, 16); aes.Encrypt(encrypted.AsSpan(3, 12), plain, encrypted.AsSpan(15, plain.Length), encrypted.AsSpan(encrypted.Length - 16));
        return encrypted;
    }
    private static async Task Deny(string root, string name, Action<bool, string> check)
    {
        try { await ClaudeDesktopSessionReader.ReadAsync(CancellationToken.None, root); }
        catch (ClaudeDesktopSessionException ex) { check(!ex.Message.Contains(Token), name); return; }
        throw new Exception("FAIL: " + name);
    }
    private static void Execute(string database, string sql)
    {
        if (sqlite3_open_v2(database, out var db, 6, IntPtr.Zero) != 0) throw new Exception("Fixture database failed");
        try
        {
            var status = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) sqlite3_free(error);
            if (status != 0) throw new Exception("Fixture SQL failed");
        }
        finally { _ = sqlite3_close_v2(db); }
    }
    private static byte[] Protect(byte[] bytes)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            if (!CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)) throw new CryptographicException("Fixture DPAPI failed");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); _ = LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr memory);
}
