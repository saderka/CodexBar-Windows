using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CodexBar.Core;

namespace CodexBar.Windows;

internal sealed record OverlayProvider(Provider Provider, UsageSnapshot? Snapshot, string? Error)
{
    public string Details => $"{Provider}{(Snapshot?.Plan is { } plan ? " · " + plan : "")}\n" +
        (Snapshot?.Source == "Claude Desktop cache" ? "Historical Desktop sample only. Current account and live quota are NOT verified.\n" : "") +
        (Snapshot is { } data ? string.Join("\n", data.Windows.Select(w =>
            $"{w.Name}: {w.RemainingPercent:0.#}% remaining · {CodexBar.Core.ResetText.Format(w.ResetsAt, DateTimeOffset.UtcNow)}" +
            (w.ResetsAt is { } reset ? $" · {reset.ToLocalTime():yyyy-MM-dd HH:mm}" : ""))) +
            $"\nSource: {data.Source}\n{(data.Source == "Claude Desktop cache" ? "Recorded" : "Fetched")}: {data.FetchedAt.ToLocalTime():g}" +
            (data.SourceDetail is { } sourceDetail ? $"\n{sourceDetail}" : "") +
            (data.Credits is { } credits ? $"\nCredits: {credits:0.##}" : "") : "No usage available") +
        (Error is { } error ? "\n\n" + error : "");
}

