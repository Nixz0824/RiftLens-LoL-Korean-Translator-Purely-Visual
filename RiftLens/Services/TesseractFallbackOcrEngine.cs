using System.Reflection;
using Tesseract;

namespace RiftLens.Services;

internal sealed class TesseractFallbackOcrEngine : IDisposable
{
    private static readonly (string FileName, string ResourceName)[] Models =
    {
        ("kor.traineddata", "RiftLens.Assets.tessdata.kor.traineddata"),
        ("eng.traineddata", "RiftLens.Assets.tessdata.eng.traineddata")
    };
    private static readonly (string FileName, string ResourceName)[] NativeLibraries =
    {
        ("leptonica-1.82.0.dll", "RiftLens.Assets.native.x64.leptonica-1.82.0.dll"),
        ("tesseract50.dll", "RiftLens.Assets.native.x64.tesseract50.dll")
    };

    private readonly string _modelDirectory;
    private readonly string _nativeRoot;
    private readonly Lazy<TesseractEngine> _engine;
    private bool _disposed;

    public static bool IsBundled => Models.Concat(NativeLibraries).All(item =>
        Assembly.GetExecutingAssembly().GetManifestResourceInfo(item.ResourceName) is not null);

    public TesseractFallbackOcrEngine(string storageRoot)
    {
        _modelDirectory = Path.Combine(storageRoot, "tessdata");
        _nativeRoot = Path.Combine(storageRoot, "native");
        _engine = new Lazy<TesseractEngine>(
            CreateEngine,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Recognize(Bitmap bitmap, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
        using var pixels = Pix.LoadFromMemory(encoded.ToArray());
        var pageMode = bitmap.Height <= 180 && bitmap.Width >= bitmap.Height * 3
            ? PageSegMode.SingleLine
            : PageSegMode.SingleBlock;
        using var page = _engine.Value.Process(pixels, pageMode);
        cancellationToken.ThrowIfCancellationRequested();
        return ExtractText(page, bitmap.Height, pageMode);
    }

    public void WarmUp()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = _engine.Value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }
    }

    private TesseractEngine CreateEngine()
    {
        ExtractAssets();
        TesseractEnviornment.CustomSearchPath = _nativeRoot;
        var engine = new TesseractEngine(_modelDirectory, "kor+eng", EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "1");
        engine.SetVariable("user_defined_dpi", "300");
        engine.DefaultPageSegMode = PageSegMode.SingleBlock;
        return engine;
    }

    private void ExtractAssets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        ExtractFiles(assembly, Models, _modelDirectory);
        ExtractFiles(assembly, NativeLibraries, Path.Combine(_nativeRoot, "x64"));
    }

    private static void ExtractFiles(
        Assembly assembly,
        IEnumerable<(string FileName, string ResourceName)> assets,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var (fileName, resourceName) in assets)
        {
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"内置 OCR 兜底资源缺失：{fileName}");
            var target = Path.Combine(destinationDirectory, fileName);
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

    private static string Normalize(string? text) => string.Join(
        Environment.NewLine,
        (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)))
        .Trim();

    private static string ExtractText(Page page, int imageHeight, PageSegMode pageMode)
    {
        if (pageMode == PageSegMode.SingleLine)
        {
            return Normalize(page.GetText());
        }

        var lines = new List<(string Text, Rect Bounds)>();
        using var iterator = page.GetIterator();
        iterator.Begin();
        do
        {
            var text = Normalize(iterator.GetText(PageIteratorLevel.TextLine));
            if (text.Length == 0
                || !iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
            {
                continue;
            }
            lines.Add((text, bounds));
        }
        while (iterator.Next(PageIteratorLevel.TextLine));

        if (lines.Count == 0)
        {
            return Normalize(page.GetText());
        }
        if (lines.Count > 1 && TouchesTopEdge(lines[0].Bounds, imageHeight))
        {
            lines.RemoveAt(0);
        }
        return string.Join(Environment.NewLine, lines.Select(line => line.Text));
    }

    private static bool TouchesTopEdge(Rect bounds, int imageHeight)
    {
        var edgeTolerance = Math.Max(3, imageHeight / 80);
        return bounds.Y1 <= edgeTolerance;
    }
}
