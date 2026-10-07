using System.Net;
using System.Text.Json;
using CodexBar.Core;

if (args.Length == 2 && args[0].StartsWith("--fake-login-", StringComparison.Ordinal))
{
    var home = args[1];
    // Test child uses only the explicitly supplied synthetic directory.
    if (!Path.GetFileName(home).StartsWith("codexbar-login-check-", StringComparison.Ordinal)) throw new Exception("Invalid test home");
    File.WriteAllText(Path.Combine(home, "started"), "synthetic child");
    if (args[0] == "--fake-login-wait") await Task.Delay(TimeSpan.FromMinutes(2));
    if (args[0] == "--fake-login-fail") { Environment.ExitCode = 1; return; }
    await File.WriteAllBytesAsync(Path.Combine(home, "auth.json"), SyntheticAuth("child", "synthetic-child"));
    return;
}

if (args.Length == 2 && args[0] == "--check-desktop-cache")
{
    using var actualCache = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
    var result = ClaudeDesktopUsage.Parse(actualCache.RootElement);
    Console.WriteLine($"Source: {result.Source}; recorded {result.FetchedAt:o}");
    foreach (var lane in result.Windows) Console.WriteLine($"{lane.Name}: {lane.RemainingPercent}% remaining");
    return;
}

var passed = 0;
var now = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
var overlaySize = new System.Drawing.Size(400, 64);
var primaryArea = new System.Drawing.Rectangle(0, 0, 1920, 1040);
Check(OverlayPlacement.Restore(new(400, 300), overlaySize, [primaryArea]) == new System.Drawing.Point(400, 300),
    "Overlay remembers an in-screen position");
Check(OverlayPlacement.Restore(new(1890, 1020), overlaySize, [primaryArea]) == new System.Drawing.Point(1520, 976),
    "Partially offscreen overlay is fully brought into working area");
Check(OverlayPlacement.Restore(new(-1800, 100), overlaySize, [primaryArea, new(-1920, 0, 1920, 1040)]) == new System.Drawing.Point(-1800, 100),
    "Negative-coordinate second monitor position is preserved");
Check(primaryArea.Contains(new System.Drawing.Rectangle(OverlayPlacement.Restore(new(-1800, 100), overlaySize, [primaryArea]), overlaySize)),
    "Disconnected monitor restores overlay onto the remaining display");
Check(OverlayPlacement.Restore(null, overlaySize, [primaryArea]) == new System.Drawing.Point(1496, 952),
    "Default overlay is inset from bottom-right taskbar-safe working area");
const string codexJson = """
{"plan_type":"plus","rate_limit":{"primary_window":{"used_percent":42,"reset_at":1790989200,"limit_window_seconds":18000},"secondary_window":{"used_percent":8,"limit_window_seconds":604800}}}
""";
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
var codex = UsageParser.Parse(Provider.Codex, codexJson, now);
Check(codex.Windows.Count == 2 && codex.Windows[0].RemainingPercent == 58 && codex.Plan == "plus", "Codex quotas and provider plan");
Check(codex.Windows[1].Name == "Weekly" && codex.Windows[1].ResetsAt == null, "Missing reset stays unavailable");
var weekly = UsageParser.Parse(Provider.Codex, """{"rate_limit":{"primary_window":{"used_percent":70,"limit_window_seconds":604800}}} """, now);
Check(weekly.Windows.Single().Name == "Weekly", "Weekly-only primary duration normalization");
var extras = UsageParser.Parse(Provider.Codex, """
{"rate_limit":{"primary_window":{"used_percent":10},"secondary_window":{"used_percent":"bad"}},"credits":{"balance":"12.50"},"additional_rate_limits":[null,{"limit_name":"Example model","rate_limit":{"primary_window":{"used_percent":80}}}]}
""", now);
Check(extras.Windows.Count == 2 && extras.Credits == 12.5, "Malformed optional lane does not erase extras or string credits");
var over = UsageParser.Parse(Provider.Claude, """{"five_hour":{"utilization":120,"resets_at":"2026-10-03T07:00:00+07:00"}}""", now);
Check(over.Windows[0].UsedPercent == 120 && over.Windows[0].RemainingPercent == 0 && over.Windows[0].ResetsAt == now, "Overage retained and offset reset parsed");
var scoped = UsageParser.Parse(Provider.Claude, """
{"five_hour":null,"limits":[{"kind":"weekly_scoped","percent":30,"scope":{"model":{"display_name":"Example model"}}},{"group":"weekly","percent":99,"is_active":false}]}
""", now);
Check(scoped.Windows.Count == 1 && scoped.Windows[0].Name == "Weekly · Example model", "Claude scoped limits and inactive exclusion");
Reject(() => UsageParser.Parse(Provider.Codex, "{}", now), "Empty payload is not zero usage");
Reject(() => UsageParser.Parse(Provider.Claude, "[]", now), "Non-object payload rejected");
Reject(() => UsageParser.Parse(Provider.Claude, """{"five_hour":{"utilization":-1}}""", now), "Negative usage rejected");
Check(ResetText.Format(now.AddMinutes(-1), now).Contains("refresh"), "Expired countdown never resets quota");
UsageSnapshot Desktop(string json)
{
    using var doc = JsonDocument.Parse(json); return ClaudeDesktopUsage.Parse(doc.RootElement);
}
var desktop = Desktop("""{"version":2,"samples":[{"t":1790985600000,"org":"synthetic-org","u":{"fh":5,"sd":1}}]}""");
Check(desktop.Source == "Claude Desktop cache" && desktop.Windows[0].RemainingPercent == 95 && desktop.Windows[1].RemainingPercent == 99,
    "Desktop abbreviated usage mapped to session and weekly percentages");
