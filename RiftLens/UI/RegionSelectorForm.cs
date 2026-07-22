using RiftLens.Models;

namespace RiftLens.UI;

public sealed class RegionSelectorForm : Form
{
    private Point? _start;
    private Point _current;

    public ScreenRegion? SelectedRegion { get; private set; }

    public RegionSelectorForm(string instruction, AppLanguage language = AppLanguage.Chinese)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.Black;
        Opacity = 0.42;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;

        var label = new Label
        {
            AutoSize = true,
            Text = instruction + (language == AppLanguage.English
                ? "\nDrag to select. Press Esc to cancel."
                : "\n拖动鼠标框选，Esc 取消"),
            Font = new Font(
                language == AppLanguage.English ? "Segoe UI Variable" : "Microsoft YaHei UI",
                14,
                FontStyle.Regular,
                GraphicsUnit.Pixel),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(38, 39, 42),
            Padding = new Padding(12, 9, 12, 9),
            Location = new Point(24, 24)
        };
        Controls.Add(label);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _start = e.Location;
        _current = e.Location;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_start is null)
        {
            return;
        }

        _current = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _start is null)
        {
            return;
        }

        var local = Normalize(_start.Value, e.Location);
        if (local.Width < 16 || local.Height < 12)
        {
            _start = null;
            Invalidate();
            return;
        }

        var screen = RectangleToScreen(local);
        SelectedRegion = ScreenRegion.FromRectangle(screen);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_start is null)
        {
            return;
        }

        var rectangle = Normalize(_start.Value, _current);
        using var fill = new SolidBrush(Color.FromArgb(65, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(231, 231, 225), 2);
        e.Graphics.FillRectangle(fill, rectangle);
        e.Graphics.DrawRectangle(border, rectangle);
        var text = $"{rectangle.Width} × {rectangle.Height}";
        TextRenderer.DrawText(
            e.Graphics,
            text,
            new Font("Segoe UI", 10),
            new Point(rectangle.Left + 5, Math.Max(5, rectangle.Top - 24)),
            Color.White);
    }

    private static Rectangle Normalize(Point first, Point second) => Rectangle.FromLTRB(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X),
        Math.Max(first.Y, second.Y));
}
