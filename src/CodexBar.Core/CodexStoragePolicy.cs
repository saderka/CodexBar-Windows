namespace CodexBar.Core;

public static class CodexStoragePolicy
{
    // Conservative line parser: unknown/ambiguous auth settings fail closed. It is
    // not a general TOML parser. File is the documented default when no setting exists.
    public static void RequireFileStorage(string? config)
    {
        if (config == null) return;
        var root = true;
        string? mode = null;
        foreach (var raw in config.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[')) root = false;
            if (!line.Contains("cli_auth_credentials_store", StringComparison.Ordinal)) continue;
            var pair = line.Split('=', 2);
            var key = pair[0].Trim();
            if (!root || pair.Length != 2 || (key != "cli_auth_credentials_store" && key != "\"cli_auth_credentials_store\"" && key != "'cli_auth_credentials_store'") || mode != null)
                throw new InvalidDataException("Account switching requires an unambiguous file-based Codex login setting.");
            var value = pair[1].Trim();
            if (value != "\"file\"" && value != "'file'")
                throw new InvalidDataException("This Codex home uses a different credential store. Account switching supports cli_auth_credentials_store = \"file\" only; its config was not changed.");
            mode = "file";
        }
    }

    private static string StripComment(string raw)
    {
        char quote = '\0';
        var escaped = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (escaped) { escaped = false; continue; }
            if (quote == '"' && c == '\\') { escaped = true; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '#') return raw[..i];
        }
        return raw;
    }
}
