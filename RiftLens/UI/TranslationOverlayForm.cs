using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RiftLens.Models;
using RiftLens.Services;

namespace RiftLens.UI;

public sealed class TranslationOverlayForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const int HorizontalPadding = 20;
    private const int VerticalPadding = 14;

    private CancellationTokenSource? _animationCancellation;
    private OverlayPlacement? _lockedAutoPlacement;
    private Rectangle _lockedCaptureRegion;
    private Font? _cachedTranslationFont;
    private Font? _cachedOriginalFont;
    private Font? _cachedSpeakerFont;
    private float _cachedTranslationFontSize;
    private AppLanguage _cachedTranslationLanguage;
    private string _speakerText = string.Empty;
    private readonly List<OverlayMessageLayout> _messageLayouts = new();
    private IReadOnlyList<string> _translationLines = Array.Empty<string>();
    private IReadOnlyList<string> _originalLines = Array.Empty<string>();
    private bool _showOriginal;
    private int _contentWidth = 410;
    private int _speakerHeight;
    private byte _surfaceOpacity;

    public event Action<Rectangle>? TranslationPositioned;

    public TranslationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = false;
        BackColor = Color.Black;
        AccessibleName = "RiftLens translation overlay";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExLayered | WsExTransparent;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawSurface(e.Graphics);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = (IntPtr)MaNoActivate;
            return;
        }

        if (message.Msg == WmNcHitTest)
        {
            message.Result = (IntPtr)HtTransparent;
            return;
        }

        base.WndProc(ref message);
    }

    public async Task ShowTranslationAsync(
        TranslationResult result,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _animationCancellation.Token;

        PrepareContent(result, settings);
        EnsureFonts(settings);
        LayoutContent(settings);

        if (settings.OverlayPlacement == OverlayPlacement.Auto)
        {
            var capture = settings.CaptureRegion.ToRectangle();
            if (_lockedAutoPlacement is null || capture != _lockedCaptureRegion)
            {
                _lockedAutoPlacement = OverlayPlacementService.ResolveAutoPlacement(settings, Size);
                _lockedCaptureRegion = capture;
            }
        }
        else
        {
            _lockedAutoPlacement = null;
        }

        Bounds = OverlayPlacementService.Calculate(settings, Size, _lockedAutoPlacement);
        TranslationPositioned?.Invoke(Bounds);
        _surfaceOpacity = 0;
        if (!Visible)
        {
            Show();
        }
        RenderLayeredSurface();
        await FadeAsync(0, settings.OverlayOpacity, 150, token);
        await Task.Delay(Math.Clamp(settings.OverlayDurationMs, 1500, 15000), token);
        await FadeAsync(settings.OverlayOpacity, 0, 220, token);
        Hide();
    }

    public void HideImmediately()
    {
        _animationCancellation?.Cancel();
        _surfaceOpacity = 0;
        if (IsHandleCreated)
        {
            RenderLayeredSurface();
        }
        Hide();
    }

    private void PrepareContent(TranslationResult result, AppSettings settings)
    {
        _speakerText = string.IsNullOrWhiteSpace(result.Speaker)
            ? settings.Language == AppLanguage.English ? "Teammate" : "队友"
            : result.Speaker.Trim();

        var explicitTranslations = (result.DisplayLines ?? new List<string>())
            .Select(line => RemoveSpeakerPrefix(line, _speakerText))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(8)
            .ToArray();
        _translationLines = explicitTranslations.Length > 0
            ? explicitTranslations
            : result.DisplayTranslation
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(line => RemoveSpeakerPrefix(line, _speakerText))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(8)
                .DefaultIfEmpty(settings.Language == AppLanguage.English ? "Translation unavailable" : "暂时没有可显示的译文")
                .ToArray();

        var explicitOriginals = result.OriginalLines ?? new List<string>();
        var hasExplicitOriginals = explicitOriginals.Count > 0;
        var originalCandidates = hasExplicitOriginals
            ? explicitOriginals
            : result.RawText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        _originalLines = originalCandidates
            .Select(line => hasExplicitOriginals ? line.Trim() : CleanOriginalLine(line, result.SourceKind))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(8)
            .ToArray();
        _showOriginal = settings.ShowOriginalText && _originalLines.Count > 0;
    }

    private void EnsureFonts(AppSettings settings)
    {
        var desiredSize = Math.Clamp(settings.OverlayFontSize, 12, 28);
        if (_cachedTranslationFont is not null
            && Math.Abs(_cachedTranslationFontSize - desiredSize) <= 0.5f
            && _cachedTranslationLanguage == settings.Language)
        {
            return;
        }

        _cachedTranslationFont?.Dispose();
        _cachedOriginalFont?.Dispose();
        _cachedSpeakerFont?.Dispose();
        _cachedTranslationFont = new Font(
            settings.Language == AppLanguage.English ? "Segoe UI Variable" : "Microsoft YaHei UI",
            desiredSize,
            FontStyle.Regular,
            GraphicsUnit.Pixel);
        _cachedOriginalFont = new Font(
            settings.Language == AppLanguage.English ? "Segoe UI Variable" : "Malgun Gothic",
            Math.Max(11, desiredSize - 4),
            FontStyle.Regular,
            GraphicsUnit.Pixel);
        _cachedSpeakerFont = new Font(
            settings.Language == AppLanguage.English ? "Segoe UI Variable" : "Microsoft YaHei UI",
            Math.Max(11, desiredSize - 4),
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        _cachedTranslationFontSize = desiredSize;
        _cachedTranslationLanguage = settings.Language;
    }

    private void LayoutContent(AppSettings settings)
    {
        var custom = settings.OverlayPlacement == OverlayPlacement.Custom
            && settings.DisplayRegion.IsValid
                ? settings.DisplayRegion.ToRectangle()
                : Rectangle.Empty;
        var fixedWidth = custom.IsEmpty
            ? Math.Clamp(settings.CaptureRegion.Width, 280, 540)
            : Math.Max(120, custom.Width);
        _contentWidth = Math.Max(80, fixedWidth - HorizontalPadding * 2 - 8);

        using var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(measureBitmap);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        _speakerHeight = MeasureTextHeight(graphics, _speakerText, _cachedSpeakerFont!, _contentWidth);
        _messageLayouts.Clear();

        var pairCount = Math.Max(_translationLines.Count, _showOriginal ? _originalLines.Count : 0);
        if (_translationLines.Count == 1 && _showOriginal && _originalLines.Count > 1)
        {
            var translation = _translationLines[0];
            var original = string.Join("  ·  ", _originalLines);
            _messageLayouts.Add(MeasureMessage(graphics, translation, original));
        }
        else
        {
            for (var index = 0; index < Math.Max(1, pairCount); index++)
            {
                var translation = index < _translationLines.Count ? _translationLines[index] : string.Empty;
                var original = _showOriginal && index < _originalLines.Count ? _originalLines[index] : string.Empty;
                _messageLayouts.Add(MeasureMessage(graphics, translation, original));
            }
        }

        if (!custom.IsEmpty)
        {
            Size = custom.Size;
            return;
        }

        var messagesHeight = _messageLayouts.Sum(item =>
            item.TranslationHeight
            + (item.OriginalHeight > 0 ? 3 + item.OriginalHeight : 0))
            + Math.Max(0, _messageLayouts.Count - 1) * 8;
        var desiredHeight = VerticalPadding * 2 + _speakerHeight + 7 + messagesHeight;
        Size = new Size(fixedWidth, Math.Clamp(desiredHeight, 58, 224));
    }

    private OverlayMessageLayout MeasureMessage(Graphics graphics, string translation, string original)
    {
        var translationHeight = string.IsNullOrWhiteSpace(translation)
            ? 0
            : Math.Min(
                MeasureTextHeight(graphics, translation, _cachedTranslationFont!, _contentWidth),
                _cachedTranslationFont!.Height * 3);
        var originalHeight = _showOriginal && !string.IsNullOrWhiteSpace(original)
            ? Math.Min(
                MeasureTextHeight(graphics, original, _cachedOriginalFont!, _contentWidth),
                _cachedOriginalFont!.Height * 2)
            : 0;
        return new OverlayMessageLayout(translation, original, translationHeight, originalHeight);
    }

    private static int MeasureTextHeight(Graphics graphics, string text, Font font, int width)
    {
        using var format = CreateTextFormat();
        return Math.Max(
            font.Height,
            (int)Math.Ceiling(graphics.MeasureString(
                string.IsNullOrWhiteSpace(text) ? " " : text,
                font,
                new SizeF(width, 1000),
                format).Height));
    }

    private void DrawSurface(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var surfaceBounds = new Rectangle(3, 2, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
        using var shadowPath = UiTheme.RoundedPath(
            new Rectangle(surfaceBounds.X + 1, surfaceBounds.Y + 3, surfaceBounds.Width, surfaceBounds.Height),
            13);
        using var shadow = new SolidBrush(Color.FromArgb(58, 3, 12, 18));
        graphics.FillPath(shadow, shadowPath);

        using var surfacePath = UiTheme.RoundedPath(surfaceBounds, 13);
        using var surface = new LinearGradientBrush(
            surfaceBounds,
            Color.FromArgb(248, 14, 29, 39),
            Color.FromArgb(246, 22, 43, 54),
            LinearGradientMode.Vertical);
        graphics.FillPath(surface, surfacePath);

        using var border = new Pen(Color.FromArgb(45, 219, 239, 244), 1f);
        graphics.DrawPath(border, surfacePath);

        using var accentPen = new Pen(Color.FromArgb(205, UiTheme.Mint), 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(
            accentPen,
            surfaceBounds.Left + 8,
            surfaceBounds.Top + 11,
            surfaceBounds.Left + 8,
            Math.Max(surfaceBounds.Top + 12, surfaceBounds.Bottom - 11));

        var textX = surfaceBounds.Left + HorizontalPadding;
        var speakerY = surfaceBounds.Top + VerticalPadding - 3;
        using var speakerBrush = new SolidBrush(Color.FromArgb(235, 91, 224, 195));
        using var translationBrush = new SolidBrush(Color.FromArgb(245, 242, 248, 250));
        using var originalBrush = new SolidBrush(Color.FromArgb(176, 188, 207, 216));
        using var format = CreateTextFormat();
        using var speakerFormat = CreateSpeakerFormat();
        graphics.DrawString(
            _speakerText,
            _cachedSpeakerFont!,
            speakerBrush,
            new RectangleF(
                textX,
                speakerY,
                _contentWidth,
                Math.Min(_speakerHeight + 1, Math.Max(1, surfaceBounds.Bottom - speakerY - 4))),
            speakerFormat);

        var textY = speakerY + _speakerHeight + 6;
        var contentBottom = surfaceBounds.Bottom - VerticalPadding + 2;
        for (var index = 0; index < _messageLayouts.Count && textY < contentBottom; index++)
        {
            var item = _messageLayouts[index];
            if (item.TranslationHeight > 0 && !string.IsNullOrWhiteSpace(item.Translation))
            {
                var height = Math.Min(item.TranslationHeight + 2, Math.Max(1, contentBottom - textY));
                var translationArea = new RectangleF(
                    textX,
                    textY,
                    _contentWidth,
                    height);
                graphics.DrawString(
                    item.Translation,
                    _cachedTranslationFont!,
                    translationBrush,
                    translationArea,
                    format);
                textY += height;
            }

            if (item.OriginalHeight > 0
                && !string.IsNullOrWhiteSpace(item.Original)
                && textY + 3 < contentBottom)
            {
                textY += 3;
                var height = Math.Min(item.OriginalHeight + 1, Math.Max(1, contentBottom - textY));
                var originalArea = new RectangleF(
                    textX,
                    textY,
                    _contentWidth,
                    height);
                graphics.DrawString(
                    item.Original,
                    _cachedOriginalFont!,
                    originalBrush,
                    originalArea,
                    format);
                textY += height;
            }

            if (index >= _messageLayouts.Count - 1 || textY + 7 >= contentBottom)
            {
                continue;
            }

            using var separator = new Pen(Color.FromArgb(24, 204, 225, 232), 1f);
            graphics.DrawLine(
                separator,
                textX,
                textY + 4,
                textX + Math.Min(_contentWidth, 92),
                textY + 4);
            textY += 8;
        }
    }

    private static StringFormat CreateTextFormat() => new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisWord,
        FormatFlags = StringFormatFlags.LineLimit
    };

    private static StringFormat CreateSpeakerFormat() => new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };

    private static string RemoveSpeakerPrefix(string? value, string speaker)
    {
        var line = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(speaker))
        {
            return line;
        }

        if (line.StartsWith(speaker, StringComparison.OrdinalIgnoreCase))
        {
            line = line[speaker.Length..]
                .TrimStart(' ', '\t', ':', '：', '-', '—', '•', '·');
        }
        return line.TrimStart('•', '·', '-', '—', ' ');
    }

    private static string CleanOriginalLine(string? value, TranslationSourceKind sourceKind)
    {
        var line = MessageDeduplicator.StripGameTimestamp(value ?? string.Empty).Trim();
        if (sourceKind != TranslationSourceKind.PlayerChat)
        {
            return line;
        }

        while (line.StartsWith('['))
        {
            var close = line.IndexOf(']');
            if (close is < 1 or > 16)
            {
                break;
            }
            line = line[(close + 1)..].TrimStart();
        }

        var colon = line.IndexOfAny(new[] { ':', '：' });
        return colon >= 0 && colon + 1 < line.Length
            ? line[(colon + 1)..].Trim()
            : line;
    }

    private void RenderLayeredSurface()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawSurface(graphics);
        }
        LayeredWindowSurface.Update(this, bitmap, _surfaceOpacity);
    }

    private async Task FadeAsync(
        double from,
        double to,
        int durationMs,
        CancellationToken cancellationToken)
    {
        const int frameMs = 25;
        var steps = Math.Max(1, durationMs / frameMs);
        for (var step = 0; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = step / (double)steps;
            var eased = 1 - Math.Pow(1 - progress, 3);
            var opacity = Math.Clamp(from + (to - from) * eased, 0, 1);
            _surfaceOpacity = (byte)Math.Round(opacity * 255);
            RenderLayeredSurface();
            await Task.Delay(frameMs, cancellationToken);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationCancellation?.Cancel();
            _animationCancellation?.Dispose();
            _cachedTranslationFont?.Dispose();
            _cachedOriginalFont?.Dispose();
            _cachedSpeakerFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record OverlayMessageLayout(
        string Translation,
        string Original,
        int TranslationHeight,
        int OriginalHeight);
}