Check(desktop.FetchedAt == DateTimeOffset.FromUnixTimeMilliseconds(1790985600000) && desktop.Windows.All(w => w.ResetsAt == null),
    "Cache capture time preserved and unknown resets never invented");
Reject(() => Desktop("""{"version":3,"samples":[]}"""), "Unsupported Desktop schema rejected");
Reject(() => Desktop("""{"version":2,"samples":[{"t":1,"org":"org-a","u":{"fh":5}},{"t":2,"org":"org-b","u":{"fh":8}}]}"""),
    "Multiple Desktop accounts cannot silently mix");
Reject(() => Desktop("""{"version":2,"samples":[{"t":1,"u":{"fh":5}}]}"""), "Unknown Desktop account rejected");
Reject(() => Desktop("""{"version":2,"samples":[{"t":1,"org":"test","u":{"fh":"bad","sd":-1}}]}"""), "Invalid cache data is not zero usage");
var newest = Desktop("""{"version":2,"samples":[{"t":2,"org":"test","u":{"fh":7}},{"t":1,"org":"test","u":{"fh":5}}]}""");
Check(newest.Windows.Single().UsedPercent == 7, "Newest timestamp selected regardless of array order");
var discoveryRoot = Path.Combine(Path.GetTempPath(), "codexbar-discovery-" + Guid.NewGuid());
try
{
    Directory.CreateDirectory(Path.Combine(discoveryRoot, "local", "Packages", "Claude_synthetic"));
    var candidates = ClaudeDesktopUsage.Candidates(Path.Combine(discoveryRoot, "local"), Path.Combine(discoveryRoot, "roaming")).ToArray();
    Check(candidates.Length == 2 && candidates[1].EndsWith(Path.Combine("Claude_synthetic", "LocalCache", "Roaming", "Claude", "plan-usage-history.json")),
        "Store and conventional Desktop cache locations discovered without auth scanning");
}
finally { if (Directory.Exists(discoveryRoot)) Directory.Delete(discoveryRoot, true); }
Credentials Parse(Provider provider, string json)
{
    using var doc = JsonDocument.Parse(json); return CredentialReader.Parse(provider, doc.RootElement, now);
}
var credentials = Parse(Provider.Codex, """{"tokens":{"access_token":"synthetic-token","account_id":"synthetic-account"}}""");
Check(credentials.AccountId == "synthetic-account" && !credentials.ToString().Contains("synthetic-token"), "Credential identity and redacted ToString");
Reject(() => Parse(Provider.Codex, """{"OPENAI_API_KEY":"synthetic-key"}"""), "API key cannot masquerade as subscription OAuth");
Reject(() => Parse(Provider.Claude, """{"claudeAiOauth":{"accessToken":"synthetic","expiresAt":1}}"""), "Claude millisecond expiry");
Reject(() => Parse(Provider.Claude, """{"claudeAiOauth":{"accessToken":"synthetic","scopes":["other"]}}"""), "Claude missing usage scope");
var sourceRoot = Path.Combine(Path.GetTempPath(), "codexbar-source-" + Guid.NewGuid());
Directory.CreateDirectory(sourceRoot);
try
{
    var defaultPath = Path.Combine(sourceRoot, ".credentials.json");
    const string mcpOnly = """{"mcpOAuth":{"example":{"accessToken":"SYNTHETIC-MCP-SECRET"}}}""";
    await File.WriteAllTextAsync(defaultPath, mcpOnly);
    Task<Credentials> ReadSource(CancellationToken cancellation) => CredentialReader.ReadAsync(Provider.Claude, defaultPath, cancellation);
    Task<UsageSnapshot> MustNotCallApi(Credentials _, CancellationToken __) => throw new Exception("Quota API must not be called for Desktop source");
    Task<UsageSnapshot?> Cache(CancellationToken _) => Task.FromResult<UsageSnapshot?>(desktop);
    Task<UsageSnapshot> Live(CancellationToken _) => Task.FromResult(desktop with { Source = "Claude Desktop live API" });
    var fallback = await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.Automatic, true,
        ReadSource, MustNotCallApi, _ => throw new Exception("Auto must never read quota history"), CancellationToken.None, Live);
    Check(fallback.Source == "Claude Desktop live API" && await File.ReadAllTextAsync(defaultPath) == mcpOnly,
        "Existing MCP-only file selects Desktop live source and leaves auth unchanged");
    try
    {
        await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.Cli, true,
            ReadSource, MustNotCallApi, Cache, CancellationToken.None, Live);
        throw new Exception("CLI-only must preserve missing subscription error");
    }
    catch (ClaudeSubscriptionMissingException ex)
    { Check(!ex.Message.Contains("SYNTHETIC"), "CLI-only gives actionable error without leaking MCP token"); }
    try
    {
        await ClaudeSourceResolver.FetchAsync(Path.Combine(sourceRoot, "custom.json"), defaultPath,
            ClaudeUsageSource.Automatic, true, ReadSource, MustNotCallApi, Cache, CancellationToken.None, Live);
        throw new Exception("Custom account path must not use Desktop fallback");
    }
    catch (ClaudeSubscriptionMissingException) { Check(true, "Custom account path does not silently switch to Desktop"); }
    var explicitDesktop = await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.DesktopCache, false,
        _ => throw new Exception("Desktop must bypass CLI auth entirely"), MustNotCallApi, Cache, CancellationToken.None);
    Check(explicitDesktop.Source == "Claude Desktop cache", "Explicit Desktop works regardless of CLI auth and Auto fallback toggle");
    var explicitLive = await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.DesktopLive, false,
        _ => throw new Exception("Desktop live must bypass CLI auth"), MustNotCallApi, _ => throw new Exception("No historical fallback"), CancellationToken.None, Live);
    Check(explicitLive.Source == "Claude Desktop live API", "Explicit live Desktop bypasses CLI file and fallback toggle");
    await File.WriteAllTextAsync(defaultPath, """{"claudeAiOauth":{"accessToken":"synthetic","expiresAt":1}}""");
    try
    {
        await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.Automatic, true,
            ReadSource, MustNotCallApi, Cache, CancellationToken.None, Live);
        throw new Exception("Expired subscription must not change accounts");
    }
    catch (InvalidDataException) { Check(true, "Expired subscription stays an auth error and does not fall back"); }
    try
    {
        await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.DesktopCache, true,
            ReadSource, MustNotCallApi, _ => Task.FromResult<UsageSnapshot?>(null), CancellationToken.None);
        throw new Exception("Absent cache must be an error");
    }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("Usage"), "Desktop without recorded quota gives guidance, not zero usage"); }
    try
    {
        await ClaudeSourceResolver.FetchAsync(defaultPath, defaultPath, ClaudeUsageSource.Automatic, true,
            _ => Task.FromResult(new Credentials("synthetic")), (_, _) => throw new AuthenticationExpiredException(), Cache, CancellationToken.None, Live);
        throw new Exception("401 must not change sources");
    }
    catch (AuthenticationExpiredException) { Check(true, "Quota API 401 cannot be hidden by Desktop cache"); }
}
finally { Directory.Delete(sourceRoot, true); }
var temporary = Path.Combine(Path.GetTempPath(), "codexbar-check-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
try
{
    var path = Path.Combine(temporary, "auth.json");
    const string contents = """{"tokens":{"access_token":"synthetic-token"}}""";
    await File.WriteAllTextAsync(path, contents);
    _ = await CredentialReader.ReadAsync(Provider.Codex, path, CancellationToken.None);
    Check(await File.ReadAllTextAsync(path) == contents, "Credential file stays byte-for-byte unchanged");
}
finally { Directory.Delete(temporary, true); }

var handler = new FakeHandler(request =>
{
    Check(request.RequestUri!.AbsoluteUri == "https://chatgpt.com/backend-api/wham/usage" &&
        request.Headers.Authorization!.Parameter == "synthetic-token" &&
        request.Headers.GetValues("ChatGPT-Account-Id").Single() == "synthetic-account", "Codex endpoint/auth/account headers");
    return new(HttpStatusCode.OK) { Content = new StringContent(codexJson) };
});
using var http = new HttpClient(handler);
var client = new UsageClient(http);
_ = await client.FetchAsync(Provider.Codex, credentials, CancellationToken.None);
var claudeHandler = new FakeHandler(request =>
{
    Check(request.RequestUri!.Host == "api.anthropic.com" && request.Headers.GetValues("anthropic-beta").Single() == "oauth-2025-04-20" &&
        request.Headers.UserAgent.ToString() == "claude-code/2.1.0", "Claude endpoint/beta/user-agent headers");
    return new(HttpStatusCode.OK) { Content = new StringContent("{\"five_hour\":{\"utilization\":20}}") };
});
using var claudeHttp = new HttpClient(claudeHandler);
_ = await new UsageClient(claudeHttp).FetchAsync(Provider.Claude, new("synthetic-token"), CancellationToken.None);
var rateHandler = new FakeHandler(_ =>
{
    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new(TimeSpan.FromMinutes(10)); return response;
});
using var rateHttp = new HttpClient(rateHandler);
var rateClient = new UsageClient(rateHttp);
for (var i = 0; i < 2; i++)
    try { await rateClient.FetchAsync(Provider.Codex, credentials, CancellationToken.None); throw new Exception("Expected 429"); }
    catch (InvalidDataException) { }
Check(rateHandler.Calls == 1, "Retry-After blocks repeated manual/automatic requests");
try { await rateClient.FetchAsync(Provider.Codex, new("another-synthetic-token", "another-account"), CancellationToken.None); }
catch (InvalidDataException) { }
Check(rateHandler.Calls == 2, "A rate-limited account does not block a different account");
var unauthorized = new FakeHandler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("SECRET_RESPONSE_MUST_NOT_APPEAR") });
using var unauthorizedHttp = new HttpClient(unauthorized);
try { await new UsageClient(unauthorizedHttp).FetchAsync(Provider.Codex, credentials, CancellationToken.None); throw new Exception("Expected 401"); }
catch (AuthenticationExpiredException ex) { Check(!ex.Message.Contains("SECRET"), "401 typed error does not leak response body"); }
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await client.FetchAsync(Provider.Codex, credentials, cancelled.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(true, "HTTP cancellation propagates"); }

