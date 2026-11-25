using CamPos.Core.Camera;
using CamPos.Core.Config;
using CamPos.Core.Monitor;

namespace CamPos.App;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly CameraMonitor _monitor = new();
    private OverlayForm? _overlay;
    private CameraFrame? _lastFrame;
    private CameraReadResult _latest;
    private readonly object _updateGate = new();
    private int _uiQueued;
    private DateTime _lastSuccessUtc = DateTime.MinValue;
    private bool _startedSource;
    private bool _closing;
    private string? _lastConnectionMessage;

    private Label _pill = null!;
    private Label _processLabel = null!;
    private Label _xValue = null!;
    private Label _yValue = null!;
    private Label _zValue = null!;
    private Label _absXValue = null!;
    private Label _absYValue = null!;
    private Label _absZValue = null!;
    private Label _yawValue = null!;
    private Label _pitchValue = null!;
    private Label _rollValue = null!;
    private Label _fovValue = null!;
    private Label _forwardValue = null!;
    private Label _charX = null!;
    private Label _charY = null!;
    private Label _charZ = null!;
    private Label _charAbsValue = null!;
    private Label _sourceLabel = null!;
    private Label _addressLabel = null!;
    private Label _ageLabel = null!;
    private Label _hintLabel = null!;
    private Label _statusBottom = null!;
    private RichTextBox _log = null!;
    private Button _overlayButton = null!;
    private Button _throughButton = null!;
    private readonly System.Windows.Forms.Timer _housekeeping = new() { Interval = 500 };

    public MainForm(bool startDemo = false, bool startInjected = false)
    {
        _config = AppConfig.Load();
        Text = "CamPos · 原神相机与角色监视器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        ClientSize = new Size(1000, 620);
        WindowState = FormWindowState.Maximized;
        BackColor = Ui.Background;
        ForeColor = Ui.Text;
        Font = Ui.Body;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildUi();
        CreateOverlay();
        _monitor.Updated += OnMonitorUpdated;
        _housekeeping.Tick += (_, _) => Housekeeping();
        _housekeeping.Start();

        Shown += (_, _) =>
        {
            if (startInjected)
            {
                AttachInjected();
            }
            else if (startDemo || _config.ShowDemoOnStartup)
            {
                StartDemo();
            }
            AppendLog("就绪。注入模式通过 CamPosHook.dll 读取相机和当前角色坐标。");
        };

        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        var top = new BufferedPanel { Dock = DockStyle.Top, Height = 64, BackColor = Ui.Background };
        top.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Border);
            e.Graphics.DrawLine(pen, 24, top.Height - 1, top.Width - 24, top.Height - 1);
        };
        Controls.Add(top);

        var brand = Ui.Label("CamPos", Ui.Title, Ui.Text);
        brand.Location = new Point(26, 8);
        top.Controls.Add(brand);

        var subtitle = Ui.Label("GENSHIN CAMERA & CHARACTER TELEMETRY  ·  injected", Ui.MonoSmall, Ui.Dim);
        subtitle.Location = new Point(28, 39);
        top.Controls.Add(subtitle);

        _pill = new Label
        {
            Text = "未连接",
            AutoSize = false,
            Size = new Size(96, 28),
            Location = new Point(312, 17),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Ui.Small,
            ForeColor = Ui.Muted,
            BackColor = Ui.PanelAlt
        };
        top.Controls.Add(_pill);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 530,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 20, 0),
            BackColor = Color.Transparent
        };
        top.Controls.Add(buttons);

        var inject = Ui.Button("注入模式", (_, _) => AttachInjected(), true);
        inject.Width = 112;
        inject.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(inject);

        var demo = Ui.Button("演示模式", (_, _) => StartDemo());
        demo.Width = 104;
        demo.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(demo);

        _overlayButton = Ui.Button("显示悬浮窗", (_, _) => ToggleOverlay());
        _overlayButton.Width = 120;
        _overlayButton.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(_overlayButton);

        var bottom = new BufferedPanel { Dock = DockStyle.Bottom, Height = 30, BackColor = Ui.Background };
        Controls.Add(bottom);
        bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Border);
            e.Graphics.DrawLine(pen, 24, 0, bottom.Width - 24, 0);
        };
        _statusBottom = Ui.Label("注入模式 / 进程内读取", Ui.Small, Ui.Dim);
        _statusBottom.Location = new Point(26, 6);
        bottom.Controls.Add(_statusBottom);

        _ageLabel = Ui.Label("", Ui.Small, Ui.Dim);
        _ageLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _ageLabel.Location = new Point(730, 6);
        _ageLabel.AutoSize = false;
        _ageLabel.Width = 240;
        _ageLabel.TextAlign = ContentAlignment.MiddleRight;
        bottom.Controls.Add(_ageLabel);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            Location = new Point(0, top.Height),
            Size = new Size(
                Math.Min(1480, Math.Max(900, (Screen.PrimaryScreen?.WorkingArea.Width ?? 1536) - 40)),
                Math.Min(750, Math.Max(560, (Screen.PrimaryScreen?.WorkingArea.Height ?? 864) - 90))),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Ui.Background,
            Padding = new Padding(24, 12, 24, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(body);
        body.BringToFront();

        var left = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Ui.Background, Padding = new Padding(0, 0, 18, 0) };
        body.Controls.Add(left, 0, 0);
        var right = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Ui.Background, Padding = new Padding(2, 0, 0, 0) };
        body.Controls.Add(right, 1, 0);

        BuildLeft(left);
        BuildRight(right);
    }

    private void BuildLeft(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Ui.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(layout);

        layout.Controls.Add(Ui.Label("相机位姿", Ui.Semibold, Ui.Text), 0, 0);

        var posRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Ui.Background };
        posRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        posRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        posRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.Controls.Add(posRow, 0, 1);

        (_xValue, _absXValue) = AddValueCard(posRow, "相机相对 X", "—", 0);
        (_yValue, _absYValue) = AddValueCard(posRow, "相机相对 Y", "—", 1);
        (_zValue, _absZValue) = AddValueCard(posRow, "相机相对 Z", "—", 2);

        layout.Controls.Add(Ui.Label("视角（Yaw / Pitch / Roll，单位度）", Ui.Small, Ui.Dim), 0, 2);

        var rotCard = Ui.Card();
        rotCard.Dock = DockStyle.Fill;
        rotCard.Margin = new Padding(0, 0, 0, 0);
        layout.Controls.Add(rotCard, 0, 3);

        _yawValue = AddInlineValue(rotCard, "YAW", 16);
        _pitchValue = AddInlineValue(rotCard, "PITCH", 190);
        _rollValue = AddInlineValue(rotCard, "ROLL", 364);

        _fovValue = AddFovCard(layout);

        AddCharacterCard(layout, 5);

        var info = Ui.Card();
        info.Dock = DockStyle.Fill;
        info.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(info, 0, 6);

        var infoTitle = Ui.Label("数据源", Ui.Small, Ui.Dim);
        infoTitle.Location = new Point(14, 9);
        info.Controls.Add(infoTitle);

        _sourceLabel = Ui.Label("未连接", Ui.Semibold, Ui.Text);
        _sourceLabel.Location = new Point(14, 26);
        _sourceLabel.MaximumSize = new Size(540, 40);
        info.Controls.Add(_sourceLabel);

        _addressLabel = Ui.Label("—", Ui.MonoSmall, Ui.Muted);
        _addressLabel.Location = new Point(14, 50);
        info.Controls.Add(_addressLabel);

        _hintLabel = Ui.Label("先运行 CamPos.Injector.exe 启动游戏；进入大世界后点击“注入模式”。", Ui.Small, Ui.Dim);
        _hintLabel.Location = new Point(14, 73);
        _hintLabel.MaximumSize = new Size(540, 48);
        info.Controls.Add(_hintLabel);

        var chain = Ui.Label("CamPosHook.dll → 共享内存 → 主界面 / 悬浮窗", Ui.MonoSmall, Ui.Dim);
        chain.Location = new Point(14, 112);
        info.Controls.Add(chain);
    }

    private (Label Value, Label Absolute) AddValueCard(TableLayoutPanel parent, string caption, string text, int column)
    {
        var card = Ui.Card();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(column == 0 ? 0 : 8, 0, 0, 0);
        parent.Controls.Add(card, column, 0);

        var cap = Ui.Label(caption, Ui.Small, Ui.Dim);
        cap.Location = new Point(14, 10);
        card.Controls.Add(cap);

        var value = Ui.Label(text, Ui.Number, Ui.Text);
        value.Location = new Point(12, 38);
        value.AutoSize = false;
        value.Size = new Size(168, 42);
        value.TextAlign = ContentAlignment.MiddleLeft;
        card.Controls.Add(value);

        var absolute = Ui.Label("ABS  —", Ui.MonoSmall, Ui.Dim);
        absolute.Location = new Point(14, 84);
        card.Controls.Add(absolute);
        return (value, absolute);
    }

    private Label AddInlineValue(Control parent, string caption, int x)
    {
        var cap = Ui.Label(caption, Ui.MonoSmall, Ui.Dim);
        cap.Location = new Point(x, 10);
        parent.Controls.Add(cap);
        var value = Ui.Label("—", Ui.NumberSmall, Ui.Text);
        value.Location = new Point(x, 31);
        parent.Controls.Add(value);
        return value;
    }

    private Label AddFovCard(TableLayoutPanel parent)
    {
        var card = Ui.Card();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 10, 0, 0);
        parent.Controls.Add(card, 0, 4);

        var cap = Ui.Label("视场角 FOV（垂直）", Ui.Small, Ui.Dim);
        cap.Location = new Point(14, 10);
        card.Controls.Add(cap);

        var value = Ui.Label("—", Ui.Number, Ui.Accent);
        value.Location = new Point(12, 34);
        card.Controls.Add(value);

        var fwdCap = Ui.Label("相机朝向单位向量", Ui.Small, Ui.Dim);
        fwdCap.Location = new Point(176, 12);
        card.Controls.Add(fwdCap);

        _forwardValue = Ui.Label("FWD  —", Ui.Mono, Ui.Muted);
        _forwardValue.Location = new Point(176, 37);
        card.Controls.Add(_forwardValue);

        return value;
    }

    private void AddCharacterCard(TableLayoutPanel parent, int row)
    {
        var card = Ui.Card();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 10, 0, 0);
        parent.Controls.Add(card, 0, row);

        var cap = Ui.Label("角色相对坐标（脚底）", Ui.Small, Ui.Dim);
        cap.Location = new Point(14, 9);
        card.Controls.Add(cap);

        _charX = AddCharacterValue(card, "CHAR X", 14);
        _charY = AddCharacterValue(card, "CHAR Y", 174);
        _charZ = AddCharacterValue(card, "CHAR Z", 334);

        _charAbsValue = Ui.Label("ABS  X —   Z —", Ui.MonoSmall, Ui.Dim);
        _charAbsValue.Location = new Point(14, 76);
        card.Controls.Add(_charAbsValue);
    }

    private Label AddCharacterValue(Control parent, string caption, int x)
    {
        var cap = Ui.Label(caption, Ui.MonoSmall, Ui.Dim);
        cap.Location = new Point(x, 31);
        parent.Controls.Add(cap);
        var value = Ui.Label("—", Ui.NumberSmall, Ui.Text);
        value.Location = new Point(x, 49);
        parent.Controls.Add(value);
        return value;
    }

    private void BuildRight(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Ui.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(layout);

        var connect = Ui.Card();
        connect.Dock = DockStyle.Fill;
        connect.Margin = new Padding(0);
        layout.Controls.Add(connect, 0, 0);

        var t = Ui.Label("注入", Ui.Semibold, Ui.Text);
        t.Location = new Point(14, 9);
        connect.Controls.Add(t);

        _processLabel = Ui.Label("尚未连接共享内存。", Ui.MonoSmall, Ui.Muted);
        _processLabel.Location = new Point(14, 36);
        _processLabel.AutoSize = false;
        _processLabel.Size = new Size(390, 58);
        connect.Controls.Add(_processLabel);

        var injectBtn = Ui.Button("注入模式", (_, _) => AttachInjected(), true);
        injectBtn.Location = new Point(14, 104);
        injectBtn.Width = 112;
        connect.Controls.Add(injectBtn);

        var demoBtn = Ui.Button("演示模式", (_, _) => StartDemo());
        demoBtn.Location = new Point(134, 104);
        demoBtn.Width = 104;
        connect.Controls.Add(demoBtn);

        var stopBtn = Ui.Button("停止读取", (_, _) => StopSource());
        stopBtn.Location = new Point(246, 104);
        stopBtn.Width = 100;
        connect.Controls.Add(stopBtn);

        var note = Ui.Label("提示：先运行 CamPos.Injector.exe；它会读取 CamPos.Injector.json 中的游戏目录。", Ui.Small, Ui.Dim);
        note.Location = new Point(14, 146);
        note.MaximumSize = new Size(340, 50);
        connect.Controls.Add(note);

        var actions = Ui.Card();
        actions.Dock = DockStyle.Fill;
        actions.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(actions, 0, 1);

        var s = Ui.Label("显示", Ui.Semibold, Ui.Text);
        s.Location = new Point(14, 9);
        actions.Controls.Add(s);

        _overlayButton = Ui.Button("显示悬浮窗", (_, _) => ToggleOverlay(), true);
        _overlayButton.Location = new Point(14, 36);
        _overlayButton.Width = 132;
        actions.Controls.Add(_overlayButton);

        _throughButton = Ui.Button("点击穿透：关", (_, _) => ToggleClickThrough());
        _throughButton.Location = new Point(156, 36);
        _throughButton.Width = 132;
        actions.Controls.Add(_throughButton);

        var clear = Ui.Button("清空日志", (_, _) => _log.Clear());
        clear.Location = new Point(14, 80);
        clear.Width = 100;
        actions.Controls.Add(clear);

        var overlayNote = Ui.Label("悬浮窗可拖动 · 右键菜单可切换穿透", Ui.Small, Ui.Dim);
        overlayNote.Location = new Point(14, 112);
        overlayNote.MaximumSize = new Size(360, 58);
        actions.Controls.Add(overlayNote);

        layout.Controls.Add(Ui.Label("日志", Ui.Semibold, Ui.Text), 0, 2);

        var logCard = Ui.Card();
        logCard.Dock = DockStyle.Fill;
        logCard.Margin = new Padding(0);
        logCard.Padding = new Padding(8);
        layout.Controls.Add(logCard, 0, 3);

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Ui.Panel,
            ForeColor = Ui.Muted,
            Font = Ui.MonoSmall,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = false
        };
        logCard.Controls.Add(_log);
    }

    private void CreateOverlay()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var defaultX = Math.Max(20, screen.Right - _config.Overlay.Width - 40);
        var x = _config.Overlay.X == 40 && _config.Overlay.Y == 40 ? defaultX : _config.Overlay.X;
        var y = _config.Overlay.X == 40 && _config.Overlay.Y == 40 ? 40 : _config.Overlay.Y;
        x = Math.Clamp(x, 20, Math.Max(20, screen.Right - 620));
        y = Math.Clamp(y, 20, Math.Max(20, screen.Bottom - 440));
        _overlay = new OverlayForm
        {
            Bounds = new Rectangle(x, y, Math.Max(460, _config.Overlay.Width), Math.Max(360, _config.Overlay.Height)),
            Opacity = Math.Clamp(_config.Overlay.Opacity, 0.2, 1.0)
        };
        _overlay.ShowMainRequested += () =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };
        _overlay.SetError("等待数据… 运行注入器并进入大世界。");
        _overlay.VisibleChanged += (_, _) =>
        {
            _config.Overlay.Visible = _overlay.Visible;
            UpdateOverlayButton();
        };
        if (_config.Overlay.ClickThrough)
            _overlay.ClickThrough = true;
        UpdateOverlayButton();
        _throughButton.Text = "点击穿透：" + (_overlay.ClickThrough ? "开" : "关");
    }

    private void AttachInjected()
    {
        ResetSourceDisplay();
        _monitor.SetSource(new InjectedCameraSource());
        _processLabel.Text = "正在查找共享内存…";
        _sourceLabel.Text = "注入 · 相机与角色坐标";
        _addressLabel.Text = "等待共享内存数据…";
        _hintLabel.Text = "等待连接时每 0.5 秒重试。";
        SetStatus("等待连接", Ui.Warn);
        ShowOverlayIfConfigured();
    }

    private void StartDemo()
    {
        _monitor.SetSource(new DemoCameraSource());
        ResetSourceDisplay();
        _sourceLabel.Text = "演示数据 · 不读取游戏";
        _addressLabel.Text = "这是 UI 演示，不是原神数据。";
        _hintLabel.Text = "演示模式用于检查相机、角色坐标、悬浮窗和字体布局。";
        _processLabel.Text = "演示数据源\n没有连接游戏。";
        SetStatus("演示", Ui.Accent);
        ShowOverlayIfConfigured();
        AppendLog("已进入演示模式。");
    }

    private void StopSource()
    {
        _monitor.SetSource(null);
        ResetSourceDisplay();
        SetStatus("已停止", Ui.Muted);
        _sourceLabel.Text = "已停止读取";
        _addressLabel.Text = "—";
        _processLabel.Text = "未连接共享内存。";
        _overlay?.SetError("已停止。");
        AppendLog("已停止读取。");
    }

    private void ShowOverlayIfConfigured()
    {
        if (_overlay is null) return;
        if (_config.Overlay.Visible)
            _overlay.Show();
        _startedSource = true;
        UpdateOverlayButton();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (!_startedSource && _lastFrame is null)
        {
            AppendLog("还没有数据源。先注入或启动演示模式。");
            return;
        }
        if (_overlay.Visible)
            _overlay.Hide();
        else
            _overlay.Show();
        _config.Overlay.Visible = _overlay.Visible;
        UpdateOverlayButton();
    }

    private void UpdateOverlayButton()
    {
        if (_overlayButton is null || _overlay is null) return;
        _overlayButton.Text = _overlay.Visible ? "隐藏悬浮窗" : "显示悬浮窗";
    }

    private void ToggleClickThrough()
    {
        if (_overlay is null) return;
        _overlay.ClickThrough = !_overlay.ClickThrough;
        _config.Overlay.ClickThrough = _overlay.ClickThrough;
        _throughButton.Text = "点击穿透：" + (_overlay.ClickThrough ? "开" : "关");
        AppendLog(_overlay.ClickThrough ? "悬浮窗已开启点击穿透（只能从主窗口关闭）。" : "悬浮窗已关闭点击穿透。");
    }

    private void OnMonitorUpdated(CameraReadResult result)
    {
        if (_closing) return;
        lock (_updateGate) _latest = result;
        if (Interlocked.Exchange(ref _uiQueued, 1) != 0) return;
        try
        {
            BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _uiQueued, 0);
                CameraReadResult latest;
                lock (_updateGate) latest = _latest;
                HandleReadResult(latest);
            });
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _uiQueued, 0);
        }
    }

    private void HandleReadResult(CameraReadResult result)
    {
        if (_closing || result.SourceGeneration != _monitor.SourceGeneration) return;

        if (_monitor.Source is InjectedCameraSource shared)
        {
            var name = shared.MappingName;
            var message = name is null ? result.Error : $"已连接 {name}。";
            if (message != _lastConnectionMessage)
            {
                _lastConnectionMessage = message;
                _processLabel.Text = message;
                AppendLog(message);
                if (name is null)
                {
                    _addressLabel.Text = message;
                    ClearReadout(message);
                }
            }
            if (name is null) return;
        }

        if (result.Success && result.Frame is not null)
        {
            var f = result.Frame;
            _lastFrame = f;
            _lastSuccessUtc = f.Timestamp.UtcDateTime;
            _xValue.Text = f.Position.X.ToString("0.00");
            _yValue.Text = f.Position.Y.ToString("0.00");
            _zValue.Text = f.Position.Z.ToString("0.00");
            _absXValue.Text = f.AbsolutePosition is { } apx ? $"ABS  {apx.X:0.00}" : "ABS  —";
            _absYValue.Text = f.AbsolutePosition is { } apy ? $"ABS  {apy.Y:0.00}" : "ABS  —";
            _absZValue.Text = f.AbsolutePosition is { } apz ? $"ABS  {apz.Z:0.00}" : "ABS  —";
            _yawValue.Text = f.EulerDegrees.Y.ToString("0.0") + "°";
            _pitchValue.Text = f.EulerDegrees.X.ToString("0.0") + "°";
            _rollValue.Text = f.EulerDegrees.Z.ToString("0.0") + "°";
            _fovValue.Text = f.HasFov ? f.FovDegrees.ToString("0.0") + "°" : "—";
            _forwardValue.Text = $"FWD  {f.Forward.X:0.00}  {f.Forward.Y:0.00}  {f.Forward.Z:0.00}";
            if (f.CharacterPosition is { } cp)
            {
                _charX.Text = cp.X.ToString("0.00");
                _charY.Text = cp.Y.ToString("0.00");
                _charZ.Text = cp.Z.ToString("0.00");
            }
            else
            {
                _charX.Text = _charY.Text = _charZ.Text = "—";
            }
            _charAbsValue.Text = f.CharacterAbsolutePosition is { } cap
                ? $"ABS  X {cap.X:0.00}   Z {cap.Z:0.00}"
                : "ABS  X —   Z —";
            _sourceLabel.Text = f.Source;
            _addressLabel.Text = f.PrimaryAddress == 0
                ? $"注入共享内存  ·  {f.Timestamp:HH:mm:ss.fff}"
                : $"相机结构 0x{f.PrimaryAddress:X}  ·  {f.Timestamp:HH:mm:ss.fff}";
            if (f.AbsoluteTiming.Length != 0) _addressLabel.Text += "  ·  " + f.AbsoluteTiming;
            _overlay?.SetFrame(f, stale: false);
            SetStatus(f.Source.StartsWith("演示", StringComparison.Ordinal) ? "演示" : "实时", Ui.Accent);
        }
        else
        {
            _addressLabel.Text = result.Error;
            SetStatus(_lastSuccessUtc == DateTime.MinValue ? "无数据" : "数据中断", Ui.Warn);
            ClearReadout(result.Error);
        }
    }

    private void ResetSourceDisplay()
    {
        _lastConnectionMessage = null;
        _startedSource = false;
        _lastSuccessUtc = DateTime.MinValue;
        _ageLabel.Text = string.Empty;
        ClearReadout("等待读数。");
    }

    private void ClearReadout(string message)
    {
        _lastFrame = null;
        _xValue.Text = _yValue.Text = _zValue.Text = "—";
        _absXValue.Text = _absYValue.Text = _absZValue.Text = "ABS  —";
        _charX.Text = _charY.Text = _charZ.Text = "—";
        _charAbsValue.Text = "ABS  X —   Z —";
        _yawValue.Text = _pitchValue.Text = _rollValue.Text = _fovValue.Text = "—";
        _forwardValue.Text = "FWD  —";
        _overlay?.SetError(message);
    }

    private void SetStatus(string text, Color color)
    {
        _pill.Text = text;
        _pill.ForeColor = color;
        _pill.BackColor = Color.FromArgb(
            Math.Min(255, color.R / 7 + 12),
            Math.Min(255, color.G / 7 + 17),
            Math.Min(255, color.B / 7 + 22));
    }

    private void Housekeeping()
    {
        if (_closing) return;
        if (_startedSource && _lastSuccessUtc != DateTime.MinValue)
        {
            var age = (DateTime.UtcNow - _lastSuccessUtc).TotalMilliseconds;
            _ageLabel.Text = age < 5000
                ? $"{_config.RefreshHz} Hz  ·  最近更新 {age:0} ms"
                : $"最后数据 {age / 1000:0.0} 秒前";
            _ageLabel.ForeColor = age > 2000 ? Ui.Warn : Ui.Dim;
            if (age > 1500 && _monitor.IsRunning)
                SetStatus("数据过期", Ui.Warn);
        }
        else
        {
            _ageLabel.Text = "";
        }
    }

    private void AppendLog(string text)
    {
        if (_log is null || _log.IsDisposed) return;
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            if (_log.TextLength > 150_000)
                _log.Clear();
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = Ui.Muted;
            _log.AppendText(line);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        catch
        {
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _closing = true;
        _housekeeping.Stop();
        _monitor.Updated -= OnMonitorUpdated;
        _monitor.Dispose();
        if (_overlay is not null)
        {
            _config.Overlay.X = _overlay.Left;
            _config.Overlay.Y = _overlay.Top;
            _config.Overlay.Width = _overlay.Width;
            _config.Overlay.Height = _overlay.Height;
            _config.Overlay.Opacity = _overlay.Opacity;
            _config.Overlay.ClickThrough = _overlay.ClickThrough;
            _overlay.Dispose();
            _overlay = null;
        }
        _config.Save();
    }
}
