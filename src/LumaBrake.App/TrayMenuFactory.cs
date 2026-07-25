using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace LumaBrake.App;

internal static class TrayMenuFactory
{
    public static Forms.ContextMenuStrip Create(
        Image? logo,
        Func<bool> protectionEnabled,
        Func<bool> oledCareActive,
        Func<TrayNightColorState> nightColorState,
        Action openDashboard,
        Action toggleProtection,
        Action applyOledCare,
        Action pauseNightColor,
        Action resumeNightColor,
        Action emergencyDisable,
        Action exportDiagnostics,
        Action exitApplication)
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = TrayMenuPalette.Chrome,
            ForeColor = TrayMenuPalette.Ink,
            Font = TrayMenuFonts.Text,
            Padding = new Forms.Padding(8, 8, 8, 8),
            ShowImageMargin = true,
            ShowCheckMargin = false,
            Renderer = new TrayMenuRenderer()
        };

        var header = new Forms.ToolStripMenuItem("LumaBrake", logo)
        {
            Enabled = false,
            Font = TrayMenuFonts.Title,
            ForeColor = TrayMenuPalette.Ink,
            Tag = TrayMenuTone.Header
        };
        var openItem = Item("Dashboard", "\uE80F", TrayMenuPalette.Accent, (_, _) => openDashboard());
        var toggleItem = Item("Toggle protection", "\uE769", TrayMenuPalette.Success, (_, _) => toggleProtection());
        var oledItem = Item("Apply OLED Care", "\uE7F4", TrayMenuPalette.Amber, (_, _) => applyOledCare());
        var nightStateItem = Item("Night Color: Off", "\uE708", TrayMenuPalette.Muted, (_, _) => { }, TrayMenuTone.Header);
        nightStateItem.Enabled = false;
        var pauseNightItem = Item("Pause Night Color 1h", "\uE769", TrayMenuPalette.AccentSoft, (_, _) => pauseNightColor());
        var resumeNightItem = Item("Resume Night Color now", "\uE768", TrayMenuPalette.Success, (_, _) => resumeNightColor());
        var exportItem = Item("Export diagnostics", "\uE896", TrayMenuPalette.Accent, (_, _) => exportDiagnostics());
        var emergencyItem = Item("Emergency disable", "\uE7BA", TrayMenuPalette.Danger, (_, _) => emergencyDisable(), TrayMenuTone.Danger);
        var exitItem = Item("Exit", "\uE8BB", TrayMenuPalette.Muted, (_, _) => exitApplication(), TrayMenuTone.Quiet);

        menu.Items.Add(header);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(oledItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(nightStateItem);
        menu.Items.Add(pauseNightItem);
        menu.Items.Add(resumeNightItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exportItem);
        menu.Items.Add(emergencyItem);
        menu.Items.Add(exitItem);

        menu.Opening += (_, e) => UpdateMenuState(
            menu,
            e,
            toggleItem,
            oledItem,
            nightStateItem,
            pauseNightItem,
            resumeNightItem,
            protectionEnabled,
            oledCareActive,
            nightColorState);
        menu.SizeChanged += (_, _) => ApplyRoundedRegion(menu);
        menu.Disposed += (_, _) => logo?.Dispose();

        return menu;
    }

    private static void UpdateMenuState(
        Forms.ContextMenuStrip menu,
        CancelEventArgs e,
        Forms.ToolStripMenuItem toggleItem,
        Forms.ToolStripMenuItem oledItem,
        Forms.ToolStripMenuItem nightStateItem,
        Forms.ToolStripMenuItem pauseNightItem,
        Forms.ToolStripMenuItem resumeNightItem,
        Func<bool> protectionEnabled,
        Func<bool> oledCareActive,
        Func<TrayNightColorState> nightColorState)
    {
        if (menu.IsDisposed)
        {
            e.Cancel = true;
            return;
        }

        var enabled = protectionEnabled();
        toggleItem.Text = enabled ? "Disable protection" : "Enable protection";
        toggleItem.Image?.Dispose();
        toggleItem.Image = TrayMenuIcon.Create(enabled ? "\uE769" : "\uE768", enabled ? TrayMenuPalette.Danger : TrayMenuPalette.Success);
        toggleItem.Tag = enabled ? TrayMenuTone.Danger : TrayMenuTone.Accent;

        var oledActive = oledCareActive();
        oledItem.Text = oledActive ? "OLED Care active" : "Apply OLED Care";
        oledItem.Enabled = !oledActive;
        oledItem.Image?.Dispose();
        oledItem.Image = TrayMenuIcon.Create("\uE7F4", oledActive ? TrayMenuPalette.Amber : TrayMenuPalette.Accent);
        oledItem.Tag = oledActive ? TrayMenuTone.Header : TrayMenuTone.Accent;

        var nightColor = nightColorState();
        var nightColorAccent = NightColorAccent(nightColor);
        nightStateItem.Text = nightColor.Summary;
        nightStateItem.ToolTipText = nightColor.Detail;
        nightStateItem.Image?.Dispose();
        nightStateItem.Image = TrayMenuIcon.Create("\uE708", nightColorAccent);

        pauseNightItem.Visible = nightColor.Enabled && nightColor.ActiveNow && !nightColor.UserPaused && !nightColor.AutoPaused;
        pauseNightItem.Enabled = pauseNightItem.Visible;
        pauseNightItem.Text = "Pause Night Color 1h";
        pauseNightItem.Image?.Dispose();
        pauseNightItem.Image = TrayMenuIcon.Create("\uE769", TrayMenuPalette.AccentSoft);

        resumeNightItem.Visible = nightColor.Enabled && nightColor.UserPaused;
        resumeNightItem.Enabled = resumeNightItem.Visible;
        resumeNightItem.Image?.Dispose();
        resumeNightItem.Image = TrayMenuIcon.Create("\uE768", TrayMenuPalette.Success);
        ApplyRoundedRegion(menu);
    }

    private static Color NightColorAccent(TrayNightColorState state)
    {
        if (!state.Enabled)
        {
            return TrayMenuPalette.Muted;
        }

        if (state.UserPaused || state.AutoPaused)
        {
            return TrayMenuPalette.Amber;
        }

        return state.ActiveNow ? TrayMenuPalette.Success : TrayMenuPalette.AccentSoft;
    }

    private static Forms.ToolStripMenuItem Item(
        string text,
        string glyph,
        Color color,
        EventHandler onClick,
        TrayMenuTone tone = TrayMenuTone.Accent)
    {
        return new Forms.ToolStripMenuItem(text, TrayMenuIcon.Create(glyph, color), onClick)
        {
            Tag = tone,
            Margin = new Forms.Padding(0, 2, 0, 2)
        };
    }

    private static void ApplyRoundedRegion(Forms.ContextMenuStrip menu)
    {
        if (menu.Width <= 0 || menu.Height <= 0)
        {
            return;
        }

        var path = RoundedRectangle(new Rectangle(0, 0, menu.Width, menu.Height), 12);
        menu.Region?.Dispose();
        menu.Region = new Region(path);
        path.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum TrayMenuTone
{
    Accent,
    Danger,
    Quiet,
    Header
}

internal readonly record struct TrayNightColorState(
    bool Enabled,
    bool UserPaused,
    bool AutoPaused,
    bool ActiveNow,
    string Summary,
    string Detail);

internal static class TrayMenuPalette
{
    public static readonly Color Chrome = Color.FromArgb(8, 12, 18);
    public static readonly Color Surface = Color.FromArgb(16, 23, 33);
    public static readonly Color Hover = Color.FromArgb(24, 45, 84);
    public static readonly Color Border = Color.FromArgb(42, 56, 74);
    public static readonly Color Ink = Color.FromArgb(244, 248, 255);
    public static readonly Color Muted = Color.FromArgb(156, 170, 190);
    public static readonly Color Accent = Color.FromArgb(74, 141, 255);
    public static readonly Color AccentSoft = Color.FromArgb(115, 167, 255);
    public static readonly Color Success = Color.FromArgb(94, 227, 126);
    public static readonly Color Amber = Color.FromArgb(233, 166, 64);
    public static readonly Color Danger = Color.FromArgb(255, 101, 118);
}

internal static class TrayMenuFonts
{
    public static readonly Font Text = new("Segoe UI Variable Text", 9.4f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Title = new("Segoe UI Variable Display", 10.4f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Glyph = new("Segoe Fluent Icons", 12.5f, FontStyle.Regular, GraphicsUnit.Point);
}

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(
            e.AffectedBounds,
            Color.FromArgb(12, 17, 25),
            TrayMenuPalette.Chrome,
            LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(TrayMenuPalette.Border);
        using var path = RoundedRectangle(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not Forms.ToolStripMenuItem item)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        var bounds = new Rectangle(4, 1, item.Width - 8, item.Height - 2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (item.Selected && item.Enabled)
        {
            using var brush = new LinearGradientBrush(
                bounds,
                TrayMenuPalette.Hover,
                Color.FromArgb(30, 34, 47),
                LinearGradientMode.Horizontal);
            using var path = RoundedRectangle(bounds, 7);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(96, TrayMenuPalette.Accent));
            e.Graphics.DrawPath(pen, path);
            return;
        }

        if (item.Tag is TrayMenuTone.Header)
        {
            using var brush = new SolidBrush(Color.FromArgb(120, TrayMenuPalette.Surface));
            using var path = RoundedRectangle(bounds, 8);
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(10, 15, 23));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Tag switch
        {
            TrayMenuTone.Danger => TrayMenuPalette.Danger,
            TrayMenuTone.Quiet => TrayMenuPalette.Muted,
            TrayMenuTone.Header => TrayMenuPalette.Ink,
            _ => e.Item.Selected ? Color.White : TrayMenuPalette.Ink
        };
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(78, TrayMenuPalette.Border));
        e.Graphics.DrawLine(pen, 44, y, e.Item.Width - 10, y);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal static class TrayMenuIcon
{
    public static Bitmap Create(string glyph, Color accent)
    {
        var image = new Bitmap(28, 28);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(46, accent));
        graphics.FillEllipse(background, 2, 2, 24, 24);
        using var border = new Pen(Color.FromArgb(120, accent));
        graphics.DrawEllipse(border, 2, 2, 24, 24);
        using var textBrush = new SolidBrush(accent);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(glyph, TrayMenuFonts.Glyph, textBrush, new RectangleF(0, 1, 28, 26), format);
        return image;
    }
}
