using CodexBar.Windows;
using CodexBar.Core;

// Explicit read-only diagnostic. Only status, quota and reset times are printed.
// Never writes credentials, account IDs, decrypted caches or response bodies.
try
{
    var profile = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
    Task<ClaudeDesktopSession> Read(CancellationToken cancellation) => ClaudeDesktopSessionReader.ReadAsync(
        cancellation, profile, args.Contains("--diagnostic") ? Console.WriteLine : null);
    if (args.Contains("--resolve-only"))
    {
        var session = await Read(CancellationToken.None);
        Console.WriteLine($"PASS: resolved current-account Code session; Desktop selected organization verified={session.ActiveOrganizationVerified}");
        return;
    }
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    var client = new ClaudeDesktopLiveClient(http, new UsageClient(http), Read);
    var snapshot = await client.FetchAsync(CancellationToken.None);
    Console.WriteLine($"PASS: verified current account and Code credential organization; source={snapshot.Source}; plan={snapshot.Plan}; fetched={snapshot.FetchedAt:o}");
    foreach (var window in snapshot.Windows)
        Console.WriteLine($"{window.Name}: {window.RemainingPercent:0.#}% remaining; reset={window.ResetsAt:o}");
}
catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or OperationCanceledException)
{
    Console.WriteLine("FAIL: " + (ex is ClaudeDesktopSessionException or InvalidDataException ? ex.Message : "Network request did not complete."));
    Environment.ExitCode = 1;
}
