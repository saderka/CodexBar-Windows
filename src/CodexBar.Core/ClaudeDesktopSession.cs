using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexBar.Core;

public sealed class ClaudeDesktopSessionException(string message) : IOException(message);

// Never use a record here: generated ToString would expose the bearer token.
public sealed class ClaudeDesktopSession(string token, string account, string organization, long expiresAt, bool activeOrganizationVerified = true)
{
    public string Token { get; } = token;
    public string Account { get; } = account;
    public string Organization { get; } = organization;
    public long ExpiresAt { get; } = expiresAt;
    public bool ActiveOrganizationVerified { get; } = activeOrganizationVerified;
    public string Owner => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Account + "\0" + Organization)));
    public bool SameLogin(ClaudeDesktopSession other) => Owner == other.Owner && Token == other.Token &&
        ActiveOrganizationVerified == other.ActiveOrganizationVerified;
    public override string ToString() => "[Claude Desktop session redacted]";
}

public static class ClaudeDesktopSessionParser
{
    public const string CodeClient = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly string[] CodeScopes = ["user:inference", "user:file_upload", "user:profile", "user:sessions:claude_code"];

    public static string Identity(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new ClaudeDesktopSessionException("Claude Desktop's active account could not be verified. Open Desktop and its Code tab, then refresh.");
        return id.ToString("D");
    }

    public static ClaudeDesktopSession Select(JsonElement cache, string account, string organization, DateTimeOffset now)
    {
        account = Identity(account); organization = Identity(organization);
        if (cache.ValueKind != JsonValueKind.Object) throw InvalidSession();
        var candidates = new List<JsonElement>();
        foreach (var entry in cache.EnumerateObject())
        {
            // Desktop constructs acct:<account>|<client>:<org>:<host>:<scopes>.
            // Legacy unbound entries cannot establish the current account.
            var prefix = $"acct:{account}|{CodeClient}:{organization}:https://api.anthropic.com:";
            if (!entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ValidScopes(entry.Name[prefix.Length..])) continue;
            // A null entry is a logout tombstone, not permission to use another cached login.
            candidates.Add(entry.Value);
        }
        if (candidates.Count != 1 || candidates[0].ValueKind != JsonValueKind.Object) throw InvalidSession();
        var item = candidates[0];
        if (!item.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(token.GetString()) || token.GetString()!.Length > 8192 ||
            token.GetString()!.Any(char.IsControl) ||
            !item.TryGetProperty("expiresAt", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out var expiresAt)) throw InvalidSession();
        if (expiresAt <= now.ToUnixTimeMilliseconds())
            throw new ClaudeDesktopSessionException("Claude Desktop's Code session has expired. Open the Code tab to renew it, then refresh.");
        return new(token.GetString()!, account, organization, expiresAt);
    }

    // Used only when Chromium reports an exclusive file lock. This proves the Code
    // credential's organization after profile verification, not Desktop's selected org.
    public static ClaudeDesktopSession SelectUniqueCodeSession(JsonElement cache, string account, DateTimeOffset now)
    {
        account = Identity(account);
        if (cache.ValueKind != JsonValueKind.Object) throw InvalidSession();
        var prefix = $"acct:{account}|{CodeClient}:";
        const string separator = ":https://api.anthropic.com:";
        var organizations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in cache.EnumerateObject())
        {
            if (!entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = entry.Name[prefix.Length..];
            var position = rest.IndexOf(separator, StringComparison.Ordinal);
            if (position < 0 || !ValidScopes(rest[(position + separator.Length)..])) continue;
            organizations.Add(Identity(rest[..position]));
        }
        if (organizations.Count != 1) throw InvalidSession();
        var selected = Select(cache, account, organizations.Single(), now);
        return new(selected.Token, selected.Account, selected.Organization, selected.ExpiresAt, false);
    }

    public static ClaudeDesktopVerifiedProfile VerifyProfile(JsonElement profile, ClaudeDesktopSession session)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("organization", out var org) || org.ValueKind != JsonValueKind.Object ||
            !account.TryGetProperty("uuid", out var a) || a.ValueKind != JsonValueKind.String ||
            !org.TryGetProperty("uuid", out var o) || o.ValueKind != JsonValueKind.String ||
            Identity(a.GetString()) != session.Account || Identity(o.GetString()) != session.Organization)
            throw new ClaudeDesktopSessionException("Claude Desktop's session belongs to a different account or organization. Reopen its Code tab, then refresh.");
        var type = org.TryGetProperty("organization_type", out var plan) && plan.ValueKind == JsonValueKind.String ? plan.GetString() : null;
        var planName = type switch { "claude_max" => "max", "claude_pro" => "pro", "claude_team" => "team", "claude_enterprise" => "enterprise", _ => null };
        var name = org.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (name is not null) name = new string(name.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();
        return new(planName, name);
    }

    private static bool ValidScopes(string text)
    {
        var scopes = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length &&
            CodeScopes.All(s => scopes.Contains(s, StringComparer.Ordinal)) &&
            scopes.All(s => CodeScopes.Contains(s, StringComparer.Ordinal) || s == "user:plugins");
    }

    private static ClaudeDesktopSessionException InvalidSession() => new(
        "No unambiguous Claude Desktop Code session for the active account. Open Desktop's Code tab, then refresh. No other account was used.");
}

public sealed record ClaudeDesktopVerifiedProfile(string? Plan, string? OrganizationName);
