using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RiftLens.Models;

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
    private const int SwShowNoActivate = 4;

    private readonly Label _translationLabel;
    private readonly Label _originalLabel;
    private CancellationTokenSource? _animationCancellation;
    private OverlayPlacement? _lockedAutoPlacement;
    private Rectangle _lockedCaptureRegion;
    private Font? _cachedTranslationFont;
    private float _cachedTranslationFontSize;
    private AppLanguage _cachedTranslationLanguage;

    public event Action<Rectangle>? TranslationPositioned;

    public TranslationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UiTheme.DarkCanvas;
        Padding = new Padding(12, 10, 12, 10);
        AutoSize = false;

        _translationLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            ForeColor = UiTheme.DarkText,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 16, FontStyle.Regular, GraphicsUnit.Pixel),
            Location = new Point(12, 10)
        };
        _originalLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            ForeColor = UiTheme.DarkTextMuted,
            BackColor = Color.Transparent,
            Font = new Font("Malgun Gothic", 12, FontStyle.Regular, GraphicsUnit.Pixel),
            Visible = false
        };
        Controls.Add(_translationLabel);
        Controls.Add(_originalLabel);
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
        // 暗色表面上的微妙边框和顶部高光线
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 2, Height - 2);
        using var border = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
        e.Graphics.DrawPath(border, UiTheme.RoundedPath(bounds, 6));
        // 顶部高光
        using var highlight = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
        e.Graphics.DrawArc(highlight, bounds.X + 3, bounds.Y + 1, bounds.Width - 6, bounds.Height - 2, 190, 100);
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

        _translationLabel.Text = result.DisplayTranslation;
        var desiredSize = Math.Clamp(settings.OverlayFontSize, 12, 28);
        if (_cachedTranslationFont is null
            || Math.Abs(_cachedTranslationFontSize - desiredSize) > 0.5f
            || _cachedTranslationLanguage != settings.Language)
        {
            _cachedTranslationFont?.Dispose();
            _cachedTranslationFont = new Font(
                settings.Language == AppLanguage.English ? "Segoe UI Variable" : "Microsoft YaHei UI",
                desiredSize,
                FontStyle.Regular,
                GraphicsUnit.Pixel);
            _cachedTranslationFontSize = desiredSize;
            _cachedTranslationLanguage = settings.Language;
        }
        _translationLabel.Font = _cachedTranslationFont;
        _originalLabel.Text = result.RawText;
        _originalLabel.Visible = settings.ShowOriginalText && !string.IsNullOrWhiteSpace(result.RawText);

        LayoutContent();
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
        Region?.Dispose();
        using (var path = new GraphicsPath())
        {
            AddRoundedRectangle(path, new Rectangle(0, 0, Width, Height), 6);
            Region = new Region(path);
        }

        Opacity = 0;
        ShowWindow(Handle, SwShowNoActivate);
        await FadeAsync(0, settings.OverlayOpacity, 150, token);
        await Task.Delay(Math.Clamp(settings.OverlayDurationMs, 1500, 15000), token);
        await FadeAsync(settings.OverlayOpacity, 0, 220, token);
        Hide();
    }

    public void HideImmediately()
    {
        _animationCancellation?.Cancel();
        Opacity = 0;
        Hide();
    }

    private void LayoutContent()
    {
        var maximumWidth = 410;
        _translationLabel.MaximumSize = new Size(maximumWidth, 0);
        _originalLabel.MaximumSize = new Size(maximumWidth, 0);
        _translationLabel.Location = new Point(Padding.Left, Padding.Top);
        _originalLabel.Location = new Point(Padding.Left, _translationLabel.Bottom + 4);
        var contentWidth = Math.Max(_translationLabel.Width, _originalLabel.Visible ? _originalLabel.Width : 0);
        var contentBottom = _originalLabel.Visible ? _originalLabel.Bottom : _translationLabel.Bottom;
        Size = new Size(
            Math.Clamp(contentWidth + Padding.Horizontal, 160, maximumWidth + Padding.Horizontal),
            Math.Max(36, contentBottom + Padding.Bottom));
    }

    private async Task FadeAsync(double from, double to, int durationMs, CancellationToken cancellationToken)
    {
        const int frameMs = 25;
        var steps = Math.Max(1, durationMs / frameMs);
        for (var step = 0; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = step / (double)steps;
            var eased = 1 - Math.Pow(1 - progress, 3);
            Opacity = Math.Clamp(from + (to - from) * eased, 0, 1);
            await Task.Delay(frameMs, cancellationToken);
        }
    }

    private static void AddRoundedRectangle(GraphicsPath path, Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationCancellation?.Cancel();
            _animationCancellation?.Dispose();
            _cachedTranslationFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
