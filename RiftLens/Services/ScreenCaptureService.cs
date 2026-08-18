using System.Drawing.Imaging;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class ScreenCaptureService
{
    public Bitmap Capture(ScreenRegion region)
    {
        if (!region.IsValid)
        {
            throw new InvalidOperationException("请先框选一个有效的屏幕识别区域。");
        }

        var requested = region.Resolve();
        var available = Rectangle.Intersect(requested, SystemInformation.VirtualScreen);
        if (available.Width < 16 || available.Height < 12)
        {
            throw new InvalidOperationException("识别区域不在当前可用显示器范围内。");
        }

        var bitmap = new Bitmap(available.Width, available.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(available.Location, Point.Empty, available.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
