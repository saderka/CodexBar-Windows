using System.Globalization;
using System.Text.Json;

namespace CodexBar.Core;

public enum Provider { Codex, Claude }
public sealed record UsageWindow(string Name, double UsedPercent, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}
public sealed record UsageSnapshot(Provider Provider, string? Plan, IReadOnlyList<UsageWindow> Windows,
    DateTimeOffset FetchedAt, double? Credits = null, string Source = "Provider API", string? SourceDetail = null);

public static class UsageParser
{
    public static UsageSnapshot Parse(Provider provider, string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid quota response shape.");
        var windows = new List<UsageWindow>();
        string? plan = null;
        double? credits = null;
        if (provider == Provider.Codex)
        {
            plan = Text(root, "plan_type");
            if (root.TryGetProperty("rate_limit", out var limits) && limits.ValueKind == JsonValueKind.Object)
            {
                Add(limits, "primary_window", "Session", "used_percent", true, windows);
                Add(limits, "secondary_window", "Weekly", "used_percent", true, windows);
            }
            if (root.TryGetProperty("additional_rate_limits", out var extras) && extras.ValueKind == JsonValueKind.Array)
                foreach (var extra in extras.EnumerateArray())
                {
                    if (extra.ValueKind != JsonValueKind.Object || !extra.TryGetProperty("rate_limit", out var lanes)
                        || lanes.ValueKind != JsonValueKind.Object) continue;
                    var title = Text(extra, "limit_name") ?? Text(extra, "metered_feature") ?? "Additional limit";
                    Add(lanes, "primary_window", title + " · Session", "used_percent", true, windows);
                    Add(lanes, "secondary_window", title + " · Weekly", "used_percent", true, windows);
                }
            if (root.TryGetProperty("credits", out var credit) && credit.ValueKind == JsonValueKind.Object &&
                credit.TryGetProperty("balance", out var balance))
            {
                if (balance.ValueKind == JsonValueKind.Number && balance.TryGetDouble(out var number) && double.IsFinite(number)) credits = number;
                else if (balance.ValueKind == JsonValueKind.String && double.TryParse(balance.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) && double.IsFinite(number)) credits = number;
            }
        }
        else
        {
            Add(root, "five_hour", "Session (5 hours)", "utilization", false, windows);
            Add(root, "seven_day", "Weekly", "utilization", false, windows);
            Add(root, "seven_day_sonnet", "Weekly · Sonnet", "utilization", false, windows);
            Add(root, "seven_day_opus", "Weekly · Opus", "utilization", false, windows);
            Add(root, "seven_day_oauth_apps", "Weekly · OAuth apps", "utilization", false, windows);
            if (root.TryGetProperty("limits", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object ||
                        (Text(entry, "kind") != "weekly_scoped" && Text(entry, "group") != "weekly")) continue;
                    if (entry.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.False) continue;
                    string? modelName = null;
                    if (entry.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object &&
                        scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
                        modelName = Text(model, "display_name") ?? Text(model, "id");
                    // Wrap to reuse the same tolerant window decoder.
                    using var wrapper = JsonDocument.Parse("{\"lane\":" + entry.GetRawText() + "}");
                    Add(wrapper.RootElement, "lane", "Weekly · " + (modelName ?? "Scoped"), "percent", false, windows);
                }
        }
        if (windows.Count == 0 && credits is null)
            throw new InvalidDataException("No supported quota windows in the response. The provider format or account plan may have changed.");
        return new(provider, plan, windows, now, credits);
    }

    private static void Add(JsonElement root, string key, string name, string percentKey,
        bool unixReset, List<UsageWindow> result)
    {
        if (!root.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!window.TryGetProperty(percentKey, out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var value)
            || !double.IsFinite(value) || value < 0) return;
        if (unixReset && window.TryGetProperty("limit_window_seconds", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetInt64(out var seconds))
        {
            var period = seconds == 604800 ? "Weekly" : seconds == 18000 ? "Session (5 hours)" : $"Window ({seconds / 3600d:0.#} hours)";
            name = name.Contains(" · ") ? name[..(name.LastIndexOf(" · ", StringComparison.Ordinal) + 3)] + period : period;
        }
        DateTimeOffset? resets = null;
        if (window.TryGetProperty(unixReset ? "reset_at" : "resets_at", out var reset))
        {
            if (unixReset && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var timestamp))
            {
                try { resets = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
                catch (ArgumentOutOfRangeException) { /* Optional timestamp. */ }
            }
            else if (!unixReset && reset.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date)) resets = date;
        }
        result.Add(new(name, value, resets));
    }

    private static string? Text(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public static class ResetText
{
    public static string Format(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "Reset time unavailable";
        var remaining = reset.Value - now;
        if (remaining <= TimeSpan.Zero) return "Reset due · refresh to confirm";
        return remaining.TotalDays >= 1 ? $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h" :
            remaining.TotalHours >= 1 ? $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m" :
            $"Resets in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
}
