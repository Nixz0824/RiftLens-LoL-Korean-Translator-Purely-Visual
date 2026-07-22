using System.Drawing.Drawing2D;

namespace RiftLens.UI;

internal enum SidebarGlyph
{
    Home,
    History,
    Voice,
    Settings
}

/// <summary>窄侧栏的无文字图标按钮，选中状态由一条薄荷绿指示条表达。</summary>
internal sealed class SidebarNavButton : Control
{
    private bool _selected;
    private bool _hovered;

    public SidebarGlyph Glyph { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    public SidebarNavButton(SidebarGlyph glyph, string accessibleName)
    {
        Glyph = glyph;
        AccessibleName = accessibleName;
        AccessibleDescription = accessibleName;
        Size = new Size(52, 52);
        Margin = new Padding(0, 6, 0, 6);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));

        var bounds = new Rectangle(4, 4, Width - 8, Height - 8);
        if (_selected || _hovered)
        {
            using var path = UiTheme.RoundedPath(bounds, 14);
            using var fill = new SolidBrush(_selected
                ? Color.FromArgb(28, UiTheme.Mint)
                : Color.FromArgb(13, UiTheme.Text));
            e.Graphics.FillPath(fill, path);
        }

        if (_selected)
        {
            using var indicator = new Pen(UiTheme.Mint, 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(indicator, 2, 17, 2, Height - 17);
        }

        var color = _selected ? UiTheme.MintDeep : UiTheme.Text;
        using var pen = new Pen(color, 1.9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        DrawGlyph(e.Graphics, pen, new PointF(Width / 2f, Height / 2f));

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), color, Color.Transparent);
    }

    private void DrawGlyph(Graphics graphics, Pen pen, PointF center)
    {
        switch (Glyph)
        {
            case SidebarGlyph.Home:
                graphics.DrawEllipse(pen, center.X - 12, center.Y - 12, 24, 24);
                graphics.DrawEllipse(pen, center.X - 4, center.Y - 4, 8, 8);
                break;
            case SidebarGlyph.History:
                using (var path = new GraphicsPath())
                {
                    path.AddArc(center.X - 12, center.Y - 10, 24, 20, 190, 160);
                    path.AddLine(center.X - 6, center.Y + 8, center.X - 9, center.Y + 12);
                    path.AddLine(center.X - 9, center.Y + 12, center.X - 8, center.Y + 7);
                    graphics.DrawPath(pen, path);
                }
                graphics.DrawLine(pen, center.X - 4, center.Y, center.X + 6, center.Y);
                graphics.DrawLine(pen, center.X - 4, center.Y + 5, center.X + 3, center.Y + 5);
                break;
            case SidebarGlyph.Voice:
                foreach (var (offset, height) in new[] { (-9f, 9f), (-3f, 19f), (3f, 13f), (9f, 7f) })
                    graphics.DrawLine(pen, center.X + offset, center.Y - height / 2f, center.X + offset, center.Y + height / 2f);
                break;
            case SidebarGlyph.Settings:
                var points = new PointF[6];
                for (var index = 0; index < points.Length; index++)
                {
                    var angle = -MathF.PI / 2f + index * MathF.PI / 3f;
                    points[index] = new PointF(center.X + MathF.Cos(angle) * 13, center.Y + MathF.Sin(angle) * 13);
                }
                graphics.DrawPolygon(pen, points);
                graphics.DrawEllipse(pen, center.X - 4, center.Y - 4, 8, 8);
                break;
        }
    }
}

internal sealed class BrandMarkControl : Control
{
    public BrandMarkControl()
    {
        Size = new Size(42, 42);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));
        var bounds = new Rectangle(5, 5, 32, 32);
        using var cyan = new SolidBrush(Color.FromArgb(224, 55, 205, 184));
        using var purple = new SolidBrush(Color.FromArgb(214, 126, 108, 238));
        using var blue = new SolidBrush(Color.FromArgb(210, 58, 156, 235));
        e.Graphics.FillPie(cyan, bounds, 205, 175);
        e.Graphics.FillPie(purple, bounds, 25, 178);
        e.Graphics.FillPie(blue, bounds, 112, 170);
        using var inner = new SolidBrush(Color.FromArgb(245, 250, 249, 248));
        e.Graphics.FillEllipse(inner, 13, 13, 16, 16);
    }
}

