using System.Drawing.Drawing2D;

namespace RiftLens.UI;

/// <summary>
/// RiftLens 设计令牌 —— 冷灰蓝"液态玻璃"风格。
/// 参考 Apple Human Interface、Linear、Raycast 的设计系统原则。
/// </summary>
internal static class UiTheme
{
    // ── 表面层级（模拟 macOS 磨砂玻璃：浅冷灰白递进） ──
    public static readonly Color Canvas       = Color.FromArgb(240, 241, 246);  // 页面底色（≈ rgba(248,248,252,0.68) 落地效果）
    public static readonly Color Surface      = Color.FromArgb(248, 249, 252);  // 卡片表面
    public static readonly Color SurfaceHover = Color.FromArgb(244, 245, 250);  // 卡片悬停
    public static readonly Color Glass        = Color.FromArgb(244, 245, 250);  // 玻璃面板
    public static readonly Color GlassBright  = Color.FromArgb(251, 252, 254);  // 玻璃高亮
    public static readonly Color SurfaceDark  = Color.FromArgb(226, 229, 237);  // 深层表面（选中态、分隔）

    // ── 文字（macOS 规范：近黑 #1d1d1f / 次级 #86868b） ──
    public static readonly Color Text      = Color.FromArgb(29, 29, 31);   // 主文字
    public static readonly Color TextMuted = Color.FromArgb(134, 134, 139);// 次要文字

    // ── 边框与分隔线 ──
    public static readonly Color Border      = Color.FromArgb(210, 214, 222);  // 卡片边框
    public static readonly Color BorderLight = Color.FromArgb(228, 231, 238);  // 微妙的内部隔线

    // ── 深石墨主操作色：与薄荷绿状态色共同构成轻量、克制的玻璃主题 ──
    public static readonly Color Accent      = Color.FromArgb(29, 43, 51);     // 主按钮、链接、Tab 指示条
    public static readonly Color AccentHover = Color.FromArgb(43, 59, 66);     // 悬停
    public static readonly Color AccentDeep  = Color.FromArgb(20, 33, 40);     // 按下
    public static readonly Color AccentSoft  = Color.FromArgb(234, 242, 240);  // 强调色浅底

    // ── 语义色（macOS 规范：绿 #34c759 / 橙 #ff9f0a，低饱和底色） ──
    public static readonly Color Success     = Color.FromArgb(52, 199, 89);    // 成功/运行中
    public static readonly Color SuccessSoft = Color.FromArgb(41, 52, 199, 89); // rgba(52,199,89,0.16)
    public static readonly Color Warning     = Color.FromArgb(255, 159, 10);   // 警告/暂停
    public static readonly Color WarningSoft = Color.FromArgb(41, 255, 159, 10);// rgba(255,159,10,0.16)
    public static readonly Color Danger      = Color.FromArgb(192, 100, 104);  // 危险/错误
    public static readonly Color Mint        = Color.FromArgb(23, 190, 133);
    public static readonly Color MintDeep    = Color.FromArgb(18, 158, 110);
    public static readonly Color MintSoft    = Color.FromArgb(44, 23, 190, 133);
    public static readonly Color FloatBorder = Color.FromArgb(214, 220, 226, 224);

    // ── 暗色表面（悬浮层用，与主窗口同一色温体系） ──
    public static readonly Color DarkCanvas    = Color.FromArgb(28, 31, 36);    // 悬浮层底色
    public static readonly Color DarkSurface   = Color.FromArgb(36, 40, 46);    // 悬浮层卡片
    public static readonly Color DarkBorder    = Color.FromArgb(56, 61, 69);    // 暗色边框
    public static readonly Color DarkText      = Color.FromArgb(235, 237, 240); // 暗色主文字
    public static readonly Color DarkTextMuted = Color.FromArgb(160, 165, 172); // 暗色次要文字

    // ── 圆角 Token ──
    public const int RadiusSm  = 6;   // 紧凑元素：小按钮、输入框、标签
    public const int RadiusMd  = 8;   // 标准按钮
    public const int RadiusLg  = 12;  // 卡片
    public const int RadiusXl  = 16;  // 大卡片、悬浮控件
    public const int RadiusPill = 9999; // 胶囊按钮、徽章

    // ── 间距 Token（基于 8px 网格） ──
    public const int SpaceXs  = 4;
    public const int SpaceSm  = 8;
    public const int SpaceMd  = 12;
    public const int SpaceLg  = 16;
    public const int SpaceXl  = 24;
    public const int SpaceXxl = 32;

