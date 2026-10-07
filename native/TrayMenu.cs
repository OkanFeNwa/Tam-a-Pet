using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Colours shared by the settings window and the tray menu (light/dark follows Windows).
static class Theme
{
    public static readonly bool Dark = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
    public static readonly Color Bg, SideBg, Card, Text, Muted, Line, Accent, AccentSoft, Track;

    static Theme()
    {
        if (Dark)
        {
            Bg = Color.FromArgb(0x14, 0x15, 0x1b); SideBg = Color.FromArgb(0x1a, 0x1b, 0x23); Card = Color.FromArgb(0x1f, 0x20, 0x29);
            Text = Color.FromArgb(0xee, 0xf0, 0xf6); Muted = Color.FromArgb(0x8d, 0x92, 0xa6); Line = Color.FromArgb(0x2a, 0x2c, 0x38);
            Accent = Color.FromArgb(0x8b, 0x7c, 0xff); AccentSoft = Color.FromArgb(0x2b, 0x28, 0x50); Track = Color.FromArgb(0x2f, 0x31, 0x42);
        }
        else
        {
            Bg = Color.FromArgb(0xf3, 0xf4, 0xf9); SideBg = Color.White; Card = Color.White;
            Text = Color.FromArgb(0x1b, 0x1d, 0x2a); Muted = Color.FromArgb(0x7b, 0x80, 0x96); Line = Color.FromArgb(0xe6, 0xe8, 0xf1);
            Accent = Color.FromArgb(0x6c, 0x5c, 0xe7); AccentSoft = Color.FromArgb(0xed, 0xea, 0xff); Track = Color.FromArgb(0xe0, 0xe3, 0xef);
        }
    }
}

// Tray menu look: same palette and rounded shapes as the settings window.
sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public static readonly MenuRenderer Instance = new();

    MenuRenderer() : base(new ProfessionalColorTable()) { RoundedEdges = false; }

    // Style a menu (and later its sub menus) once
    public static void Apply(ToolStripDropDownMenu m)
    {
        m.Renderer = Instance;
        m.BackColor = Theme.Card;
        m.ForeColor = Theme.Text;
        m.Font = new Font("Segoe UI", 9.5f);
        m.ShowImageMargin = false;
        m.ShowCheckMargin = true;
        m.Padding = new Padding(4, 6, 4, 6);
        m.HandleCreated += (_, _) =>
        {
            int round = 2;   // DWMWCP_ROUND: rounded corners on Windows 11
            DwmSetWindowAttribute(m.Handle, 33, ref round, 4);
        };
        foreach (ToolStripItem item in m.Items)
        {
            item.Padding = new Padding(2, 6, 2, 6);
            if (item is ToolStripMenuItem mi && mi.HasDropDownItems && mi.DropDown is ToolStripDropDownMenu sub) Apply(sub);
        }
    }

    static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath(); int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Card);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Line);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }   // no separate margin strip

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new Rectangle(4, 1, e.Item.Width - 9, e.Item.Height - 3), 8);
        using var b = new SolidBrush(Theme.AccentSoft);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Muted : e.Item.Selected ? Theme.Accent : Theme.Text;
        // centred on the highlighted pill (which starts 1 px down and is 3 px shorter than the item), not on the padded item
        var r = e.TextRectangle;
        e.TextRectangle = new Rectangle(r.X, 1, r.Width, e.Item.Height - 3);
        e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom)) | TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item.Selected ? Theme.Accent : Theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        using var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - 4, cy), new PointF(cx - 1, cy + 3), new PointF(cx + 4, cy - 3) });
    }
}