internal sealed class OverlaySurface : Control
{
    public const int LogicalWidth = 400;
    public const int HeaderHeight = 24;
    public const int CardHeight = 34;
    public const int CardGap = 6;
    private readonly ToolTip hint = new() { AutoPopDelay = 15000, InitialDelay = 450, ReshowDelay = 100 };
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal IReadOnlyList<OverlayProvider> Providers { get; set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Pinned { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Busy { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Demo { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? AccountLabel { get; set; }
    public event Action? DragRequested;
    public event Action? RefreshRequested;
    public event Action? PinRequested;
    public event Action? MenuRequested;
    public event Action? HideRequested;
    public event Action<OverlayProvider>? DetailsRequested;
    public event Action? AccountsRequested;
    private Point hover = new(-1, -1);
    public int LogicalHeight => HeaderHeight + CardHeight + CardGap;

    public OverlaySurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(16, 20, 28);
        AccessibleName = "CodexBar quota overlay";
    }

    private Point LogicalPoint(Point point) => new((int)(point.X * 96f / DeviceDpi), (int)(point.Y * 96f / DeviceDpi));
    private static Rectangle ActionBox(int index) => new(LogicalWidth - 108 + index * 26, 0, 24, 23);
    private Rectangle CardBox(int index)
    {
        var count = Math.Max(1, Providers.Count);
        var width = (LogicalWidth - CardGap * (count + 1)) / count;
        return new(CardGap + index * (width + CardGap), HeaderHeight, width, CardHeight);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = LogicalPoint(e.Location);
        if (next == hover) return;
        hover = next;
        var tooltip = "Drag to move · right-click for options";
        for (var i = 0; i < 4; i++)
            if (ActionBox(i).Contains(hover)) tooltip = new[] { "Refresh (Ctrl+R)", "Always on top", "Options", "Hide to tray (Esc)" }[i];
        var provider = ProviderAt(hover);
        if (provider != null) tooltip = provider.Details + (provider.Provider == Provider.Codex ? "\n\nClick Codex name to switch accounts · click meters for details" : "\n\nClick for details");
        hint.SetToolTip(this, tooltip);
        Cursor = hover.Y < HeaderHeight && hover.X < ActionBox(0).X ? Cursors.SizeAll : Cursors.Hand;
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { hover = new(-1, -1); Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var point = LogicalPoint(e.Location);
        if (e.Button == MouseButtons.Right) { MenuRequested?.Invoke(); return; }
        if (e.Button != MouseButtons.Left) return;
        if (point.Y < HeaderHeight)
        {
            if (ActionBox(0).Contains(point)) RefreshRequested?.Invoke();
            else if (ActionBox(1).Contains(point)) PinRequested?.Invoke();
            else if (ActionBox(2).Contains(point)) MenuRequested?.Invoke();
            else if (ActionBox(3).Contains(point)) HideRequested?.Invoke();
            else DragRequested?.Invoke();
        }
        else if (ProviderAt(point) is { } provider)
        {
            var box = CardBox(Providers.ToList().IndexOf(provider));
            if (provider.Provider == Provider.Codex && point.X < box.X + 60) AccountsRequested?.Invoke();
            else DetailsRequested?.Invoke(provider);
        }
        else DragRequested?.Invoke();
    }

    private OverlayProvider? ProviderAt(Point point)
    {
        for (var i = 0; i < Providers.Count; i++)
            if (CardBox(i).Contains(point)) return Providers[i];
        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(DeviceDpi / 96f, DeviceDpi / 96f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var bounds = new RectangleF(.5f, .5f, LogicalWidth - 1, LogicalHeight - 1);
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(26, 31, 43), Color.FromArgb(14, 18, 26), 65);
        using var shape = Rounded(bounds, 12);
        g.FillPath(background, shape);
        using var border = new Pen(Color.FromArgb(60, 70, 87)); g.DrawPath(border, shape);
        using var accent = new SolidBrush(Color.FromArgb(114, 190, 255));
        using var orange = new SolidBrush(Color.FromArgb(255, 172, 114));
        g.FillRectangle(accent, 11, 13, 2, 6); g.FillRectangle(accent, 16, 9, 2, 10); g.FillRectangle(orange, 21, 5, 2, 14);
        DrawText(g, "CodexBar", new(29, 2, 78, 21), 11.5f, Color.FromArgb(238, 243, 250), true);
        DrawText(g, Demo ? "DEMO · LEFT" : "LEFT", new(107, 4, 75, 17), 7, Color.FromArgb(133, 149, 170), true);
        if (AccountLabel != null) DrawText(g, AccountLabel, new(191, 4, 94, 17), 8, Color.FromArgb(114, 190, 255), false, true);
        if (Busy)
        { g.FillEllipse(accent, 180, 10, 4, 4); }
        for (var i = 0; i < 4; i++) DrawAction(g, i);
        for (var i = 0; i < Providers.Count; i++) DrawProvider(g, Providers[i], CardBox(i));
        if (Providers.Count == 0)
            DrawText(g, "Enable a provider in Settings", new(15, HeaderHeight + 5, 270, 22), 11, Color.Silver);
    }

    private void DrawAction(Graphics g, int index)
    {
        var box = ActionBox(index);
        if (box.Contains(hover))
        { using var brush = new SolidBrush(Color.FromArgb(47, 56, 74)); using var shape = Rounded(box, 7); g.FillPath(brush, shape); }
        var color = index == 1 && Pinned ? Color.FromArgb(114, 190, 255) : Color.FromArgb(150, 164, 183);
        using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = box.X + 12; var y = box.Y + 11;
        switch (index)
        {
            case 0: g.DrawArc(pen, x - 6, y - 6, 12, 12, 35, 290); g.DrawLines(pen, [new(x + 4, y - 7), new(x + 4, y - 2), new(x + 8, y - 3)]); break;
            case 1: g.DrawLines(pen, [new(x - 4, y - 6), new(x + 4, y - 6), new(x + 3, y), new(x + 6, y + 2), new(x - 6, y + 2), new(x - 3, y), new(x - 4, y - 6)]); g.DrawLine(pen, x, y + 2, x, y + 8); break;
            case 2: using (var brush = new SolidBrush(color)) { for (var n = -1; n <= 1; n++) g.FillEllipse(brush, x - 1, y + n * 5 - 1, 2.5f, 2.5f); } break;
            case 3: g.DrawLine(pen, x - 5, y, x + 5, y); break;
        }
    }

    private void DrawProvider(Graphics g, OverlayProvider view, Rectangle box)
    {
        var x = box.X; var y = box.Y; var width = box.Width;
        var bounds = (RectangleF)box;
        using var shape = Rounded(bounds, 8);
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(34, 41, 55), Color.FromArgb(26, 32, 44), 35);
        g.FillPath(background, shape);
        using var line = new Pen(Color.FromArgb(52, 62, 80)); g.DrawPath(line, shape);
        var color = view.Provider == Provider.Codex ? Color.FromArgb(114, 190, 255) : Color.FromArgb(255, 172, 114);
        DrawText(g, view.Provider.ToString(), new(x + 8, y + 4, 43, 16), 10.5f, color, true);
        var cached = view.Snapshot?.Source == "Claude Desktop cache";
        var codeSession = view.Snapshot?.Source == "Claude Desktop Code live API";
        var badge = view.Error != null ? view.Snapshot == null ? "CONNECT" : cached ? "CACHE·OLD" : "STALE" : cached ? "CACHED" :
            codeSession ? "CODE*" : view.Snapshot?.Plan?.ToUpperInvariant() ?? "LIVE";
        if (view.Snapshot == null && view.Error == null) badge = "WAITING";
        DrawText(g, badge, new(x + 8, y + 20, 43, 9), 6, view.Error != null || codeSession ? Color.FromArgb(244, 180, 113) : Color.FromArgb(143, 157, 179), true);
        if (cached)
        {
            // Even a recent historical sample cannot identify the current Desktop login.
            // Do not present its numbers as the current account's available quota.
            DrawText(g, "Live quota unavailable", new(x + 62, y + 1, width - 70, 17), 9, Color.FromArgb(229, 237, 248), true);
            DrawText(g, "History only · click details", new(x + 62, y + 19, width - 70, 12), 7, Color.FromArgb(174, 189, 210));
            return;
        }
        if (view.Snapshot is not { } snapshot)
        {
            DrawText(g, view.Error == null ? "Loading…" : "Connect account", new(x + 62, y + 3, width - 70, 17), 10, Color.FromArgb(207, 218, 234), true);
            DrawText(g, view.Error == null ? "Fetching usage" : "Click for details", new(x + 62, y + 20, width - 70, 10), 7, Color.FromArgb(144, 158, 178));
            return;
        }
        var windows = snapshot.Windows.Take(2).ToArray();
        for (var index = 0; index < windows.Length; index++)
        {
            var window = windows[index];
            // Reserve enough width for all four characters of 100%, while keeping
            // both quota windows and the provider inside the existing compact card.
            var cellWidth = (width - 64) / 2f;
            var cellX = x + 56 + index * (cellWidth + 4);
            var title = window.Name.Contains("Weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" : window.Name.Contains("Session", StringComparison.OrdinalIgnoreCase) ? "Session" : window.Name;
            DrawText(g, title, new(cellX, y + 2, cellWidth * .44f, 15), 7, Color.FromArgb(153, 167, 188));
            DrawText(g, $"{window.RemainingPercent:0}%", new(cellX + cellWidth * .44f, y, cellWidth * .56f, 20), 11.5f, color, true, true);
            using var track = new SolidBrush(Color.FromArgb(51, 62, 81)); using var fill = new SolidBrush(color);
            g.FillRectangle(track, cellX, y + 18, cellWidth, 2);
            g.FillRectangle(fill, cellX, y + 18, (float)(cellWidth * window.RemainingPercent / 100), 2);
            var reset = CodexBar.Core.ResetText.Format(window.ResetsAt, DateTimeOffset.UtcNow);
            var compactReset = window.ResetsAt == null ? "Reset unknown" :
                reset.StartsWith("Resets in ", StringComparison.Ordinal) ? "↻ " + reset[10..] : "↻ Due · refresh";
            DrawText(g, compactReset, new(cellX, y + 21, cellWidth, 11), 7, Color.FromArgb(174, 189, 210));
        }
        if (windows.Length == 0)
            DrawText(g, snapshot.Credits is { } credits ? $"{credits:0.##} credits" : "No quota", new(x + 62, y + 5, width - 70, 22), 11.5f, color, true);
    }

    private static void DrawText(Graphics g, string text, RectangleF box, float pixels, Color color, bool bold = false, bool right = false)
    {
        using var font = new Font("Segoe UI", pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
            Alignment = right ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, box, format);
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath(); var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    protected override void Dispose(bool disposing) { if (disposing) hint.Dispose(); base.Dispose(disposing); }
}

internal sealed class OverlayMenuColors : ProfessionalColorTable
{
    public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override Color ToolStripDropDownBackground => Color.FromArgb(27, 33, 45);
    public override Color MenuItemSelected => Color.FromArgb(48, 60, 79);
    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
    public override Color MenuItemBorder => Color.FromArgb(68, 81, 103);
    public override Color MenuBorder => MenuItemBorder;
    public override Color MenuItemPressedGradientBegin => MenuItemSelected;
    public override Color MenuItemPressedGradientMiddle => MenuItemSelected;
    public override Color MenuItemPressedGradientEnd => MenuItemSelected;
    public override Color SeparatorDark => MenuItemBorder;
    public override Color SeparatorLight => ToolStripDropDownBackground;
    public override Color CheckBackground => MenuItemSelected;
    public override Color CheckSelectedBackground => MenuItemSelected;
    public override Color CheckPressedBackground => MenuItemSelected;
}

internal static class OverlayMenuTheme
{
    internal static readonly Color Foreground = Color.FromArgb(229, 237, 248);
    internal static readonly Color Muted = Color.FromArgb(143, 159, 182);

    public static void Apply(ToolStrip menu)
    {
        menu.BackColor = Color.FromArgb(27, 33, 45);
        menu.ForeColor = Foreground;
        menu.Renderer = new OverlayMenuRenderer();
        if (menu is ToolStripDropDownMenu dropdown) { dropdown.ShowImageMargin = false; dropdown.ShowCheckMargin = true; }
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = item.Enabled ? Foreground : Muted;
            if (item is ToolStripDropDownItem nested && nested.HasDropDownItems) Apply(nested.DropDown);
        }
    }
}

internal sealed class OverlayMenuRenderer : ToolStripProfessionalRenderer
{
    public OverlayMenuRenderer() : base(new OverlayMenuColors()) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // The standard renderer substitutes a system color for disabled items.
        // Draw explicitly so both states retain contrast on the dark background.
        var color = e.Item.Enabled ? OverlayMenuTheme.Foreground : OverlayMenuTheme.Muted;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled != false ? OverlayMenuTheme.Foreground : OverlayMenuTheme.Muted;
        base.OnRenderArrow(e);
    }
}
