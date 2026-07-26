using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RiftLens.Models;

namespace RiftLens.Services;

internal sealed class PaddleOcrEngine : IDisposable
{
    private const float DetectionPixelThreshold = 0.30f;
    private const float DetectionBoxThreshold = 0.60f;
    private const int DetectionLongSidePerScaleStep = 320;
    private const int RecognitionHeight = 48;
    private const int RecognitionMaxWidth = 1600;

    private readonly InferenceSession _detector;
    private readonly InferenceSession _koreanRecognizer;
    private readonly InferenceSession _chineseRecognizer;
    private readonly string _detectorInput;
    private readonly string _koreanInput;
    private readonly string _chineseInput;
    private readonly string[] _koreanDictionary;
    private readonly string[] _chineseDictionary;
    private bool _disposed;

    public PaddleOcrEngine(string modelDirectory)
    {
        using var options = CreateSessionOptions();
        _detector = new InferenceSession(
            Path.Combine(modelDirectory, "ppocrv5_mobile_det.onnx"),
            options);
        _koreanRecognizer = new InferenceSession(
            Path.Combine(modelDirectory, "ppocrv5_mobile_rec_korean.onnx"),
            options);
        _chineseRecognizer = new InferenceSession(
            Path.Combine(modelDirectory, "ppocrv5_mobile_rec_chinese.onnx"),
            options);
        _detectorInput = _detector.InputMetadata.Keys.Single();
        _koreanInput = _koreanRecognizer.InputMetadata.Keys.Single();
        _chineseInput = _chineseRecognizer.InputMetadata.Keys.Single();
        _koreanDictionary = File.ReadAllLines(
            Path.Combine(modelDirectory, "ppocrv5_korean_dict.txt"));
        _chineseDictionary = File.ReadAllLines(
            Path.Combine(modelDirectory, "ppocrv5_dict.txt"));
    }

    public string Recognize(
        Bitmap bitmap,
        int ocrScale,
        CancellationToken cancellationToken) =>
        RecognizeDetailed(bitmap, ocrScale, cancellationToken).RawText;

    public OcrDocument RecognizeDetailed(
        Bitmap bitmap,
        int ocrScale,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = DetectLines(bitmap, ocrScale, cancellationToken);
        if (lines.Count == 0 && bitmap.Width >= bitmap.Height * 3)
        {
            lines.Add(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }
        if (lines.Count > 1 && TouchesTopEdge(lines[0], bitmap.Height))
        {
            lines.RemoveAt(0);
        }

        var recognized = new List<OcrRecognizedLine>(lines.Count);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var crop = bitmap.Clone(line, PixelFormat.Format24bppRgb);
            var candidate = RecognizeLine(crop, cancellationToken);
            if (!string.IsNullOrWhiteSpace(candidate.Text))
            {
                recognized.Add(new OcrRecognizedLine(
                    candidate.Text,
                    line,
                    candidate.Confidence,
                    OcrColorAnalyzer.Analyze(crop, candidate.Text)));
            }
        }
        return new OcrDocument(
            string.Join(Environment.NewLine, recognized.Select(line => line.Text)),
            recognized);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _detector.Dispose();
        _koreanRecognizer.Dispose();
        _chineseRecognizer.Dispose();
    }

    private OcrTextCandidate RecognizeLine(Bitmap line, CancellationToken cancellationToken)
    {
        var spans = SegmentWords(line);
        if (spans.Count >= 2 && spans.Count <= 18)
        {
            var selected = new List<OcrTextCandidate>(spans.Count);
            foreach (var span in spans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var segment = line.Clone(
                    new Rectangle(span.Start, 0, span.End - span.Start, line.Height),
                    PixelFormat.Format24bppRgb);
                var candidate = RecognizeWithBothModels(segment);
                if (candidate.Text.Length > 0 && candidate.Confidence >= 0.24f)
                {
                    selected.Add(candidate);
                }
            }

            var segmented = TrilingualOcrFusion.JoinSegments(selected);
            if (selected.Count >= 2 && HasReadableText(segmented))
            {
                var strongest = selected.MaxBy(candidate => candidate.Confidence)!;
                return new OcrTextCandidate(
                    segmented,
                    (float)selected.Average(candidate => candidate.Confidence),
                    strongest.Model);
            }
        }

        return RecognizeWithBothModels(line);
    }

