using System.Drawing.Drawing2D;

namespace RiftLens.UI;

/// <summary>
/// Native overlay & floating control visual tokens and GDI+ path helpers.
/// </summary>
internal static class UiTheme
{
    public static readonly Color Accent = Color.FromArgb(29, 43, 51);
    public static readonly Color Mint = Color.FromArgb(23, 190, 133);

    public const int RadiusXl = 16;

    public static Font Caption(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

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
}