internal sealed class LensArtControl : Control
{
    public LensArtControl()
    {
        MinimumSize = new Size(160, 160);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));
        var size = Math.Min(Width - 20, Height - 20);
        var x = (Width - size) / 2;
        var y = (Height - size) / 2;
        var lens = new Rectangle(x, y, size, size);
        using var fill = new LinearGradientBrush(lens,
            Color.FromArgb(82, 86, 216, 192), Color.FromArgb(42, 255, 216, 173), 35f);
        using var rim = new Pen(Color.FromArgb(125, Color.White), 2f);
        using var accent = new Pen(Color.FromArgb(150, UiTheme.Mint), 1.2f);
        e.Graphics.FillEllipse(fill, lens);
        e.Graphics.DrawEllipse(rim, lens);
        for (var inset = 13; inset < size / 2; inset += 14)
        {
            var ring = Rectangle.Inflate(lens, -inset, -inset);
            if (ring.Width <= 2 || ring.Height <= 2) break;
            e.Graphics.DrawEllipse(accent, ring);
        }
        using var highlight = new Pen(Color.FromArgb(190, Color.White), 3f) { StartCap = LineCap.Round };
        e.Graphics.DrawArc(highlight, x + 11, y + 9, size - 22, size - 22, 202, 92);
        using var core = new SolidBrush(Color.FromArgb(155, Color.White));
        e.Graphics.FillEllipse(core, x + size / 2 - 4, y + size / 2 - 4, 8, 8);
    }
}

internal sealed class VoiceAvatarControl : Control
{
    public VoiceAvatarControl()
    {
        Size = new Size(46, 46);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));
        using var rim = new Pen(Color.FromArgb(110, UiTheme.Mint), 1.2f);
        using var fill = new LinearGradientBrush(new Rectangle(5, 5, 36, 36),
            Color.FromArgb(255, 238, 222, 207), Color.FromArgb(255, 198, 220, 212), 45f);
        e.Graphics.FillEllipse(fill, 5, 5, 36, 36);
        e.Graphics.DrawEllipse(rim, 5, 5, 36, 36);
        using var face = new SolidBrush(Color.FromArgb(255, 251, 232, 215));
        e.Graphics.FillEllipse(face, 15, 13, 16, 17);
        using var hair = new SolidBrush(Color.FromArgb(255, 78, 57, 51));
        e.Graphics.FillPie(hair, 12, 10, 22, 20, 180, 180);
        using var text = new SolidBrush(UiTheme.Text);
        using var font = UiTheme.Font(8.5f, FontStyle.Bold);
        e.Graphics.DrawString("晓", font, text, 17, 27);
    }
}

internal sealed class ThinVolumeSlider : Control
{
    private int _value = 65;
    private bool _dragging;

    public event Action<int>? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (_value == next) return;
            _value = next;
            Invalidate();
        }
    }

    public ThinVolumeSlider()
    {
        Size = new Size(150, 28);
        MinimumSize = new Size(104, 28);
        Cursor = Cursors.SizeWE;
        AccessibleName = "播报音量";
        AccessibleDescription = "向左或向右拖动可调整播报音量";
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            SetFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Down) SetValueAndRaise(_value - 5);
        if (e.KeyCode is Keys.Right or Keys.Up) SetValueAndRaise(_value + 5);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiTheme.DrawingBackColor(Parent));
        var y = Height / 2f;
        var left = 8f;
        var right = Width - 8f;
        using (var track = new Pen(Color.FromArgb(120, UiTheme.Border), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            e.Graphics.DrawLine(track, left, y, right, y);
        var knobX = left + (right - left) * _value / 100f;
        using (var value = new Pen(UiTheme.Mint, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            e.Graphics.DrawLine(value, left, y, knobX, y);
        using var knob = new SolidBrush(Color.White);
        using var ring = new Pen(Color.FromArgb(100, UiTheme.Border));
        e.Graphics.FillEllipse(knob, knobX - 6, y - 6, 12, 12);
        e.Graphics.DrawEllipse(ring, knobX - 6, y - 6, 12, 12);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 6, Height - 6), UiTheme.MintDeep, Color.Transparent);
    }

    private void SetFromX(int x)
    {
        var ratio = Math.Clamp((x - 8d) / Math.Max(1d, Width - 16d), 0d, 1d);
        SetValueAndRaise((int)Math.Round(ratio * 100));
    }

    private void SetValueAndRaise(int value)
    {
        var next = Math.Clamp(value, 0, 100);
        if (next == _value) return;
        _value = next;
        Invalidate();
        ValueChanged?.Invoke(next);
    }
}
