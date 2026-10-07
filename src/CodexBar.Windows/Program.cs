namespace CodexBar.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var smoke = args.Contains("--smoke-test") || args.Contains("--missing-credentials-smoke");
        using var mutex = new Mutex(true, smoke ? "Local\\CodexBarWindows-smoke-" + Guid.NewGuid() : "Local\\CodexBarWindows-v1", out var first);
        if (!first) { MessageBox.Show("CodexBar is already running. Open it from the system tray."); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new Dashboard(args.Contains("--demo"), args.Contains("--smoke-test"),
            args.Contains("--missing-credentials-smoke")));
    }
}
