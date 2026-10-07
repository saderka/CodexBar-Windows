using System.Text.Json;

namespace CodexBar.Core;

// Read quota history only. Never inspect Electron cookies, session storage or auth tokens.
public static class ClaudeDesktopUsage
{
    public static IEnumerable<string> Candidates(string localAppData, string roamingAppData)
    {
        yield return Path.Combine(roamingAppData, "Claude", "plan-usage-history.json");
        var packages = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packages)) yield break;
        foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*").Take(10))
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude", "plan-usage-history.json");
    }

    public static async Task<UsageSnapshot?> ReadAvailableAsync(CancellationToken cancellation)
    {
        foreach (var path in Candidates(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)))
        {
            if (!File.Exists(path)) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, true);
            if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Claude Desktop usage history is too large.");
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            return Parse(doc.RootElement);
        }
        return null;
    }

    public static UsageSnapshot Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var format) || format != 2 ||
            !root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Claude Desktop cache format is unsupported. Use the Claude Code CLI credential source.");
        var organizations = new HashSet<string>();
        JsonElement? newest = null;
        long latest = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            if (sample.ValueKind != JsonValueKind.Object) continue;
            if (sample.TryGetProperty("org", out var org) && org.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(org.GetString()))
                organizations.Add(org.GetString()!);
            if (sample.TryGetProperty("t", out var time) && time.ValueKind == JsonValueKind.Number &&
                time.TryGetInt64(out var timestamp) && timestamp > latest)
            { newest = sample; latest = timestamp; }
        }
        // This history format cannot reliably identify the currently selected organization.
        if (organizations.Count != 1)
            throw new InvalidDataException("Desktop cache has multiple or unknown accounts. Select a Claude Code CLI credential file to avoid mixing accounts.");
        if (newest is not { } entry || !entry.TryGetProperty("u", out var usage) || usage.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Claude Desktop has no recorded quota data yet. Open its Usage view, then refresh here.");
        DateTimeOffset captured;
        try { captured = DateTimeOffset.FromUnixTimeMilliseconds(latest); }
        catch (ArgumentOutOfRangeException) { throw new InvalidDataException("Claude Desktop cache timestamp is invalid."); }
        if (captured > DateTimeOffset.UtcNow.AddMinutes(5)) throw new InvalidDataException("Claude Desktop cache timestamp is in the future.");
        var windows = new List<UsageWindow>();
        Add("fh", "Session (5 hours)"); Add("sd", "Weekly");
        if (windows.Count == 0) throw new InvalidDataException("Claude Desktop cache contains no usable quota values.");
        return new(Provider.Claude, null, windows, captured, Source: "Claude Desktop cache");

        void Add(string key, string name)
        {
            if (usage.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var percent) && double.IsFinite(percent) && percent >= 0 && percent <= 100)
                windows.Add(new(name, percent, null));
        }
    }
}
