using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RiftLens.Services;

/// <summary>
/// Color-preserving upscale for small Vision crops. Unlike local OCR, this
/// does not grayscale or stretch contrast — VLMs read original HUD color.
/// </summary>
public static class VisionFramePreparer
{
    public static Bitmap Prepare(Bitmap source)
    {
        var output = new Bitmap(
            Math.Max(1, source.Width),
            Math.Max(1, source.Height),
            PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.DrawImage(source, new Rectangle(0, 0, output.Width, output.Height));
        return output;
    }

    public static int ChooseScale(int width, int height)
    {
        _ = width;
        _ = height;
        return 1;
    }
}
