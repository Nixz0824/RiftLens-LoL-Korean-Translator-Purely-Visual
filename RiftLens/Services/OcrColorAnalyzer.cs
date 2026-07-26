using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Extracts low-cost colour evidence from an already cropped OCR line.
/// LoL uses cyan for channel/player metadata, neutral light text for chat
/// content, and red/yellow accents for many system events. Colour is only
/// supporting evidence because themes, capture colour and accessibility
/// settings can change these values.
/// </summary>
internal static partial class OcrColorAnalyzer
{
    public static OcrColorEvidence Analyze(Bitmap source, string recognizedText)
    {
        using var converted = source.PixelFormat == PixelFormat.Format24bppRgb
            ? null
            : source.Clone(
                new Rectangle(0, 0, source.Width, source.Height),
                PixelFormat.Format24bppRgb);
        var bitmap = converted ?? source;
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            var splitX = EstimateContentStart(bitmap.Width, recognizedText);
            var step = bitmap.Width * bitmap.Height > 48_000 ? 2 : 1;
            var total = 0;
            var cyan = 0;
            var neutral = 0;
            var warning = 0;
            var prefixTotal = 0;
            var prefixCyan = 0;
            var contentTotal = 0;
            var contentNeutral = 0;

            for (var y = 0; y < bitmap.Height; y += step)
            {
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var offset = y * stride + x * 3;
                    var blue = pixels[offset];
                    var green = pixels[offset + 1];
                    var red = pixels[offset + 2];
                    var maximum = Math.Max(red, Math.Max(green, blue));
                    var minimum = Math.Min(red, Math.Min(green, blue));
                    var spread = maximum - minimum;
                    var luminance = (red * 3 + green * 6 + blue) / 10;

                    // Skip the dark translucent chat background and anti-aliasing haze.
                    if (maximum < 88 || luminance < 72)
                    {
                        continue;
                    }

                    var isCyan = blue >= 112
                        && green >= 102
                        && red + 16 <= Math.Min(blue, green);
                    var isNeutral = luminance >= 132 && spread <= 52;
                    var isWarning = red >= 128
                        && ((red >= green + 28 && red >= blue + 24)
                            || (green >= 112 && blue + 34 <= Math.Min(red, green)));

                    total++;
                    if (isCyan) cyan++;
                    if (isNeutral) neutral++;
                    if (isWarning) warning++;

                    if (x < splitX)
                    {
                        prefixTotal++;
                        if (isCyan) prefixCyan++;
                    }
                    else
                    {
                        contentTotal++;
                        if (isNeutral) contentNeutral++;
                    }
                }
            }

            return new OcrColorEvidence(
                total,
                Ratio(cyan, total),
                Ratio(neutral, total),
                Ratio(warning, total),
                Ratio(prefixCyan, prefixTotal),
                Ratio(contentNeutral, contentTotal));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int EstimateContentStart(int width, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return width / 2;
        }

        var marker = HeroColonRegex().Match(text);
        var index = marker.Success
            ? marker.Index + marker.Length
            : FindLikelyChatColon(text);
        var fraction = index > 0
            ? index / (double)Math.Max(1, text.Length)
            : 0.56;
        return (int)Math.Round(width * Math.Clamp(fraction, 0.24, 0.82));
    }

    private static int FindLikelyChatColon(string text)
    {
        var timestampEnd = text.StartsWith("[", StringComparison.Ordinal)
            ? text.IndexOf(']')
            : -1;
        for (var index = Math.Max(0, timestampEnd + 1); index < text.Length; index++)
        {
            if (text[index] is ':' or '：')
            {
                return index + 1;
            }
        }
        return -1;
    }

    private static float Ratio(int count, int total) =>
        total <= 0 ? 0f : count / (float)total;

    [GeneratedRegex(@"[\)\]）]\s*[:：]", RegexOptions.CultureInvariant)]
    private static partial Regex HeroColonRegex();
}
