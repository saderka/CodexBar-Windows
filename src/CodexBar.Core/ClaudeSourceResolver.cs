namespace CodexBar.Core;

public enum ClaudeUsageSource { Automatic = 0, Cli = 1, DesktopCache = 2, DesktopLive = 3 }

public static class ClaudeSourceResolver
{
    public static async Task<UsageSnapshot> FetchAsync(string credentialPath, string defaultCredentialPath,
        ClaudeUsageSource source, bool allowDesktopFallback,
        Func<CancellationToken, Task<Credentials>> readCredentials,
        Func<Credentials, CancellationToken, Task<UsageSnapshot>> fetchApi,
        Func<CancellationToken, Task<UsageSnapshot?>> readCache, CancellationToken cancellation,
        Func<CancellationToken, Task<UsageSnapshot>>? fetchDesktopLive = null)
    {
        if (source == ClaudeUsageSource.DesktopLive)
            return await (fetchDesktopLive ?? throw new ClaudeDesktopSessionException("Claude Desktop live quota is unavailable."))(cancellation);
        if (source == ClaudeUsageSource.DesktopCache)
            return await readCache(cancellation) ?? throw new InvalidDataException(
                "No Claude Desktop quota cache found. Open Desktop Settings > Usage, then refresh. Desktop may not record a cache on this version.");
        Credentials credentials;
        try { credentials = await readCredentials(cancellation); }
        catch (Exception ex) when (source == ClaudeUsageSource.Automatic && allowDesktopFallback && fetchDesktopLive is not null &&
            ex is CredentialFileMissingException or ClaudeSubscriptionMissingException &&
            string.Equals(Path.GetFullPath(credentialPath), Path.GetFullPath(defaultCredentialPath), StringComparison.OrdinalIgnoreCase))
        {
            return await fetchDesktopLive(cancellation);
        }
        // Expired tokens, malformed files, API denial and custom account paths do not switch sources.
        return await fetchApi(credentials, cancellation);
    }
}
