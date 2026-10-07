using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexBar.Core;

public sealed class ClaudeDesktopLiveClient(HttpClient http, UsageClient usage,
    Func<CancellationToken, Task<ClaudeDesktopSession>> readSession)
{
    private readonly Dictionary<string, DateTimeOffset> profileBlockedUntil = [];

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellation, Action<string>? observeOwner = null)
    {
        var session = await readSession(cancellation);
        observeOwner?.Invoke(session.Owner);
        var credential = new Credentials(session.Token, usageOwner: "Desktop:" + session.Owner);
        usage.ThrowIfRateLimited(Provider.Claude, credential);
        if (profileBlockedUntil.TryGetValue(session.Owner, out var until) && until > DateTimeOffset.UtcNow)
            throw new InvalidDataException($"Rate limited. Try after {until.ToLocalTime():HH:mm:ss}.");
        ClaudeDesktopVerifiedProfile verified;
        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/profile"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            request.Headers.Accept.Add(new("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter;
                var next = retry?.Date ?? DateTimeOffset.UtcNow.Add(retry?.Delta ?? TimeSpan.FromMinutes(5));
                profileBlockedUntil[session.Owner] = next > DateTimeOffset.UtcNow ? next : DateTimeOffset.UtcNow.AddMinutes(1);
                throw new InvalidDataException("Claude rate limited account verification. Automatic refresh will wait before retrying.");
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ClaudeDesktopSessionException("Claude Desktop's Code session was rejected. Open its Code tab to renew the session, then refresh.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException($"Claude account verification failed (HTTP {(int)response.StatusCode}). Try again later.");
            var bytes = await ReadBounded(response.Content, cancellation);
            try
            {
                using var profile = JsonDocument.Parse(bytes);
                verified = ClaudeDesktopSessionParser.VerifyProfile(profile.RootElement, session);
            }
            catch (JsonException) { throw new ClaudeDesktopSessionException("Claude returned an invalid account verification response. No quota was displayed."); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        UsageSnapshot snapshot;
        try { snapshot = await usage.FetchAsync(Provider.Claude, credential, cancellation); }
        catch (AuthenticationExpiredException)
        { throw new ClaudeDesktopSessionException("Claude Desktop's Code session has expired. Open its Code tab to renew it, then refresh."); }
        // Recheck local context after both requests: a concurrent account/org switch discards this response.
        var current = await readSession(cancellation);
        if (!session.SameLogin(current))
            throw new ClaudeDesktopSessionException("Claude Desktop's account or session changed during refresh. Refresh again for the current account.");
        return snapshot with { Plan = verified.Plan,
            Source = session.ActiveOrganizationVerified ? "Claude Desktop live API" : "Claude Desktop Code live API",
            SourceDetail = $"Verified organization: {verified.OrganizationName ?? "Code session organization"}. " +
                (session.ActiveOrganizationVerified ? "Matches Desktop's selected organization." :
                "Live quota for this Code session. Desktop's selected organization could not be verified while its cookie database is locked.") };
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
            {
                if (result.Length + count > 1024 * 1024) throw new ClaudeDesktopSessionException("Claude account response is too large.");
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
            if (result.TryGetBuffer(out var bytes)) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes.AsSpan());
        }
    }
}
