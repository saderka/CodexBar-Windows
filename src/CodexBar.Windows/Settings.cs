using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core;

namespace CodexBar.Windows;

public sealed class Settings
{
    public bool CodexEnabled { get; set; } = true;
    public bool ClaudeEnabled { get; set; } = true;
    public int RefreshMinutes { get; set; } = 5;
    public string CodexPath { get; set; } = CredentialReader.DefaultPath(Provider.Codex);
    public string? CodexAccountLabel { get; set; }
    public string? CodexAccountIdentity { get; set; }
    public string ClaudePath { get; set; } = CredentialReader.DefaultPath(Provider.Claude);
    public bool NotifyLowQuota { get; set; } = true;
    public bool StartAtLogin { get; set; }
    // Preserve the pre-0.4 preference key and disabled fallback choice.
    [JsonPropertyName("UseClaudeDesktopCache")]
    public bool UseClaudeDesktopLive { get; set; } = true;
    public string? ClaudeDesktopProfile { get; set; }
    public ClaudeUsageSource ClaudeSource { get; set; } = ClaudeUsageSource.Automatic;
    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }
    public bool OverlayPinned { get; set; } = true;
    public double OverlayOpacity { get; set; } = .95;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexBarWindows", "settings.json");

    public static Settings Load()
    {
        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    internal static Settings Parse(string json)
    {
            var result = JsonSerializer.Deserialize<Settings>(json) ?? new();
            result.RefreshMinutes = Math.Clamp(result.RefreshMinutes, 1, 60);
            if (!Enum.IsDefined(result.ClaudeSource)) result.ClaudeSource = ClaudeUsageSource.Automatic;
            if (result.ClaudeSource == ClaudeUsageSource.DesktopCache) result.ClaudeSource = ClaudeUsageSource.DesktopLive;
            if (string.IsNullOrWhiteSpace(result.ClaudeDesktopProfile)) result.ClaudeDesktopProfile = null;
            result.OverlayOpacity = double.IsFinite(result.OverlayOpacity) ? Math.Clamp(result.OverlayOpacity, .75, 1) : .95;
            if (string.IsNullOrWhiteSpace(result.CodexPath)) result.CodexPath = CredentialReader.DefaultPath(Provider.Codex);
            if (string.IsNullOrWhiteSpace(result.ClaudePath)) result.ClaudePath = CredentialReader.DefaultPath(Provider.Claude);
            return result;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}

internal sealed class SettingsForm : Form
{
    public SettingsForm(Settings settings)
    {
        Text = "CodexBar · Settings";
        ClientSize = new(620, 510);
        Font = new("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var codex = new CheckBox { Text = "Enable Codex", Checked = settings.CodexEnabled, AutoSize = true, Location = new(22, 20) };
        var claude = new CheckBox { Text = "Enable Claude", Checked = settings.ClaudeEnabled, AutoSize = true, Location = new(320, 20) };
        var codexPath = new TextBox { Text = settings.CodexPath, Location = new(22, 80), Width = 570 };
        var claudePath = new TextBox { Text = settings.ClaudePath, Location = new(22, 143), Width = 570 };
        var interval = new NumericUpDown { Minimum = 1, Maximum = 60, Value = settings.RefreshMinutes, Location = new(210, 187), Width = 65 };
        var notify = new CheckBox { Text = "Notify when quota drops to 10% remaining", Checked = settings.NotifyLowQuota, AutoSize = true, Location = new(22, 225) };
        var startup = new CheckBox { Text = "Start minimized when I sign in to Windows", Checked = settings.StartAtLogin, AutoSize = true, Location = new(22, 260) };
        var desktopLive = new CheckBox { Text = "Auto: use Desktop live quota when CLI subscription login is absent", Checked = settings.UseClaudeDesktopLive, AutoSize = true, Location = new(22, 293) };
        var source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new(170, 328), Width = 420 };
        var sources = new[] { ClaudeUsageSource.Automatic, ClaudeUsageSource.Cli, ClaudeUsageSource.DesktopLive };
        source.Items.AddRange(["Automatic (CLI, then Desktop live API)", "Claude Code CLI only", "Claude Desktop (live quota)"]);
        source.SelectedIndex = Math.Max(0, Array.IndexOf(sources, settings.ClaudeSource));
        var profile = new TextBox { Text = settings.ClaudeDesktopProfile, PlaceholderText = "Automatic · leave blank for the installed Desktop profile", Location = new(22, 416), Width = 570 };
        var save = new Button { Text = "Save", Location = new(500, 461), Size = new(92, 34) };
        Controls.AddRange([codex, claude, codexPath, claudePath, interval, notify, startup, desktopLive, source, profile, save,
            new Label { Text = "Claude source", AutoSize = true, Location = new(22, 333) },
            new Label { Text = "Desktop: verify Code account, fetch live quota and reset times.", AutoSize = true, Location = new(22, 366) },
            new Label { Text = "Desktop profile folder (optional)", AutoSize = true, Location = new(22, 391) },
            new Label { Text = "Codex auth.json path", AutoSize = true, Location = new(22, 56) },
            new Label { Text = "Claude .credentials.json path", AutoSize = true, Location = new(22, 119) },
            new Label { Text = "Refresh every (minutes)", AutoSize = true, Location = new(22, 191) }]);
        save.Click += (_, _) =>
        {
            var proposed = new Settings { CodexEnabled = codex.Checked, ClaudeEnabled = claude.Checked,
                CodexPath = codexPath.Text.Trim(), ClaudePath = claudePath.Text.Trim(),
                CodexAccountLabel = codexPath.Text.Trim() == settings.CodexPath ? settings.CodexAccountLabel : null,
                CodexAccountIdentity = codexPath.Text.Trim() == settings.CodexPath ? settings.CodexAccountIdentity : null,
                RefreshMinutes = (int)interval.Value, NotifyLowQuota = notify.Checked, StartAtLogin = startup.Checked,
                UseClaudeDesktopLive = desktopLive.Checked, ClaudeSource = sources[source.SelectedIndex],
                ClaudeDesktopProfile = string.IsNullOrWhiteSpace(profile.Text) ? null : profile.Text.Trim(),
                OverlayX = settings.OverlayX, OverlayY = settings.OverlayY, OverlayPinned = settings.OverlayPinned,
                OverlayOpacity = settings.OverlayOpacity };
            if (!Path.IsPathFullyQualified(proposed.CodexPath) || !Path.IsPathFullyQualified(proposed.ClaudePath))
            { MessageBox.Show(this, "Use a full credential file path for each provider."); return; }
            if (proposed.ClaudeDesktopProfile is not null && !Path.IsPathFullyQualified(proposed.ClaudeDesktopProfile))
            { MessageBox.Show(this, "Use a full Desktop profile folder path, or leave it blank for automatic detection."); return; }
            try
            {
                proposed.Save();
                if (settings.StartAtLogin != proposed.StartAtLogin) WindowsStartup.Set(proposed.StartAtLogin);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { MessageBox.Show(this, "Settings could not be saved. Check folder permissions."); return; }
            settings.CodexEnabled = proposed.CodexEnabled; settings.ClaudeEnabled = proposed.ClaudeEnabled;
            settings.CodexPath = proposed.CodexPath; settings.ClaudePath = proposed.ClaudePath;
            settings.CodexAccountLabel = proposed.CodexAccountLabel; settings.CodexAccountIdentity = proposed.CodexAccountIdentity;
            settings.RefreshMinutes = proposed.RefreshMinutes; settings.NotifyLowQuota = proposed.NotifyLowQuota;
            settings.StartAtLogin = proposed.StartAtLogin;
            settings.UseClaudeDesktopLive = proposed.UseClaudeDesktopLive;
            settings.ClaudeSource = proposed.ClaudeSource;
            settings.ClaudeDesktopProfile = proposed.ClaudeDesktopProfile;
            DialogResult = DialogResult.OK;
        };
    }
}

internal static class WindowsStartup
{
    public static void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("CodexBarWindows", "\"" + Application.ExecutablePath + "\" --start-hidden");
        else key.DeleteValue("CodexBarWindows", false);
    }
}
