using System.Text.Json;

namespace CodexBar.Core;

public sealed class CredentialFileMissingException(string message) : IOException(message);
public sealed class ClaudeSubscriptionMissingException() : IOException(
    "This CLI file has no Claude subscription OAuth login. If you use the Desktop Code tab, select Claude Desktop in Settings.");

// Deliberately not a record: ToString must never expose credentials.
public sealed class Credentials(string accessToken, string? accountId = null, CodexAccountIdentity? identity = null, string? usageOwner = null)
{
    public string AccessToken { get; } = accessToken;
    public string? AccountId { get; } = accountId;
    public CodexAccountIdentity? Identity { get; } = identity;
    public string? UsageOwner { get; } = usageOwner;
    public override string ToString() => "[Credentials redacted]";
}

public static class CredentialReader
{
    public static string DefaultPath(Provider provider)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configured = Environment.GetEnvironmentVariable(provider == Provider.Codex ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR");
        return Path.Combine(string.IsNullOrWhiteSpace(configured) ?
            Path.Combine(home, provider == Provider.Codex ? ".codex" : ".claude") : configured,
            provider == Provider.Codex ? "auth.json" : ".credentials.json");
    }

    public static async Task<Credentials> ReadAsync(Provider provider, string path, CancellationToken cancellation)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, true);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("Credential file is too large.");
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            return Parse(provider, document.RootElement, DateTimeOffset.UtcNow);
        }
        catch (FileNotFoundException) { throw new CredentialFileMissingException(MissingMessage(provider)); }
        catch (DirectoryNotFoundException) { throw new CredentialFileMissingException(MissingMessage(provider)); }
        catch (UnauthorizedAccessException) { throw new InvalidDataException("Cannot read the credential file. Check its permissions."); }
        catch (JsonException) { throw new InvalidDataException("Credential file is invalid. Sign in again with the provider CLI."); }
    }

    public static Credentials Parse(Provider provider, JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Credential file has an invalid shape.");
        var key = provider == Provider.Codex ? "tokens" : "claudeAiOauth";
        if (!root.TryGetProperty(key, out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            if (provider == Provider.Claude) throw new ClaudeSubscriptionMissingException();
            throw new InvalidDataException($"OAuth credentials are required. Run {Command(provider)}.");
        }
        var access = String(tokens, provider == Provider.Codex ? "access_token" : "accessToken") ??
            (provider == Provider.Codex ? String(tokens, "accessToken") : null);
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidDataException("OAuth access token is missing. Sign in again.");
        if (provider == Provider.Claude && tokens.TryGetProperty("expiresAt", out var expiry)
            && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out var milliseconds) && milliseconds <= now.ToUnixTimeMilliseconds())
            throw new InvalidDataException("Claude credentials have expired. Run claude to renew your session, then refresh.");
        if (provider == Provider.Claude && tokens.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array &&
            !scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == "user:profile"))
            throw new InvalidDataException("Claude token lacks user:profile scope. Sign in again with Claude Code.");
        return new(access.Trim(), provider == Provider.Codex ? String(tokens, "account_id") ?? String(tokens, "accountId") : null,
            provider == Provider.Codex ? CodexAccountIdentity.TryRead(tokens) : null);
    }

    private static string Command(Provider provider) => provider == Provider.Codex ? "codex login" : "claude";
    private static string MissingMessage(Provider provider) => provider == Provider.Claude
        ? "Claude Code CLI credential file was not found. For Desktop's Code tab, select Claude Desktop (live quota) in Settings. Otherwise select the CLI credential file."
        : "Codex credential file was not found. Check its path in Settings or run codex login.";
    private static string? String(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