var accountRoot = Path.Combine(Path.GetTempPath(), "codexbar-account-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(accountRoot);
try
{
    var vault = new CodexAccountVault(Path.Combine(accountRoot, "vault"), Seal, Open);
    var path = Path.Combine(accountRoot, "auth.json");
    var a = SyntheticAuth("a", "synthetic-a"); var b = SyntheticAuth("b", "synthetic-b");
    var accountA = vault.Save(a, "Personal"); var accountB = vault.Save(b, "Work");
    Check(vault.List().Count == 2 && accountA.IdentityKey != accountB.IdentityKey,
        "Users sharing one workspace remain distinct saved accounts");
    var rotated = SyntheticAuth("a", "synthetic-a-renewed");
    Check(vault.Save(rotated).Id == accountA.Id && vault.List().Count == 2,
        "Token rotation preserves account identity and custom name without duplication");
    Check(vault.List().Single(x => x.Id == accountA.Id).Label == "Personal", "Token update preserves saved account label");
    await File.WriteAllBytesAsync(path, rotated);
    CodexAuthFileSwitcher.Switch(vault, accountB.Id, path);
    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(b), "Switch activates selected account's complete auth file");
    CodexAuthFileSwitcher.Switch(vault, accountA.Id, path);
    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(rotated), "A-B-A returns to renewed outgoing credentials");
    var renewedAgain = SyntheticAuth("a", "synthetic-a-newest");
    File.WriteAllBytes(path, renewedAgain);
    CodexAuthFileSwitcher.Switch(vault, accountA.Id, path);
    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(renewedAgain), "Selecting current account never restores older saved tokens");
    var unsaved = SyntheticAuth("unsaved", "synthetic-unsaved");
    File.WriteAllBytes(path, unsaved);
    CodexAuthFileSwitcher.Switch(vault, accountB.Id, path);
    var unsavedAccount = vault.List().Single(x => x.IdentityKey == Identity(unsaved));
    CodexAuthFileSwitcher.Switch(vault, unsavedAccount.Id, path);
    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(unsaved), "Unsaved outgoing account is automatically recoverable");
    var unrelated = SyntheticAuth("external", "synthetic-external");
    try
    {
        CodexAuthFileSwitcher.Switch(vault, accountB.Id, path, () => File.WriteAllBytes(path, unrelated));
        throw new Exception("Expected concurrent-change rejection");
    }
    catch (IOException) { Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(unrelated), "Detected concurrent auth change is not overwritten"); }
    File.WriteAllBytes(path, a);
    try
    {
        CodexAuthFileSwitcher.Switch(vault, accountB.Id, path, afterCommit: () => File.WriteAllBytes(path, unrelated));
        throw new Exception("Expected readback conflict");
    }
    catch (IOException) { Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(unrelated), "Readback conflict never rolls back over a later login"); }
    File.WriteAllBytes(path, a);
    var failingVault = new CodexAccountVault(Path.Combine(accountRoot, "vault"), _ => throw new IOException("synthetic protection failure"), Open);
    try { CodexAuthFileSwitcher.Switch(failingVault, accountB.Id, path); throw new Exception("Expected protection failure"); }
    catch (IOException) { Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(a), "Failed recovery protection leaves original login intact"); }
    var malformed = System.Text.Encoding.UTF8.GetBytes("{\"OPENAI_API_KEY\":\"synthetic-api-key\"}");
    File.WriteAllBytes(path, malformed);
    try { CodexAuthFileSwitcher.Switch(vault, accountB.Id, path); throw new Exception("Expected unsupported current login rejection"); }
    catch (InvalidDataException)
    { Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(malformed), "Unsupported existing login is preserved instead of overwritten"); }
    Check(Open(File.ReadAllBytes(Path.Combine(accountRoot, "vault", "recovery.bin"))).AsSpan().SequenceEqual(malformed),
        "Encrypted recovery retains complete unsupported current login bytes");
    try { vault.Save(System.Text.Encoding.UTF8.GetBytes("{\"tokens\":{\"access_token\":\"synthetic\",\"account_id\":\"shared\"}}")); throw new Exception("Expected unknown identity rejection"); }
    catch (InvalidDataException) { Check(true, "Workspace ID alone cannot merge different users"); }
    try { vault.ReadAuth("../auth"); throw new Exception("Expected path traversal rejection"); }
    catch (InvalidDataException) { Check(true, "Saved-account identifier cannot escape its vault"); }
    Check(Directory.GetFiles(accountRoot, "*.tmp").Length == 0, "Switch stages are removed after success and failure");
    vault.Remove(accountB.Id);
    Check(vault.List().All(x => x.Id != accountB.Id) && File.ReadAllBytes(path).AsSpan().SequenceEqual(malformed),
        "Removing a saved account leaves active login unchanged");
    foreach (var config in new string?[] { null, "model = 'x'\n", "cli_auth_credentials_store = \"file\" # comment\n[features]\nx=true", "'cli_auth_credentials_store' = 'file'\n" })
        CodexStoragePolicy.RequireFileStorage(config);
    Check(true, "Explicit file mode and documented default are supported");
    foreach (var config in new[] { "cli_auth_credentials_store = 'auto'", "cli_auth_credentials_store = 'keyring'", "cli_auth_credentials_store = 'ephemeral'",
        "[profile]\ncli_auth_credentials_store='file'", "cli_auth_credentials_store='file'\ncli_auth_credentials_store='file'", "cli_auth_credentials_store = \"file\" + bad" })
    {
        try { CodexStoragePolicy.RequireFileStorage(config); throw new Exception("Expected unsupported storage mode"); }
        catch (InvalidDataException) { }
    }
    Check(true, "Keyring, auto, ephemeral and ambiguous settings cannot silently switch a stale file");
    var expiredClaims = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { exp = 1 })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var expired = SyntheticAuth("expired", "e30." + expiredClaims + ".synthetic");
    var expiredAccount = vault.Save(expired);
    File.Delete(path);
    CodexAuthFileSwitcher.Switch(vault, expiredAccount.Id, path);
    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(expired),
        "An expired access token retains its refresh token for the official CLI to renew");
    var badId = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(accountRoot, "vault", badId + ".account"), "synthetic-invalid-vault");
    Check(vault.List().Single(x => x.Id == badId).IdentityKey == "", "A corrupt saved account stays removable without hiding other accounts");
    try { CodexAuthFileSwitcher.Switch(vault, badId, path); throw new Exception("Expected invalid target rejection"); }
    catch (InvalidDataException) { Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(expired), "A corrupt selected login cannot replace the active account"); }
    vault.Remove(badId);
}
finally { Directory.Delete(accountRoot, true); }

