using System.Drawing.Drawing2D;
using System.Reflection;
using RiftLens.Models;

namespace RiftLens.UI;

internal enum SpatialPage
{
    Home,
    History,
    Voice,
    Settings
}

internal enum SpatialAction
{
    Navigate,
    Minimize,
    Maximize,
    Close,
    BeginWindowDrag,
    TogglePrimary,
    SelectRegion,
    SelectDisplayRegion,
    TestOcr,
    SaveApiKey,
    ShowApiHelp,
    ClearHistory,
    ExportDiagnostics,
    ToggleEvaluationRecording,
    TestSpeech,
    PreviewOverlay,
    RestoreDefaults,
    SaveSettings,
    ManageApiKey,
    CheckUpdate,
    ShowAbout,
    VolumeChanged,
    SpeechRateChanged,
    SpeechFilterChanged,
    UrgentPlayerSpeechChanged,
    ImportantPlayerSpeechChanged,
    NeutralPlayerSpeechChanged,
    ToggleAutoSpeech,
    OutputModeChanged,
    SystemFilterChanged,
    ChangeOnlyChanged,
    SignalVoiceChanged,
    TacticalSignalsChanged,
    KillEventsChanged,
    ObjectiveEventsChanged,
    ClientRemindersChanged,
    OtherSystemEventsChanged,
    KeepEmotionChanged,
    FloatingButtonChanged,
    LowUsageChanged,
    MinimizeAfterStartChanged,
    CloseToTrayChanged,
    OverlayOpacityChanged,
    OverlayPlacementChanged,
    LanguageChanged,
    InterfacePaletteChanged,
    CustomThemeColorChanged,
    OnboardingCompleted,
    SaveTranslationHistoryChanged,
    ApplyPreset,
    SpeechPresetChanged
}

internal sealed record SpatialHistoryItem(
    DateTime Time,
    string Original,
    string Translation,
    bool Spoken,
    int Priority,
    string Kind,
    string? SilenceReason = null);

/// <summary>
/// 高保真 Soft Spatial 工作区。界面由 GDI+ 一次合成，避免 WinForms 系统控件
/// 在不同 DPI 下出现边框、字号和间距漂移；仅 API Key 使用原生文本输入以保留
/// 输入法、选择和无障碍支持。
/// </summary>
internal sealed class SpatialWorkspace : Control
{
    private const float DesignWidth = 1536f;
    private const float DesignHeight = 1004f;
    private readonly List<HitTarget> _hits = new();
    private readonly List<SpatialHistoryItem> _history = new();
    private readonly ToolTip _toolTip = new() { InitialDelay = 300, ReshowDelay = 80, AutoPopDelay = 6000 };
    private readonly TextBox _apiKeyBox;
    private Image? _mainReference;
    private Image? _voiceReference;
    private HitTarget? _pressed;
    private string? _hoverHelp;
    private int _historyScroll;
    private bool _draggingVolume;
    private bool _draggingOpacity;

    public event Action<SpatialAction, object?>? ActionRequested;

    public SpatialPage Page { get; private set; } = SpatialPage.Home;
    public bool RegionReady { get; set; }
    public bool OcrReady { get; set; }
    public bool AiReady { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool SpeechAvailable { get; set; } = true;
    public string StatusMessage { get; set; } = "正在读取配置…";
    public string RegionSummary { get; set; } = "尚未框选需要识别的文字区域";
    public string ModelName { get; set; } = "deepseek-v4-flash";
    public string RecentOriginal { get; set; } = "레오나: 탑 조심";
    public string RecentTranslation { get; set; } = "蕾欧娜：上路小心";
    public string RecentMeta { get; set; } = "刚刚 · 已播报";
    public int Volume { get; set; } = 65;
    public int SpeechRate { get; set; } = 50;
    public int OutputModeIndex { get; set; } = 2;
    public int SystemFilterIndex { get; set; } = 1;
    public bool AutoSpeech { get; set; } = true;
    public bool ChangeOnly { get; set; } = true;
    public bool SignalVoice { get; set; }
    public bool KeepEmotion { get; set; } = true;
    public bool LowPrioritySpeech { get; set; }
    public bool FloatingButton { get; set; } = true;
    public bool LowUsage { get; set; } = true;
    public bool MinimizeAfterStart { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public int OverlayOpacity { get; set; } = 80;

    public string ApiKeyText
    {
        get => _apiKeyBox.Text;
        set
        {
            if (_apiKeyBox.Text != value)
                _apiKeyBox.Text = value;
        }
    }

    public SpatialWorkspace()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(248, 247, 245);
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        _apiKeyBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(250, 249, 248),
            ForeColor = SpatialPalette.Navy,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point),
            UseSystemPasswordChar = true,
            PlaceholderText = "sk-••••••••••••",
            AccessibleName = "DeepSeek API Key",
            TabIndex = 1
        };
        _apiKeyBox.TextChanged += (_, _) => Invalidate();
        Controls.Add(_apiKeyBox);

