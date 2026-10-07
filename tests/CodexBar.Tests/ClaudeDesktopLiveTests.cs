using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBar.Core;

internal static class ClaudeDesktopLiveTests
{
    private const string Account = "11111111-1111-4111-8111-111111111111";
    private const string OtherAccount = "22222222-2222-4222-8222-222222222222";
    private const string Org = "33333333-3333-4333-8333-333333333333";
    private const string OtherOrg = "44444444-4444-4444-8444-444444444444";
    private const string Scopes = "user:inference user:file_upload user:profile user:sessions:claude_code";
    private const string Token = "SYNTHETIC-DESKTOP-SECRET";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public static async Task RunAsync(Action<bool, string> check)
    {
        var map = new Dictionary<string, object?>
        {
            [Key(OtherAccount, Org)] = Entry("wrong-account"),
            [Key(Account, OtherOrg)] = Entry("wrong-org"),
            [Key(Account, Org)] = Entry(Token),
            [$"{ClaudeDesktopSessionParser.CodeClient}:{Org}:https://api.anthropic.com:{Scopes}"] = Entry("legacy-unbound")
        };
        var session = Select(map);
        check(session.Token == Token && session.Account == Account && session.Organization == Org && session.ActiveOrganizationVerified,
            "Desktop selects exact account/org/production Code scope, excluding other and unbound sessions");
        check(!session.ToString().Contains(Token) && !session.Owner.Contains(Account), "Desktop session and owner diagnostics redact credentials and identifiers");
        Deny(() => Select(new() { [Key(Account, Org)] = null }), "Desktop logout tombstone cannot resurrect another session", check);
        Deny(() => Select(new() { [Key(Account, Org)] = Entry(Token, 1) }), "Expired Desktop session requires provider renewal", check);
        Deny(() => Select(new() { [Key(Account, Org)] = Entry(Token), [Key(Account, Org) + " user:plugins"] = Entry("ambiguous") }),
            "Multiple matching Desktop scope variants are refused", check);
        Deny(() => Select(new() { [Key(Account, Org).Replace(Scopes, "user:profile")] = Entry(Token) }),
            "Unrelated Desktop profile token cannot be used as a Code session", check);
        Deny(() => Select(new() { [Key(Account, Org).Replace("api.anthropic.com", "example.test")] = Entry(Token) }),
            "Desktop cache cannot change the fixed Anthropic host", check);
        Deny(() => Select(new() { [Key(Account, Org)] = new { token = Token, expiresAt = "invalid" } }),
            "Malformed Desktop expiry fails without exposing token", check);
        using (var cache = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { [Key(Account, Org)] = Entry(Token) })))
        {
            var unique = ClaudeDesktopSessionParser.SelectUniqueCodeSession(cache.RootElement, Account, Now);
            check(unique.Token == Token && !unique.ActiveOrganizationVerified,
                "Locked-cookie fallback labels unique Code credential without claiming Desktop's selected org");
        }
        using (var cache = JsonDocument.Parse(JsonSerializer.Serialize(map)))
            Deny(() => ClaudeDesktopSessionParser.SelectUniqueCodeSession(cache.RootElement, Account, Now),
                "Locked-cookie fallback refuses multiple Code organizations", check);
        using (var valid = JsonDocument.Parse(Profile()))
            check(ClaudeDesktopSessionParser.VerifyProfile(valid.RootElement, session).Plan == "pro", "Profile verifies both account and organization and provider plan");
        using (var wrong = JsonDocument.Parse(Profile(OtherAccount)))
            Deny(() => ClaudeDesktopSessionParser.VerifyProfile(wrong.RootElement, session), "Profile account mismatch rejected", check);
        using (var wrong = JsonDocument.Parse(Profile(Account, OtherOrg)))
            Deny(() => ClaudeDesktopSessionParser.VerifyProfile(wrong.RootElement, session), "Profile organization mismatch rejected", check);

        var key = RandomNumberGenerator.GetBytes(32);
        var clear = Encoding.UTF8.GetBytes("synthetic Desktop cache");
        var encrypted = new byte[clear.Length + 31]; "v10"u8.CopyTo(encrypted);
        RandomNumberGenerator.Fill(encrypted.AsSpan(3, 12));
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(encrypted.AsSpan(3, 12), clear, encrypted.AsSpan(15, clear.Length), encrypted.AsSpan(encrypted.Length - 16));
        check(ClaudeDesktopCipher.Decrypt(encrypted, key, _ => throw new Exception()).AsSpan().SequenceEqual(clear), "Desktop v10 AES-GCM decrypts authenticated synthetic payload");
        encrypted[^1] ^= 1;
        try { _ = ClaudeDesktopCipher.Decrypt(encrypted, key, _ => throw new Exception()); throw new Exception("Tampering accepted"); }
        catch (CryptographicException) { check(true, "Desktop AES-GCM rejects tampered ciphertext"); }
        try { _ = ClaudeDesktopCipher.Decrypt("v20unsupported"u8.ToArray(), key, _ => throw new Exception()); throw new Exception("Unsupported accepted"); }
        catch (InvalidDataException) { check(true, "Unsupported Desktop cipher fails closed"); }
        CryptographicOperations.ZeroMemory(key);

        const string quota = """{"five_hour":{"utilization":65,"resets_at":"2026-10-06T17:20:00Z"},"seven_day":{"utilization":9,"resets_at":"2026-10-11T09:00:00Z"}}""";
        var successful = new FakeHandler(request =>
        {
            check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.anthropic.com" && request.Headers.Authorization?.Parameter == Token,
                "Desktop requests use read-only fixed-host HTTPS and the selected token");
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/profile") ? Profile() : quota) };
        });
        using (var http = new HttpClient(successful))
        {
            var live = new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(session));
            var result = await live.FetchAsync(CancellationToken.None);
            check(successful.Calls == 2 && result.Source == "Claude Desktop live API" && result.Plan == "pro" && result.Windows[0].RemainingPercent == 35 &&
                result.Windows[1].RemainingPercent == 91 && result.Windows.All(w => w.ResetsAt is not null),
                "Verified Desktop API returns current remaining percentages and real reset timestamps");
        }
        using (var http = new HttpClient(new FakeHandler(request => new(HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/profile") ? Profile() : quota) })))
        {
            var unbound = new ClaudeDesktopSession(Token, Account, Org, Now.AddHours(1).ToUnixTimeMilliseconds(), false);
            var result = await new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(unbound)).FetchAsync(CancellationToken.None);
            check(result.Source == "Claude Desktop Code live API" && result.SourceDetail!.Contains("could not be verified") && result.Windows[0].RemainingPercent == 35,
                "Code fallback displays live quota with explicit active-org qualification");
        }
        var rejected = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Profile(OtherAccount)) });
        using (var http = new HttpClient(rejected))
        {
            await DenyAsync(() => new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(session)).FetchAsync(CancellationToken.None),
                "Wrong API identity never requests quota", check);
            check(rejected.Calls == 1, "Quota request is prevented after profile mismatch");
        }
        using (var http = new HttpClient(new FakeHandler(request => new(HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/profile") ? Profile() : quota) })))
        {
            var reads = 0;
            var changed = new ClaudeDesktopSession("changed", OtherAccount, Org, Now.AddHours(1).ToUnixTimeMilliseconds());
            await DenyAsync(() => new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(++reads == 1 ? session : changed)).FetchAsync(CancellationToken.None),
                "Concurrent Desktop account change discards old API quota", check);
        }
        var limited = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(10)); return response;
        });
        using (var http = new HttpClient(limited))
        {
            var live = new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(session));
            for (var i = 0; i < 2; i++)
                try { await live.FetchAsync(CancellationToken.None); throw new Exception("Expected rate limit"); }
                catch (InvalidDataException) { }
            check(limited.Calls == 1, "Profile Retry-After stops repeated automatic and manual requests");
        }
        var usageLimited = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/profile")) return new(HttpStatusCode.OK) { Content = new StringContent(Profile()) };
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromMinutes(10)); return response;
        });
        using (var http = new HttpClient(usageLimited))
        {
            var rateSession = session;
            var live = new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(rateSession));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1) rateSession = new("rotated-synthetic-token", Account, Org, Now.AddHours(1).ToUnixTimeMilliseconds());
                try { await live.FetchAsync(CancellationToken.None); throw new Exception("Expected rate limit"); }
                catch (InvalidDataException) { }
            }
            check(usageLimited.Calls == 2, "Usage Retry-After stops profile requests even after same-account token rotation");
        }
        using (var http = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent(Token) })))
            await DenyAsync(() => new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(session)).FetchAsync(CancellationToken.None),
                "Desktop 401 clears session with guidance and redacts response body", check);
        using (var http = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new("https://example.test/") } })))
        {
            try { await new ClaudeDesktopLiveClient(http, new(http), _ => Task.FromResult(session)).FetchAsync(CancellationToken.None); throw new Exception("Expected redirect rejection"); }
            catch (InvalidDataException) { check(true, "Desktop verification does not accept redirected endpoints"); }
        }
    }

    private static string Key(string account, string org) => $"acct:{account}|{ClaudeDesktopSessionParser.CodeClient}:{org}:https://api.anthropic.com:{Scopes}";
    private static object Entry(string token, long? expiry = null) => new { token, expiresAt = expiry ?? Now.AddHours(1).ToUnixTimeMilliseconds() };
    private static ClaudeDesktopSession Select(Dictionary<string, object?> values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return ClaudeDesktopSessionParser.Select(document.RootElement, Account, Org, Now);
    }
    private static string Profile(string account = Account, string org = Org) => JsonSerializer.Serialize(new
    { account = new { uuid = account }, organization = new { uuid = org, organization_type = "claude_pro", name = "Synthetic organization" } });
    private static void Deny(Action action, string name, Action<bool, string> check)
    {
        try { action(); } catch (ClaudeDesktopSessionException ex) { check(!ex.Message.Contains(Token), name); return; }
        throw new Exception("FAIL: " + name);
    }
    private static async Task DenyAsync(Func<Task<UsageSnapshot>> action, string name, Action<bool, string> check)
    {
        try { await action(); } catch (ClaudeDesktopSessionException ex) { check(!ex.Message.Contains(Token), name); return; }
        throw new Exception("FAIL: " + name);
    }
}
