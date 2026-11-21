using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RiftLens.Services;

public sealed class ImageChangeDetector
{
    private const int SignatureWidth = 64;
    private const int SignatureHeight = 24;
    private byte[]? _previous;

    public bool HasMeaningfulChange(Bitmap bitmap, double threshold)
    {
        var current = CreateSignature(bitmap);
        if (_previous is null)
        {
            _previous = current;
            return true;
        }

        if (threshold <= 0)
        {
            _previous = current;
            return true;
        }

        var changed = 0;
        for (var index = 0; index < current.Length; index++)
        {
            if (Math.Abs(current[index] - _previous[index]) >= 18)
            {
                changed++;
            }
        }

        _previous = current;
        return changed / (double)current.Length >= Math.Clamp(threshold, 0.005, 0.5);
    }

    public void Reset() => _previous = null;

    private static byte[] CreateSignature(Bitmap source)
    {
        using var sampled = new Bitmap(SignatureWidth, SignatureHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(sampled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            graphics.DrawImage(source, new Rectangle(0, 0, sampled.Width, sampled.Height));
        }

        var bounds = new Rectangle(0, 0, sampled.Width, sampled.Height);
        var data = sampled.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * sampled.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var signature = new byte[SignatureWidth * SignatureHeight];
            for (var y = 0; y < SignatureHeight; y++)
            {
                for (var x = 0; x < SignatureWidth; x++)
                {
                    var sourceIndex = y * data.Stride + x * 4;
                    var blue = bytes[sourceIndex];
                    var green = bytes[sourceIndex + 1];
                    var red = bytes[sourceIndex + 2];
                    var luminance = (red * 54 + green * 183 + blue * 19) >> 8;
                    signature[y * SignatureWidth + x] = (byte)(luminance / 8 * 8);
                }
            }

            return signature;
        }
        finally
        {
            sampled.UnlockBits(data);
        }
    }
}