        _history.AddRange(new[]
        {
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(14).AddSeconds(8), "레오나: 탑 조심", "蕾欧娜：上路小心", true, 3, "紧急"),
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(14).AddSeconds(21), "카이사: 미드 가는 중", "卡莎：正在去中路", true, 2, ""),
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(14).AddSeconds(46), "정글러: 바론 시야 없음", "打野：大龙区域没有视野", false, 2, "提醒"),
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(15).AddSeconds(3), "서포터: 와드 박았음", "辅助：已经插眼", false, 1, ""),
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(15).AddSeconds(19), "아리: 미드 미아", "阿狸：中路敌人消失", false, 2, "提醒"),
            new SpatialHistoryItem(DateTime.Today.AddHours(20).AddMinutes(15).AddSeconds(37), "레오나: 용 먼저", "蕾欧娜：先打小龙", true, 3, "")
        });

        _mainReference = LoadReference("RiftLens.Assets.design.main.png");
        _voiceReference = LoadReference("RiftLens.Assets.design.voice.png");
    }

    public void SetPage(SpatialPage page)
    {
        if (Page == page) return;
        Page = page;
        _historyScroll = 0;
        LayoutApiInput();
        Invalidate();
    }

    public void AddHistory(SpatialHistoryItem item)
    {
        _history.Insert(0, item);
        if (_history.Count > 50)
            _history.RemoveRange(50, _history.Count - 50);
        Invalidate();
    }

    public void ClearHistory()
    {
        _history.Clear();
        _historyScroll = 0;
        Invalidate();
    }

    public void FocusApiKey()
    {
        SetPage(SpatialPage.Home);
        _apiKeyBox.Focus();
        _apiKeyBox.SelectAll();
    }

    public void ApplySettings(AppSettings settings, bool minimizeAfterStart)
    {
        Volume = settings.SpeechVolume;
        SpeechRate = Math.Clamp((settings.SpeechRate + 5) * 10, 0, 100);
        OutputModeIndex = (int)settings.OutputMode;
        SystemFilterIndex = settings.SystemMessageFilter switch
        {
            SystemMessageFilterMode.Off => 0,
            SystemMessageFilterMode.Strict => 2,
            _ => 1
        };
        SignalVoice = settings.SignalVoiceEnabled;
        KeepEmotion = settings.KeepEmotionSummary;
        LowPrioritySpeech = settings.SpeechFilter == SpeechFilterLevel.Full;
        OverlayOpacity = Math.Clamp((int)Math.Round(settings.OverlayOpacity * 100), 20, 90);
        LowUsage = settings.PollIntervalMs >= 450;
        MinimizeAfterStart = minimizeAfterStart;
        ModelName = settings.ApiModel;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutApiInput();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mainReference?.Dispose();
            _voiceReference?.Dispose();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hits.Clear();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(SpatialPalette.WarmWhite);

        var sx = Width / DesignWidth;
        var sy = Height / DesignHeight;
        var state = g.Save();
        g.ScaleTransform(sx, sy);

        DrawAmbientBackground(g);
        DrawHeader(g);
        DrawSidebar(g);
        switch (Page)
        {
            case SpatialPage.History: DrawHistoryPage(g); break;
            case SpatialPage.Voice: DrawVoicePage(g); break;
            case SpatialPage.Settings: DrawSettingsPage(g); break;
            default: DrawHomePage(g); break;
        }
        g.Restore(state);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Page != SpatialPage.History) return;
        var p = ToDesign(e.Location);
        if (!new RectangleF(190, 315, 1265, 480).Contains(p)) return;
        var max = Math.Max(0, _history.Count * 72 - 430);
        _historyScroll = Math.Clamp(_historyScroll - Math.Sign(e.Delta) * 76, 0, max);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToDesign(e.Location);
        if (_draggingVolume)
        {
            SetVolumeFromPoint(p);
            return;
        }
        if (_draggingOpacity)
        {
            SetOpacityFromPoint(p);
            return;
        }

        var hit = _hits.LastOrDefault(item => item.Bounds.Contains(p));
        Cursor = hit is null ? Cursors.Default : Cursors.Hand;
        var nextHelp = hit?.HelpText;
        if (nextHelp == _hoverHelp) return;
        _hoverHelp = nextHelp;
        _toolTip.Hide(this);
        if (!string.IsNullOrWhiteSpace(nextHelp))
            _toolTip.Show(nextHelp, this, e.X + 12, e.Y + 18, 5000);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var p = ToDesign(e.Location);
        if (CommonVolumeRect().Contains(p))
        {
            _draggingVolume = true;
            Capture = true;
            SetVolumeFromPoint(p);
            return;
        }
        if (Page == SpatialPage.Settings && new RectangleF(405, 700, 300, 28).Contains(p))
        {
            _draggingOpacity = true;
            Capture = true;
            SetOpacityFromPoint(p);
            return;
        }
        _pressed = _hits.LastOrDefault(item => item.Bounds.Contains(p));
        if (_pressed is null && p.Y <= 88)
            ActionRequested?.Invoke(SpatialAction.BeginWindowDrag, null);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (_draggingVolume || _draggingOpacity)
        {
            _draggingVolume = false;
            _draggingOpacity = false;
            Capture = false;
            return;
        }
        var p = ToDesign(e.Location);
        var hit = _hits.LastOrDefault(item => item.Bounds.Contains(p));
        if (_pressed is not null && ReferenceEquals(_pressed, hit))
            ActionRequested?.Invoke(hit.Action, hit.Value);
        _pressed = null;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var p = ToDesign(e.Location);
        if (e.Button == MouseButtons.Left && p.Y <= 88)
            ActionRequested?.Invoke(SpatialAction.Maximize, null);
    }

    private void DrawAmbientBackground(Graphics g)
    {
        using var baseBrush = new LinearGradientBrush(
            new RectangleF(0, 0, DesignWidth, DesignHeight),
            Color.FromArgb(251, 250, 249), Color.FromArgb(246, 247, 247), 20f);
        g.FillRectangle(baseBrush, 0, 0, DesignWidth, DesignHeight);
        DrawGlow(g, new RectangleF(-180, -100, 820, 720), Color.FromArgb(28, 234, 215, 202));
        DrawGlow(g, new RectangleF(1020, -160, 760, 690), Color.FromArgb(28, 255, 205, 177));
        DrawGlow(g, new RectangleF(390, 610, 780, 500), Color.FromArgb(18, 180, 236, 218));
    }

    private static void DrawGlow(Graphics g, RectangleF bounds, Color center)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = center,
            SurroundColors = new[] { Color.FromArgb(0, center) }
        };
        g.FillEllipse(brush, bounds);
    }

    private void DrawHeader(Graphics g)
    {
        DrawBrandMark(g, new RectangleF(40, 30, 44, 44));
        DrawText(g, "R I F T L E N S", 20, FontStyle.Regular,
            new RectangleF(99, 29, 260, 48), SpatialPalette.Navy, ContentAlignment.MiddleLeft, false, "Segoe UI");

        DrawWindowGlyph(g, new RectangleF(1350, 25, 45, 45), "min");
        DrawWindowGlyph(g, new RectangleF(1418, 25, 45, 45), "max");
        DrawWindowGlyph(g, new RectangleF(1486, 25, 45, 45), "close");
        AddHit(new RectangleF(1340, 15, 60, 60), SpatialAction.Minimize);
        AddHit(new RectangleF(1408, 15, 60, 60), SpatialAction.Maximize);
        AddHit(new RectangleF(1476, 15, 60, 60), SpatialAction.Close);
    }

    private void DrawSidebar(Graphics g)
    {
        var nav = new RectangleF(20, 88, 80, 882);
        DrawCard(g, nav, 19, Color.FromArgb(94, 255, 255, 255), Color.FromArgb(126, 212, 215, 219), false);
        var pages = new[] { SpatialPage.Home, SpatialPage.History, SpatialPage.Voice, SpatialPage.Settings };
        var ys = new[] { 172f, 268f, 363f, 458f };
        for (var i = 0; i < pages.Length; i++)
        {
            var selected = Page == pages[i];
            if (selected)
            {
                using var pen = new Pen(SpatialPalette.Mint, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, 28, ys[i] - 22, 28, ys[i] + 22);
            }
            DrawNavIcon(g, new PointF(61, ys[i]), i, selected);
            AddHit(new RectangleF(31, ys[i] - 34, 58, 68), SpatialAction.Navigate, pages[i]);
        }

        DrawNavIcon(g, new PointF(61, 748), 4, false);
        DrawText(g, "更新", 12, FontStyle.Regular, new RectangleF(34, 772, 54, 26), SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        AddHit(new RectangleF(28, 718, 66, 88), SpatialAction.CheckUpdate);
        DrawNavIcon(g, new PointF(61, 846), 5, false);
        DrawText(g, "软件", 12, FontStyle.Regular, new RectangleF(34, 870, 54, 26), SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        AddHit(new RectangleF(28, 816, 66, 88), SpatialAction.ShowAbout);
    }

    private void DrawHomePage(Graphics g)
    {
        var leftX = 120f;
        var leftW = 728f;
        var gap = 16f;
        var rightX = leftX + leftW + gap;
        var rightW = 652f;

        DrawTranslatingCard(g, new RectangleF(leftX, 88, leftW, 489));
        DrawRecognitionCard(g, new RectangleF(rightX, 88, rightW, 489));
        DrawRecentCard(g, new RectangleF(leftX, 592, leftW, 269));
        DrawApiCard(g, new RectangleF(rightX, 592, rightW, 269));
        DrawBottomStatus(g, new RectangleF(120, 877, 1396, 93));
    }

    private void DrawTranslatingCard(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 19, Color.FromArgb(112, 255, 255, 255), SpatialPalette.Border, true);
        if (_mainReference is not null)
        {
            var source = new Rectangle(135, 125, 420, 435);
            DrawFeatheredImage(g, _mainReference, new RectangleF(r.X + 20, r.Y + 32, 425, 426), source, Color.FromArgb(238, 241, 240), 46);
        }
        else
        {
            DrawLensFallback(g, new RectangleF(r.X + 95, r.Y + 90, 330, 330));
        }

        var title = IsRunning ? (IsPaused ? "翻译已暂停" : "正在翻译") : (RegionReady && OcrReady && AiReady ? "可以开始翻译" : "准备开始");
        var subtitle = IsRunning ? "韩文聊天 · 实时识别" : "框选区域 · 连接翻译服务";
        var button = IsRunning ? (IsPaused ? "继续" : "暂停") : "开始翻译";
        DrawText(g, title, 34, FontStyle.Bold, new RectangleF(r.X + 450, r.Y + 128, 250, 58), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, subtitle, 17, FontStyle.Regular, new RectangleF(r.X + 450, r.Y + 194, 245, 40), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        var buttonRect = new RectangleF(r.X + 450, r.Y + 292, 210, 66);
        DrawPillButton(g, buttonRect, button, true);
        AddHit(buttonRect, SpatialAction.TogglePrimary);
        DrawStepNumber(g, "3", new RectangleF(r.Right - 118, r.Bottom - 120, 100, 110), Color.FromArgb(35, 95, 130, 140));
    }

    private void DrawRecognitionCard(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 19, Color.FromArgb(105, 255, 253, 250), SpatialPalette.Border, true);
        DrawText(g, "识别区域", 25, FontStyle.Bold, new RectangleF(r.X + 34, r.Y + 20, 180, 52), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawDot(g, new PointF(r.X + 250, r.Y + 42), RegionReady ? SpatialPalette.Mint : SpatialPalette.Peach);
        DrawText(g, RegionReady ? "已就绪" : "待框选", 14, FontStyle.Bold,
            new RectangleF(r.X + 268, r.Y + 22, 100, 42), RegionReady ? SpatialPalette.MintDeep : SpatialPalette.TextMuted, ContentAlignment.MiddleLeft);

        var preview = new RectangleF(r.X + 35, r.Y + 78, r.Width - 70, 314);
        DrawCard(g, preview, 16, Color.FromArgb(172, 250, 249, 248), Color.FromArgb(145, 212, 213, 216), false);
        var chat = new RectangleF(preview.X + 80, preview.Y + 42, preview.Width - 160, 174);
        DrawCard(g, chat, 13, Color.FromArgb(105, 249, 248, 247), Color.FromArgb(145, 214, 215, 218), false);
        DrawCornerGuides(g, RectangleF.Inflate(chat, 20, 14));
        DrawChatPreview(g, chat);
        DrawText(g, "游戏聊天框", 18, FontStyle.Bold, new RectangleF(preview.X, preview.Y + 222, preview.Width, 36), SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        DrawText(g, "适用于游戏、选人及直播画面", 13.5f, FontStyle.Regular,
            new RectangleF(preview.X, preview.Y + 258, preview.Width, 34), SpatialPalette.BlueMuted, ContentAlignment.MiddleCenter);

        var select = new RectangleF(r.X + 68, r.Bottom - 79, 212, 52);
        DrawOutlineButton(g, select, RegionReady ? "重新框选" : "框选区域", SpatialPalette.Mint);
        AddHit(select, SpatialAction.SelectRegion);
        var test = new RectangleF(r.X + 330, r.Bottom - 79, 190, 52);
        DrawText(g, "测试识别", 16, FontStyle.Regular, test, SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        AddHit(test, SpatialAction.TestOcr);
        DrawStepNumber(g, "1", new RectangleF(r.Right - 114, r.Bottom - 177, 105, 160), Color.FromArgb(32, 94, 132, 129));
    }

    private void DrawRecentCard(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(105, 255, 255, 255), SpatialPalette.Border, true);
        DrawText(g, "最近结果", 20, FontStyle.Bold, new RectangleF(r.X + 34, r.Y + 20, 210, 42), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSpeaker(g, new PointF(r.X + 51, r.Y + 95), Color.FromArgb(104, 105, 125, 255));
        DrawText(g, RecentOriginal, 15, FontStyle.Regular, new RectangleF(r.X + 83, r.Y + 72, 570, 40), Color.FromArgb(104, 105, 125, 255), ContentAlignment.MiddleLeft, true, "Malgun Gothic");
        DrawText(g, RecentTranslation, 21, FontStyle.Bold, new RectangleF(r.X + 34, r.Y + 116, 610, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, RecentMeta, 13, FontStyle.Regular, new RectangleF(r.X + 34, r.Y + 166, 260, 38), SpatialPalette.MintDeep, ContentAlignment.MiddleLeft);
        DrawSpeaker(g, new PointF(r.X + 130, r.Y + 185), SpatialPalette.Mint);
        var more = new RectangleF(r.Right - 285, r.Bottom - 61, 235, 44);
        DrawText(g, "查看全部记录   ›", 14, FontStyle.Regular, more, SpatialPalette.BlueMuted, ContentAlignment.MiddleCenter);
        AddHit(more, SpatialAction.Navigate, SpatialPage.History);
    }

    private void DrawApiCard(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(105, 255, 255, 255), SpatialPalette.Border, true);
        DrawText(g, "DeepSeek API", 21, FontStyle.Regular, new RectangleF(r.X + 27, r.Y + 18, 250, 40), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "连接翻译服务", 13, FontStyle.Regular, new RectangleF(r.X + 27, r.Y + 53, 180, 28), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawStatusChip(g, new RectangleF(r.X + 185, r.Y + 54, 76, 26), AiReady ? "已连接" : "未配置", AiReady);
        DrawModeChip(g, new RectangleF(r.Right - 195, r.Y + 18, 165, 42));
        DrawText(g, "API Key", 15, FontStyle.Regular, new RectangleF(r.X + 27, r.Y + 96, 95, 42), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var input = new RectangleF(r.X + 118, r.Y + 100, r.Width - 175, 42);
        DrawCard(g, input, 12, Color.FromArgb(164, 250, 249, 248), Color.FromArgb(150, 214, 216, 219), false);
        DrawEye(g, new PointF(input.Right - 24, input.Y + 21));
        var save = new RectangleF(r.X + 27, r.Y + 157, 236, 48);
        DrawPillButton(g, save, "保存并连接", true);
        AddHit(save, SpatialAction.SaveApiKey);
        var help = new RectangleF(r.X + 320, r.Y + 157, 235, 48);
        DrawText(g, "如何获取 API Key", 13.5f, FontStyle.Regular, help, SpatialPalette.BlueMuted, ContentAlignment.MiddleCenter);
        AddHit(help, SpatialAction.ShowApiHelp);
        DrawShield(g, new PointF(r.X + 35, r.Bottom - 31));
        DrawText(g, "仅保存在 Windows 凭据管理器", 12.5f, FontStyle.Regular,
            new RectangleF(r.X + 55, r.Bottom - 49, 320, 36), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawStepNumber(g, "2", new RectangleF(r.Right - 114, r.Bottom - 168, 100, 150), Color.FromArgb(30, 114, 100, 220));
    }

    private void DrawHistoryPage(Graphics g)
    {
        var panel = new RectangleF(124, 100, 1392, 768);
        DrawCard(g, panel, 18, Color.FromArgb(98, 255, 255, 255), SpatialPalette.Border, true);
        DrawText(g, "翻译记录", 30, FontStyle.Bold, new RectangleF(174, 125, 300, 56), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "本次运行中的识别与翻译", 15, FontStyle.Regular, new RectangleF(174, 174, 320, 35), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);

        DrawMetric(g, new RectangleF(884, 126, 184, 73), 1, _history.Count.ToString(), "条消息");
        DrawMetric(g, new RectangleF(1088, 126, 196, 73), 2, _history.Count(item => item.Spoken).ToString(), "条已播报");
        DrawMetric(g, new RectangleF(1304, 126, 184, 73), 3, _history.Count(item => !item.Spoken).ToString(), "条已过滤");

        var body = new RectangleF(168, 220, 1304, 594);
        DrawCard(g, body, 17, Color.FromArgb(82, 255, 255, 255), Color.FromArgb(120, 214, 216, 220), false);
        var search = new RectangleF(190, 244, 456, 52);
        DrawCard(g, search, 13, Color.FromArgb(142, 255, 255, 255), Color.FromArgb(145, 210, 213, 218), false);
        DrawSearch(g, new PointF(search.X + 28, search.Y + 26));
        DrawText(g, "搜索英雄或内容", 14, FontStyle.Regular, new RectangleF(search.X + 54, search.Y, 300, search.Height), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);

        var filter = new RectangleF(676, 244, 406, 52);
        DrawSegmented(g, filter, new[] { "全部", "已播报", "高优先级" }, 0, SpatialPalette.Mint);
        var clear = new RectangleF(1310, 244, 152, 52);
        DrawOutlineButton(g, clear, "清空记录", SpatialPalette.BorderDark);
        DrawTrash(g, new PointF(clear.X + 25, clear.Y + 26));
        AddHit(clear, SpatialAction.ClearHistory);

        var list = new RectangleF(190, 314, 1260, 480);
        DrawCard(g, list, 15, Color.FromArgb(66, 255, 255, 255), Color.FromArgb(130, 216, 218, 221), false);
        DrawText(g, "今天 · 本次会话", 16, FontStyle.Bold, new RectangleF(list.X + 28, list.Y + 15, 260, 40), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "共 " + _history.Count + " 条 · 可滚动查看", 12, FontStyle.Regular,
            new RectangleF(list.X + 220, list.Y + 17, 250, 36), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);

        var clipState = g.Save();
        g.SetClip(new RectangleF(list.X + 12, list.Y + 58, list.Width - 36, list.Height - 70));
        var y = list.Y + 72 - _historyScroll;
        var items = _history.Count == 0
            ? Array.Empty<SpatialHistoryItem>()
            : _history.ToArray();
        if (items.Length == 0)
        {
            DrawText(g, "本次运行还没有翻译记录", 16, FontStyle.Regular,
                new RectangleF(list.X, list.Y + 180, list.Width, 60), SpatialPalette.TextMuted, ContentAlignment.MiddleCenter);
        }
        else
        {
            foreach (var item in items)
            {
                DrawHistoryRow(g, new RectangleF(list.X + 22, y, list.Width - 55, 72), item);
                y += 72;
            }
        }
        g.Restore(clipState);
        DrawHistoryScrollbar(g, new RectangleF(list.Right - 23, list.Y + 55, 7, list.Height - 72));
        DrawShield(g, new PointF(180, 839));
        DrawText(g, "记录仅保存在本次运行中，关闭软件后自动清除", 12.5f, FontStyle.Regular,
            new RectangleF(200, 820, 480, 42), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawBottomStatus(g, new RectangleF(124, 885, 1392, 93));
    }

    private void DrawHistoryRow(Graphics g, RectangleF r, SpatialHistoryItem item)
    {
        using var divider = new Pen(Color.FromArgb(100, 210, 213, 217), 1f);
        g.DrawLine(divider, r.X + 45, r.Bottom, r.Right, r.Bottom);
        var dotColor = item.Priority >= 3 ? SpatialPalette.Mint : item.Priority == 2 ? SpatialPalette.Violet : Color.FromArgb(160, 172, 183, 195);
        using var line = new Pen(Color.FromArgb(110, 210, 214, 218), 1f);
        g.DrawLine(line, r.X + 10, r.Y - 5, r.X + 10, r.Bottom + 5);
        using var dot = new SolidBrush(dotColor);
        g.FillEllipse(dot, r.X + 3, r.Y + 25, 14, 14);
        using var ring = new Pen(Color.FromArgb(180, 255, 255, 255), 4f);
        g.DrawEllipse(ring, r.X + 2, r.Y + 24, 16, 16);
        DrawText(g, item.Time.ToString("HH:mm:ss"), 13, FontStyle.Regular,
            new RectangleF(r.X + 50, r.Y + 10, 130, 48), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawText(g, item.Original, 12.5f, FontStyle.Regular,
            new RectangleF(r.X + 220, r.Y + 3, 570, 28), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft, true, "Malgun Gothic");
        DrawText(g, item.Translation, 15.5f, FontStyle.Bold,
            new RectangleF(r.X + 220, r.Y + 29, 650, 36), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        if (!string.IsNullOrEmpty(item.Kind))
            DrawTag(g, new RectangleF(r.Right - 330, r.Y + 17, 74, 38), item.Kind, item.Priority >= 3 ? SpatialPalette.Mint : SpatialPalette.Violet);
        DrawSpeaker(g, new PointF(r.Right - 130, r.Y + 36), item.Spoken ? SpatialPalette.Mint : SpatialPalette.BlueMuted);
        DrawText(g, item.Spoken ? "已播报" : "未播报", 12, item.Spoken ? FontStyle.Bold : FontStyle.Regular,
            new RectangleF(r.Right - 106, r.Y + 12, 98, 48), item.Spoken ? SpatialPalette.MintDeep : SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
    }

    private void DrawVoicePage(Graphics g)
    {
        DrawText(g, "语音播报", 30, FontStyle.Bold, new RectangleF(142, 102, 320, 55), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "把重要信息自然地说出来", 15, FontStyle.Regular, new RectangleF(142, 151, 360, 36), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawStatusPill(g, new RectangleF(1320, 101, 196, 48), SpeechAvailable ? "语音服务可用" : "语音服务离线", SpeechAvailable);

        var voice = new RectangleF(134, 195, 770, 330);
        var quick = new RectangleF(920, 195, 596, 330);
        DrawCard(g, voice, 18, Color.FromArgb(94, 249, 255, 252), SpatialPalette.Border, true);
        DrawCard(g, quick, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawVoiceIdentity(g, voice);
        DrawQuickVoice(g, quick);
        DrawVoiceStrategy(g, new RectangleF(134, 540, 1382, 350));
        DrawBottomStatus(g, new RectangleF(120, 907, 1416, 81));
    }

    private void DrawVoiceIdentity(Graphics g, RectangleF r)
    {
        if (_voiceReference is not null)
        {
            var state = g.Save();
            using var avatarClip = RoundedPath(new RectangleF(r.X + 37, r.Y + 42, 174, 174), 87);
            g.SetClip(avatarClip);
            g.DrawImage(_voiceReference, new RectangleF(r.X + 37, r.Y + 42, 174, 174), new Rectangle(166, 230, 190, 192), GraphicsUnit.Pixel);
            g.Restore(state);
            using var ring = new Pen(Color.FromArgb(175, 255, 255, 255), 6f);
            g.DrawEllipse(ring, r.X + 37, r.Y + 42, 174, 174);
            DrawFeatheredImage(g, _voiceReference, new RectangleF(r.X + 485, r.Y + 12, 265, 302), new Rectangle(620, 185, 278, 315), Color.FromArgb(239, 245, 242), 28);
        }
        else
        {
            DrawAvatarFallback(g, new RectangleF(r.X + 37, r.Y + 42, 174, 174));
            DrawLensFallback(g, new RectangleF(r.X + 500, r.Y + 35, 240, 240));
        }
        DrawText(g, "晓晓 Neural", 22, FontStyle.Regular, new RectangleF(r.X + 235, r.Y + 45, 260, 45), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "Windows 神经语音 · 中文", 14, FontStyle.Regular, new RectangleF(r.X + 235, r.Y + 90, 300, 36), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawTag(g, new RectangleF(r.X + 235, r.Y + 137, 104, 34), "当前音色", SpatialPalette.Mint);
        DrawText(g, "自然、清晰，适合游戏提醒", 14, FontStyle.Regular, new RectangleF(r.X + 235, r.Y + 177, 310, 38), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        var test = new RectangleF(r.X + 235, r.Y + 233, 176, 52);
        DrawOutlineButton(g, test, "▶  试听音色", SpatialPalette.BlueMuted);
        AddHit(test, SpatialAction.TestSpeech);
    }

    private void DrawQuickVoice(Graphics g, RectangleF r)
    {
        DrawText(g, "快速控制", 20, FontStyle.Regular, new RectangleF(r.X + 28, r.Y + 18, 220, 45), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var rows = new[] { r.Y + 66, r.Y + 126, r.Y + 186, r.Y + 246 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                using var line = new Pen(Color.FromArgb(105, 210, 213, 217));
                g.DrawLine(line, r.X + 25, rows[i], r.Right - 25, rows[i]);
            }
        }
        DrawSpeaker(g, new PointF(r.X + 44, rows[0] + 30), SpatialPalette.Navy);
        DrawText(g, "自动播报", 15, FontStyle.Regular, new RectangleF(r.X + 70, rows[0] + 5, 180, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var autoToggle = new RectangleF(r.Right - 84, rows[0] + 13, 56, 32);
        DrawToggle(g, autoToggle, AutoSpeech);
        AddHit(autoToggle, SpatialAction.ToggleAutoSpeech, !AutoSpeech);

        DrawSpeaker(g, new PointF(r.X + 44, rows[1] + 30), SpatialPalette.Navy);
        DrawText(g, "播报音量", 15, FontStyle.Regular, new RectangleF(r.X + 70, rows[1] + 5, 180, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSlider(g, new RectangleF(r.X + 265, rows[1] + 23, 225, 15), Volume, SpatialPalette.Mint);
        DrawText(g, Volume + "%", 14, FontStyle.Regular, new RectangleF(r.Right - 88, rows[1] + 5, 62, 50), SpatialPalette.Navy, ContentAlignment.MiddleRight);

        DrawGauge(g, new PointF(r.X + 44, rows[2] + 30));
        DrawText(g, "语速", 15, FontStyle.Regular, new RectangleF(r.X + 70, rows[2] + 5, 180, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSlider(g, new RectangleF(r.X + 265, rows[2] + 23, 225, 15), SpeechRate, SpatialPalette.Mint);
        DrawText(g, "标准", 14, FontStyle.Regular, new RectangleF(r.Right - 88, rows[2] + 5, 62, 50), SpatialPalette.Navy, ContentAlignment.MiddleRight);

        DrawMonitor(g, new PointF(r.X + 44, rows[3] + 30));
        DrawText(g, "输出设备", 15, FontStyle.Regular, new RectangleF(r.X + 70, rows[3] + 5, 180, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "系统默认  ﹀", 14, FontStyle.Regular, new RectangleF(r.Right - 180, rows[3] + 5, 150, 50), SpatialPalette.Navy, ContentAlignment.MiddleRight);
    }

    private void DrawVoiceStrategy(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(92, 255, 255, 255), SpatialPalette.Border, true);
        DrawText(g, "播报策略", 20, FontStyle.Regular, new RectangleF(r.X + 28, r.Y + 11, 220, 42), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var inner = new RectangleF(r.X + 24, r.Y + 52, r.Width - 48, 182);
        DrawCard(g, inner, 14, Color.FromArgb(62, 255, 255, 255), Color.FromArgb(112, 214, 216, 219), false);
        using var separator = new Pen(Color.FromArgb(110, 204, 208, 213));
        g.DrawLine(separator, inner.X + inner.Width / 3, inner.Y, inner.X + inner.Width / 3, inner.Bottom);
        g.DrawLine(separator, inner.X + inner.Width * 2 / 3, inner.Y, inner.X + inner.Width * 2 / 3, inner.Bottom);

        var col = inner.Width / 3;
        DrawText(g, "A. 播报方式", 15, FontStyle.Regular, new RectangleF(inner.X + 24, inner.Y + 10, col - 48, 38), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawPolicySummary(g, new RectangleF(inner.X + 24, inner.Y + 55, col - 48, 82),
            "按发言人归纳", "换人或短暂停顿后，忠实播报上一位玩家", SpatialPalette.Mint);

        var bx = inner.X + col + 36;
        DrawText(g, "B. 播报内容", 15, FontStyle.Regular, new RectangleF(bx, inner.Y + 10, col - 58, 38), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawCheckRow(g, new RectangleF(bx, inner.Y + 52, col - 60, 38), "队友聊天", true, "撤退、协助、视野等固定游戏信号");
        DrawCheckRow(g, new RectangleF(bx, inner.Y + 92, col - 60, 38), "战术信号", SignalVoice, "仅在玩家无发言时限频播报重要信号");
        DrawCheckRow(g, new RectangleF(bx, inner.Y + 132, col - 60, 38), "低优先级消息", LowPrioritySpeech, "开启后才播报回城、金币或技能状态");

        var cx = inner.X + col * 2 + 36;
        DrawText(g, "C. 表达方式", 15, FontStyle.Regular, new RectangleF(cx, inner.Y + 10, col - 58, 38), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawPolicySummary(g, new RectangleF(cx, inner.Y + 55, col - 58, 82),
            KeepEmotion ? "保留情绪摘要" : "克制摘要",
            KeepEmotion ? "保留焦急、提醒等语气，不复述辱骂" : "只播报战术含义，尽量省略情绪",
            SpatialPalette.Mint);
        DrawText(g, "可在设置页切换“保留玩家情绪”", 12.5f, FontStyle.Regular,
            new RectangleF(cx, inner.Y + 139, col - 58, 30), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);

        var sample = new RectangleF(r.X + 24, r.Bottom - 86, r.Width - 48, 64);
        DrawCard(g, sample, 13, Color.FromArgb(90, 251, 250, 253), Color.FromArgb(105, 214, 216, 220), false);
        DrawSpeaker(g, new PointF(sample.X + 38, sample.Y + 32), SpatialPalette.Violet);
        DrawText(g, "蕾欧娜提醒上路小心，并注意敌方打野。", 15, FontStyle.Regular,
            new RectangleF(sample.X + 70, sample.Y + 5, 700, sample.Height - 10), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var trySentence = new RectangleF(sample.Right - 205, sample.Y + 10, 185, 44);
        DrawOutlineButton(g, trySentence, "▶  试听这句话", SpatialPalette.BorderDark);
        AddHit(trySentence, SpatialAction.TestSpeech);
    }

    private void DrawSettingsPage(Graphics g)
    {
        using var sideLine = new Pen(Color.FromArgb(112, 210, 213, 217));
        g.DrawLine(sideLine, 102, 108, 102, 972);
        DrawText(g, "设置", 31, FontStyle.Bold, new RectangleF(150, 100, 280, 58), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "按你的使用习惯调整 RiftLens", 14.5f, FontStyle.Regular, new RectangleF(150, 153, 400, 36), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        DrawCloud(g, new PointF(1338, 118));
        DrawText(g, "设置会自动保存", 13, FontStyle.Regular, new RectangleF(1370, 96, 150, 44), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);

        var leftX = 150f;
        var leftW = 646f;
        var rightX = 816f;
        var rightW = 670f;
        DrawServiceSettings(g, new RectangleF(leftX, 205, leftW, 292));
        DrawRecognitionSettings(g, new RectangleF(rightX, 205, rightW, 292));
        DrawDisplaySettings(g, new RectangleF(leftX, 513, leftW, 285));
        DrawRuntimeSettings(g, new RectangleF(rightX, 513, rightW, 285));

        var defaults = new RectangleF(150, 817, 210, 49);
        DrawOutlineButton(g, defaults, "恢复默认设置", SpatialPalette.Mint);
        AddHit(defaults, SpatialAction.RestoreDefaults);
        var done = new RectangleF(1298, 817, 188, 49);
        DrawPillButton(g, done, "完成", true);
        AddHit(done, SpatialAction.SaveSettings);
        DrawBottomStatus(g, new RectangleF(124, 887, 1392, 93));
    }

    private void DrawServiceSettings(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawSparkles(g, new PointF(r.X + 42, r.Y + 43), SpatialPalette.Mint);
        DrawText(g, "翻译服务", 18, FontStyle.Bold, new RectangleF(r.X + 72, r.Y + 18, 220, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var rows = new[] { r.Y + 73, r.Y + 127, r.Y + 181 };
        DrawSettingDivider(g, r, rows[1]);
        DrawSettingDivider(g, r, rows[2]);
        DrawText(g, "服务商", 14, FontStyle.Regular, new RectangleF(r.X + 28, rows[0], 140, 45), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSelect(g, new RectangleF(r.Right - 230, rows[0] + 3, 198, 42), "DeepSeek");
        DrawText(g, "模型", 14, FontStyle.Regular, new RectangleF(r.X + 28, rows[1], 140, 45), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, ModelName, 14, FontStyle.Regular, new RectangleF(r.Right - 280, rows[1], 248, 45), SpatialPalette.Navy, ContentAlignment.MiddleRight);
        DrawText(g, "API 密钥", 14, FontStyle.Regular, new RectangleF(r.X + 28, rows[2], 140, 45), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, AiReady ? "已安全保存" : "尚未配置", 13, FontStyle.Bold,
            new RectangleF(r.Right - 292, rows[2], 130, 45), AiReady ? SpatialPalette.Mint : SpatialPalette.PeachDeep, ContentAlignment.MiddleRight);
        var manage = new RectangleF(r.Right - 148, rows[2] + 2, 116, 44);
        DrawOutlineButton(g, manage, "管理密钥", SpatialPalette.BorderDark);
        AddHit(manage, SpatialAction.ManageApiKey);
        DrawText(g, "密钥仅保存在 Windows 凭据管理器", 12.5f, FontStyle.Regular,
            new RectangleF(r.X + 28, r.Bottom - 54, r.Width - 56, 38), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
    }

    private void DrawRecognitionSettings(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawFilter(g, new PointF(r.X + 42, r.Y + 43), SpatialPalette.Mint);
        DrawText(g, "识别与过滤", 18, FontStyle.Bold, new RectangleF(r.X + 72, r.Y + 18, 240, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var y = r.Y + 72;
        DrawText(g, "系统消息过滤", 14, FontStyle.Regular, new RectangleF(r.X + 28, y, 190, 47), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var segmented = new RectangleF(r.Right - 330, y + 3, 300, 43);
        DrawSegmented(g, segmented, new[] { "宽松", "均衡", "严格" }, SystemFilterIndex, SpatialPalette.Mint);
        for (var i = 0; i < 3; i++)
            AddHit(new RectangleF(segmented.X + segmented.Width / 3 * i, segmented.Y, segmented.Width / 3, segmented.Height), SpatialAction.SystemFilterChanged, i);
        DrawSettingDivider(g, r, y + 50);

        DrawToggleSetting(g, r, y + 54, "仅在画面变化时识别", ChangeOnly, SpatialAction.ChangeOnlyChanged);
        DrawToggleSetting(g, r, y + 98, "播报战术信号", SignalVoice, SpatialAction.SignalVoiceChanged);
        DrawToggleSetting(g, r, y + 142, "保留玩家情绪", KeepEmotion, SpatialAction.KeepEmotionChanged);
        DrawText(g, "系统与击杀信息始终静音；宽松模式仅接收未分类文字", 12.5f, FontStyle.Regular,
            new RectangleF(r.X + 28, r.Bottom - 48, r.Width - 56, 34), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
    }

    private void DrawDisplaySettings(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawMonitor(g, new PointF(r.X + 42, r.Y + 43), SpatialPalette.Violet);
        DrawText(g, "屏显与悬浮", 18, FontStyle.Bold, new RectangleF(r.X + 72, r.Y + 18, 240, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var y = r.Y + 74;
        DrawText(g, "输出方式", 14, FontStyle.Regular, new RectangleF(r.X + 28, y, 150, 47), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var mode = new RectangleF(r.Right - 365, y + 3, 333, 43);
        DrawSegmented(g, mode, new[] { "仅屏显", "仅播报", "屏显+播报" }, OutputModeIndex, SpatialPalette.Violet);
        for (var i = 0; i < 3; i++)
            AddHit(new RectangleF(mode.X + mode.Width / 3 * i, mode.Y, mode.Width / 3, mode.Height), SpatialAction.OutputModeChanged, i);
        DrawSettingDivider(g, r, y + 51);
        DrawText(g, "显示位置", 14, FontStyle.Regular, new RectangleF(r.X + 28, y + 54, 150, 47), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSelect(g, new RectangleF(r.Right - 205, y + 57, 173, 43), "自动避让");
        DrawSettingDivider(g, r, y + 105);
        DrawText(g, "文字透明度", 14, FontStyle.Regular, new RectangleF(r.X + 28, y + 108, 160, 47), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawSlider(g, new RectangleF(r.X + 255, y + 124, 300, 15), OverlayOpacity, SpatialPalette.Violet);
        DrawText(g, OverlayOpacity + "%", 14, FontStyle.Regular, new RectangleF(r.Right - 86, y + 107, 54, 48), SpatialPalette.Navy, ContentAlignment.MiddleRight);
        DrawSettingDivider(g, r, y + 159);
        DrawToggleSetting(g, r, y + 163, "显示悬浮控制按钮", FloatingButton, SpatialAction.FloatingButtonChanged);
    }

    private void DrawRuntimeSettings(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawBolt(g, new PointF(r.X + 42, r.Y + 43), Color.FromArgb(245, 160, 43));
        DrawText(g, "运行方式", 18, FontStyle.Bold, new RectangleF(r.X + 72, r.Y + 18, 240, 50), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var y = r.Y + 75;
        DrawToggleSetting(g, r, y, "游戏中保持低占用", LowUsage, SpatialAction.LowUsageChanged);
        DrawToggleSetting(g, r, y + 44, "启动后最小化", MinimizeAfterStart, SpatialAction.MinimizeAfterStartChanged);
        DrawToggleSetting(g, r, y + 88, "开机自动启动", false, SpatialAction.Navigate);
        DrawToggleSetting(g, r, y + 132, "关闭窗口时缩小到托盘", CloseToTray, SpatialAction.CloseToTrayChanged);
        DrawSettingDivider(g, r, y + 180);
        DrawText(g, "OCR 模型", 14, FontStyle.Regular, new RectangleF(r.X + 28, y + 182, 140, 46), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, "韩文 + 英文 · 已内置", 14, FontStyle.Regular, new RectangleF(r.Right - 300, y + 182, 225, 46), SpatialPalette.Navy, ContentAlignment.MiddleRight);
        DrawChevron(g, new PointF(r.Right - 47, y + 205));
    }

    private void DrawBottomStatus(Graphics g, RectangleF r)
    {
        DrawCard(g, r, 18, Color.FromArgb(100, 255, 255, 255), SpatialPalette.Border, true);
        DrawStatusAvatar(g, new RectangleF(r.X + 20, r.Y + 13, 64, 64));
        DrawText(g, "晓晓 · 自然播报", 15, FontStyle.Bold, new RectangleF(r.X + 94, r.Y + 13, 185, 58), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawMiniWave(g, new PointF(r.X + 276, r.Y + 46), SpatialPalette.Mint);
        DrawVSeparator(g, r.X + 312, r.Y + 19, r.Bottom - 19);
        DrawSpeaker(g, new PointF(r.X + 346, r.Y + 46), SpatialPalette.Navy);
        DrawSlider(g, new RectangleF(r.X + 380, r.Y + 39, 235, 15), Volume, SpatialPalette.Mint);
        DrawText(g, Volume + "%", 14, FontStyle.Regular, new RectangleF(r.X + 625, r.Y + 18, 65, 56), SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        DrawVSeparator(g, r.X + 716, r.Y + 19, r.Bottom - 19);

        DrawSystemPill(g, new RectangleF(r.X + 742, r.Y + 19, 204, 55), "本地 OCR", OcrReady, 0);
        DrawSystemPill(g, new RectangleF(r.X + 978, r.Y + 19, 190, 55), "AI 在线", AiReady, 1);
        DrawVSeparator(g, r.X + 1194, r.Y + 19, r.Bottom - 19);
        DrawPerson(g, new PointF(r.X + 1244, r.Y + 46));
        DrawText(g, "@Nixz", 15, FontStyle.Regular, new RectangleF(r.X + 1273, r.Y + 15, 110, 62), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
    }

    private void LayoutApiInput()
    {
        if (Width <= 0 || Height <= 0) return;
        _apiKeyBox.Visible = Page == SpatialPage.Home;
        if (!_apiKeyBox.Visible) return;
        var sx = Width / DesignWidth;
        var sy = Height / DesignHeight;
        var logical = new RectangleF(982, 697, 326, 28);
        _apiKeyBox.Bounds = new Rectangle(
            (int)Math.Round(logical.X * sx),
            (int)Math.Round(logical.Y * sy),
            Math.Max(100, (int)Math.Round(logical.Width * sx)),
            Math.Max(20, (int)Math.Round(logical.Height * sy)));
        _apiKeyBox.Font = new Font("Microsoft YaHei UI", Math.Max(8.5f, 11f * Math.Min(sx, sy)), FontStyle.Regular, GraphicsUnit.Point);
    }

    private void SetVolumeFromPoint(PointF p)
    {
        var track = CommonVolumeRect();
        var next = (int)Math.Round(Math.Clamp((p.X - track.X) / track.Width, 0f, 1f) * 100);
        if (next == Volume) return;
        Volume = next;
        ActionRequested?.Invoke(SpatialAction.VolumeChanged, next);
        Invalidate();
    }

    private void SetOpacityFromPoint(PointF p)
    {
        const float start = 405f;
        const float width = 300f;
        var next = (int)Math.Round(20 + Math.Clamp((p.X - start) / width, 0f, 1f) * 70);
        if (next == OverlayOpacity) return;
        OverlayOpacity = next;
        ActionRequested?.Invoke(SpatialAction.OverlayOpacityChanged, next);
        Invalidate();
    }

    private RectangleF CommonVolumeRect()
    {
        return Page switch
        {
            SpatialPage.Voice => new RectangleF(500, 944, 235, 24),
            SpatialPage.Settings => new RectangleF(505, 924, 235, 24),
            _ => new RectangleF(500, 914, 235, 24)
        };
    }

    private PointF ToDesign(Point p) => new(
        Width <= 0 ? 0 : p.X * DesignWidth / Width,
        Height <= 0 ? 0 : p.Y * DesignHeight / Height);

    private void AddHit(RectangleF bounds, SpatialAction action, object? value = null, string? help = null) =>
        _hits.Add(new HitTarget(bounds, action, value, help));

    private static Image? LoadReference(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            using var temporary = Image.FromStream(stream);
            return new Bitmap(temporary);
        }
        catch
        {
            return null;
        }
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2);
        var arc = new RectangleF(bounds.X, bounds.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawCard(Graphics g, RectangleF r, float radius, Color fill, Color border, bool shadow)
    {
        if (r.Width <= 1 || r.Height <= 1) return;
        if (shadow)
        {
            for (var i = 7; i >= 1; i--)
            {
                var shadowRect = new RectangleF(r.X - i * .15f, r.Y + i * .55f, r.Width + i * .3f, r.Height + i * .25f);
                using var shadowPath = RoundedPath(shadowRect, radius + i * .2f);
                using var shadowBrush = new SolidBrush(Color.FromArgb(Math.Max(1, 2 + (7 - i)), 26, 55, 72));
                g.FillPath(shadowBrush, shadowPath);
            }
        }
        using var path = RoundedPath(r, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(border, 1f);
        g.DrawPath(pen, path);
        using var highlightPath = RoundedPath(RectangleF.Inflate(r, -1.5f, -1.5f), Math.Max(2, radius - 1));
        using var highlight = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
        g.DrawPath(highlight, highlightPath);
    }

    private static void DrawFeatheredImage(Graphics g, Image image, RectangleF destination, Rectangle source, Color edgeColor, float feather)
    {
        var state = g.Save();
        using var clip = RoundedPath(destination, Math.Min(72, Math.Min(destination.Width, destination.Height) / 4));
        g.SetClip(clip);
        g.DrawImage(image, destination, source, GraphicsUnit.Pixel);
        using var left = new LinearGradientBrush(
            new PointF(destination.X, destination.Y), new PointF(destination.X + feather, destination.Y),
            edgeColor, Color.FromArgb(0, edgeColor));
        using var right = new LinearGradientBrush(
            new PointF(destination.Right, destination.Y), new PointF(destination.Right - feather, destination.Y),
            edgeColor, Color.FromArgb(0, edgeColor));
        using var top = new LinearGradientBrush(
            new PointF(destination.X, destination.Y), new PointF(destination.X, destination.Y + feather),
            edgeColor, Color.FromArgb(0, edgeColor));
        using var bottom = new LinearGradientBrush(
            new PointF(destination.X, destination.Bottom), new PointF(destination.X, destination.Bottom - feather),
            edgeColor, Color.FromArgb(0, edgeColor));
        g.FillRectangle(left, destination.X, destination.Y, feather, destination.Height);
        g.FillRectangle(right, destination.Right - feather, destination.Y, feather, destination.Height);
        g.FillRectangle(top, destination.X, destination.Y, destination.Width, feather);
        g.FillRectangle(bottom, destination.X, destination.Bottom - feather, destination.Width, feather);
        g.Restore(state);
    }

    private static void DrawText(
        Graphics g,
        string text,
        float size,
        FontStyle style,
        RectangleF rect,
        Color color,
        ContentAlignment align,
        bool ellipsis = false,
        string family = "Microsoft YaHei UI")
    {
        using var font = new Font(family, size, style, GraphicsUnit.Point);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        };
        format.Alignment = align switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => StringAlignment.Center,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => StringAlignment.Far,
            _ => StringAlignment.Near
        };
        format.LineAlignment = align switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => StringAlignment.Center,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => StringAlignment.Far,
            _ => StringAlignment.Near
        };
        g.DrawString(text, font, brush, rect, format);
    }

    private static void DrawPillButton(Graphics g, RectangleF r, string text, bool dark)
    {
        using var brush = dark
            ? new LinearGradientBrush(r, Color.FromArgb(48, 64, 73), Color.FromArgb(20, 32, 39), 90f)
            : new LinearGradientBrush(r, Color.White, Color.FromArgb(245, 247, 248), 90f);
        using var path = RoundedPath(r, r.Height / 2);
        g.FillPath(brush, path);
        using var pen = new Pen(dark ? Color.FromArgb(70, 32, 49, 58) : SpatialPalette.BorderDark);
        g.DrawPath(pen, path);
        DrawText(g, text, 16, FontStyle.Bold, r, dark ? Color.White : SpatialPalette.Navy, ContentAlignment.MiddleCenter);
    }

    private static void DrawOutlineButton(Graphics g, RectangleF r, string text, Color color)
    {
        using var path = RoundedPath(r, Math.Min(14, r.Height / 2));
        using var brush = new SolidBrush(Color.FromArgb(58, 255, 255, 255));
        g.FillPath(brush, path);
        using var pen = new Pen(color, color == SpatialPalette.Mint ? 1.7f : 1f);
        g.DrawPath(pen, path);
        DrawText(g, text, 14.5f, FontStyle.Regular, r, color == SpatialPalette.BorderDark ? SpatialPalette.Navy : color, ContentAlignment.MiddleCenter);
    }

    private static void DrawStepNumber(Graphics g, string text, RectangleF r, Color color) =>
        DrawText(g, text, 76, FontStyle.Regular, r, color, ContentAlignment.MiddleCenter, false, "Segoe UI");

    private static void DrawDot(Graphics g, PointF center, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, center.X - 6, center.Y - 6, 12, 12);
    }

    private static void DrawStatusChip(Graphics g, RectangleF r, string text, bool ready)
    {
        using var path = RoundedPath(r, r.Height / 2);
        using var brush = new SolidBrush(ready ? Color.FromArgb(28, 16, 187, 124) : Color.FromArgb(32, 113, 125, 136));
        g.FillPath(brush, path);
        DrawText(g, text, 10.5f, FontStyle.Bold, r, ready ? SpatialPalette.MintDeep : SpatialPalette.TextMuted, ContentAlignment.MiddleCenter);
    }

    private static void DrawStatusPill(Graphics g, RectangleF r, string text, bool ready)
    {
        DrawCard(g, r, r.Height / 2, ready ? Color.FromArgb(50, 21, 191, 132) : Color.FromArgb(50, 224, 145, 96), ready ? Color.FromArgb(90, 21, 177, 121) : Color.FromArgb(90, 224, 145, 96), false);
        DrawDot(g, new PointF(r.X + 27, r.Y + r.Height / 2), ready ? SpatialPalette.Mint : SpatialPalette.PeachDeep);
        DrawText(g, text, 14, FontStyle.Regular, new RectangleF(r.X + 43, r.Y, r.Width - 52, r.Height), ready ? SpatialPalette.MintDeep : SpatialPalette.PeachDeep, ContentAlignment.MiddleLeft);
    }

    private static void DrawModeChip(Graphics g, RectangleF r)
    {
        DrawCard(g, r, r.Height / 2, Color.FromArgb(82, 255, 255, 255), Color.FromArgb(125, 213, 216, 220), false);
        DrawMonitor(g, new PointF(r.X + 28, r.Y + r.Height / 2), SpatialPalette.Navy);
        DrawText(g, "屏显 + 播报", 12.5f, FontStyle.Regular, new RectangleF(r.X + 48, r.Y, r.Width - 56, r.Height), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
    }

    private static void DrawSystemPill(Graphics g, RectangleF r, string text, bool ready, int icon)
    {
        DrawCard(g, r, r.Height / 2, Color.FromArgb(82, 255, 255, 255), Color.FromArgb(115, 213, 216, 220), false);
        if (icon == 0) DrawFocus(g, new PointF(r.X + 35, r.Y + r.Height / 2));
        else DrawCloud(g, new PointF(r.X + 35, r.Y + r.Height / 2));
        DrawText(g, text, 13.5f, FontStyle.Regular, new RectangleF(r.X + 58, r.Y, r.Width - 94, r.Height), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        using var brush = new SolidBrush(ready ? SpatialPalette.Mint : Color.FromArgb(179, 187, 194));
        g.FillEllipse(brush, r.Right - 37, r.Y + 17, 21, 21);
        if (ready)
        {
            using var pen = new Pen(Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[] { new PointF(r.Right - 31, r.Y + 27), new PointF(r.Right - 27, r.Y + 31), new PointF(r.Right - 21, r.Y + 23) });
        }
    }

    private static void DrawTag(Graphics g, RectangleF r, string text, Color color)
    {
        using var path = RoundedPath(r, 9);
        using var brush = new SolidBrush(Color.FromArgb(22, color));
        g.FillPath(brush, path);
        using var pen = new Pen(Color.FromArgb(72, color));
        g.DrawPath(pen, path);
        DrawText(g, text, 12, FontStyle.Regular, r, color, ContentAlignment.MiddleCenter);
    }

    private static void DrawSegmented(Graphics g, RectangleF r, string[] items, int selected, Color accent)
    {
        DrawCard(g, r, 13, Color.FromArgb(64, 255, 255, 255), Color.FromArgb(135, 207, 211, 216), false);
        var width = r.Width / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var segment = new RectangleF(r.X + i * width + 3, r.Y + 3, width - 6, r.Height - 6);
            if (i == selected)
            {
                using var path = RoundedPath(segment, 10);
                using var brush = new SolidBrush(Color.FromArgb(28, accent));
                g.FillPath(brush, path);
            }
            DrawText(g, items[i], 12.5f, i == selected ? FontStyle.Bold : FontStyle.Regular, segment,
                i == selected ? accent : SpatialPalette.Navy, ContentAlignment.MiddleCenter);
        }
    }

    private static void DrawSelect(Graphics g, RectangleF r, string text)
    {
        DrawCard(g, r, 13, Color.FromArgb(72, 255, 255, 255), Color.FromArgb(145, 207, 211, 216), false);
        DrawText(g, text, 13.5f, FontStyle.Regular, new RectangleF(r.X + 18, r.Y, r.Width - 50, r.Height), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawChevron(g, new PointF(r.Right - 25, r.Y + r.Height / 2));
    }

    private static void DrawSlider(Graphics g, RectangleF r, int value, Color accent)
    {
        var y = r.Y + r.Height / 2;
        using var background = new Pen(Color.FromArgb(190, 211, 213, 216), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(background, r.X, y, r.Right, y);
        var x = r.X + r.Width * Math.Clamp(value, 0, 100) / 100f;
        using var active = new Pen(accent, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(active, r.X, y, x, y);
        using var shadow = new SolidBrush(Color.FromArgb(18, 17, 36, 48));
        g.FillEllipse(shadow, x - 12, y - 10, 24, 24);
        using var knob = new SolidBrush(Color.FromArgb(252, 252, 252));
        g.FillEllipse(knob, x - 10, y - 10, 20, 20);
        using var ring = new Pen(Color.FromArgb(170, 205, 209, 214));
        g.DrawEllipse(ring, x - 10, y - 10, 20, 20);
    }

    private static void DrawToggle(Graphics g, RectangleF r, bool on)
    {
        using var path = RoundedPath(r, r.Height / 2);
        using var brush = new SolidBrush(on ? SpatialPalette.Mint : Color.FromArgb(195, 198, 202, 207));
        g.FillPath(brush, path);
        var cx = on ? r.Right - r.Height / 2 : r.X + r.Height / 2;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, cx - r.Height * .38f, r.Y + r.Height * .12f, r.Height * .76f, r.Height * .76f);
    }

    private void DrawToggleSetting(Graphics g, RectangleF card, float y, string text, bool on, SpatialAction action)
    {
        DrawText(g, text, 14, FontStyle.Regular, new RectangleF(card.X + 28, y, 380, 42), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        var toggle = new RectangleF(card.Right - 84, y + 6, 54, 30);
        DrawToggle(g, toggle, on);
        if (action != SpatialAction.Navigate)
            AddHit(toggle, action, !on);
    }

    private static void DrawCheckbox(Graphics g, RectangleF r, bool check)
    {
        using var path = RoundedPath(r, 5);
        using var brush = new SolidBrush(check ? SpatialPalette.Mint : Color.FromArgb(60, 255, 255, 255));
        g.FillPath(brush, path);
        using var pen = new Pen(check ? SpatialPalette.Mint : Color.FromArgb(170, 184, 192, 199), 1.2f);
        g.DrawPath(pen, path);
        if (!check) return;
        using var tick = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(tick, new[] { new PointF(r.X + 5, r.Y + 12), new PointF(r.X + 10, r.Y + 17), new PointF(r.X + 19, r.Y + 7) });
    }

    private void DrawCheckRow(Graphics g, RectangleF r, string text, bool check, string help)
    {
        DrawCheckbox(g, new RectangleF(r.X, r.Y + 7, 22, 22), check);
        DrawText(g, text, 13.5f, FontStyle.Regular, new RectangleF(r.X + 34, r.Y, r.Width - 80, r.Height), check ? SpatialPalette.Navy : SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
        var q = new RectangleF(r.Right - 32, r.Y + 7, 22, 22);
        using var pen = new Pen(SpatialPalette.BlueMuted, 1.2f);
        g.DrawEllipse(pen, q);
        DrawText(g, "?", 10.5f, FontStyle.Bold, q, SpatialPalette.BlueMuted, ContentAlignment.MiddleCenter);
        AddHit(q, SpatialAction.Navigate, null, help);
    }

    private static void DrawPolicySummary(Graphics g, RectangleF r, string title, string detail, Color accent)
    {
        DrawCard(g, r, 13, Color.FromArgb(72, 255, 255, 255), Color.FromArgb(96, 196, 205, 211), false);
        using var dot = new SolidBrush(accent);
        g.FillEllipse(dot, r.X + 17, r.Y + 19, 10, 10);
        DrawText(g, title, 14, FontStyle.Bold, new RectangleF(r.X + 39, r.Y + 9, r.Width - 52, 31),
            SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, detail, 11.8f, FontStyle.Regular, new RectangleF(r.X + 39, r.Y + 37, r.Width - 52, 35),
            SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
    }

    private static void DrawSettingDivider(Graphics g, RectangleF card, float y)
    {
        using var pen = new Pen(Color.FromArgb(105, 207, 211, 216));
        g.DrawLine(pen, card.X + 28, y, card.Right - 28, y);
    }

    private static void DrawVSeparator(Graphics g, float x, float y1, float y2)
    {
        using var pen = new Pen(Color.FromArgb(105, 207, 211, 216));
        g.DrawLine(pen, x, y1, x, y2);
    }

    private static void DrawBrandMark(Graphics g, RectangleF r)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shadow = new SolidBrush(Color.FromArgb(20, 40, 70, 90));
        g.FillEllipse(shadow, r.X + 1, r.Y + 3, r.Width, r.Height);
        using var path = new GraphicsPath();
        path.AddEllipse(r);
        using var gradient = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(247, 251, 251),
            SurroundColors = new[] { Color.FromArgb(58, 216, 209), Color.FromArgb(116, 102, 240), Color.FromArgb(246, 119, 188), Color.FromArgb(58, 216, 209) }
        };
        g.FillEllipse(gradient, r);
        using var white = new SolidBrush(Color.FromArgb(220, 250, 251, 251));
        g.FillEllipse(white, r.X + r.Width * .28f, r.Y + r.Height * .28f, r.Width * .44f, r.Height * .44f);
        g.Restore(state);
    }

    private static void DrawWindowGlyph(Graphics g, RectangleF r, string glyph)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;
        if (glyph == "min") g.DrawLine(pen, cx - 7, cy, cx + 7, cy);
        else if (glyph == "max") g.DrawRectangle(pen, cx - 6, cy - 7, 12, 14);
        else
        {
            g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
            g.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
        }
    }

    private static void DrawNavIcon(Graphics g, PointF c, int icon, bool selected)
    {
        var color = SpatialPalette.Navy;
        using var pen = new Pen(color, selected ? 2.2f : 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (icon)
        {
            case 0:
                g.DrawEllipse(pen, c.X - 13, c.Y - 13, 26, 26);
                g.DrawEllipse(pen, c.X - 4, c.Y - 4, 8, 8);
                g.DrawArc(pen, c.X - 8, c.Y - 8, 16, 16, 210, 120);
                break;
            case 1:
                using (var path = RoundedPath(new RectangleF(c.X - 15, c.Y - 13, 30, 24), 10)) g.DrawPath(pen, path);
                g.DrawLines(pen, new[] { new PointF(c.X - 7, c.Y + 9), new PointF(c.X - 11, c.Y + 15), new PointF(c.X - 2, c.Y + 11) });
                g.FillEllipse(new SolidBrush(color), c.X - 8, c.Y - 2, 3, 3);
                g.FillEllipse(new SolidBrush(color), c.X - 1.5f, c.Y - 2, 3, 3);
                g.FillEllipse(new SolidBrush(color), c.X + 5, c.Y - 2, 3, 3);
                break;
            case 2:
                var heights = new[] { 10, 21, 31, 22, 12 };
                for (var i = 0; i < heights.Length; i++)
                    g.DrawLine(pen, c.X - 12 + i * 6, c.Y - heights[i] / 2f, c.X - 12 + i * 6, c.Y + heights[i] / 2f);
                break;
            case 3:
                var pts = Enumerable.Range(0, 6).Select(i =>
                {
                    var a = Math.PI / 3 * i;
                    return new PointF(c.X + (float)Math.Cos(a) * 15, c.Y + (float)Math.Sin(a) * 15);
                }).ToArray();
                g.DrawPolygon(pen, pts);
                g.DrawEllipse(pen, c.X - 5, c.Y - 5, 10, 10);
                break;
            case 4:
                g.DrawArc(pen, c.X - 13, c.Y - 13, 26, 26, 35, 235);
                g.DrawLines(pen, new[] { new PointF(c.X + 11, c.Y - 8), new PointF(c.X + 13, c.Y + 1), new PointF(c.X + 5, c.Y - 2) });
                break;
            default:
                g.DrawEllipse(pen, c.X - 13, c.Y - 13, 26, 26);
                DrawText(g, "i", 13, FontStyle.Bold, new RectangleF(c.X - 13, c.Y - 13, 26, 26), color, ContentAlignment.MiddleCenter, false, "Segoe UI");
                break;
        }
    }

    private static void DrawLensFallback(Graphics g, RectangleF r)
    {
        for (var i = 8; i >= 0; i--)
        {
            var inner = RectangleF.Inflate(r, -i * 8, -i * 8);
            using var brush = new SolidBrush(Color.FromArgb(8 + i * 2, i % 2 == 0 ? 68 : 244, i % 2 == 0 ? 211 : 189, i % 2 == 0 ? 181 : 165));
            g.FillEllipse(brush, inner);
            using var pen = new Pen(Color.FromArgb(55, 255, 255, 255), 2f);
            g.DrawEllipse(pen, inner);
        }
        using var baseBrush = new LinearGradientBrush(new RectangleF(r.X + 10, r.Bottom - 20, r.Width - 20, 35), Color.FromArgb(85, 64, 210, 166), Color.FromArgb(62, 255, 191, 149), 0f);
        g.FillEllipse(baseBrush, r.X + 10, r.Bottom - 20, r.Width - 20, 35);
    }

    private static void DrawAvatarFallback(Graphics g, RectangleF r)
    {
        using var bg = new LinearGradientBrush(r, Color.FromArgb(231, 246, 237), Color.FromArgb(246, 222, 207), 50f);
        g.FillEllipse(bg, r);
        using var hair = new SolidBrush(Color.FromArgb(91, 62, 50));
        g.FillEllipse(hair, r.X + r.Width * .25f, r.Y + r.Height * .17f, r.Width * .5f, r.Height * .67f);
        using var skin = new SolidBrush(Color.FromArgb(245, 203, 171));
        g.FillEllipse(skin, r.X + r.Width * .34f, r.Y + r.Height * .24f, r.Width * .34f, r.Height * .42f);
    }

    private void DrawStatusAvatar(Graphics g, RectangleF r)
    {
        var state = g.Save();
        using var clip = RoundedPath(r, r.Width / 2);
        g.SetClip(clip);
        if (_voiceReference is not null)
            g.DrawImage(_voiceReference, r, new Rectangle(166, 230, 190, 192), GraphicsUnit.Pixel);
        else DrawAvatarFallback(g, r);
        g.Restore(state);
        using var ring = new Pen(Color.FromArgb(180, 255, 255, 255), 3f);
        g.DrawEllipse(ring, r);
    }

    private static void DrawCornerGuides(Graphics g, RectangleF r)
    {
        using var pen = new Pen(SpatialPalette.Mint, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        const float l = 18;
        g.DrawLines(pen, new[] { new PointF(r.X, r.Y + l), new PointF(r.X, r.Y), new PointF(r.X + l, r.Y) });
        g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Y + l) });
        g.DrawLines(pen, new[] { new PointF(r.X, r.Bottom - l), new PointF(r.X, r.Bottom), new PointF(r.X + l, r.Bottom) });
        g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - l) });
    }

    private static void DrawChatPreview(Graphics g, RectangleF r)
    {
        DrawDot(g, new PointF(r.X + 30, r.Y + 82), SpatialPalette.Violet);
        DrawDot(g, new PointF(r.X + 30, r.Y + 126), SpatialPalette.Mint);
        using var line = new Pen(Color.FromArgb(195, 213, 212, 214), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, r.X + 58, r.Y + 76, r.X + 132, r.Y + 76);
        g.DrawLine(line, r.X + 58, r.Y + 94, r.X + 262, r.Y + 94);
        g.DrawLine(line, r.X + 58, r.Y + 120, r.X + 132, r.Y + 120);
        g.DrawLine(line, r.X + 58, r.Y + 138, r.X + 235, r.Y + 138);
        using var bubble = new Pen(SpatialPalette.Mint, 2f);
        using var path = RoundedPath(new RectangleF(r.Right / 2 + 15, r.Y + 20, 36, 29), 7);
        g.DrawPath(bubble, path);
    }

    private static void DrawMetric(Graphics g, RectangleF r, int icon, string value, string label)
    {
        DrawCard(g, r, 14, Color.FromArgb(76, 255, 255, 255), Color.FromArgb(120, 211, 214, 218), false);
        if (icon == 1) DrawNavIcon(g, new PointF(r.X + 40, r.Y + r.Height / 2), 1, false);
        else if (icon == 2) DrawSpeaker(g, new PointF(r.X + 40, r.Y + r.Height / 2), SpatialPalette.Navy);
        else DrawFilter(g, new PointF(r.X + 40, r.Y + r.Height / 2), SpatialPalette.Navy);
        DrawText(g, value, 19, FontStyle.Regular, new RectangleF(r.X + 70, r.Y, 42, r.Height), SpatialPalette.Navy, ContentAlignment.MiddleLeft);
        DrawText(g, label, 13, FontStyle.Regular, new RectangleF(r.X + 106, r.Y, r.Width - 112, r.Height), SpatialPalette.BlueMuted, ContentAlignment.MiddleLeft);
    }

    private void DrawHistoryScrollbar(Graphics g, RectangleF r)
    {
        using var track = new SolidBrush(Color.FromArgb(70, 207, 211, 216));
        g.FillRectangle(track, r);
        var content = Math.Max(1, _history.Count * 72);
        var viewport = 410f;
        var thumbHeight = Math.Max(44, r.Height * Math.Min(1f, viewport / content));
        var maxScroll = Math.Max(1, content - viewport);
        var y = r.Y + (r.Height - thumbHeight) * Math.Clamp(_historyScroll / maxScroll, 0f, 1f);
        using var thumb = new SolidBrush(Color.FromArgb(175, 151, 160, 168));
        using var path = RoundedPath(new RectangleF(r.X - 1, y, r.Width + 2, thumbHeight), r.Width);
        g.FillPath(thumb, path);
    }

    private static void DrawSearch(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.BlueMuted, 1.8f) { StartCap = LineCap.Round };
        g.DrawEllipse(pen, c.X - 10, c.Y - 10, 18, 18);
        g.DrawLine(pen, c.X + 4, c.Y + 5, c.X + 12, c.Y + 13);
    }

    private static void DrawSpeaker(Graphics g, PointF c, Color color)
    {
        using var pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawPolygon(pen, new[] { new PointF(c.X - 10, c.Y - 5), new PointF(c.X - 4, c.Y - 5), new PointF(c.X + 3, c.Y - 11), new PointF(c.X + 3, c.Y + 11), new PointF(c.X - 4, c.Y + 5), new PointF(c.X - 10, c.Y + 5) });
        g.DrawArc(pen, c.X - 1, c.Y - 9, 15, 18, -55, 110);
    }

    private static void DrawMiniWave(Graphics g, PointF c, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var h = new[] { 7, 15, 25, 16, 8 };
        for (var i = 0; i < h.Length; i++) g.DrawLine(pen, c.X - 12 + i * 6, c.Y - h[i] / 2f, c.X - 12 + i * 6, c.Y + h[i] / 2f);
    }

    private static void DrawShield(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.BlueMuted, 1.6f) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, new[] { new PointF(c.X, c.Y - 11), new PointF(c.X + 8, c.Y - 7), new PointF(c.X + 7, c.Y + 4), new PointF(c.X, c.Y + 11), new PointF(c.X - 7, c.Y + 4), new PointF(c.X - 8, c.Y - 7) });
        g.DrawLines(pen, new[] { new PointF(c.X - 3, c.Y), new PointF(c.X, c.Y + 3), new PointF(c.X + 5, c.Y - 4) });
    }

    private static void DrawEye(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.BlueMuted, 1.7f);
        g.DrawArc(pen, c.X - 11, c.Y - 6, 22, 12, 200, 140);
        g.DrawArc(pen, c.X - 11, c.Y - 6, 22, 12, 20, 140);
        g.DrawEllipse(pen, c.X - 3, c.Y - 3, 6, 6);
    }

    private static void DrawTrash(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.6f) { StartCap = LineCap.Round };
        g.DrawRectangle(pen, c.X - 7, c.Y - 7, 14, 17);
        g.DrawLine(pen, c.X - 10, c.Y - 10, c.X + 10, c.Y - 10);
        g.DrawLine(pen, c.X - 4, c.Y - 13, c.X + 4, c.Y - 13);
    }

    private static void DrawGauge(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.7f) { StartCap = LineCap.Round };
        g.DrawArc(pen, c.X - 12, c.Y - 10, 24, 24, 180, 180);
        g.DrawLine(pen, c.X, c.Y + 2, c.X + 7, c.Y - 5);
    }

    private static void DrawMonitor(Graphics g, PointF c, Color? color = null)
    {
        using var pen = new Pen(color ?? SpatialPalette.Navy, 1.7f) { StartCap = LineCap.Round };
        g.DrawRectangle(pen, c.X - 11, c.Y - 9, 22, 15);
        g.DrawLine(pen, c.X, c.Y + 6, c.X, c.Y + 11);
        g.DrawLine(pen, c.X - 6, c.Y + 11, c.X + 6, c.Y + 11);
    }

    private static void DrawSparkles(Graphics g, PointF c, Color color)
    {
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, c.X, c.Y - 12, c.X, c.Y + 12);
        g.DrawLine(pen, c.X - 7, c.Y, c.X + 7, c.Y);
        g.DrawLine(pen, c.X + 12, c.Y - 11, c.X + 12, c.Y - 3);
        g.DrawLine(pen, c.X + 8, c.Y - 7, c.X + 16, c.Y - 7);
    }

    private static void DrawFilter(Graphics g, PointF c, Color color)
    {
        using var pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, new[] { new PointF(c.X - 13, c.Y - 11), new PointF(c.X + 13, c.Y - 11), new PointF(c.X + 4, c.Y), new PointF(c.X + 4, c.Y + 11), new PointF(c.X - 4, c.Y + 8), new PointF(c.X - 4, c.Y) });
    }

    private static void DrawBolt(Graphics g, PointF c, Color color)
    {
        using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(c.X + 2, c.Y - 14), new PointF(c.X - 10, c.Y + 2), new PointF(c.X - 1, c.Y + 2), new PointF(c.X - 5, c.Y + 15), new PointF(c.X + 12, c.Y - 5), new PointF(c.X + 3, c.Y - 5), new PointF(c.X + 2, c.Y - 14) });
    }

    private static void DrawCloud(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.BlueMuted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, c.X - 12, c.Y - 4, 13, 12, 100, 180);
        g.DrawArc(pen, c.X - 3, c.Y - 10, 16, 16, 170, 190);
        g.DrawArc(pen, c.X + 5, c.Y - 4, 12, 12, 270, 180);
        g.DrawLine(pen, c.X - 9, c.Y + 7, c.X + 11, c.Y + 7);
    }

    private static void DrawFocus(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.7f) { StartCap = LineCap.Round };
        const float a = 10;
        const float b = 4;
        g.DrawLine(pen, c.X - a, c.Y - b, c.X - a, c.Y - a);
        g.DrawLine(pen, c.X - a, c.Y - a, c.X - b, c.Y - a);
        g.DrawLine(pen, c.X + a, c.Y - b, c.X + a, c.Y - a);
        g.DrawLine(pen, c.X + a, c.Y - a, c.X + b, c.Y - a);
        g.DrawLine(pen, c.X - a, c.Y + b, c.X - a, c.Y + a);
        g.DrawLine(pen, c.X - a, c.Y + a, c.X - b, c.Y + a);
        g.DrawLine(pen, c.X + a, c.Y + b, c.X + a, c.Y + a);
        g.DrawLine(pen, c.X + a, c.Y + a, c.X + b, c.Y + a);
        g.DrawEllipse(pen, c.X - 3, c.Y - 3, 6, 6);
    }

    private static void DrawPerson(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.7f);
        g.DrawEllipse(pen, c.X - 6, c.Y - 12, 12, 12);
        g.DrawArc(pen, c.X - 13, c.Y + 1, 26, 22, 180, 180);
    }

    private static void DrawChevron(Graphics g, PointF c)
    {
        using var pen = new Pen(SpatialPalette.Navy, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[] { new PointF(c.X - 6, c.Y - 3), new PointF(c.X, c.Y + 4), new PointF(c.X + 6, c.Y - 3) });
    }

    private sealed record HitTarget(RectangleF Bounds, SpatialAction Action, object? Value, string? HelpText);
}

internal static class SpatialPalette
{
    public static readonly Color WarmWhite = Color.FromArgb(249, 248, 247);
    public static readonly Color Navy = Color.FromArgb(15, 45, 68);
    public static readonly Color BlueMuted = Color.FromArgb(73, 106, 130);
    public static readonly Color TextMuted = Color.FromArgb(119, 130, 141);
    public static readonly Color Border = Color.FromArgb(142, 216, 219, 222);
    public static readonly Color BorderDark = Color.FromArgb(105, 126, 143);
    public static readonly Color Mint = Color.FromArgb(15, 188, 126);
    public static readonly Color MintDeep = Color.FromArgb(11, 164, 108);
    public static readonly Color Violet = Color.FromArgb(121, 106, 248);
    public static readonly Color Peach = Color.FromArgb(242, 174, 137);
    public static readonly Color PeachDeep = Color.FromArgb(211, 126, 85);
}
