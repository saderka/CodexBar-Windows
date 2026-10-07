using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using CodexBar.Core;

namespace CodexBar.Windows;

internal sealed class CodexAccountService
{
    public string Root { get; }
    public CodexAccountVault Vault { get; }
    public CodexAccountService(string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "accounts");
        Vault = new(Root, WindowsAccountProtection.Protect, WindowsAccountProtection.Unprotect);
    }

    public void Prepare()
    {
        var directory = Directory.CreateDirectory(Root);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException();
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    public static void RequireClosedClients()
    {
        var processes = Process.GetProcesses();
        var running = false;
        try
        {
            foreach (var process in processes)
            {
                if (process.Id == Environment.ProcessId) continue;
                try { running |= process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception)
                { throw new InvalidDataException("Running applications could not be checked. Close Codex and try again."); }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (running) throw new InvalidDataException("Close Codex Desktop, Codex CLI and VS Code before switching, then click Switch again. Your running work was left untouched.");
    }

    public static void RequireFileHome(string authPath)
    {
        var home = Path.GetDirectoryName(authPath)!;
        var config = Path.Combine(home, "config.toml");
        CodexStoragePolicy.RequireFileStorage(File.Exists(config) ? File.ReadAllText(config) : null);
    }

    public static string FindExecutable()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(folder.Trim('"'), "codex.exe");
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path;
        }
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(bundled))
        {
            var path = Directory.EnumerateDirectories(bundled).Select(folder => Path.Combine(folder, "codex.exe"))
                .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (path != null) return path;
        }
        throw new FileNotFoundException("Codex CLI was not found. Select codex.exe from your Codex installation.");
    }

    public async Task<CodexAccountInfo> SignInAsync(string executable, CancellationToken cancellation)
    {
        Prepare();
        var home = Path.Combine(Root, "login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var cleanup = true;
        try
        {
            var auth = await CodexLoginRunner.RunAsync(CodexLoginRunner.StartInfo(executable, home), home, cancellation);
            try { return Vault.Save(auth); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(auth); }
        }
        catch (CodexLoginProcessStillRunningException)
        {
            cleanup = false;
            throw new InvalidDataException("Sign-in could not stop. Its files were kept. Close the sign-in process before removing the login-* folder under Local AppData/CodexBarWindows/accounts.");
        }
        finally
        {
            // The runner has exited (or killed and awaited its own process) first.
            // Only this generated child directory is removed, never any Codex home.
            if (!Path.GetFullPath(home).StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Temporary sign-in folder is outside the account store.");
            try { if (cleanup) Directory.Delete(home, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new InvalidDataException("Temporary sign-in files could not be removed. Close CodexBar and remove the login-* folder from the CodexBarWindows/accounts directory under Local AppData."); }
        }
    }
}
