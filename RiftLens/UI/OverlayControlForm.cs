using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using RiftLens.Models;

namespace RiftLens.UI;

/// <summary>
/// 屏幕译文旁的轻量悬浮控制器。
/// 运行时仅绘制一组低帧率声波；悬停后声波冻结并向右展开音量滑条。
/// 窗口始终不激活，避免让游戏失焦。
/// </summary>
public sealed class OverlayControlForm : Form
{
    private const int CollapsedWidth = 48;
    private const int ExpandedWidth = 232;
    private const int ControlHeight = 48;
    private const int ContentCenterY = 23;
    private const int VolumeSpeakerOriginX = CollapsedWidth + 13;
    private const int SpeakerGlyphRightOffset = 23;
    private const int SpeakerToTrackGap = 8;
    private const int ExpandDurationMs = 170;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _volumeMenu;
    private readonly ToolStripMenuItem _volumeMuteItem;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ContextMenuStrip _menu;
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _collapseTimer = new() { Interval = 520 };
    private readonly System.Windows.Forms.Timer _waveTimer = new() { Interval = 80 };
    private readonly System.Windows.Forms.Timer _sizeTimer = new() { Interval = 15 };
    private readonly Stopwatch _sizeStopwatch = new();

    private bool _paused;
    private bool _hovered;
    private bool _draggingVolume;
    private bool _draggingWindow;
    private bool _manuallyPositioned;
    private Point _dragCursorOrigin;
    private Point _dragWindowOrigin;
    private int _animationStartWidth = CollapsedWidth;
    private int _targetWidth = CollapsedWidth;
    private int _volume = 65;
    private int _waveFrame;
    private Rectangle _anchor;
    private bool _hasAnchor;
    private byte _surfaceOpacity = 235;
    private AppLanguage _language = AppLanguage.Chinese;
    private FloatingPalette _palette = FloatingPalette.Create("porcelain", "#6f7f91");

    public event Action? TogglePauseRequested;
    public event Action<int>? VolumeChanged;
    public event Action? ShowMainWindowRequested;
    public event Action? StopRequested;
    public event Action<Point>? PositionChanged;

    public OverlayControlForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(CollapsedWidth, ControlHeight);
        MinimumSize = new Size(CollapsedWidth, ControlHeight);
        MaximumSize = new Size(ExpandedWidth, ControlHeight);
        BackColor = Color.Black;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AccessibleName = "RiftLens 翻译控制：正在运行，音量 65%";
        AccessibleDescription = "点击声波可暂停或继续翻译。悬停后可调音量并拖动悬浮球位置，右键可显示主窗口或停止翻译。";

