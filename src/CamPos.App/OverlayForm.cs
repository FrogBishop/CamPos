using System.Drawing.Drawing2D;
using CamPos.Core.Camera;

namespace CamPos.App;

internal sealed class OverlayForm : Form
{
    private CameraFrame? _frame;
    private string _error = "等待数据…";
    private bool _clickThrough;
    private bool _dragging;
    private Point _dragOffset;
    private bool _stale;

    public event Action? ShowMainRequested;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Ui.Background;
        Opacity = 0.98;
        Width = 460;
        Height = 360;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.SizeAll;
        BuildMenu();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            var ex = GetWindowLong(Handle, GwlExStyle);
            if (value)
                ex |= WsExTransparent | WsExLayered;
            else
                ex &= ~WsExTransparent;
            SetWindowLong(Handle, GwlExStyle, ex);
        }
    }

    public void SetFrame(CameraFrame frame, bool stale)
    {
        _frame = frame;
        _stale = stale;
        Invalidate();
    }

    public void SetError(string error)
    {
        _frame = null;
        _error = error;
        _stale = true;
        Invalidate();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Ui.PanelAlt,
            ForeColor = Ui.Text,
            Font = Ui.Body,
            ShowImageMargin = false
        };
        var show = new ToolStripMenuItem("显示主窗口");
        show.Click += (_, _) => ShowMainRequested?.Invoke();
        var click = new ToolStripMenuItem("点击穿透");
        click.Click += (_, _) => ClickThrough = !ClickThrough;
        var close = new ToolStripMenuItem("隐藏悬浮窗");
        close.Click += (_, _) => Hide();
        menu.Items.Add(show);
        menu.Items.Add(click);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(close);
        ContextMenuStrip = menu;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var w = Width;
        var h = Height;
        var rect = new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f);
        using (var bg = new LinearGradientBrush(rect, Color.FromArgb(13, 18, 27), Color.FromArgb(8, 11, 16), 90f))
            Ui.FillRoundedRect(g, bg, rect, 12);
        using (var pen = new Pen(Color.FromArgb(47, 61, 78)))
            Ui.DrawRoundedRect(g, pen, rect, 12);

        using (var accent = new Pen(Ui.Accent, 2f))
            g.DrawLine(accent, 18, 2, w - 18, 2);

        using var titleFont = new Font("Consolas", 10f, FontStyle.Bold);
        g.DrawString("CAMERA // GENSHIN", titleFont, new SolidBrush(Ui.Accent), 16, 12);

        var status = _stale ? "STALE" : "LIVE";
        var statusColor = _stale ? Ui.Warn : Ui.Accent;
        var statusSize = TextRenderer.MeasureText(status, Ui.MonoSmall);
        TextRenderer.DrawText(g, status, Ui.MonoSmall, new Point(w - statusSize.Width - 16, 14), statusColor);
        using (var dot = new SolidBrush(statusColor))
            g.FillEllipse(dot, w - statusSize.Width - 27, 18, 6, 6);

        if (_frame is null)
        {
            TextRenderer.DrawText(g, _error, Ui.Body, new Rectangle(16, 52, w - 32, 40), Ui.Muted,
                TextFormatFlags.WordBreak);
        }
        else
        {
            DrawPosition(g, _frame, w);
            DrawAbsolute(g, _frame, w);
            DrawAngles(g, _frame, w);
            DrawCharacter(g, _frame, w);
        }

        using var footerPen = new Pen(Color.FromArgb(47, 61, 78));
        g.DrawLine(footerPen, 16, h - 32, w - 16, h - 32);
        string footer = _frame?.AbsoluteTiming is { Length: > 0 } timing ? timing + "  ·  右键菜单" : "右键菜单 · 拖动移动";
        TextRenderer.DrawText(g, footer, Ui.Small,
            new Rectangle(16, h - 25, w - 32, 20), Ui.Muted, TextFormatFlags.EndEllipsis);
    }

    private int Y(int logical) => (int)MathF.Round(logical * (Height / 288f));

    private void DrawPosition(Graphics g, CameraFrame f, int w)
    {
        var labels = new[] { "X", "Y", "Z" };
        var values = new[] { f.Position.X, f.Position.Y, f.Position.Z };
        for (var i = 0; i < 3; i++)
        {
            var x = 18 + i * (w - 36) / 3;
            TextRenderer.DrawText(g, labels[i], Ui.MonoSmall, new Point(x, Y(43)), Ui.Muted);
            using var tick = new Pen(Color.FromArgb(52, 67, 84));
            g.DrawLine(tick, x, Y(36), x + 18, Y(36));
            TextRenderer.DrawText(g, values[i].ToString("0.00"), Ui.NumberSmall,
                new Point(x, Y(56)), Ui.Text);
        }
    }

    private void DrawAbsoluteLine(Graphics g, Vec3 p, int y)
    {
        TextRenderer.DrawText(g, "ABS", Ui.MonoSmall, new Point(18, y), Ui.Accent);
        TextRenderer.DrawText(g, $"X {p.X:0.00}   Y {p.Y:0.00}   Z {p.Z:0.00}", Ui.MonoSmall,
            new Point(54, y), Ui.Muted);
    }

    private void DrawAbsolute(Graphics g, CameraFrame f, int w)
    {
        if (f.AbsolutePosition is { } p)
            DrawAbsoluteLine(g, p, Y(79));
    }

    private void DrawAngles(Graphics g, CameraFrame f, int w)
    {
        var y = Y(104);
        var x1 = 18;
        var x2 = (int)(w * 0.37f);
        var x3 = (int)(w * 0.68f);
        TextRenderer.DrawText(g, "YAW", Ui.MonoSmall, new Point(x1, y), Ui.Muted);
        TextRenderer.DrawText(g, $"{f.EulerDegrees.Y:0.0}°", Ui.Mono, new Point(x1, Y(119)), Ui.Text);
        TextRenderer.DrawText(g, "PITCH", Ui.MonoSmall, new Point(x2, y), Ui.Muted);
        TextRenderer.DrawText(g, $"{f.EulerDegrees.X:0.0}°", Ui.Mono, new Point(x2, Y(119)), Ui.Text);
        TextRenderer.DrawText(g, "ROLL", Ui.MonoSmall, new Point(x3, y), Ui.Muted);
        TextRenderer.DrawText(g, $"{f.EulerDegrees.Z:0.0}°", Ui.Mono, new Point(x3, Y(119)), Ui.Text);

        using var line = new Pen(Color.FromArgb(47, 61, 78));
        g.DrawLine(line, 18, Y(150), w - 18, Y(150));
        TextRenderer.DrawText(g, "FOV", Ui.MonoSmall, new Point(18, Y(157)), Ui.Muted);
        TextRenderer.DrawText(g, f.HasFov ? $"{f.FovDegrees:0.0}°" : "—", Ui.Mono,
            new Point(54, Y(155)), f.HasFov ? Ui.Accent : Ui.Muted);
        var fwd = $"FWD  {f.Forward.X:0.00}  {f.Forward.Y:0.00}  {f.Forward.Z:0.00}";
        TextRenderer.DrawText(g, fwd, Ui.MonoSmall, new Point((int)(w * 0.38f), Y(158)), Ui.Muted);
    }

    private void DrawCharacter(Graphics g, CameraFrame f, int w)
    {
        using var line = new Pen(Color.FromArgb(47, 61, 78));
        g.DrawLine(line, 18, Y(182), w - 18, Y(182));
        TextRenderer.DrawText(g, "CHARACTER", Ui.MonoSmall, new Point(18, Y(188)), Ui.Muted);
        var p = f.CharacterPosition;
        if (p is null)
        {
            TextRenderer.DrawText(g, "—", Ui.Mono, new Point(18, Y(205)), Ui.Muted);
            return;
        }
        TextRenderer.DrawText(g, $"X {p.Value.X:0.00}", Ui.MonoSmall, new Point(18, Y(207)), Ui.Text);
        TextRenderer.DrawText(g, $"Y {p.Value.Y:0.00}", Ui.MonoSmall, new Point((int)(w * 0.35f), Y(207)), Ui.Text);
        TextRenderer.DrawText(g, $"Z {p.Value.Z:0.00}", Ui.MonoSmall, new Point((int)(w * 0.68f), Y(207)), Ui.Text);

        if (f.CharacterAbsolutePosition is { } ap)
            DrawAbsoluteLine(g, ap, Y(228));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOffset = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            Location = new Point(Left + e.X - _dragOffset.X, Top + e.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = new GraphicsPath();
        var r = 12;
        var d = r * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d, 0, d, d, 270, 90);
        path.AddArc(Width - d, Height - d, d, d, 0, 90);
        path.AddArc(0, Height - d, d, d, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
