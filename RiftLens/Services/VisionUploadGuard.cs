using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RiftLens.Services;

/// <summary>
/// Conservative, local-only preflight for a large opaque obstruction covering
/// the selected crop. It runs before PNG encoding or any Vision network call.
/// Ambiguous frames are allowed; only a dominant, contiguous flat surface is
/// blocked to avoid suppressing normal game backgrounds.
/// </summary>
public static class VisionUploadGuard
{
    private const int SampleWidth = 64;
    private const int SampleHeight = 32;
    private const int TileColumns = 16;
    private const int TileRows = 8;

    public static VisionUploadDecision Evaluate(Bitmap source)
    {
        if (source.Width < 16 || source.Height < 12)
        {
            return new VisionUploadDecision(true, "capture_too_small", 1, 1);
        }

        using var sampled = new Bitmap(SampleWidth, SampleHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(sampled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            graphics.DrawImage(source, new Rectangle(0, 0, sampled.Width, sampled.Height));
        }

        var pixels = ReadPixels(sampled);
        var bins = new Dictionary<int, int>();
        foreach (var pixel in pixels)
        {
            var bin = Quantize(pixel.Red, pixel.Green, pixel.Blue);
            bins.TryGetValue(bin, out var count);
            bins[bin] = count + 1;
        }
        var dominant = bins.MaxBy(pair => pair.Value);
        var dominantRatio = dominant.Value / (double)pixels.Length;

        var flatTiles = new bool[TileColumns * TileRows];
        var tileWidth = SampleWidth / TileColumns;
        var tileHeight = SampleHeight / TileRows;
        for (var tileY = 0; tileY < TileRows; tileY++)
        {
            for (var tileX = 0; tileX < TileColumns; tileX++)
            {
                var matching = 0;
                var total = 0;
                for (var y = tileY * tileHeight; y < (tileY + 1) * tileHeight; y++)
                {
                    for (var x = tileX * tileWidth; x < (tileX + 1) * tileWidth; x++)
                    {
                        total++;
                        var pixel = pixels[y * SampleWidth + x];
                        if (Quantize(pixel.Red, pixel.Green, pixel.Blue) == dominant.Key)
                        {
                            matching++;
                        }
                    }
                }
                flatTiles[tileY * TileColumns + tileX] = matching / (double)total >= 0.48;
            }
        }

        var largestComponent = LargestComponent(flatTiles);
        var componentRatio = largestComponent.Count / (double)flatTiles.Length;
        var wideEnough = largestComponent.Width >= Math.Ceiling(TileColumns * 0.58);
        var tallEnough = largestComponent.Height >= Math.Ceiling(TileRows * 0.50);
        var shouldBlock = dominantRatio >= 0.68
            && componentRatio >= 0.68
            && wideEnough
            && tallEnough;
        return new VisionUploadDecision(
            shouldBlock,
            shouldBlock ? "large_opaque_obstruction" : string.Empty,
            dominantRatio,
            componentRatio);
    }

    private static Pixel[] ReadPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var pixels = new Pixel[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = y * stride + x * 3;
                    pixels[y * bitmap.Width + x] = new Pixel(
                        bytes[offset + 2],
                        bytes[offset + 1],
                        bytes[offset]);
                }
            }
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Component LargestComponent(bool[] mask)
    {
        var visited = new bool[mask.Length];
        var queue = new int[mask.Length];
        var best = new Component(0, 0, 0);
        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start]) continue;
            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            var count = 0;
            var minX = TileColumns;
            var maxX = 0;
            var minY = TileRows;
            var maxY = 0;
            while (head < tail)
            {
                var index = queue[head++];
                var x = index % TileColumns;
                var y = index / TileColumns;
                count++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                Visit(index - 1, x > 0);
                Visit(index + 1, x + 1 < TileColumns);
                Visit(index - TileColumns, y > 0);
                Visit(index + TileColumns, y + 1 < TileRows);
            }

            var component = new Component(count, maxX - minX + 1, maxY - minY + 1);
            if (component.Count > best.Count) best = component;

            void Visit(int index, bool inBounds)
            {
                if (!inBounds || visited[index] || !mask[index]) return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }
        return best;
    }

    private static int Quantize(byte red, byte green, byte blue) =>
        (red >> 5) << 6 | (green >> 5) << 3 | blue >> 5;

    private readonly record struct Pixel(byte Red, byte Green, byte Blue);
    private readonly record struct Component(int Count, int Width, int Height);
}

public sealed record VisionUploadDecision(
    bool ShouldBlock,
    string Reason,
    double DominantColourRatio,
    double CoveredTileRatio);
