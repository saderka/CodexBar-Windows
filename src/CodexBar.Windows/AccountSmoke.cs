using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.Windows;

internal static class AccountSmoke
{
    public static bool Run()
    {
        var smoke = Path.Combine(AppContext.BaseDirectory, "smoke");
        var diagnostic = Path.Combine(smoke, "accounts-diagnostic.txt");
        if (File.Exists(diagnostic)) File.Delete(diagnostic);
        var root = Path.Combine(smoke, "account-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = false;
        try
        {
            var bytes = Encoding.UTF8.GetBytes("synthetic-DPAPI-roundtrip");
            var ciphertext = WindowsAccountProtection.Protect(bytes);
            if (ciphertext.AsSpan().SequenceEqual(bytes) || !WindowsAccountProtection.Unprotect(ciphertext).AsSpan().SequenceEqual(bytes)) return false;
            ciphertext[^1] ^= 1;
            try { _ = WindowsAccountProtection.Unprotect(ciphertext); return false; }
            catch (CryptographicException) { }
            var service = new CodexAccountService(Path.Combine(root, "vault")); service.Prepare();
            var a = Auth("a", "synthetic-a"); var b = Auth("b", "synthetic-b");
            var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
            var path = Path.Combine(home, "auth.json");
            File.WriteAllBytes(path, a);
            var first = service.Vault.Save(a, "Personal"); var second = service.Vault.Save(b, "Work");
            CodexAuthFileSwitcher.Switch(service.Vault, second.Id, path);
            CodexAuthFileSwitcher.Switch(service.Vault, first.Id, path);
            passed = File.ReadAllBytes(path).AsSpan().SequenceEqual(a) &&
                Directory.EnumerateFiles(service.Root).All(file => !Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("synthetic-"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(smoke, "accounts-diagnostic.txt"), ex.ToString());
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(smoke) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException();
            Directory.Delete(root, true);
            File.WriteAllText(Path.Combine(smoke, "accounts-result.txt"), passed
                ? "PASS: Windows DPAPI roundtrip/tamper rejection; secured vault; synthetic A-B-A activation; no plaintext vault tokens"
                : "FAIL: Windows account smoke");
        }
        return passed;
    }
    private static byte[] Auth(string user, string token)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { sub = user, email = user + "@example.test" })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.SerializeToUtf8Bytes(new { tokens = new { account_id = "synthetic-workspace", access_token = token, refresh_token = "synthetic-refresh", id_token = "e30." + payload + ".synthetic" } });
    }
}
