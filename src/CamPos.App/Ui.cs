using System.Drawing.Drawing2D;

namespace CamPos.App;

internal static class Ui
{
    public static readonly Color Background = Color.FromArgb(10, 13, 19);
    public static readonly Color Panel = Color.FromArgb(16, 21, 30);
    public static readonly Color PanelAlt = Color.FromArgb(20, 27, 38);
    public static readonly Color Border = Color.FromArgb(37, 48, 62);
    public static readonly Color Text = Color.FromArgb(232, 239, 246);
    public static readonly Color Muted = Color.FromArgb(170, 183, 196);
    public static readonly Color Dim = Color.FromArgb(126, 141, 156);
    public static readonly Color Accent = Color.FromArgb(77, 225, 193);
    public static readonly Color AccentDark = Color.FromArgb(24, 92, 82);
    public static readonly Color Warn = Color.FromArgb(255, 184, 84);
    public static readonly Color Error = Color.FromArgb(255, 107, 107);
    public static readonly Color Blue = Color.FromArgb(109, 177, 255);

    public static readonly Font Body = MakeFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
    public static readonly Font Small = MakeFont("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
    public static readonly Font LabelFont = MakeFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
    public static readonly Font Semibold = MakeFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
    public static readonly Font Title = MakeFont("Microsoft YaHei UI", 18f, FontStyle.Bold);
    public static readonly Font Mono = MakeFont("Consolas", 9.5f, FontStyle.Regular);
    public static readonly Font MonoSmall = MakeFont("Consolas", 8.5f, FontStyle.Regular);
    public static readonly Font Number = MakeFont("Consolas", 22f, FontStyle.Bold);
    public static readonly Font NumberSmall = MakeFont("Consolas", 14f, FontStyle.Bold);

    private static Font MakeFont(string family, float size, FontStyle style)
    {
        try { return new Font(family, size, style, GraphicsUnit.Point); }
        catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
    }

    public static Label Label(string text, Font? font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = font ?? Body,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
    }

    public static Button Button(string text, EventHandler? click = null, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 34,
            Width = 110,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? AccentDark : PanelAlt,
            ForeColor = primary ? Accent : Color.White,
            Font = primary ? Semibold : Body,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TabStop = false
        };
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(48, 150, 129) : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(31, 116, 101) : Color.FromArgb(30, 39, 53);
        b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(24, 92, 82) : Color.FromArgb(23, 31, 43);
        if (click is not null) b.Click += click;
        return b;
    }

    public static BufferedPanel Card()
    {
        var p = new BufferedPanel
        {
            BackColor = Panel,
            Padding = new Padding(14)
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = Panel;
        box.ForeColor = Muted;
        box.BorderStyle = BorderStyle.None;
        box.Font = MonoSmall;
    }

    public static void StyleListView(ListView list)
    {
        list.BackColor = Panel;
        list.ForeColor = Text;
        list.BorderStyle = BorderStyle.None;
        list.Font = MonoSmall;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.GridLines = false;
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(PanelAlt), e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Semibold, e.Bounds, Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) =>
        {
            var bg = e.Item!.Selected ? Color.FromArgb(28, 57, 59) : (e.ItemIndex % 2 == 0 ? Panel : PanelAlt);
            e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);
        };
        list.DrawSubItem += (_, e) =>
        {
            var bg = e.Item!.Selected ? Color.FromArgb(28, 57, 59) : (e.ItemIndex % 2 == 0 ? Panel : PanelAlt);
            e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, MonoSmall, e.Bounds, Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
    }

    public static void DrawRoundedRect(Graphics g, Pen pen, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    public static void FillRoundedRect(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        DoubleBuffered = true;
    }
}