    private OcrTextCandidate RecognizeWithBothModels(Bitmap bitmap)
    {
        var korean = Recognize(
            _koreanRecognizer,
            _koreanInput,
            _koreanDictionary,
            OcrRecognitionModel.KoreanEnglish,
            bitmap);
        var chinese = Recognize(
            _chineseRecognizer,
            _chineseInput,
            _chineseDictionary,
            OcrRecognitionModel.ChineseEnglish,
            bitmap);
        return TrilingualOcrFusion.Select(korean, chinese);
    }

    private static OcrTextCandidate Recognize(
        InferenceSession session,
        string inputName,
        string[] dictionary,
        OcrRecognitionModel model,
        Bitmap bitmap)
    {
        var ratio = bitmap.Width / (double)Math.Max(1, bitmap.Height);
        var width = Math.Clamp(
            RoundUp((int)Math.Ceiling(ratio * RecognitionHeight), 4),
            32,
            RecognitionMaxWidth);
        using var resized = Resize(bitmap, width, RecognitionHeight);
        var tensor = CreateRecognitionTensor(resized);
        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        });
        var output = results.First().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 3 || dimensions[0] < 1)
        {
            return new OcrTextCandidate(string.Empty, 0f, model);
        }

        var timeSteps = dimensions[1];
        var classes = dimensions[2];
        var values = output.ToArray();
        var text = new System.Text.StringBuilder();
        var confidence = 0f;
        var accepted = 0;
        var previous = -1;
        for (var time = 0; time < timeSteps; time++)
        {
            var offset = time * classes;
            var bestIndex = 0;
            var bestScore = values[offset];
            for (var index = 1; index < classes; index++)
            {
                var score = values[offset + index];
                if (score > bestScore)
                {
                    bestIndex = index;
                    bestScore = score;
                }
            }

            if (bestIndex != 0 && bestIndex != previous && bestIndex - 1 < dictionary.Length)
            {
                text.Append(dictionary[bestIndex - 1]);
                confidence += bestScore;
                accepted++;
            }
            previous = bestIndex;
        }

        return new OcrTextCandidate(
            text.ToString().Normalize(System.Text.NormalizationForm.FormC),
            accepted == 0 ? 0f : confidence / accepted,
            model);
    }

    private List<Rectangle> DetectLines(
        Bitmap source,
        int ocrScale,
        CancellationToken cancellationToken)
    {
        var detectionLongSide = DetectionLongSidePerScaleStep * Math.Clamp(ocrScale, 3, 4);
        var scale = detectionLongSide / (double)Math.Max(source.Width, source.Height);
        var inputWidth = Math.Max(32, RoundToNearest(source.Width * scale, 32));
        var inputHeight = Math.Max(32, RoundToNearest(source.Height * scale, 32));
        using var resized = Resize(source, inputWidth, inputHeight);
        var tensor = CreateDetectionTensor(resized);
        cancellationToken.ThrowIfCancellationRequested();
        using var results = _detector.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(_detectorInput, tensor)
        });
        var output = results.First().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 4 || dimensions[0] < 1 || dimensions[1] < 1)
        {
            return new List<Rectangle>();
        }

        var outputHeight = dimensions[2];
        var outputWidth = dimensions[3];
        var probabilities = output.ToArray();
        var components = FindComponents(probabilities, outputWidth, outputHeight);
        var ratioX = source.Width / (double)outputWidth;
        var ratioY = source.Height / (double)outputHeight;
        var boxes = new List<Rectangle>(components.Count);
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var componentWidth = component.Right - component.Left + 1;
            var componentHeight = component.Bottom - component.Top + 1;
            var expandX = Math.Max(3, (int)Math.Round(componentHeight * 0.55));
            var expandY = Math.Max(2, (int)Math.Round(componentHeight * 0.35));
            var left = Math.Clamp(
                (int)Math.Floor((component.Left - expandX) * ratioX),
                0,
                source.Width - 1);
            var top = Math.Clamp(
                (int)Math.Floor((component.Top - expandY) * ratioY),
                0,
                source.Height - 1);
            var right = Math.Clamp(
                (int)Math.Ceiling((component.Right + 1 + expandX) * ratioX),
                left + 1,
                source.Width);
            var bottom = Math.Clamp(
                (int)Math.Ceiling((component.Bottom + 1 + expandY) * ratioY),
                top + 1,
                source.Height);
            var box = Rectangle.FromLTRB(left, top, right, bottom);
            if (box.Width >= 4 && box.Height >= 4)
            {
                boxes.Add(box);
            }
        }
        return MergeLineBoxes(boxes);
    }

    private static List<DetectionComponent> FindComponents(
        float[] probabilities,
        int width,
        int height)
    {
        var mask = new byte[width * height];
        for (var index = 0; index < mask.Length; index++)
        {
            mask[index] = probabilities[index] >= DetectionPixelThreshold ? (byte)1 : (byte)0;
        }

        var queue = new int[mask.Length];
        var result = new List<DetectionComponent>();
        for (var start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0)
            {
                continue;
            }

            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            mask[start] = 0;
            var left = start % width;
            var right = left;
            var top = start / width;
            var bottom = top;
            var score = 0f;
            var count = 0;

            while (head < tail)
            {
                var index = queue[head++];
                var x = index % width;
                var y = index / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                score += probabilities[index];
                count++;

                TryQueue(index - 1, x > 0);
                TryQueue(index + 1, x + 1 < width);
                TryQueue(index - width, y > 0);
                TryQueue(index + width, y + 1 < height);
            }

            if (right - left + 1 >= 4
                && bottom - top + 1 >= 3
                && count >= 12
                && score / count >= DetectionBoxThreshold)
            {
                result.Add(new DetectionComponent(left, top, right, bottom));
            }

            void TryQueue(int index, bool inBounds)
            {
                if (!inBounds || mask[index] == 0)
                {
                    return;
                }
                mask[index] = 0;
                queue[tail++] = index;
            }
        }
        return result;
    }

    private static List<Rectangle> MergeLineBoxes(List<Rectangle> boxes)
    {
        var merged = new List<Rectangle>();
        foreach (var box in boxes.OrderBy(item => item.Top).ThenBy(item => item.Left))
        {
            var mergedIndex = -1;
            for (var index = 0; index < merged.Count; index++)
            {
                var candidate = merged[index];
                var overlap = Math.Min(candidate.Bottom, box.Bottom)
                    - Math.Max(candidate.Top, box.Top);
                var minimumHeight = Math.Min(candidate.Height, box.Height);
                var centerDistance = Math.Abs(
                    (candidate.Top + candidate.Bottom) / 2.0
                    - (box.Top + box.Bottom) / 2.0);
                var horizontalGap = Math.Max(
                    0,
                    Math.Max(candidate.Left, box.Left) - Math.Min(candidate.Right, box.Right));
                if ((overlap >= minimumHeight * 0.42 || centerDistance <= minimumHeight * 0.45)
                    && horizontalGap <= Math.Max(candidate.Height, box.Height) * 8)
                {
                    mergedIndex = index;
                    break;
                }
            }

            if (mergedIndex < 0)
            {
                merged.Add(box);
            }
            else
            {
                merged[mergedIndex] = Rectangle.Union(merged[mergedIndex], box);
            }
        }
        return merged.OrderBy(item => item.Top).ThenBy(item => item.Left).ToList();
    }

    private static List<(int Start, int End)> SegmentWords(Bitmap bitmap)
    {
        var (pixels, stride) = ReadBgr(bitmap);
        var borderSamples = new List<(byte B, byte G, byte R)>(
            (bitmap.Width + bitmap.Height) * 2);
        for (var x = 0; x < bitmap.Width; x++)
        {
            borderSamples.Add(ReadPixel(pixels, stride, x, 0));
            borderSamples.Add(ReadPixel(pixels, stride, x, bitmap.Height - 1));
        }
        for (var y = 0; y < bitmap.Height; y++)
        {
            borderSamples.Add(ReadPixel(pixels, stride, 0, y));
            borderSamples.Add(ReadPixel(pixels, stride, bitmap.Width - 1, y));
        }

        var background = MedianColor(borderSamples);
        var occupied = new bool[bitmap.Width];
        var minimumPixels = Math.Max(1, (int)Math.Round(bitmap.Height * 0.08));
        for (var x = 0; x < bitmap.Width; x++)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = ReadPixel(pixels, stride, x, y);
                var difference = Math.Max(
                    Math.Abs(pixel.B - background.B),
                    Math.Max(
                        Math.Abs(pixel.G - background.G),
                        Math.Abs(pixel.R - background.R)));
                if (difference >= 20 && ++count >= minimumPixels)
                {
                    occupied[x] = true;
                    break;
                }
            }
        }

        var runs = new List<(int Start, int End)>();
        int? start = null;
        for (var x = 0; x < occupied.Length; x++)
        {
            if (occupied[x] && start is null)
            {
                start = x;
            }
            else if (!occupied[x] && start is not null)
            {
                runs.Add((start.Value, x));
                start = null;
            }
        }
        if (start is not null)
        {
            runs.Add((start.Value, occupied.Length));
        }

        var grouped = new List<(int Start, int End)>();
        var wordGap = Math.Max(3, (int)Math.Round(bitmap.Height * 0.40));
        foreach (var run in runs)
        {
            if (grouped.Count > 0 && run.Start - grouped[^1].End <= wordGap)
            {
                grouped[^1] = (grouped[^1].Start, run.End);
            }
            else
            {
                grouped.Add(run);
            }
        }

        return grouped
            .Where(run => run.End - run.Start >= 2)
            .Select(run => (
                Math.Max(0, run.Start - 2),
                Math.Min(bitmap.Width, run.End + 2)))
            .Where(run => run.Item2 > run.Item1)
            .ToList();
    }

    private static DenseTensor<float> CreateDetectionTensor(Bitmap bitmap)
    {
        var (pixels, stride) = ReadBgr(bitmap);
        var plane = bitmap.Width * bitmap.Height;
        var values = new float[plane * 3];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = ReadPixel(pixels, stride, x, y);
                var index = y * bitmap.Width + x;
                values[index] = (pixel.B / 255f - 0.485f) / 0.229f;
                values[plane + index] = (pixel.G / 255f - 0.456f) / 0.224f;
                values[plane * 2 + index] = (pixel.R / 255f - 0.406f) / 0.225f;
            }
        }
        return new DenseTensor<float>(values, new[] { 1, 3, bitmap.Height, bitmap.Width });
    }

    private static DenseTensor<float> CreateRecognitionTensor(Bitmap bitmap)
    {
        var (pixels, stride) = ReadBgr(bitmap);
        var plane = bitmap.Width * bitmap.Height;
        var values = new float[plane * 3];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = ReadPixel(pixels, stride, x, y);
                var index = y * bitmap.Width + x;
                values[index] = pixel.B / 127.5f - 1f;
                values[plane + index] = pixel.G / 127.5f - 1f;
                values[plane * 2 + index] = pixel.R / 127.5f - 1f;
            }
        }
        return new DenseTensor<float>(values, new[] { 1, 3, bitmap.Height, bitmap.Width });
    }

    private static (byte[] Pixels, int Stride) ReadBgr(Bitmap bitmap)
    {
        using var converted = bitmap.PixelFormat == PixelFormat.Format24bppRgb
            ? null
            : bitmap.Clone(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                PixelFormat.Format24bppRgb);
        var target = converted ?? bitmap;
        var data = target.LockBits(
            new Rectangle(0, 0, target.Width, target.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * target.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return (pixels, stride);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    private static (byte B, byte G, byte R) ReadPixel(
        byte[] pixels,
        int stride,
        int x,
        int y)
    {
        var offset = y * stride + x * 3;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static (byte B, byte G, byte R) MedianColor(
        List<(byte B, byte G, byte R)> samples)
    {
        var blue = samples.Select(sample => sample.B).ToArray();
        var green = samples.Select(sample => sample.G).ToArray();
        var red = samples.Select(sample => sample.R).ToArray();
        Array.Sort(blue);
        Array.Sort(green);
        Array.Sort(red);
        var middle = samples.Count / 2;
        return (blue[middle], green[middle], red[middle]);
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel);
        return result;
    }

    private static SessionOptions CreateSessionOptions()
    {
        return new SessionOptions
        {
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };
    }

    private static bool HasReadableText(string text) =>
        text.Count(char.IsLetterOrDigit) >= 2;

    private static bool TouchesTopEdge(Rectangle bounds, int imageHeight)
    {
        var edgeTolerance = Math.Max(1, imageHeight / 80);
        return bounds.Top <= edgeTolerance;
    }

    private static int RoundToNearest(double value, int multiple) =>
        Math.Max(multiple, (int)Math.Round(value / multiple) * multiple);

    private static int RoundUp(int value, int multiple) =>
        Math.Max(multiple, ((value + multiple - 1) / multiple) * multiple);

    private sealed record DetectionComponent(int Left, int Top, int Right, int Bottom);
}
