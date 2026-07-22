using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RiftLens.Services;

public sealed class BitmapPreprocessor
{
    public Bitmap Prepare(Bitmap source, int scale)
    {
        // 1080p HUD 中的韩文字体经常只有 12-16px；正常聊天框至少放大 3 倍再交给 LSTM。
        scale = Math.Clamp(scale, 3, 4);
        // 用户误框整屏时限制中间图像体积，避免 4K 画面放大后占用数百 MB。
        const long maxPreparedPixels = 24_000_000;
        while (scale > 1 && (long)source.Width * source.Height * scale * scale > maxPreparedPixels)
        {
            scale--;
        }
        var width = Math.Max(1, source.Width * scale);
        var height = Math.Max(1, source.Height * scale);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        const float contrast = 1.22f;
        const float offset = (1f - contrast) / 2f;
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.299f * contrast, 0.299f * contrast, 0.299f * contrast, 0f, 0f },
            new[] { 0.587f * contrast, 0.587f * contrast, 0.587f * contrast, 0f, 0f },
            new[] { 0.114f * contrast, 0.114f * contrast, 0.114f * contrast, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { offset, offset, offset, 0f, 1f }
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return output;
    }
}