var loginRoot = Path.Combine(Path.GetTempPath(), "codexbar-login-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(loginRoot);
try
{
    var start = CodexLoginRunner.StartInfo(Environment.ProcessPath!, loginRoot);
    Check(start.Environment["CODEX_HOME"] == loginRoot && start.ArgumentList.Contains("cli_auth_credentials_store=\"file\"") &&
        !start.Environment.Keys.Any(k => k.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)),
        "New login isolates its home, forces file storage and removes inherited auth overrides");
    start.ArgumentList.Clear(); start.ArgumentList.Add("--fake-login-success"); start.ArgumentList.Add(loginRoot);
    var auth = await CodexLoginRunner.RunAsync(start, loginRoot, CancellationToken.None);
    Check(Identity(auth) == Identity(SyntheticAuth("child", "synthetic-child")), "Completed isolated login returns only its own credentials");
    start.ArgumentList[0] = "--fake-login-fail";
    try { await CodexLoginRunner.RunAsync(start, loginRoot, CancellationToken.None); throw new Exception("Expected failed child"); }
    catch (IOException) { Check(true, "Failed login cannot import a leftover credential file"); }
    File.Delete(Path.Combine(loginRoot, "started"));
    start.ArgumentList[0] = "--fake-login-wait";
    using var cancelLogin = new CancellationTokenSource();
    var waiting = CodexLoginRunner.RunAsync(start, loginRoot, cancelLogin.Token);
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!File.Exists(Path.Combine(loginRoot, "started")) && DateTime.UtcNow < deadline) await Task.Delay(20);
    cancelLogin.Cancel();
    try { await waiting; throw new Exception("Expected login cancellation"); }
    catch (OperationCanceledException) { Check(true, "Cancel waits for the app-owned login process to stop before cleanup"); }
    Directory.Delete(loginRoot, true);
    Check(!Directory.Exists(loginRoot), "Cancelled synthetic login home can be fully removed");
}
finally { if (Directory.Exists(loginRoot)) Directory.Delete(loginRoot, true); }
await ClaudeDesktopLiveTests.RunAsync(Check);
if (OperatingSystem.IsWindows()) await ClaudeDesktopWindowsTests.RunAsync(Check);
Console.WriteLine($"All {passed} checks passed. Synthetic files and fake HTTP only.");

static byte[] Seal(byte[] plain) => plain.Select(b => (byte)(b ^ 0xA5)).ToArray(); // Test-only protector; production uses DPAPI.
static byte[] Open(byte[] encrypted) => Seal(encrypted);
static byte[] SyntheticAuth(string user, string access)
{
    var claims = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { sub = user, email = user + "@example.test" })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return JsonSerializer.SerializeToUtf8Bytes(new { tokens = new { account_id = "shared-workspace", access_token = access, refresh_token = "synthetic-refresh", id_token = "e30." + claims + ".synthetic" } });
}
static string Identity(byte[] auth)
{
    using var document = JsonDocument.Parse(auth);
    return CredentialReader.Parse(Provider.Codex, document.RootElement, DateTimeOffset.UtcNow).Identity!.Key;
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls++;
        return Task.FromResult(response(request));
    }
}
