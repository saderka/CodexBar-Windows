using System.Security.Cryptography;
using CodexBar.Core;

namespace CodexBar.Windows;

internal sealed class CodexAccountsForm : Form
{
    private readonly CodexAccountService service;
    private readonly string authPath;
    private readonly ListBox accounts;
    private readonly Label status;
    private readonly Button switchButton;
    private readonly Button addButton;
    private readonly Button saveButton;
    private readonly Button renameButton;
    private readonly Button removeButton;
    private readonly Button cancelButton;
    private CancellationTokenSource? login;
    private bool closeAfterLogin;
    public CodexAccountInfo? SwitchedAccount { get; private set; }
    private readonly bool preview;

    public CodexAccountsForm(CodexAccountService service, string authPath, string? activeLabel, bool preview = false)
    {
        this.service = service; this.authPath = authPath; this.preview = preview;
        Text = "CodexBar · Codex accounts";
        ClientSize = new(510, 370);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(19, 23, 31); ForeColor = Color.FromArgb(229, 237, 248);
        Font = new("Segoe UI", 9);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        accounts = new ListBox { Location = new(20, 73), Size = new(470, 146), BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(31, 38, 51), ForeColor = ForeColor, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34 };
        accounts.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(42, 69, 95) : accounts.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            var account = (CodexAccountInfo)accounts.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, account.Label, Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 24, e.Bounds.Height),
                ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        saveButton = Button("Save current", 20, 232, 112);
        addButton = Button("+ Sign in another", 142, 232, 150);
        renameButton = Button("Rename", 302, 232, 85);
        removeButton = Button("Remove", 397, 232, 93);
        switchButton = Button("Switch account", 346, 321, 144);
        switchButton.BackColor = Color.FromArgb(39, 85, 127);
        cancelButton = Button("Cancel sign-in", 20, 321, 135); cancelButton.Visible = false;
        status = new Label { Location = new(20, 272), Size = new(470, 40), ForeColor = Color.FromArgb(158, 177, 198),
            Text = "Save your current login once. Add another account, then select it and Switch." };
        Controls.AddRange([accounts, saveButton, addButton, renameButton, removeButton, switchButton, cancelButton, status,
            new Label { Text = "Codex accounts", Font = new("Segoe UI", 16, FontStyle.Bold), Location = new(18, 14), Size = new(470, 30) },
            new Label { Text = "Current: " + (activeLabel ?? "current Codex login"), Location = new(20, 47), Size = new(470, 22) }]);
        accounts.SelectedIndexChanged += (_, _) => UpdateButtons();
        saveButton.Click += (_, _) => RunAction(() =>
        {
            service.Prepare();
            var auth = CodexAuthFileSwitcher.ReadBounded(authPath);
            try { Reload(service.Vault.Save(auth).Id); status.Text = "Current account saved. You can return to it after switching."; }
            finally { CryptographicOperations.ZeroMemory(auth); }
        });
        addButton.Click += async (_, _) => await SignInAsync();
        renameButton.Click += (_, _) => RunAction(() =>
        {
            if (Selected is not { } selected) return;
            using var name = new AccountNameForm(selected.Label);
            if (name.ShowDialog(this) != DialogResult.OK) return;
            var auth = service.Vault.ReadAuth(selected.Id);
            try { Reload(service.Vault.Save(auth, name.AccountName).Id); }
            finally { CryptographicOperations.ZeroMemory(auth); }
        });
        removeButton.Click += (_, _) => RunAction(() =>
        {
            if (Selected is not { } selected) return;
            service.Vault.Remove(selected.Id); Reload(); status.Text = "Removed from saved accounts. The active Codex login was kept.";
        });
        switchButton.Click += (_, _) => RunAction(() =>
        {
            if (Selected is not { } selected) return;
            CodexAccountService.RequireFileHome(authPath);
            CodexAccountService.RequireClosedClients();
            service.Prepare();
            SwitchedAccount = CodexAuthFileSwitcher.Switch(service.Vault, selected.Id, authPath);
            DialogResult = DialogResult.OK;
        });
        cancelButton.Click += (_, _) => login?.Cancel();
        FormClosing += (_, e) =>
        {
            if (login == null) return;
            e.Cancel = true; closeAfterLogin = true; login.Cancel(); status.Text = "Cancelling sign-in…";
        };
        if (preview)
        {
            accounts.Items.AddRange([new CodexAccountInfo(Guid.NewGuid().ToString("N"), "Personal · Plus", "demo-personal"),
                new CodexAccountInfo(Guid.NewGuid().ToString("N"), "Work · Team", "demo-work")]);
            accounts.SelectedIndex = 1;
        }
        else RunAction(() => Reload());
        UpdateButtons();
    }

    private CodexAccountInfo? Selected => accounts.SelectedItem as CodexAccountInfo;
    private void Reload(string? select = null)
    {
        accounts.Items.Clear();
        accounts.Items.AddRange(service.Vault.List().Cast<object>().ToArray());
        for (var i = 0; i < accounts.Items.Count; i++) if (((CodexAccountInfo)accounts.Items[i]).Id == select) accounts.SelectedIndex = i;
        if (accounts.SelectedIndex == -1 && accounts.Items.Count > 0) accounts.SelectedIndex = 0;
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        saveButton.Enabled = addButton.Enabled = !preview && login == null;
        removeButton.Enabled = !preview && login == null && Selected != null;
        switchButton.Enabled = renameButton.Enabled = removeButton.Enabled && !string.IsNullOrEmpty(Selected?.IdentityKey);
        cancelButton.Visible = login != null;
    }

    private async Task SignInAsync()
    {
        string executable;
        try { executable = CodexAccountService.FindExecutable(); }
        catch (FileNotFoundException)
        {
            using var choose = new OpenFileDialog { Title = "Select your Codex CLI executable", Filter = "Codex CLI|codex.exe", CheckFileExists = true };
            if (choose.ShowDialog(this) != DialogResult.OK) return;
            executable = choose.FileName;
        }
        using var operation = new CancellationTokenSource(); login = operation; UpdateButtons();
        status.Text = "Finish sign-in in your browser. Choose the account you want to add.";
        try
        {
            var account = await service.SignInAsync(executable, operation.Token);
            Reload(account.Id); status.Text = "Account saved. Close Codex before switching, then click Switch account.";
        }
        catch (OperationCanceledException) { status.Text = "Sign-in cancelled. Your current Codex login was kept."; }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(ex); }
        finally { login = null; UpdateButtons(); if (closeAfterLogin) Close(); }
    }
    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(ex); }
    }
    private static bool IsExpected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or
        System.ComponentModel.Win32Exception or System.Security.SecurityException or InvalidOperationException;
    private void ShowError(Exception ex)
    {
        var message = ex is InvalidDataException or CryptographicException ? ex.Message :
            ex is FileNotFoundException ? "No Codex login was found. Sign in to Codex first, or use Sign in another." :
            ex is IOException ? "Account operation could not finish. Close Codex and check folder access, then try again. The previous account is saved before any switch." :
            "Account operation could not finish. Check your Codex installation and folder permissions.";
        status.Text = "Account operation needs attention.";
        MessageBox.Show(this, message, "Codex accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private static Button Button(string text, int x, int y, int width) => new AccountActionButton { Text = text, Location = new(x, y), Size = new(width, 32),
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 44, 59), ForeColor = Color.FromArgb(229, 237, 248) };
}

internal sealed class AccountActionButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.FromArgb(19, 23, 31));
        using var shape = OverlaySurface.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5);
        using var background = new SolidBrush(Enabled ? BackColor : Color.FromArgb(25, 32, 43));
        using var border = new Pen(Enabled ? Color.FromArgb(71, 91, 117) : Color.FromArgb(49, 62, 79));
        g.FillPath(background, shape); g.DrawPath(border, shape);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(137, 154, 177),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}

internal sealed class AccountNameForm : Form
{
    private readonly TextBox name;
    public string AccountName => name.Text;
    public AccountNameForm(string current)
    {
        Text = "Account name"; ClientSize = new(340, 105); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        name = new() { Text = current, MaxLength = 60, Location = new(15, 15), Width = 310 };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new(235, 60), Size = new(90, 30) };
        Controls.AddRange([name, save]); AcceptButton = save;
    }
}
