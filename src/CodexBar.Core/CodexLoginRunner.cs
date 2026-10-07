using System.Diagnostics;

namespace CodexBar.Core;

public sealed class CodexLoginProcessStillRunningException() : IOException(
    "The sign-in process could not stop. Its temporary files were kept. Close that sign-in process before removing its login-* folder.");

public static class CodexLoginRunner
{
    public static ProcessStartInfo StartInfo(string executable, string home)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = home
        };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        start.ArgumentList.Add("login");
        foreach (var key in start.Environment.Keys.Where(k =>
            k.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["CODEX_HOME"] = home;
        return start;
    }

    public static async Task<byte[]> RunAsync(ProcessStartInfo start, string home, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Codex login could not be started.");
        // Drain output without recording login URLs, authorization codes, or tokens.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            await process.WaitForExitAsync(cancellation);
            await Task.WhenAll(stdout, stderr);
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("Codex sign-in did not finish. Try again or check your Codex installation.");
            var bytes = CodexAuthFileSwitcher.ReadBounded(Path.Combine(home, "auth.json"));
            try { _ = CodexAccountIdentity.Validate(bytes); return bytes; }
            catch { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); throw; }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                { throw new CodexLoginProcessStillRunningException(); }
                await process.WaitForExitAsync();
            }
            await Task.WhenAll(stdout, stderr);
        }
    }
}
