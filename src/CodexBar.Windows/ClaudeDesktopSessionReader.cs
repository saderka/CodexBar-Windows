using System.Security.Cryptography;
using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.Windows;

internal static class ClaudeDesktopSessionReader
{
    public static async Task<ClaudeDesktopSession> ReadAsync(CancellationToken cancellation, string? explicitProfile = null, Action<string>? diagnostic = null)
    {
        byte[]? key = null, encrypted = null, clear = null;
        try
        {
            var profile = explicitProfile ?? await FindProfile(cancellation);
            using var config = await ReadJson(Path.Combine(profile, "config.json"), cancellation);
            using var state = await ReadJson(Path.Combine(profile, "Local State"), cancellation);
            var account = ClaudeDesktopSessionParser.Identity(config.RootElement.GetProperty("lastKnownAccountUuid").GetString());
            key = ClaudeDesktopCipher.ReadKey(state.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!, NativeDpapi.Unprotect);
            // Presence of V2 is authoritative, including logout tombstones. Never fall back to V1 on failure.
            var cache = config.RootElement.TryGetProperty("oauth:tokenCacheV2", out var v2) ? v2 :
                config.RootElement.GetProperty("oauth:tokenCache");
            if (cache.ValueKind != JsonValueKind.String) throw new JsonException();
            encrypted = Convert.FromBase64String(cache.GetString()!);
            clear = ClaudeDesktopCipher.Decrypt(encrypted, key, NativeDpapi.Unprotect);
            using var document = JsonDocument.Parse(clear);
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var organization = ClaudeDesktopOrganization.Read(Path.Combine(profile, "Network", "Cookies"), key, diagnostic);
                return ClaudeDesktopSessionParser.Select(document.RootElement, account, organization, DateTimeOffset.UtcNow);
            }
            catch (ClaudeDesktopCookieLockedException)
            {
                diagnostic?.Invoke("Cookie locked: using uniquely bound Code credential; active Desktop organization remains unverified.");
                return ClaudeDesktopSessionParser.SelectUniqueCodeSession(document.RootElement, account, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException or UnauthorizedAccessException or IOException && ex is not ClaudeDesktopSessionException)
        {
            throw new ClaudeDesktopSessionException("Cannot read Claude Desktop's current Code session. Open Desktop's Code tab, then refresh. Its stored login was left unchanged.");
        }
        finally
        {
            if (key != null) CryptographicOperations.ZeroMemory(key);
            if (encrypted != null) CryptographicOperations.ZeroMemory(encrypted);
            if (clear != null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static async Task<string> FindProfile(CancellationToken cancellation)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // Only official Desktop locations, never arbitrary browsers or third-party profiles.
        var candidates = new[] { Path.Combine(roaming, "Claude"),
            Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude") };
        var available = new List<string>();
        foreach (var profile in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(profile, "config.json");
            if (!File.Exists(path)) continue;
            using var config = await ReadJson(path, cancellation);
            if (config.RootElement.TryGetProperty("lastKnownAccountUuid", out var account) && account.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(account.GetString(), "D", out var id) && id != Guid.Empty) available.Add(profile);
        }
        if (available.Count != 1) throw new ClaudeDesktopSessionException(available.Count == 0
            ? "Claude Desktop is not signed in at a supported Windows location. Open Desktop's Code tab, then refresh."
            : "Several signed-in Claude Desktop profiles were found. Select the active Desktop profile folder in Settings.");
        return available[0];
    }

    private static async Task<JsonDocument> ReadJson(string path, CancellationToken cancellation)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length > 4 * 1024 * 1024) throw new ClaudeDesktopSessionException("Claude Desktop session data is too large.");
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
    }
}