    // ── 字体层级 ──
    public static Font Display(float size, FontStyle style = FontStyle.Bold) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font Heading(float size) =>
        new("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Point);

    public static Font Body(float size = 10f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font Caption(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font Small(float size = 8f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font ButtonFont(float size = 9.5f) =>
        new("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Point);

    // 兼容旧调用
    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    // ── 圆角路径辅助 ──
    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// WinForms 的 Transparent 并不是真正的 alpha 合成；自绘控件需要向上找到
    /// 一个不透明父背景，避免在玻璃布局容器中被绘制成黑块。
    /// </summary>
    public static Color DrawingBackColor(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is CardPanel card)
                return card.FillColor;
            if (current.BackColor.A == 255)
                return current.BackColor;
        }
        return Canvas;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// CardPanel — 液态玻璃卡片（无边阴影，用 hairline border + 表面色差分层）
// ═══════════════════════════════════════════════════════════════════════
internal sealed class CardPanel : Panel
{
    public Color FillColor { get; set; } = UiTheme.Surface;
    public Color BorderColor { get; set; } = UiTheme.Border;
    public int CornerRadius { get; set; } = UiTheme.RadiusLg;
    public bool DrawBorder { get; set; } = true;

    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Padding = new Padding(20);
        Resize += (_, _) => UpdateRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var parentColor = UiTheme.DrawingBackColor(Parent);
        e.Graphics.Clear(parentColor);

        // 对称的卡片区域（左右上下边距一致）
        var cardBounds = new Rectangle(1, 1, Width - 2, Height - 2);

        using var path = UiTheme.RoundedPath(cardBounds, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);

        if (DrawBorder)
        {
            using var border = new Pen(Color.FromArgb(180, BorderColor));
            e.Graphics.DrawPath(border, path);
        }

        // 微妙的顶部高光线（Linear 风格的 white edge highlight）
        if (Height > 12)
        {
            using var highlightPath = UiTheme.RoundedPath(
                new Rectangle(cardBounds.X + 2, cardBounds.Y + 2,
                              cardBounds.Width - 4, cardBounds.Height - 4),
                CornerRadius - 1);
            using var highlight = new Pen(Color.FromArgb(140, 255, 255, 255));
            e.Graphics.DrawPath(highlight, highlightPath);
        }
    }

    private void UpdateRegion()
    {
        if (Width <= 1 || Height <= 1) return;
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// UiButton — 自绘按钮，统一 8px 圆角
// ═══════════════════════════════════════════════════════════════════════
internal enum UiButtonKind { Primary, Secondary, Quiet, Danger }

internal sealed class UiButton : Button
{
    private bool _hovered;
    private UiButtonKind _kind = UiButtonKind.Secondary;

    public UiButtonKind Kind
    {
        get => _kind;
        set { _kind = value; ApplyVisual(); }
    }

    public int CornerRadius { get; set; } = UiTheme.RadiusMd;
    public bool ShowTabIndicator { get; set; }

    public UiButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = UiTheme.ButtonFont();
        Height = 38;
        TabStop = true;
        UseVisualStyleBackColor = false;
        Resize += (_, _) => UpdateRegion();
        ApplyVisual();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; ApplyVisual(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; ApplyVisual(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Padding = new Padding(0, 1, 0, 0); ApplyVisual(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Padding = Padding.Empty; ApplyVisual(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { ApplyVisual(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var background = Parent?.BackColor ?? UiTheme.Canvas;
        if (background.A < 255) background = UiTheme.Surface;
        e.Graphics.Clear(background);

        var (fill, border, text) = Colors();
        var pressedOffset = MouseButtons == MouseButtons.Left
            && ClientRectangle.Contains(PointToClient(Cursor.Position)) ? 1 : 0;
        var bounds = new Rectangle(1, 1 + pressedOffset,
                                   Math.Max(1, Width - 3),
                                   Math.Max(1, Height - 3 - pressedOffset));

        using (var path = UiTheme.RoundedPath(bounds, CornerRadius))
        using (var brush = new SolidBrush(fill == Color.Transparent ? background : fill))
        {
            e.Graphics.FillPath(brush, path);

            if (border != Color.Transparent)
            {
                using var pen = new Pen(border);
                e.Graphics.DrawPath(pen, path);
            }

            // Secondary 按钮的顶部高光线
            if (Kind == UiButtonKind.Secondary && Enabled)
            {
                using var highlight = new Pen(Color.FromArgb(140, 255, 255, 255));
                e.Graphics.DrawArc(highlight,
                    bounds.X + 2, bounds.Y + 2,
                    Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 4),
                    190, 145);
            }
        }

        var textBounds = new Rectangle(5, 2 + pressedOffset,
                                       Math.Max(1, Width - 10),
                                       Math.Max(1, Height - 5));
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), text, fill);

        // macOS 风格 Tab 底部蓝色指示条
        if (ShowTabIndicator && Enabled)
        {
            var indicatorWidth = Math.Min(24, bounds.Width - 8);
            var indicatorX = bounds.X + (bounds.Width - indicatorWidth) / 2;
            var indicatorY = bounds.Bottom - 3;
            using var indicatorPen = new Pen(UiTheme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(indicatorPen, indicatorX, indicatorY, indicatorX + indicatorWidth, indicatorY);
        }
    }

    private void ApplyVisual()
    {
        var (fill, border, text) = Colors();
        BackColor = fill;
        ForeColor = text;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.BorderColor = border == Color.Transparent
            ? (fill == Color.Transparent ? UiTheme.Border : fill) : border;
        FlatAppearance.MouseOverBackColor = fill;
        FlatAppearance.MouseDownBackColor = fill;
        Invalidate();
    }

    private (Color Fill, Color Border, Color Text) Colors()
    {
        if (!Enabled)
            return (Color.FromArgb(210, 219, 229), Color.Transparent, Color.FromArgb(140, 155, 172));

        return Kind switch
        {
            UiButtonKind.Primary => (
                _hovered ? UiTheme.AccentHover : UiTheme.Accent,
                Color.Transparent,
                Color.White),

            UiButtonKind.Danger => (
                _hovered ? Color.FromArgb(168, 82, 86) : UiTheme.Danger,
                Color.Transparent,
                Color.White),

            UiButtonKind.Quiet => (
                _hovered ? UiTheme.SurfaceDark : Color.Transparent,
                Color.Transparent,
                _hovered ? UiTheme.Text : UiTheme.TextMuted),

            _ => ( // Secondary
                _hovered ? UiTheme.GlassBright : UiTheme.Glass,
                Color.FromArgb(170, UiTheme.Border),
                UiTheme.Text)
        };
    }

    private void UpdateRegion()
    {
        if (Width <= 1 || Height <= 1) return;
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// FlatWindowButton — 标准 Windows 风格窗口控制按钮
// ═══════════════════════════════════════════════════════════════════════
internal sealed class FlatWindowButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public bool IsCloseButton { get; set; }

    public FlatWindowButton(string text, string accessibleName)
    {
        Text = text;
        AccessibleName = accessibleName;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Width = 36;
        Height = 28;
        Margin = new Padding(0, 12, 0, 12);
        TabStop = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = UiTheme.Font(11, FontStyle.Regular);
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var parentColor = Parent?.BackColor ?? UiTheme.Canvas;
        e.Graphics.Clear(parentColor);

        var offset = _pressed ? 1 : 0;

        // 悬停背景
        if (_hovered)
        {
            var bgColor = IsCloseButton
                ? Color.FromArgb(232, 17, 35)  // 关闭按钮悬停红色
                : Color.FromArgb(10, 0, 0, 0); // 其他按钮浅灰底
            using var bgBrush = new SolidBrush(bgColor);
            e.Graphics.FillRectangle(bgBrush, 1, 1, Width - 2, Height - 2);
        }

        // 文字颜色
        var textColor = (_hovered && IsCloseButton)
            ? Color.White
            : (_hovered ? UiTheme.Text : UiTheme.TextMuted);

        var textBounds = new Rectangle(0, 1 + offset, Width, Height - 2);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// StatusBadge — 胶囊状态标签
// ═══════════════════════════════════════════════════════════════════════
internal sealed class StatusBadge : Label
{
    private bool _ready;

    public void SetStatus(string text, bool ready)
    {
        Text = text;
        _ready = ready;
        Invalidate();
    }

    public StatusBadge()
    {
        Height = 28;
        MinimumSize = new Size(80, 28);
        Font = UiTheme.Small(8.5f, FontStyle.Bold);
        TextAlign = ContentAlignment.MiddleCenter;
        AutoEllipsis = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));

        var textWidth = TextRenderer.MeasureText(e.Graphics, Text, Font).Width;
        var badgeWidth = Math.Max(72, Math.Min(Width, textWidth + 24));
        var bounds = new Rectangle(
            Math.Max(0, (Width - badgeWidth) / 2), 1,
            badgeWidth, Math.Max(1, Height - 2));

        // macOS 风格：软半透明底色 + 圆角气泡
        var bgColor = _ready ? UiTheme.SuccessSoft : UiTheme.WarningSoft;
        var textColor = _ready
            ? Color.FromArgb(33, 122, 52)   // Apple 绿字 #217a34
            : Color.FromArgb(176, 104, 0);  // Apple 橙字 #b06800

        using var path = UiTheme.RoundedPath(bounds, UiTheme.RadiusSm);
        using var fill = new SolidBrush(bgColor);
        e.Graphics.FillPath(fill, path);

        // 极细的低透明度内边框
        var borderColor = Color.FromArgb(35, _ready ? 52 : 255, _ready ? 199 : 159, _ready ? 89 : 10);
        using var borderPen = new Pen(borderColor);
        e.Graphics.DrawPath(borderPen, path);

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
