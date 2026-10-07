using System.Net;
using System.Net.Http.Headers;

namespace CodexBar.Core;

public sealed class AuthenticationExpiredException() : IOException("Session expired. Sign in again with the provider CLI, then refresh.");

public sealed class UsageClient(HttpClient http)
{
    private readonly Dictionary<string, DateTimeOffset> blockedUntil = [];

    public async Task<UsageSnapshot> FetchAsync(Provider provider, Credentials credentials, CancellationToken cancellation)
    {
        var accountKey = AccountKey(provider, credentials);
        ThrowIfRateLimited(provider, credentials);
        var uri = provider == Provider.Codex ? "https://chatgpt.com/backend-api/wham/usage" :
            "https://api.anthropic.com/api/oauth/usage";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Accept.Add(new("application/json"));
        request.Headers.UserAgent.ParseAdd(provider == Provider.Codex ? "CodexBar" : "claude-code/2.1.0");
        if (provider == Provider.Codex && !string.IsNullOrWhiteSpace(credentials.AccountId))
            request.Headers.Add("ChatGPT-Account-Id", credentials.AccountId);
        if (provider == Provider.Claude) request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var next = retry?.Date ?? DateTimeOffset.UtcNow.Add(retry?.Delta ?? TimeSpan.FromMinutes(5));
            blockedUntil[accountKey] = next > DateTimeOffset.UtcNow ? next : DateTimeOffset.UtcNow.AddMinutes(1);
            throw new InvalidDataException("Provider rate limited this request. Automatic refresh will wait before retrying.");
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthenticationExpiredException();
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidDataException("Provider denied quota access for this account (HTTP 403).");
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"Provider request failed (HTTP {(int)response.StatusCode}). Try again later.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellation)) > 0)
        {
            if (buffer.Length + count > 1024 * 1024) throw new InvalidDataException("Provider response is too large.");
            buffer.Write(bytes, 0, count);
        }
        try { return UsageParser.Parse(provider, System.Text.Encoding.UTF8.GetString(buffer.ToArray()), DateTimeOffset.UtcNow); }
        catch (System.Text.Json.JsonException) { throw new InvalidDataException("Provider returned an invalid quota response."); }
    }

    public void ThrowIfRateLimited(Provider provider, Credentials credentials)
    {
        if (blockedUntil.TryGetValue(AccountKey(provider, credentials), out var until) && until > DateTimeOffset.UtcNow)
            throw new InvalidDataException($"Rate limited. Try after {until.ToLocalTime():HH:mm:ss}.");
    }

    private static string AccountKey(Provider provider, Credentials credentials) => provider + ":" +
        (credentials.Identity?.Key ?? credentials.UsageOwner ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(credentials.AccessToken + "\0" + credentials.AccountId))));
}
