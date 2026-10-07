using CodexBar.Core;

namespace CodexBar.Windows;

internal sealed class Dashboard : Form
{
    private readonly Settings settings;
    private readonly bool demo;
    private readonly bool smoke;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly System.Windows.Forms.Timer countdown = new() { Interval = 60_000 };
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly UsageClient client;
    private readonly ClaudeDesktopLiveClient desktopClient;
    private readonly CancellationTokenSource shutdown = new();
    private CancellationTokenSource? refreshCancellation;
    private int generation;
    private readonly Dictionary<Provider, string> credentialOwners = [];
    private readonly Dictionary<Provider, UsageSnapshot> snapshots = [];
    private readonly Dictionary<Provider, string> errors = [];
    private readonly HashSet<Provider> notified = [];
    private readonly OverlaySurface surface;
    private readonly ContextMenuStrip menu;
    private bool refreshing;
    private bool quitting;
    private bool ready;
    private readonly Icon appIcon;
    private readonly CodexAccountService accounts = new();
    private readonly ToolStripMenuItem accountMenu;
    private string? currentAccountLabel;
    private string? currentAccountKey;

    public Dashboard(bool demo, bool smoke, bool missingCredentialsSmoke = false)
    {
        this.demo = demo || (smoke && !missingCredentialsSmoke);
        this.smoke = smoke || missingCredentialsSmoke;
        settings = this.demo || missingCredentialsSmoke ? new Settings() : Settings.Load();
        if (missingCredentialsSmoke)
        {
            // Isolated nonexistent paths: exercise real auth errors without reading user credentials.
            var absentHome = Path.Combine(Path.GetTempPath(), "codexbar-absent-" + Guid.NewGuid());
            settings.CodexPath = Path.Combine(absentHome, "auth.json");
            settings.ClaudePath = Path.Combine(absentHome, ".credentials.json");
            settings.UseClaudeDesktopLive = false;
            settings.ClaudeSource = ClaudeUsageSource.Cli;
        }
        client = new(http);
        desktopClient = new(http, client, cancellation => ClaudeDesktopSessionReader.ReadAsync(cancellation, settings.ClaudeDesktopProfile));
        Text = "CodexBar for Windows · v0.4.1";
        currentAccountLabel = this.demo ? "Personal" : settings.CodexAccountLabel;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        TopMost = settings.OverlayPinned;
        Opacity = settings.OverlayOpacity;
        BackColor = Color.FromArgb(19, 23, 31);
        ForeColor = Color.FromArgb(230, 235, 242);
        Font = new("Segoe UI", 10);
        appIcon = CreateIcon();
        Icon = appIcon;
        surface = new() { Dock = DockStyle.Fill, Demo = this.demo, Pinned = TopMost };
        Controls.Add(surface);
        menu = new ContextMenuStrip { BackColor = Color.FromArgb(27, 33, 45), ForeColor = Color.FromArgb(229, 237, 248),
            ShowImageMargin = false, Font = new("Segoe UI", 9), Renderer = new OverlayMenuRenderer() };
        menu.Items.Add("Show overlay", null, (_, _) => OpenDashboard());
        menu.Items.Add("Refresh", null, async (_, _) => await RefreshAsync());
        accountMenu = new ToolStripMenuItem("Codex accounts");
        accountMenu.DropDownItems.Add("Manage accounts…", null, (_, _) => ManageAccounts());
        accountMenu.DropDownOpening += (_, _) => PopulateAccountMenu();
        menu.Items.Add(accountMenu);
        var pin = new ToolStripMenuItem("Always on top") { Checked = TopMost };
        pin.Click += (_, _) => TogglePin();
        menu.Items.Add(pin);
        var opacity = new ToolStripMenuItem("Opacity");
        foreach (var percent in new[] { 100, 95, 85, 75 })
        {
            var item = new ToolStripMenuItem($"{percent}%");
            item.Click += (_, _) => { Opacity = percent / 100d; settings.OverlayOpacity = Opacity; SavePreferences(); };
            opacity.DropDownItems.Add(item);
        }
        menu.Items.Add(opacity);
        menu.Items.Add("Reset position", null, (_, _) => { RestorePosition(null); SavePosition(); });
        menu.Items.Add("Usage details", null, (_, _) => ShowDetails(null));
        menu.Items.Add("Settings", null, (_, _) => Configure());
        menu.Items.Add("Hide overlay", null, (_, _) => Hide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        menu.Opening += (_, _) =>
        {
            OverlayMenuTheme.Apply(menu);
            pin.Checked = TopMost;
            foreach (ToolStripMenuItem item in opacity.DropDownItems)
                item.Checked = item.Text == $"{Math.Round(Opacity * 100)}%";
        };
        tray = new() { Text = "CodexBar · loading", Icon = appIcon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenDashboard();
        surface.RefreshRequested += async () => await RefreshAsync();
        surface.PinRequested += TogglePin;
        surface.MenuRequested += () => menu.Show(Cursor.Position);
        surface.HideRequested += Hide;
        surface.DetailsRequested += provider => ShowDetails(provider);
        surface.AccountsRequested += () => { PopulateAccountMenu(); accountMenu.DropDown.Show(Cursor.Position); };
        surface.DragRequested += () => { ReleaseCapture(); SendMessage(Handle, 0x00A1, 2, 0); };
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.R) { await RefreshAsync(); e.Handled = true; }
            if (e.Control && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                Location = new(Left + (e.KeyCode == Keys.Left ? -10 : e.KeyCode == Keys.Right ? 10 : 0),
                    Top + (e.KeyCode == Keys.Up ? -10 : e.KeyCode == Keys.Down ? 10 : 0));
                SavePosition(); e.Handled = true;
            }
            if (e.Shift && e.KeyCode == Keys.F10) { menu.Show(Cursor.Position); e.Handled = true; }
        };
        RenderCards();
        RestorePosition(settings.OverlayX is { } x && settings.OverlayY is { } y ? new Point(x, y) : null);
        timer.Interval = settings.RefreshMinutes * 60_000;
        timer.Tick += async (_, _) => await RefreshAsync();
        countdown.Tick += (_, _) => RenderCards();
        Shown += async (_, _) =>
        {
            ready = true;
            if (Environment.GetCommandLineArgs().Contains("--start-hidden")) Hide();
            await RefreshAsync();
            if (quitting) return;
            timer.Start(); countdown.Start();
            if (this.smoke)
            {
                BeginInvoke(() =>
                {
                    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "smoke"));
                    var previousLocation = Location;
                    Location = new(Left - 20, Top - 20);
                    SendMessage(Handle, 0x0232, 0, 0);
                    var positionSaved = settings.OverlayX == Left && settings.OverlayY == Top;
                    var previousPin = TopMost;
                    TogglePin(); var pinChanged = TopMost != previousPin; TogglePin();
                    Hide(); var hid = !Visible; OpenDashboard();
                    var overlayPassed = positionSaved && pinChanged && TopMost == previousPin && hid && Visible &&
                        FormBorderStyle == FormBorderStyle.None && !ShowInTaskbar;
                    RestorePosition(previousLocation);
                    using var image = new Bitmap(Width, Height);
                    DrawToBitmap(image, new Rectangle(Point.Empty, Size));
                    image.Save(Path.Combine(AppContext.BaseDirectory, "smoke", missingCredentialsSmoke ? "missing-credentials.png" : "dashboard.png"));
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke", missingCredentialsSmoke ? "missing-credentials-result.txt" : "result.txt"),
                        missingCredentialsSmoke
                            ? errors.Count == 2 && snapshots.Count == 0 && !refreshing
                                ? "PASS: missing credentials displayed for both providers; refresh recovered; no unhandled exception" : "FAIL"
                            : snapshots.Count == 2 && errors.Count == 0 && overlayPassed ? "PASS: compact overlay rendered; position saved; pin toggled; hide/show and tray initialized" : "FAIL");
                    if (!missingCredentialsSmoke)
                    {
                        var accountsPassed = AccountSmoke.Run();
                        if (!accountsPassed) File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke", "result.txt"), "FAIL: account checks");
                        using var chooser = new CodexAccountsForm(accounts, settings.CodexPath, "Personal · Plus", true);
                        chooser.Show(); chooser.Update();
                        using var chooserImage = new Bitmap(chooser.Width, chooser.Height);
                        chooser.DrawToBitmap(chooserImage, new Rectangle(Point.Empty, chooser.Size));
                        chooserImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "accounts.png")); chooser.Hide();
                        PopulateAccountMenu();
                        // Synthetic menu rows exercise normal enabled text, not only demo-disabled text.
                        accountMenu.DropDownItems[0].Enabled = accountMenu.DropDownItems[1].Enabled = true;
                        accountMenu.DropDown.Show(new Point(Left, Bottom)); accountMenu.DropDown.Update();
                        using var menuImage = new Bitmap(accountMenu.DropDown.Width, accountMenu.DropDown.Height);
                        accountMenu.DropDown.DrawToBitmap(menuImage, new Rectangle(Point.Empty, accountMenu.DropDown.Size));
                        menuImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "account-menu.png"));
                        accountMenu.DropDownItems[1].Enabled = false;
                        accountMenu.DropDown.Invalidate(); accountMenu.DropDown.Update();
                        accountMenu.DropDown.DrawToBitmap(menuImage, new Rectangle(Point.Empty, accountMenu.DropDown.Size));
                        menuImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "account-menu-disabled.png"));
                        accountMenu.DropDown.Hide();
                        using var preferences = new SettingsForm(settings);
                        preferences.Show();
                        preferences.Update();
                        using var settingsImage = new Bitmap(preferences.Width, preferences.Height);
                        preferences.DrawToBitmap(settingsImage, new Rectangle(Point.Empty, preferences.Size));
                        settingsImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "settings.png"));
                        preferences.Hide();
                        var migrated = Settings.Parse("{\"ClaudeSource\":2,\"UseClaudeDesktopCache\":false}");
                        if (migrated.ClaudeSource != ClaudeUsageSource.DesktopLive || migrated.UseClaudeDesktopLive)
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke", "result.txt"), "FAIL: Desktop preference migration");
                        var codeSnapshot = Demo(Provider.Claude) with { Source = "Claude Desktop Code live API",
                            SourceDetail = "Verified organization: Synthetic organization. Desktop's selected organization could not be verified while its cookie database is locked." };
                        var codeCard = new OverlayProvider(Provider.Claude, codeSnapshot, null);
                        if (!codeCard.Details.Contains(codeSnapshot.SourceDetail) || !codeCard.Details.Contains("Fetched:"))
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke", "result.txt"), "FAIL: Desktop live source qualification");
                        surface.Providers = [new(Provider.Codex, Demo(Provider.Codex), null), codeCard];
                        surface.Invalidate(); surface.Update();
                        using var codeImage = new Bitmap(Width, Height);
                        DrawToBitmap(codeImage, new Rectangle(Point.Empty, Size));
                        codeImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "desktop-code-overlay.png"));
                        surface.Providers = Enum.GetValues<Provider>().Select(provider => new OverlayProvider(provider,
                            Demo(provider) with { Windows = Demo(provider).Windows.Select(window => window with { UsedPercent = 0 }).ToArray() }, null)).ToArray();
                        surface.Invalidate(); surface.Update();
                        using var fullQuotaImage = new Bitmap(Width, Height);
                        DrawToBitmap(fullQuotaImage, new Rectangle(Point.Empty, Size));
                        fullQuotaImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "full-quota-overlay.png"));
                        surface.Providers = [new(Provider.Codex, Demo(Provider.Codex), null),
                            new(Provider.Claude, Demo(Provider.Claude) with { Source = "Claude Desktop cache",
                                FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                                Windows = Demo(Provider.Claude).Windows.Select(w => w with { ResetsAt = null }).ToArray() }, null)];
                        surface.Invalidate(); surface.Update();
                        using var cachedImage = new Bitmap(Width, Height);
                        DrawToBitmap(cachedImage, new Rectangle(Point.Empty, Size));
                        cachedImage.Save(Path.Combine(AppContext.BaseDirectory, "smoke", "cached-overlay.png"));
                    }
                    Quit();
                });
            }
        };
        FormClosing += (_, e) =>
        {
            if (!quitting && ready) { e.Cancel = true; Hide(); }
        };
        FormClosed += (_, _) =>
        {
            SavePosition();
            shutdown.Cancel(); timer.Dispose(); countdown.Dispose(); tray.Visible = false; tray.Dispose();
            menu.Dispose(); http.Dispose(); appIcon.Dispose();
        };
    }

    private void OpenDashboard()
    {
        RestorePosition(Location); Show(); WindowState = FormWindowState.Normal; Activate();
    }
    private void Quit() { quitting = true; shutdown.Cancel(); Close(); }
    private void Configure()
    {
        if (demo) return;
        using var form = new SettingsForm(settings);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        timer.Interval = settings.RefreshMinutes * 60_000;
        // A changed account path must not keep an old account's snapshot on screen.
        generation++; refreshCancellation?.Cancel();
        snapshots.Clear(); errors.Clear(); notified.Clear(); credentialOwners.Clear(); RenderCards();
        if (refreshing) return; // Its finally block starts the superseding refresh.
        _ = RefreshAsync();
    }

    private void PopulateAccountMenu()
    {
        accountMenu.DropDownItems.Clear();
        try
        {
            if (demo)
            {
                accountMenu.DropDownItems.Add(new ToolStripMenuItem("Personal · Plus") { Checked = true, Enabled = false });
                accountMenu.DropDownItems.Add(new ToolStripMenuItem("Work · Team") { Enabled = false });
            }
            else foreach (var account in accounts.Vault.List())
            {
                var row = new ToolStripMenuItem(account.Label) { Checked = account.IdentityKey == currentAccountKey, Enabled = account.IdentityKey.Length > 0 };
                row.Click += (_, _) => SwitchAccount(account);
                accountMenu.DropDownItems.Add(row);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        { accountMenu.DropDownItems.Add(new ToolStripMenuItem("Saved accounts need attention") { Enabled = false }); }
        accountMenu.DropDownItems.Add(new ToolStripSeparator());
        accountMenu.DropDownItems.Add("Manage / add accounts…", null, (_, _) => ManageAccounts());
        // This dropdown also opens directly from the Codex card, without the parent menu.
        OverlayMenuTheme.Apply(accountMenu.DropDown);
    }

    private void ManageAccounts()
    {
        using var form = new CodexAccountsForm(accounts, settings.CodexPath, currentAccountLabel, demo);
        if (form.ShowDialog(this) == DialogResult.OK && form.SwitchedAccount is { } selected) AccountSwitched(selected);
    }

    private void SwitchAccount(CodexAccountInfo account)
    {
        if (demo) return;
        try
        {
            CodexAccountService.RequireFileHome(settings.CodexPath);
            CodexAccountService.RequireClosedClients();
            accounts.Prepare();
            AccountSwitched(CodexAuthFileSwitcher.Switch(accounts.Vault, account.Id, settings.CodexPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(this, ex is InvalidDataException ? ex.Message : "Account switch could not finish. Close Codex and check folder access, then try again.",
                "Codex accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void AccountSwitched(CodexAccountInfo account)
    {
        currentAccountLabel = settings.CodexAccountLabel = account.Label;
        currentAccountKey = settings.CodexAccountIdentity = account.IdentityKey;
        SavePreferences();
        // Supersede every in-flight result before displaying the selected account.
        generation++; refreshCancellation?.Cancel();
        snapshots.Remove(Provider.Codex); errors.Remove(Provider.Codex); notified.Remove(Provider.Codex); credentialOwners.Remove(Provider.Codex);
        RenderCards(); if (!refreshing) _ = RefreshAsync();
        if (!demo && !smoke) tray.ShowBalloonTip(5000, "Codex account switched", "Selected " + account.Label + ". Reopen Codex to use this login.", ToolTipIcon.Info);
    }

    private async Task RefreshAsync()
    {
        if (refreshing || quitting) return;
        refreshing = true; surface.Busy = true; surface.Invalidate();
        var currentGeneration = generation;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        refreshCancellation = operation;
        try
        {
            foreach (var provider in Enum.GetValues<Provider>())
            {
                if (!(provider == Provider.Codex ? settings.CodexEnabled : settings.ClaudeEnabled))
                { snapshots.Remove(provider); errors.Remove(provider); continue; }
                try
                {
                    UsageSnapshot snapshot;
                    if (demo) snapshot = Demo(provider);
                    else
                    {
                        snapshot = await FetchProviderAsync(provider, operation.Token);
                    }
                    if (quitting || currentGeneration != generation) return;
                    snapshots[provider] = snapshot; errors.Remove(provider);
                    if (snapshot.Source == "Claude Desktop cache")
                        errors[provider] = "Desktop history cannot verify the current account or live quota. These are historical values only. Use a Claude Code CLI subscription login for API quota.";
                    var low = snapshot.Windows.Any(w => w.RemainingPercent <= 10);
                    if (!low) notified.Remove(provider);
                    else if (!demo && snapshot.Source != "Claude Desktop cache" && settings.NotifyLowQuota && notified.Add(provider))
                        tray.ShowBalloonTip(5000, $"{provider} quota is running low", "10% or less remains in a quota window.", ToolTipIcon.Warning);
                }
                catch (OperationCanceledException) when (quitting || currentGeneration != generation) { return; }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException or ArgumentException)
                {
                    if (quitting || currentGeneration != generation) return;
                    if (ex is AuthenticationExpiredException or ClaudeDesktopSessionException) snapshots.Remove(provider);
                    errors[provider] = ex is InvalidDataException or AuthenticationExpiredException or CredentialFileMissingException or ClaudeSubscriptionMissingException or ClaudeDesktopSessionException ? ex.Message :
                        ex is OperationCanceledException ? "Request timed out. Try refreshing again." :
                        "Unable to read usage. Check the credential path and network, then refresh.";
                }
            }
            RenderCards();
        }
        finally
        {
            refreshCancellation = null; refreshing = false;
            if (!IsDisposed && !quitting)
            {
                surface.Busy = false; surface.Invalidate();
                if (currentGeneration != generation) _ = RefreshAsync();
            }
        }
    }

    private async Task<UsageSnapshot> FetchProviderAsync(Provider provider, CancellationToken cancellation)
    {
        var path = provider == Provider.Codex ? settings.CodexPath : settings.ClaudePath;
        if (provider == Provider.Claude)
            return await ClaudeSourceResolver.FetchAsync(path, CredentialReader.DefaultPath(provider), settings.ClaudeSource,
                settings.UseClaudeDesktopLive, ReadCredential, FetchApi, ClaudeDesktopUsage.ReadAvailableAsync, cancellation, FetchDesktopLive);
        return await FetchApi(await ReadCredential(cancellation), cancellation);

        async Task<Credentials> ReadCredential(CancellationToken token)
        {
            try
            {
                var credential = await CredentialReader.ReadAsync(provider, path, token);
                token.ThrowIfCancellationRequested();
                if (provider == Provider.Codex)
                {
                    currentAccountKey = credential.Identity?.Key;
                    currentAccountLabel = credential.Identity is { } identity
                        ? identity.Key == settings.CodexAccountIdentity ? settings.CodexAccountLabel ?? identity.Label : identity.Label : null;
                }
                return credential;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                var canUseDesktop = provider == Provider.Claude && settings.ClaudeSource == ClaudeUsageSource.Automatic &&
                    settings.UseClaudeDesktopLive && ex is CredentialFileMissingException or ClaudeSubscriptionMissingException &&
                    string.Equals(Path.GetFullPath(path), Path.GetFullPath(CredentialReader.DefaultPath(provider)), StringComparison.OrdinalIgnoreCase);
                if (!canUseDesktop) snapshots.Remove(provider);
                if (provider == Provider.Codex) { currentAccountKey = null; currentAccountLabel = null; }
                throw;
            }
        }

        Task<UsageSnapshot> FetchDesktopLive(CancellationToken token) => desktopClient.FetchAsync(token, owner =>
        {
            if (credentialOwners.TryGetValue(provider, out var previous) && previous != owner)
            { snapshots.Remove(provider); notified.Remove(provider); }
            credentialOwners[provider] = owner;
        });

        async Task<UsageSnapshot> FetchApi(Credentials credential, CancellationToken token)
        {
            var owner = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(credential.Identity?.Key ?? credential.AccessToken + "\0" + credential.AccountId)));
            if (credentialOwners.TryGetValue(provider, out var previous) && previous != owner)
            { snapshots.Remove(provider); notified.Remove(provider); }
            credentialOwners[provider] = owner;
            return await client.FetchAsync(provider, credential, token);
        }
    }

    private void RenderCards()
    {
        if (quitting) return;
        var providers = new List<OverlayProvider>();
        foreach (var provider in Enum.GetValues<Provider>())
        {
            if (!(provider == Provider.Codex ? settings.CodexEnabled : settings.ClaudeEnabled)) continue;
            snapshots.TryGetValue(provider, out var snapshot);
            errors.TryGetValue(provider, out var error);
            providers.Add(new(provider, snapshot, error));
        }
        surface.Providers = providers;
        surface.AccountLabel = currentAccountLabel;
        surface.AccessibleDescription = string.Join("\n\n", providers.Select(p => p.Details));
        ResizeOverlay();
        if (ready) RestorePosition(Location);
        surface.Invalidate();
        var summary = string.Join(" | ", snapshots.Values.Select(s => s.Source == "Claude Desktop cache" ? "Claude live quota unavailable" : $"{s.Provider} {(s.Windows.Count > 0 ? $"{s.Windows[0].RemainingPercent:0}%" : "credits")}{(errors.ContainsKey(s.Provider) ? " stale" : "")}"));
        var tooltip = "CodexBar · " + (summary.Length == 0 ? "Needs sign-in" : summary);
        tray.Text = tooltip[..Math.Min(63, tooltip.Length)];
    }

    private void ResizeOverlay()
    {
        var scale = DeviceDpi / 96f;
        ClientSize = new((int)Math.Round(OverlaySurface.LogicalWidth * scale), (int)Math.Round(surface.LogicalHeight * scale));
        using var outline = OverlaySurface.Rounded(new RectangleF(0, 0, Width, Height), 12 * scale);
        var previous = Region; Region = new(outline); previous?.Dispose();
    }

    private void RestorePosition(Point? saved) => Location = OverlayPlacement.Restore(saved, Size,
        Screen.AllScreens.OrderByDescending(s => s.Primary).Select(s => s.WorkingArea).ToArray());

    private void SavePosition()
    {
        settings.OverlayX = Left; settings.OverlayY = Top; SavePreferences();
    }
    private void SavePreferences()
    {
        if (demo || smoke) return;
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { tray.ShowBalloonTip(3000, "CodexBar", "Overlay preferences could not be saved.", ToolTipIcon.Info); }
    }

    private void TogglePin()
    {
        TopMost = !TopMost; settings.OverlayPinned = TopMost; surface.Pinned = TopMost; surface.Invalidate(); SavePreferences();
    }

    private void ShowDetails(OverlayProvider? provider)
    {
        using var details = new Form { Text = "CodexBar · usage details", Size = new(450, 380), Font = new("Segoe UI", 10),
            StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(24, 30, 41),
            ForeColor = Color.FromArgb(226, 235, 248), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            TopMost = TopMost };
        var text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, BackColor = details.BackColor, ForeColor = details.ForeColor,
            Text = (provider?.Details ?? string.Join("\n\n", surface.Providers.Select(p => p.Details))).Replace("\n", Environment.NewLine) };
        var container = new Panel { Dock = DockStyle.Fill, Padding = new(18) }; container.Controls.Add(text); details.Controls.Add(container);
        details.ShowDialog(this);
    }

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x80; parameters.ClassStyle |= 0x20000; return parameters; }
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x0232) { RestorePosition(Location); SavePosition(); }
        if (message.Msg == 0x007E && ready) { RestorePosition(Location); SavePosition(); }
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e); ResizeOverlay(); RestorePosition(Location);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, nint wParam, nint lParam);

    private static UsageSnapshot Demo(Provider provider) => new(provider, provider == Provider.Codex ? "Plus" : null,
        [new("Session (5 hours)", provider == Provider.Codex ? 26 : 57, DateTimeOffset.UtcNow.AddHours(3)),
         new("Weekly", provider == Provider.Codex ? 44 : 68, DateTimeOffset.UtcNow.AddDays(4))], DateTimeOffset.UtcNow);

    private static Icon CreateIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }
}
