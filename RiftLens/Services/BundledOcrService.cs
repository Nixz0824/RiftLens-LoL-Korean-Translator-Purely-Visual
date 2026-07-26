using System.Reflection;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class BundledOcrService : IDisposable
{
    private const string ModelVersion = "ppocrv5-mobile-trilingual-v1";
    private static readonly (string FileName, string ResourceName)[] PaddleAssets =
    {
        ("ppocrv5_mobile_det.onnx", "RiftLens.Assets.ocr.ppocrv5_mobile_det.onnx"),
        ("ppocrv5_mobile_rec_korean.onnx", "RiftLens.Assets.ocr.ppocrv5_mobile_rec_korean.onnx"),
        ("ppocrv5_mobile_rec_chinese.onnx", "RiftLens.Assets.ocr.ppocrv5_mobile_rec_chinese.onnx"),
        ("ppocrv5_korean_dict.txt", "RiftLens.Assets.ocr.ppocrv5_korean_dict.txt"),
        ("ppocrv5_dict.txt", "RiftLens.Assets.ocr.ppocrv5_dict.txt")
    };

    private readonly string _storageRoot;
    private readonly string _paddleDirectory;
    private readonly Lazy<PaddleOcrEngine> _paddle;
    private readonly Lazy<TesseractFallbackOcrEngine> _fallback;
    private readonly BitmapPreprocessor _fallbackPreprocessor = new();
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private bool _disposed;

    public static string DisplayName => "PaddleOCRv5 韩中英（Tesseract 兜底）";

    public static bool IsBundled => PaddleAssets.All(item =>
            Assembly.GetExecutingAssembly().GetManifestResourceInfo(item.ResourceName) is not null)
        && TesseractFallbackOcrEngine.IsBundled;

    public BundledOcrService(string? storageRoot = null)
    {
        _storageRoot = storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftLens",
            "ocr",
            ModelVersion);
        _paddleDirectory = Path.Combine(_storageRoot, "paddle");
        _paddle = new Lazy<PaddleOcrEngine>(
            CreatePaddleEngine,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _fallback = new Lazy<TesseractFallbackOcrEngine>(
            () => new TesseractFallbackOcrEngine(Path.Combine(_storageRoot, "tesseract")),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> RecognizeAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken,
        int ocrScale = 3) =>
        (await RecognizeDetailedAsync(bitmap, cancellationToken, ocrScale)).RawText;

    public async Task<OcrDocument> RecognizeDetailedAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken,
        int ocrScale = 3)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _recognitionGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => RecognizeCore(bitmap, ocrScale, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    public void WarmUp()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _ = _paddle.Value;
        }
        catch
        {
            // A machine-specific ONNX Runtime failure must not disable the existing local OCR.
            _fallback.Value.WarmUp();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_paddle.IsValueCreated)
        {
            _paddle.Value.Dispose();
        }
        if (_fallback.IsValueCreated)
        {
            _fallback.Value.Dispose();
        }
        _recognitionGate.Dispose();
    }

    private OcrDocument RecognizeCore(
        Bitmap bitmap,
        int ocrScale,
        CancellationToken cancellationToken)
    {
        try
        {
            var paddleDocument = _paddle.Value.RecognizeDetailed(bitmap, ocrScale, cancellationToken);
            if (HasPlausibleText(paddleDocument.RawText))
            {
                return Normalize(paddleDocument);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Keep OCR local and usable if a model, native runtime, or unusual image fails.
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = _fallbackPreprocessor.Prepare(bitmap, ocrScale);
        return OcrDocument.FromText(Normalize(_fallback.Value.Recognize(prepared, cancellationToken)));
    }

    private PaddleOcrEngine CreatePaddleEngine()
    {
        ExtractPaddleAssets();
        return new PaddleOcrEngine(_paddleDirectory);
    }

    private void ExtractPaddleAssets()
    {
        Directory.CreateDirectory(_paddleDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var (fileName, resourceName) in PaddleAssets)
        {
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"内置 PaddleOCR 资源缺失：{fileName}");
            var target = Path.Combine(_paddleDirectory, fileName);
            if (File.Exists(target) && new FileInfo(target).Length == resource.Length)
            {
                continue;
            }

            var temporary = target + "." + Environment.ProcessId + ".tmp";
            try
            {
                using (var output = new FileStream(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    resource.CopyTo(output);
                }
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    private static bool HasPlausibleText(string text)
    {
        var readable = text.Count(char.IsLetterOrDigit);
        return readable >= 2;
    }

    private static string Normalize(string? text) => string.Join(
        Environment.NewLine,
        (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)))
        .Trim();

    private static OcrDocument Normalize(OcrDocument document)
    {
        var lines = document.Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Select(line => line with { Text = line.Text.Trim() })
            .ToArray();
        return new OcrDocument(
            string.Join(Environment.NewLine, lines.Select(line => line.Text)),
            lines);
    }
}