        _menu = new ContextMenuStrip
        {
            Font = UiTheme.Font(9),
            BackColor = _palette.Paper,
            ForeColor = _palette.Ink,
            ShowImageMargin = false,
            Padding = new Padding(6)
        };
        _pauseItem = new ToolStripMenuItem("暂停翻译", null, (_, _) => TogglePauseRequested?.Invoke());
        _volumeMenu = new ToolStripMenuItem("播报音量 · 65%") { AccessibleName = "播报音量" };
        foreach (var volume in new[] { 25, 50, 65, 75, 100 })
        {
            var item = new ToolStripMenuItem($"{volume}%") { Tag = volume };
            item.Click += (_, _) => RequestVolume(volume);
            _volumeMenu.DropDownItems.Add(item);
        }
        _volumeMenu.DropDownItems.Add(new ToolStripSeparator());
        _volumeMuteItem = new ToolStripMenuItem("静音", null, (_, _) => RequestVolume(0));
        _volumeMenu.DropDownItems.Add(_volumeMuteItem);
        _showItem = new ToolStripMenuItem("显示主窗口", null, (_, _) => ShowMainWindowRequested?.Invoke());
        _stopItem = new ToolStripMenuItem("停止翻译", null, (_, _) => StopRequested?.Invoke());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_volumeMenu);
        _menu.Items.Add(_showItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_stopItem);
        ContextMenuStrip = _menu;

        MouseEnter += (_, _) =>
        {
            _collapseTimer.Stop();
            _hovered = true;
            SetExpanded(true);
            UpdateWaveAnimation();
            RefreshSurface();
        };
        MouseLeave += (_, _) =>
        {
            if (_draggingWindow)
            {
                return;
            }
            _hovered = false;
            _draggingVolume = false;
            _collapseTimer.Stop();
            _collapseTimer.Start();
            UpdateWaveAnimation();
            RefreshSurface();
        };
        MouseMove += (_, args) =>
        {
            if (_draggingWindow)
            {
                var cursor = Cursor.Position;
                Location = ClampToWorkingArea(new Point(
                    _dragWindowOrigin.X + cursor.X - _dragCursorOrigin.X,
                    _dragWindowOrigin.Y + cursor.Y - _dragCursorOrigin.Y));
                _manuallyPositioned = true;
                RefreshSurface();
                return;
            }

            if (_draggingVolume)
                SetVolumeFromX(args.X);

            Cursor = IsDragHandleBounds(args.Location)
                ? Cursors.SizeAll
                : IsPrimaryBounds(args.Location)
                ? Cursors.Hand
                : (IsSliderBounds(args.Location) ? Cursors.SizeWE : Cursors.Default);
        };
        MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            if (IsDragHandleBounds(args.Location))
            {
                _collapseTimer.Stop();
                _draggingWindow = true;
                _dragCursorOrigin = Cursor.Position;
                _dragWindowOrigin = Location;
                Capture = true;
                Cursor = Cursors.SizeAll;
                return;
            }
            if (IsSliderBounds(args.Location))
            {
                _draggingVolume = true;
                SetVolumeFromX(args.X);
                return;
            }

            if (IsPrimaryBounds(args.Location))
                TogglePauseRequested?.Invoke();
        };
        MouseUp += (_, _) =>
        {
            _draggingVolume = false;
            if (_draggingWindow)
            {
                _draggingWindow = false;
                Capture = false;
                PositionChanged?.Invoke(Location);
            }
        };
        MouseWheel += (_, args) => RequestVolume(_volume + (args.Delta > 0 ? 5 : -5));
        _collapseTimer.Tick += (_, _) =>
        {
            if (ContextMenuStrip?.Visible == true || Bounds.Contains(Cursor.Position)) return;
            _collapseTimer.Stop();
            SetExpanded(false);
        };
        _waveTimer.Tick += (_, _) =>
        {
            _waveFrame = (_waveFrame + 1) % 120;
            RefreshSurface();
        };
        _sizeTimer.Tick += (_, _) => AnimateWidth();
        VisibleChanged += (_, _) => UpdateWaveAnimation();

        UpdateToolTip();
        UpdateRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExLayered;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = (IntPtr)MaNoActivate;
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        DrawSurface(e.Graphics);
    }

    private void DrawSurface(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        DrawContainer(graphics);
        DrawPrimaryControl(graphics);

        if (Width > CollapsedWidth + 2)
        {
            DrawVolumeControl(graphics);
            DrawDragHandle(graphics);
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _pauseItem.Text = paused ? L("继续翻译", "Resume translation") : L("暂停翻译", "Pause translation");
        UpdateAccessibleText();
        UpdateToolTip();
        UpdateWaveAnimation();
        RefreshSurface();
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        _volumeMenu.Text = $"{L("播报音量", "Speech volume")} · {_volume}%";
        foreach (ToolStripItem item in _volumeMenu.DropDownItems)
        {
            if (item is ToolStripMenuItem preset && preset.Tag is int presetVolume)
                preset.Checked = presetVolume == _volume;
        }
        UpdateAccessibleText();
        UpdateToolTip();
        RefreshSurface();
    }

    public void SetLanguage(AppLanguage language)
    {
        _language = language;
        _pauseItem.Text = _paused ? L("继续翻译", "Resume translation") : L("暂停翻译", "Pause translation");
        _volumeMenu.Text = $"{L("播报音量", "Speech volume")} · {_volume}%";
        _volumeMenu.AccessibleName = L("播报音量", "Speech volume");
        _volumeMuteItem.Text = L("静音", "Mute");
        _showItem.Text = L("显示主窗口", "Show main window");
        _stopItem.Text = L("停止翻译", "Stop translation");
        UpdateAccessibleText();
        UpdateToolTip();
    }

    public void SetTheme(string? interfacePalette, string? customColor)
    {
        _palette = FloatingPalette.Create(interfacePalette, customColor);
        _menu.BackColor = _palette.Paper;
        _menu.ForeColor = _palette.Ink;
        _volumeMenu.DropDown.BackColor = _palette.Paper;
        _volumeMenu.DropDown.ForeColor = _palette.Ink;
        RefreshSurface();
    }

    public void ShowNear(Rectangle anchor)
    {
        _anchor = anchor;
        _hasAnchor = true;
        Reposition();
        if (!Visible)
            Show();
        RenderLayeredSurface();
        UpdateWaveAnimation();
    }

    public void SetPreferredPosition(int? x, int? y)
    {
        if (x is null || y is null)
        {
            _manuallyPositioned = false;
            return;
        }

        var preferred = new Point(x.Value, y.Value);
        if (Screen.AllScreens.Any(screen =>
                Rectangle.Inflate(screen.WorkingArea, 24, 24).Contains(preferred)))
        {
            Location = preferred;
            _manuallyPositioned = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _collapseTimer.Dispose();
            _waveTimer.Dispose();
            _sizeTimer.Dispose();
            _toolTip.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawContainer(Graphics graphics)
    {
        var bounds = new Rectangle(3, 2, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
        using var shadowPath = UiTheme.RoundedPath(
            new Rectangle(bounds.X, bounds.Y + 2, bounds.Width, bounds.Height),
            UiTheme.RadiusXl + 2);
        using var shadow = new SolidBrush(Color.FromArgb(32, _palette.Ink));
        graphics.FillPath(shadow, shadowPath);

        using var outerPath = UiTheme.RoundedPath(bounds, UiTheme.RadiusXl + 2);
        using var fill = new SolidBrush(Color.FromArgb(
            _hovered ? 254 : 250,
            _hovered ? Mix(_palette.Paper, _palette.Accent, 0.035d) : _palette.Paper));
        using var border = new Pen(Color.FromArgb(142, _palette.Border), 1f);
        graphics.FillPath(fill, outerPath);
        graphics.DrawPath(border, outerPath);

        using var highlightPath = UiTheme.RoundedPath(
            new Rectangle(5, 3, Math.Max(1, Width - 11), Math.Max(1, Height - 9)),
            UiTheme.RadiusXl + 1);
        using var highlight = new Pen(Color.FromArgb(150, Color.White), 1f);
        graphics.DrawPath(highlight, highlightPath);
    }

    private void DrawPrimaryControl(Graphics graphics)
    {
        var primaryBounds = new Rectangle(4, 4, CollapsedWidth - 8, ControlHeight - 9);
        var orbBounds = new Rectangle(
            primaryBounds.Left + (primaryBounds.Width - 32) / 2,
            primaryBounds.Top + (primaryBounds.Height - 32) / 2,
            32,
            32);
        using var orbPath = UiTheme.RoundedPath(orbBounds, 16);
        using var orbFill = new SolidBrush(_hovered
            ? Mix(_palette.AccentSoft, _palette.Paper, 0.18d)
            : _palette.AccentSoft);
        using var orbBorder = new Pen(Color.FromArgb(_hovered ? 190 : 135, _palette.Accent), 1f);
        graphics.FillPath(orbFill, orbPath);
        graphics.DrawPath(orbBorder, orbPath);

        if (_paused)
        {
            DrawPlayGlyph(graphics, orbBounds);
            return;
        }

        DrawSoundWave(graphics, orbBounds);
    }

    private void DrawSoundWave(Graphics graphics, Rectangle bounds)
    {
        var centerY = bounds.Top + bounds.Height / 2f;
        var phase = _hovered ? 0f : _waveFrame * 0.42f;
        var baseHeights = new[] { 8f, 18f, 13f, 7f };
        var xOffsets = new[] { -8.5f, -3f, 3f, 8.5f };

        for (var index = 0; index < baseHeights.Length; index++)
        {
            var modulation = _hovered
                ? 1f
                : 0.72f + 0.28f * MathF.Sin(phase + index * 0.92f);
            var height = Math.Max(5f, baseHeights[index] * modulation);
            var alpha = 170 + index * 20;
            using var pen = new Pen(Color.FromArgb(Math.Min(255, alpha), _palette.Accent), 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var x = bounds.Left + bounds.Width / 2f + xOffsets[index];
            graphics.DrawLine(pen, x, centerY - height / 2f, x, centerY + height / 2f);
        }
    }

    private void DrawPlayGlyph(Graphics graphics, Rectangle bounds)
    {
        var centerX = bounds.Left + bounds.Width / 2f;
        var centerY = bounds.Top + bounds.Height / 2f;
        var triangle = new[]
        {
            new PointF(centerX - 4, centerY - 6),
            new PointF(centerX - 4, centerY + 6),
            new PointF(centerX + 6, centerY)
        };
        using var soft = new SolidBrush(Color.FromArgb(42, _palette.Accent));
        graphics.FillEllipse(soft, centerX - 12, centerY - 12, 24, 24);
        using var brush = new SolidBrush(_palette.AccentStrong);
        graphics.FillPolygon(brush, triangle);
    }

    private void DrawVolumeControl(Graphics graphics)
    {
        var expansion = Math.Clamp((Width - CollapsedWidth) / (float)(ExpandedWidth - CollapsedWidth), 0f, 1f);
        if (expansion <= 0.02f) return;

        var alpha = (int)(expansion * 255);
        var dividerX = CollapsedWidth + 7;
        using (var divider = new Pen(Color.FromArgb((int)(alpha * 0.5f), _palette.Border), 1f))
            graphics.DrawLine(divider, dividerX, ContentCenterY - 14, dividerX, ContentCenterY + 14);

        DrawSpeakerGlyph(graphics, new Point(VolumeSpeakerOriginX, ContentCenterY - 9), _volume == 0, alpha);

        var track = SliderTrackBounds;
        using (var trackPen = new Pen(Color.FromArgb((int)(alpha * 0.78f), _palette.SurfaceStrong), 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(trackPen, track.Left, track.Top + track.Height / 2f, track.Right, track.Top + track.Height / 2f);
        }

        var filledWidth = Math.Max(0f, track.Width * _volume / 100f);
        using (var valuePen = new Pen(Color.FromArgb(alpha, _palette.Accent), 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(valuePen, track.Left, track.Top + track.Height / 2f,
                track.Left + filledWidth, track.Top + track.Height / 2f);
        }

        var knobX = track.Left + filledWidth;
        using (var knobShadow = new SolidBrush(Color.FromArgb((int)(alpha * 0.13f), _palette.Ink)))
            graphics.FillEllipse(knobShadow, knobX - 7, track.Top + track.Height / 2f - 6, 14, 14);
        using (var knob = new SolidBrush(Color.FromArgb(alpha, _palette.Paper)))
            graphics.FillEllipse(knob, knobX - 5, track.Top + track.Height / 2f - 5, 10, 10);
        using (var ring = new Pen(Color.FromArgb((int)(alpha * 0.52f), _palette.Accent), 1f))
            graphics.DrawEllipse(ring, knobX - 5, track.Top + track.Height / 2f - 5, 10, 10);

        var previousTextRendering = graphics.TextRenderingHint;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var textFont = UiTheme.Caption(9f, FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(alpha, _palette.Ink));
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(
            $"{_volume}%",
            textFont,
            textBrush,
            new RectangleF(CollapsedWidth + 124, ContentCenterY - 19, 42, 38),
            textFormat);
        graphics.TextRenderingHint = previousTextRendering;
    }

    private void DrawSpeakerGlyph(Graphics graphics, Point origin, bool muted, int alpha)
    {
        using var pen = new Pen(Color.FromArgb(alpha, _palette.InkMuted), 1.55f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var speaker = new[]
        {
            new Point(origin.X, origin.Y + 6),
            new Point(origin.X + 5, origin.Y + 6),
            new Point(origin.X + 10, origin.Y + 1),
            new Point(origin.X + 10, origin.Y + 17),
            new Point(origin.X + 5, origin.Y + 12),
            new Point(origin.X, origin.Y + 12)
        };
        graphics.DrawPolygon(pen, speaker);

        if (muted)
        {
            graphics.DrawLine(pen, origin.X + 14, origin.Y + 3, origin.X + 23, origin.Y + 15);
            graphics.DrawLine(pen, origin.X + 23, origin.Y + 3, origin.X + 14, origin.Y + 15);
            return;
        }

        graphics.DrawArc(pen, origin.X + 8, origin.Y + 3, 8, 12, -60, 120);
        graphics.DrawArc(pen, origin.X + 8, origin.Y, 15, 18, -55, 110);
    }

    private void DrawDragHandle(Graphics graphics)
    {
        var expansion = Math.Clamp((Width - CollapsedWidth) / (float)(ExpandedWidth - CollapsedWidth), 0f, 1f);
        if (expansion < 0.88f)
        {
            return;
        }

        var alpha = (int)Math.Round((expansion - 0.88f) / 0.12f * 255);
        var bounds = DragHandleBounds;
        using var backgroundPath = UiTheme.RoundedPath(bounds, 6);
        using var background = new SolidBrush(Color.FromArgb((int)(alpha * 0.88f), _palette.AccentSoft));
        graphics.FillPath(background, backgroundPath);
        using var outline = new Pen(Color.FromArgb((int)(alpha * 0.48f), _palette.Accent), 1f);
        graphics.DrawPath(outline, backgroundPath);
        using var dot = new SolidBrush(Color.FromArgb((int)(alpha * 0.78f), _palette.AccentStrong));
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                graphics.FillEllipse(
                    dot,
                    bounds.Left + 4 + column * 4,
                    bounds.Top + 3 + row * 3,
                    2.4f,
                    2.4f);
            }
        }
    }

    private Rectangle SliderTrackBounds => new(
        VolumeSpeakerOriginX + SpeakerGlyphRightOffset + SpeakerToTrackGap,
        ContentCenterY - 7,
        80,
        14);
    private Rectangle DragHandleBounds => new(10, ControlHeight - 17, 15, 13);

    private bool IsPrimaryBounds(Point point) => point.X < CollapsedWidth;

    private bool IsSliderBounds(Point point) =>
        Width > CollapsedWidth + 20 && Rectangle.Inflate(SliderTrackBounds, 4, 10).Contains(point);

    private bool IsDragHandleBounds(Point point) =>
        Width >= ExpandedWidth - 2 && Rectangle.Inflate(DragHandleBounds, 2, 2).Contains(point);

    private void SetVolumeFromX(int x)
    {
        var track = SliderTrackBounds;
        var ratio = Math.Clamp((x - track.Left) / (double)track.Width, 0d, 1d);
        RequestVolume((int)Math.Round(ratio * 100d));
    }

    private void RequestVolume(int volume)
    {
        var next = Math.Clamp(volume, 0, 100);
        if (next == _volume) return;
        SetVolume(next);
        VolumeChanged?.Invoke(next);
    }

    private void SetExpanded(bool expanded)
    {
        _targetWidth = expanded ? ExpandedWidth : CollapsedWidth;
        if (!SystemInformation.UIEffectsEnabled)
        {
            _sizeTimer.Stop();
            Size = new Size(_targetWidth, ControlHeight);
            UpdateOpacityForWidth();
            UpdateRegion();
            Reposition();
            RefreshSurface();
            return;
        }
        if (Width == _targetWidth)
        {
            UpdateOpacityForWidth();
            UpdateRegion();
            Reposition();
            return;
        }

        _animationStartWidth = Width;
        _sizeStopwatch.Restart();
        _sizeTimer.Start();
    }

    private void AnimateWidth()
    {
        var progress = Math.Clamp(_sizeStopwatch.Elapsed.TotalMilliseconds / ExpandDurationMs, 0d, 1d);
        // Smoothstep 让进入快、收起稳，避免线性动画显得机械。
        var eased = progress * progress * (3d - 2d * progress);
        var next = (int)Math.Round(_animationStartWidth + (_targetWidth - _animationStartWidth) * eased);
        if (next != Width)
        {
            Size = new Size(next, ControlHeight);
            UpdateOpacityForWidth();
            UpdateRegion();
            Reposition();
            RefreshSurface();
        }

        if (progress < 1d) return;
        _sizeTimer.Stop();
        Size = new Size(_targetWidth, ControlHeight);
        UpdateOpacityForWidth();
        UpdateRegion();
        Reposition();
        RefreshSurface();
    }

    private void Reposition()
    {
        if (!_hasAnchor) return;
        if (_manuallyPositioned)
        {
            Location = ClampToWorkingArea(Location);
            return;
        }

        var screen = Screen.FromRectangle(_anchor).WorkingArea;
        const int gap = 7;
        var x = _anchor.Right + gap;
        if (x + Width > screen.Right)
            x = _anchor.Left - Width - gap;
        x = Math.Clamp(x, screen.Left + 6, screen.Right - Width - 6);
        var y = Math.Clamp(
            _anchor.Top + Math.Max(0, (_anchor.Height - Height) / 2),
            screen.Top + 6,
            screen.Bottom - Height - 6);
        Location = new Point(x, y);
    }

    private Point ClampToWorkingArea(Point location)
    {
        var screen = Screen.FromPoint(new Point(
            location.X + Math.Max(1, Width / 2),
            location.Y + Math.Max(1, Height / 2))).WorkingArea;
        return new Point(
            Math.Clamp(location.X, screen.Left + 6, Math.Max(screen.Left + 6, screen.Right - Width - 6)),
            Math.Clamp(location.Y, screen.Top + 6, Math.Max(screen.Top + 6, screen.Bottom - Height - 6)));
    }

    private void UpdateWaveAnimation()
    {
        _waveTimer.Enabled = SystemInformation.UIEffectsEnabled && Visible && !_paused && !_hovered;
    }

    private void UpdateAccessibleText()
    {
        AccessibleName = _language == AppLanguage.English
            ? $"RiftLens translation control: {(_paused ? "paused" : "running")}, volume {_volume}%"
            : $"RiftLens 翻译控制：{(_paused ? "已暂停" : "正在运行")}，音量 {_volume}%";
        AccessibleDescription = L(
            "点击声波可暂停或继续翻译。悬停后可调音量并拖动悬浮球位置，右键可显示主窗口或停止翻译。",
            "Select the wave to pause or resume. Hover to adjust volume or drag the control; right-click to show the main window or stop translation.");
    }

    private void UpdateToolTip()
    {
        _toolTip.SetToolTip(this, _paused
            ? L($"已暂停 · 点击继续翻译 · 当前音量 {_volume}%", $"Paused · Select to resume · Volume {_volume}%")
            : L($"正在翻译 · 点击暂停 · 悬停展开音量 · 当前 {_volume}%", $"Translating · Select to pause · Hover for volume · {_volume}%"));
    }

    private string L(string chinese, string english) =>
        _language == AppLanguage.English ? english : chinese;

    private void UpdateRegion()
    {
        Region?.Dispose();
        Region = null;
    }

    private void UpdateOpacityForWidth()
    {
        var expansion = Math.Clamp((Width - CollapsedWidth) / (double)(ExpandedWidth - CollapsedWidth), 0d, 1d);
        _surfaceOpacity = (byte)Math.Round(235 + 11 * expansion);
    }

    private void RefreshSurface()
    {
        Invalidate();
        if (Visible && IsHandleCreated)
        {
            RenderLayeredSurface();
        }
    }

    private void RenderLayeredSurface()
    {
        if (Width <= 0 || Height <= 0 || !IsHandleCreated)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawSurface(graphics);
        }
        LayeredWindowSurface.Update(this, bitmap, _surfaceOpacity);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        var ratio = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * ratio),
            (int)Math.Round(from.G + (to.G - from.G) * ratio),
            (int)Math.Round(from.B + (to.B - from.B) * ratio));
    }

    private readonly record struct FloatingPalette(
        Color Paper,
        Color Surface,
        Color SurfaceStrong,
        Color Accent,
        Color AccentStrong,
        Color AccentSoft,
        Color Ink,
        Color InkMuted,
        Color Border)
    {
        public static FloatingPalette Create(string? name, string? customColor)
        {
            return name?.Trim().ToLowerInvariant() switch
            {
                "mono" => Build("#ffffff", "#e5e5e5", "#d2d2d2", "#171717", "#000000", "#e2e2e2", "#111111", "#555555"),
                "pearl" => Build("#ffffff", "#f0efec", "#e5e3df", "#7a7f8a", "#5f6570", "#e7e8eb", "#202126", "#62646a"),
                "coral" => Build("#fffaf7", "#eedfd9", "#e2ccc4", "#e56e61", "#c75449", "#f5dfda", "#302522", "#715f59"),
                "blue" => Build("#fbfcfc", "#dfe8ec", "#cedce2", "#3f6f86", "#315d72", "#d9e8ee", "#1f2b31", "#5e6b71"),
                "teal" => Build("#fbfdfc", "#dce8e4", "#cadbd6", "#3e817d", "#2e6966", "#d9ebe8", "#20312f", "#5d706c"),
                "sage" => Build("#fcfcf8", "#e3e5d9", "#d4d8c8", "#7b8068", "#626950", "#e1e4d8", "#2b2f26", "#666d5d"),
                "sand" => Build("#fffdf8", "#e8dfd0", "#d9cdbb", "#a7654d", "#8b4e38", "#eedfd7", "#332c24", "#74685a"),
                "custom" => BuildCustom(Parse(customColor, "#6f7f91")),
                _ => Build("#fbfaf9", "#e8e3e2", "#d9d2d1", "#e95a5f", "#c94850", "#f3dfe0", "#262426", "#676164")
            };
        }

        private static FloatingPalette Build(
            string paper,
            string surface,
            string surfaceStrong,
            string accent,
            string accentStrong,
            string accentSoft,
            string ink,
            string inkMuted)
        {
            var parsedPaper = Parse(paper, "#fbfaf9");
            var parsedSurface = Parse(surface, "#e8e3e2");
            var parsedSurfaceStrong = Parse(surfaceStrong, "#d9d2d1");
            var parsedAccent = Parse(accent, "#e95a5f");
            var parsedInk = Parse(ink, "#262426");
            return new FloatingPalette(
                parsedPaper,
                parsedSurface,
                parsedSurfaceStrong,
                parsedAccent,
                Parse(accentStrong, "#c94850"),
                Parse(accentSoft, "#f3dfe0"),
                parsedInk,
                Parse(inkMuted, "#676164"),
                Mix(parsedSurfaceStrong, parsedInk, 0.17d));
        }

        private static FloatingPalette BuildCustom(Color accent)
        {
            var white = Color.White;
            var black = Color.Black;
            return new FloatingPalette(
                Mix(accent, white, 0.97d),
                Mix(accent, white, 0.79d),
                Mix(accent, white, 0.66d),
                accent,
                Mix(accent, black, 0.20d),
                Mix(accent, white, 0.78d),
                Mix(accent, black, 0.70d),
                Mix(accent, Color.FromArgb(49, 49, 49), 0.55d),
                Mix(Mix(accent, white, 0.66d), Mix(accent, black, 0.70d), 0.17d));
        }

        private static Color Parse(string? value, string fallback)
        {
            try
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (normalized.Length == 7
                    && normalized[0] == '#'
                    && normalized.Skip(1).All(Uri.IsHexDigit))
                {
                    return ColorTranslator.FromHtml(normalized);
                }
            }
            catch
            {
            }
            return ColorTranslator.FromHtml(fallback);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

}
