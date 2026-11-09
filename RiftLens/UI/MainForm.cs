using System.Runtime.InteropServices;
using System.Text.Json;
using RiftLens.Models;
using RiftLens.Services;

namespace RiftLens.UI;

public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore = new();
    private readonly TranslationOverlayForm _overlay = new();
    private readonly OverlayControlForm _overlayControl = new();
    private readonly NaturalSpeechService _speech = new();
    private readonly DiagnosticSessionRecorder _diagnostics = new();
    private RealEvaluationSessionRecorder? _evaluationSession;
    private readonly System.Windows.Forms.Timer _volumeSaveTimer = new() { Interval = 500 };
    private readonly NotifyIcon _trayIcon;
    private readonly WebWorkspace _workspace;

    private AppSettings _settings = new();
    private HeroDictionary? _heroes;
    private Task<HeroDictionary>? _heroLoadTask;
    private Task? _ocrWarmupTask;
    private CaptureCoordinator? _coordinator;
    private bool _exiting;
    private bool _shutdownComplete;
    private Task? _shutdownTask;
    private Task? _stopListeningTask;
    private bool _ocrReady;
    private bool _ocrInitializing;
    private string? _ocrError;
    private readonly SemaphoreSlim _historySaveLock = new(1, 1);
    private string? _historySavePath;

    private readonly Panel _contentHost = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
    private readonly SidebarNavButton _homeNav = new(SidebarGlyph.Home, "主界面");
    private readonly SidebarNavButton _historyNav = new(SidebarGlyph.History, "翻译记录");
    private readonly SidebarNavButton _outputNav = new(SidebarGlyph.Voice, "语音播报");
    private readonly SidebarNavButton _advancedNav = new(SidebarGlyph.Settings, "设置");
    private readonly FlatWindowButton _minimizeWindow = new("—", "最小化");
    private readonly FlatWindowButton _maximizeWindow = new("□", "最大化");
    private readonly FlatWindowButton _closeWindow = new("✕", "关闭") { IsCloseButton = true };
    private string _currentPage = "home";

    private readonly StatusBadge _appBadge = new();
    private readonly Label _headerStatus = MutedLabel("正在读取配置…");
    private readonly Label _heroTitle = Heading("准备开始屏幕翻译", 24);
    private readonly Label _heroDescription = MutedLabel("框选文字区域并连接 AI 后，点击右侧按钮即可。运行期间不会抢占游戏焦点。");
    private readonly UiButton _primaryAction = PrimaryButton("检查配置", 176, 48);
    private readonly Label _primaryHint = MutedLabel("首次识别只建立聊天基线，不会播报旧消息");

    private readonly StatusBadge _regionBadge = new();
    private readonly StatusBadge _ocrBadge = new();
    private readonly StatusBadge _apiBadge = new();
    private readonly Label _regionSummary = MutedLabel("尚未框选需要识别的文字区域");
    private readonly Label _outputRegionSummary = MutedLabel("尚未框选需要识别的文字区域");
    private readonly Label _displayRegionSummary = MutedLabel("不设置时会自动选择上、下、左、右");
    private readonly Label _ocrSummary = MutedLabel("韩语与英语模型已随应用内置");
    private readonly Label _apiSummary = MutedLabel("尚未保存 AI 服务密钥");
    private readonly UiButton _regionAction = SecondaryButton("框选区域", 104);
    private readonly UiButton _ocrAction = SecondaryButton("无需安装", 104);
    private readonly UiButton _apiAction = SecondaryButton("配置 AI", 104);
    private readonly Label _modeSummary = BodyLabel("文字悬浮 + 中文语音");
    private readonly Label _placementSummary = MutedLabel("自动选择显示方位");
    private readonly Label _recentOriginal = MutedLabel("雷欧娜: 탑 조심");
    private readonly Label _recentTranslation = Heading("蕾欧娜：上路小心", 16);
    private readonly Label _recentMeta = MutedLabel("刚刚 · 已播报");
    private readonly ThinVolumeSlider _homeVolumeSlider = new();
    private readonly Label _homeVolumeValue = BodyLabel("65%", FontStyle.Bold);
    private readonly FlowLayoutPanel _historyList = new()
    {
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        BackColor = UiTheme.Canvas,
        Margin = Padding.Empty
    };

    private readonly TextBox _apiBaseUrl = TextInput();
    private readonly TextBox _apiModel = TextInput();
    private readonly TextBox _apiKey = TextInput();
    private readonly ComboBox _outputMode = SelectInput();
    private readonly ComboBox _placement = SelectInput();
    private readonly ComboBox _presetSelector = SelectInput();
    private readonly ComboBox _speechFilter = SelectInput();
    private readonly NumericUpDown _pollInterval = NumberInput(250, 3000, 500, 50);
    private readonly NumericUpDown _opacity = NumberInput(20, 90, 68, 1);
    private readonly NumericUpDown _fontSize = NumberInput(12, 28, 16, 1);
    private readonly NumericUpDown _duration = NumberInput(1500, 15000, 5200, 100);
    private readonly NumericUpDown _speechVolume = NumberInput(0, 100, 65, 5);
    private readonly NumericUpDown _speechRate = NumberInput(-5, 5, 1, 1);
    private readonly NumericUpDown _summaryWindow = NumberInput(200, 3000, 1200, 100);
    private readonly NumericUpDown _ocrScale = NumberInput(3, 4, 3, 1);
    private readonly CheckBox _showOriginal = CheckInput("在译文下方显示 OCR 原文");
    private readonly CheckBox _minimizeAfterStart = CheckInput("开始后隐藏到系统托盘");
    private readonly CheckBox _tacticalSignals = CheckInput("战术信号（撤退、支援、危险）");
    private readonly CheckBox _killEvents = CheckInput("击杀播报");
    private readonly CheckBox _objectiveEvents = CheckInput("地图资源与建筑提醒");
    private readonly CheckBox _clientReminders = CheckInput("防沉迷与客户端提醒");
    private readonly CheckBox _otherSystemEvents = CheckInput("对局流程与系统通知");
    private readonly TextBox _activity = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.TextMuted,
        Font = UiTheme.Font(9),
        Dock = DockStyle.Fill
    };

    public MainForm(SettingsStore? settingsStore = null, string? webViewUserDataFolder = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _workspace = new WebWorkspace(webViewUserDataFolder);
        Text = "RiftLens · 韩服屏幕翻译";
        FormBorderStyle = FormBorderStyle.None;
        Padding = Padding.Empty;
        MinimumSize = new Size(1040, 680);
        Size = new Size(1240, 810);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(239, 237, 236);
        Font = UiTheme.Font(9);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = CreateBrandIcon();

        PopulateOptions();
        _apiKey.UseSystemPasswordChar = true;
        _ocrAction.Enabled = false;
        _minimizeAfterStart.Checked = true;
        _speech.StatusChanged += message => SafeUi(() => SetStatus(message));
        _trayIcon = CreateTrayIcon();

        _overlay.TranslationPositioned += bounds => _overlayControl.ShowNear(bounds);
        _overlayControl.TogglePauseRequested += TogglePause;
        _overlayControl.VolumeChanged += volume => ApplySpeechVolume(volume, persist: true);
        _overlayControl.ShowMainWindowRequested += ShowMainWindow;
        _overlayControl.StopRequested += async () => await StopListeningAsync();
        _overlayControl.PositionChanged += point =>
        {
            _settings.FloatingControlX = point.X;
            _settings.FloatingControlY = point.Y;
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
        };
        _volumeSaveTimer.Tick += async (_, _) =>
        {
            _volumeSaveTimer.Stop();
            try
            {
                await _settingsStore.SaveAsync(_settings);
            }
            catch (Exception exception)
            {
                SetStatus(L("保存音量失败：", "Failed to save volume: ") + exception.Message);
            }
        };

        Controls.Add(BuildShell());
        WireEvents();
        _minimizeWindow.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _maximizeWindow.Click += (_, _) => ToggleMaximize();
        _closeWindow.Click += (_, _) => Close();
        HandleCreated += (_, _) => ApplyWindowCorners();
        Resize += (_, _) => UpdateWindowChrome();
        Shown += async (_, _) =>
        {
            FitToWorkingArea();
            await LoadSettingsAsync();
        };
        FormClosing += OnFormClosing;
    }

    private Control BuildShell()
    {
        _workspace.ActionRequested += async (action, value) => await HandleSpatialActionAsync(action, value);
        _workspace.Dock = DockStyle.Fill;
        return _workspace;
    }

    private async Task HandleSpatialActionAsync(SpatialAction action, object? value)
    {
        switch (action)
        {
            case SpatialAction.Navigate:
                if (value is SpatialPage page)
                {
                    ShowPage(page switch
                    {
                        SpatialPage.History => "history",
                        SpatialPage.Voice => "output",
                        SpatialPage.Settings => "advanced",
                        _ => "home"
                    });
                }
                break;
            case SpatialAction.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case SpatialAction.Maximize:
                ToggleMaximize();
                break;
            case SpatialAction.Close:
                if (_settings.CloseToTray)
                {
                    HideToTray();
                }
                else
                {
                    Close();
                }
                break;
            case SpatialAction.BeginWindowDrag:
                if (WindowState == FormWindowState.Normal)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
                }
                break;
            case SpatialAction.TogglePrimary:
                await HandlePrimaryActionAsync();
                break;
            case SpatialAction.SelectRegion:
                await SelectRegionAsync(false);
                break;
            case SpatialAction.SelectDisplayRegion:
                await SelectRegionAsync(true);
                break;
            case SpatialAction.TestOcr:
                await TestOcrAsync();
                break;
            case SpatialAction.SaveApiKey:
                _apiKey.Text = _workspace.ApiKeyText;
                await SaveApiKeyAsync();
                break;
            case SpatialAction.ShowApiHelp:
                SetStatus(L(
                    "在 DeepSeek 开放平台创建 API Key，粘贴到输入框后点击“保存并连接”。密钥只会写入 Windows 凭据管理器。",
                    "Create an API key on the DeepSeek platform, paste it here, then select Save & connect. It is stored only in Windows Credential Manager."));
                break;
            case SpatialAction.ClearHistory:
                _workspace.ClearHistory();
                _diagnostics.Reset();
                SetStatus(L("本次会话的翻译记录已清空。", "Translation history for this session was cleared."));
                break;
            case SpatialAction.ExportDiagnostics:
                await ExportDiagnosticsAsync();
                break;
            case SpatialAction.ToggleEvaluationRecording:
                await ToggleEvaluationRecordingAsync();
                break;
            case SpatialAction.TestSpeech:
                TestSpeech();
                break;
            case SpatialAction.PreviewOverlay:
                await PreviewOverlayAsync();
                break;
            case SpatialAction.RestoreDefaults:
                var capture = _settings.CaptureRegion;
                var display = _settings.DisplayRegion;
                _settings = new AppSettings
                {
                    CaptureRegion = capture,
                    DisplayRegion = display,
                    OnboardingCompleted = _settings.OnboardingCompleted,
                    InterfacePalette = _settings.InterfacePalette,
                    CustomThemeColor = _settings.CustomThemeColor
                };
                ApplyNativeLanguage();
                ApplySettingsToControls();
                await _settingsStore.SaveAsync(_settings);
                SetStatus(L("已恢复默认设置，并保留当前框选区域。", "Defaults restored. Your selected regions were kept."));
                break;
            case SpatialAction.SaveSettings:
                await SaveSettingsWithFeedbackAsync();
                ShowPage("home");
                break;
            case SpatialAction.ManageApiKey:
                ShowPage("home");
                _workspace.FocusApiKey();
                break;
            case SpatialAction.CheckUpdate:
                SetStatus(L(
                    "当前为本地开发版；正式发布后会在这里连接更新通道。",
                    "This is a local development build. The update channel will be connected for public releases."));
                break;
            case SpatialAction.ShowAbout:
                SetStatus(L(
                    "RiftLens · 本地 OCR、韩服聊天翻译与晓晓自然播报 · 开发者 @Nixz",
                    "RiftLens · Local OCR, Korean-server chat translation, and natural neural speech · Developer @Nixz"));
                break;
            case SpatialAction.VolumeChanged when value is int volume:
                ApplySpeechVolume(volume, persist: true);
                break;
            case SpatialAction.SpeechRateChanged when value is int speechRate:
                speechRate = Math.Clamp(speechRate, -5, 5);
                _speechRate.Value = speechRate;
                _workspace.SpeechRate = speechRate;
                await SaveSettingsAsync();
                break;
            case SpatialAction.SpeechFilterChanged when value is int speechFilter:
                _settings.SpeakNeutralPlayerChat = speechFilter >= 2;
                _workspace.SpeakNeutralPlayerChat = _settings.SpeakNeutralPlayerChat;
                SyncSpeechFilterFromPlayerSwitches();
                await SaveSettingsAsync();
                break;
            case SpatialAction.UrgentPlayerSpeechChanged when value is bool urgentPlayerSpeech:
                _settings.SpeakUrgentPlayerChat = urgentPlayerSpeech;
                _workspace.SpeakUrgentPlayerChat = urgentPlayerSpeech;
                SyncSpeechFilterFromPlayerSwitches();
                await SaveSettingsAsync();
                break;
            case SpatialAction.ImportantPlayerSpeechChanged when value is bool importantPlayerSpeech:
                _settings.SpeakImportantPlayerChat = importantPlayerSpeech;
                _workspace.SpeakImportantPlayerChat = importantPlayerSpeech;
                SyncSpeechFilterFromPlayerSwitches();
                await SaveSettingsAsync();
                break;
            case SpatialAction.NeutralPlayerSpeechChanged when value is bool neutralPlayerSpeech:
                _settings.SpeakNeutralPlayerChat = neutralPlayerSpeech;
                _workspace.SpeakNeutralPlayerChat = neutralPlayerSpeech;
                SyncSpeechFilterFromPlayerSwitches();
                await SaveSettingsAsync();
                break;
            case SpatialAction.OutputModeChanged when value is int outputMode:
                SelectValue(_outputMode, (OutputMode)Math.Clamp(outputMode, 0, 2));
                _workspace.OutputModeIndex = Math.Clamp(outputMode, 0, 2);
                await SaveSettingsAsync();
                break;
            case SpatialAction.ChangeOnlyChanged when value is bool changeOnly:
                _workspace.ChangeOnly = changeOnly;
                _settings.ChangeThreshold = changeOnly ? 0.035 : 0;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.SignalVoiceChanged when value is bool legacySignal:
                _workspace.TacticalSignals = legacySignal;
                _tacticalSignals.Checked = legacySignal;
                await SaveSettingsAsync();
                break;
            case SpatialAction.TacticalSignalsChanged when value is bool tacticalSignals:
                _workspace.TacticalSignals = tacticalSignals;
                _tacticalSignals.Checked = tacticalSignals;
                await SaveSettingsAsync();
                break;
            case SpatialAction.KillEventsChanged when value is bool killEvents:
                _workspace.KillEvents = killEvents;
                _killEvents.Checked = killEvents;
                await SaveSettingsAsync();
                break;
            case SpatialAction.ObjectiveEventsChanged when value is bool objectiveEvents:
                _workspace.ObjectiveEvents = objectiveEvents;
                _objectiveEvents.Checked = objectiveEvents;
                await SaveSettingsAsync();
                break;
            case SpatialAction.ClientRemindersChanged when value is bool clientReminders:
                _workspace.ClientReminders = clientReminders;
                _clientReminders.Checked = clientReminders;
                await SaveSettingsAsync();
                break;
            case SpatialAction.OtherSystemEventsChanged when value is bool otherSystemEvents:
                _workspace.OtherSystemEvents = otherSystemEvents;
                _otherSystemEvents.Checked = otherSystemEvents;
                await SaveSettingsAsync();
                break;
            case SpatialAction.KeepEmotionChanged when value is bool keepEmotion:
                _workspace.KeepEmotion = keepEmotion;
                _settings.KeepEmotionSummary = keepEmotion;
                await SaveSettingsAsync();
                break;
            case SpatialAction.FloatingButtonChanged when value is bool floating:
                _workspace.FloatingButton = floating;
                _settings.ShowFloatingControl = floating;
                if (!floating) _overlayControl.Hide();
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.LowUsageChanged when value is bool lowUsage:
                _workspace.LowUsage = lowUsage;
                _pollInterval.Value = lowUsage ? 600 : 350;
                await SaveSettingsAsync();
                break;
            case SpatialAction.MinimizeAfterStartChanged when value is bool minimizeAfterStart:
                _workspace.MinimizeAfterStart = minimizeAfterStart;
                _minimizeAfterStart.Checked = minimizeAfterStart;
                _settings.MinimizeAfterStart = minimizeAfterStart;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.CloseToTrayChanged when value is bool closeToTray:
                _workspace.CloseToTray = closeToTray;
                _settings.CloseToTray = closeToTray;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OverlayOpacityChanged when value is int opacity:
                _opacity.Value = Math.Clamp(opacity, (int)_opacity.Minimum, (int)_opacity.Maximum);
                _workspace.OverlayOpacity = opacity;
                await SaveSettingsAsync();
                break;
            case SpatialAction.OverlayPlacementChanged when value is int placement:
                placement = Math.Clamp(placement, 0, Enum.GetValues<OverlayPlacement>().Length - 1);
                SelectValue(_placement, (OverlayPlacement)placement);
                _workspace.OverlayPlacementIndex = placement;
                await SaveSettingsAsync();
                break;
            case SpatialAction.ApplyPreset when value is int presetIndex:
                if (presetIndex >= 0 && presetIndex < _presetSelector.Items.Count)
                {
                    _presetSelector.SelectedIndex = presetIndex;
                    ApplySelectedPreset();
                    RefreshRegionPreview();
                }
                break;
            case SpatialAction.SpeechPresetChanged when value is int speechPreset:
                ApplySpeechPreset(speechPreset);
                await SaveSettingsAsync();
                break;
            case SpatialAction.LanguageChanged when value is string language:
                _settings.Language = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.English
                    : AppLanguage.Chinese;
                _workspace.Language = _settings.Language == AppLanguage.English ? "en-US" : "zh-CN";
                Text = _settings.Language == AppLanguage.English
                    ? "RiftLens · Korean-server screen translator"
                    : "RiftLens · 韩服屏幕翻译";
                ApplyNativeLanguage();
                _speech.Clear();
                await _settingsStore.SaveAsync(_settings);
                UpdateReadiness();
                SetStatus(L(
                    "已切换为中文；后续译文与播报将使用中文。",
                    "Switched to English. New translations and speech will use English."));
                break;
            case SpatialAction.InterfacePaletteChanged when value is string palette:
                _settings.InterfacePalette = NormalizeInterfacePalette(palette);
                _workspace.InterfacePalette = _settings.InterfacePalette;
                _overlayControl.SetTheme(_settings.InterfacePalette, _settings.CustomThemeColor);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.CustomThemeColorChanged when value is string customColor:
                _settings.InterfacePalette = "custom";
                _settings.CustomThemeColor = NormalizeThemeColor(customColor);
                _workspace.InterfacePalette = _settings.InterfacePalette;
                _workspace.CustomThemeColor = _settings.CustomThemeColor;
                _overlayControl.SetTheme(_settings.InterfacePalette, _settings.CustomThemeColor);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OnboardingCompleted when value is bool onboardingCompleted:
                _settings.OnboardingCompleted = onboardingCompleted;
                _workspace.OnboardingCompleted = onboardingCompleted;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.SaveTranslationHistoryChanged when value is bool saveHistory:
                _settings.SaveTranslationHistory = saveHistory;
                _workspace.SaveTranslationHistory = saveHistory;
                await _settingsStore.SaveAsync(_settings);
                SetStatus(saveHistory
                    ? L("本地翻译记录保存已开启。记录只写入当前用户的 RiftLens 本地目录。", "Local history saving is on. Records stay in the current user's RiftLens data folder.")
                    : L("本地翻译记录保存已关闭。", "Local history saving is off."));
                break;
            case SpatialAction.RecognitionModeChanged when value is int recognitionMode:
                if (_coordinator is not null)
                {
                    await StopListeningAsync();
                }
                _settings.RecognitionMode = recognitionMode == (int)RecognitionPipelineMode.DeepSeekVisionExperimental
                    ? RecognitionPipelineMode.DeepSeekVisionExperimental
                    : RecognitionPipelineMode.LocalOcr;
                _workspace.RecognitionMode = (int)_settings.RecognitionMode;
                await _settingsStore.SaveAsync(_settings);
                UpdateReadiness();
                SetStatus(_settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental
                    ? L(
                        "已选择 DeepSeek V4 Vision 实验识别。启用前请确认截图发送同意项。",
                        "DeepSeek V4 Vision experimental recognition selected. Confirm screenshot consent before starting.")
                    : L(
                        "已切换为本地 PP-OCRv5；框选截图不会发送给视觉模型。",
                        "Switched to local PP-OCRv5. Selected-region screenshots are not sent to a vision model."));
                break;
            case SpatialAction.VisionPrivacyConsentChanged when value is bool visionConsent:
                if (_coordinator is not null)
                {
                    await StopListeningAsync();
                }
                _settings.VisionPrivacyConsent = visionConsent;
                _workspace.VisionPrivacyConsent = visionConsent;
                await _settingsStore.SaveAsync(_settings);
                UpdateReadiness();
                SetStatus(visionConsent
                    ? L(
                        "已同意 Vision 实验模式发送框选区域截图；不会发送全屏或读取游戏内存。",
                        "Vision consent enabled. Only the selected crop may be sent; RiftLens never sends the full screen or reads game memory.")
                    : L(
                        "已撤回 Vision 截图发送同意；实验识别将无法启动。",
                        "Vision screenshot consent withdrawn. Experimental recognition cannot start."));
                break;
            case SpatialAction.ToggleAutoSpeech when value is bool autoSpeech:
                _workspace.AutoSpeech = autoSpeech;
                SelectValue(_outputMode, autoSpeech ? OutputMode.OverlayAndVoice : OutputMode.OverlayOnly);
                await SaveSettingsAsync();
                break;
        }
        _workspace.Invalidate();
    }

    private void ApplySettingsToControls()
    {
        _apiBaseUrl.Text = _settings.ApiBaseUrl;
        _apiModel.Text = _settings.ApiModel;
        SelectValue(_outputMode, _settings.OutputMode);
        SelectValue(_placement, _settings.OverlayPlacement);
        SelectValue(_speechFilter, _settings.SpeechFilter);
        _pollInterval.Value = Clamp(_settings.PollIntervalMs, _pollInterval);
        _opacity.Value = Clamp((decimal)(_settings.OverlayOpacity * 100), _opacity);
        _fontSize.Value = Clamp(_settings.OverlayFontSize, _fontSize);
        _duration.Value = Clamp(_settings.OverlayDurationMs, _duration);
        _speechVolume.Value = Clamp(_settings.SpeechVolume, _speechVolume);
        _speechRate.Value = Clamp(_settings.SpeechRate, _speechRate);
        _summaryWindow.Value = Clamp(_settings.SummaryWindowMs, _summaryWindow);
        _ocrScale.Value = Clamp(_settings.OcrScale, _ocrScale);
        _showOriginal.Checked = _settings.ShowOriginalText;
        _tacticalSignals.Checked = _settings.TacticalSignalEnabled;
        _killEvents.Checked = _settings.KillEventEnabled;
        _objectiveEvents.Checked = _settings.ObjectiveEventEnabled;
        _clientReminders.Checked = _settings.ClientReminderEnabled;
        _otherSystemEvents.Checked = _settings.OtherSystemEventEnabled;
        _minimizeAfterStart.Checked = _settings.MinimizeAfterStart;
        _overlayControl.SetTheme(_settings.InterfacePalette, _settings.CustomThemeColor);
        _overlayControl.SetPreferredPosition(
            _settings.FloatingControlX,
            _settings.FloatingControlY);
        _workspace.ApplySettings(_settings, _settings.MinimizeAfterStart);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Canvas,
            Padding = new Padding(18, 8, 8, 6),
            ColumnCount = 3,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var mark = new BrandMarkControl
        {
            Size = new Size(42, 42),
            Margin = new Padding(13, 0, 14, 0)
        };
        var wordmark = new Label
        {
            Text = "RIFTLENS",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 9, 0, 0)
        };
        brand.Controls.Add(mark);
        brand.Controls.Add(wordmark);
        header.Controls.Add(brand, 0, 0);

        _headerStatus.Dock = DockStyle.Fill;
        _headerStatus.TextAlign = ContentAlignment.MiddleRight;
        _headerStatus.AutoEllipsis = true;
        _headerStatus.Margin = new Padding(0, 0, 14, 0);
        header.Controls.Add(_headerStatus, 1, 0);

        var rightCluster = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = UiTheme.Canvas
        };
        _appBadge.Anchor = AnchorStyles.Right;
        _appBadge.Margin = new Padding(0, 12, 8, 12);
        rightCluster.Controls.Add(_appBadge);
        rightCluster.Controls.Add(_minimizeWindow);
        rightCluster.Controls.Add(_maximizeWindow);
        rightCluster.Controls.Add(_closeWindow);
        header.Controls.Add(rightCluster, 2, 0);

        EnableWindowDrag(header);
        return header;
    }

    private Control BuildNavigation()
    {
        var nav = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 16, 8, 12),
            Margin = new Padding(0, 8, 12, 0),
            CornerRadius = 18,
            FillColor = UiTheme.Glass,
            BorderColor = UiTheme.Border
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var primary = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        foreach (var button in new[] { _homeNav, _historyNav, _outputNav, _advancedNav })
        {
            button.Margin = new Padding(0, 4, 0, 4);
            primary.Controls.Add(button);
        }

        var utilities = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 64),
            Margin = Padding.Empty
        };
        var update = QuietButton("更新", 48);
        update.Height = 30;
        update.Font = UiTheme.Small(8.2f, FontStyle.Bold);
        update.AccessibleName = "检查更新";
        update.Click += (_, _) => SetStatus("当前为本地开发版；更新检查将在发布渠道接入后启用。");
        var about = QuietButton("软件", 48);
        about.Height = 30;
        about.Font = UiTheme.Small(8.2f, FontStyle.Bold);
        about.AccessibleName = "软件信息";
        about.Click += (_, _) => SetStatus("RiftLens · 本地 OCR、屏幕翻译与中文自然播报。开发者 @Nixz");
        utilities.Controls.Add(update);
        utilities.Controls.Add(about);
        layout.Controls.Add(primary, 0, 0);
        layout.Controls.Add(utilities, 0, 2);
        nav.Controls.Add(layout);
        return nav;
    }

    private Control BuildLegacyHomePage()
    {
        var scroll = PagePanel();
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(30, 8, 30, 28),
            BackColor = UiTheme.Canvas
        };
        stack.Controls.Add(BuildHeroCard());
        var setup = BuildSetupCard();
        setup.Margin = new Padding(0, 16, 0, 0);
        stack.Controls.Add(setup);
        stack.Controls.Add(BuildQuickActionsCard());
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildHomePage()
    {
        var scroll = PagePanel();
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(4, 8, 4, 10),
            BackColor = UiTheme.Canvas
        };

        var upper = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 294,
            ColumnCount = 2,
            BackColor = UiTheme.Canvas,
            Margin = Padding.Empty
        };
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        var translating = BuildTranslatingCard();
        translating.Margin = new Padding(0, 0, 10, 0);
        upper.Controls.Add(translating, 0, 0);
        upper.Controls.Add(BuildRecognitionCard(), 1, 0);

        var lower = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 214,
            ColumnCount = 2,
            BackColor = UiTheme.Canvas,
            Margin = new Padding(0, 14, 0, 0)
        };
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        var recent = BuildRecentResultsCard();
        recent.Margin = new Padding(0, 0, 10, 0);
        lower.Controls.Add(recent, 0, 0);
        lower.Controls.Add(BuildQuickApiCard(), 1, 0);

        stack.Controls.Add(upper, 0, 0);
        stack.Controls.Add(lower, 0, 1);
        stack.Controls.Add(BuildHomeStatusBar(), 0, 2);
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildTranslatingCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 18,
            FillColor = UiTheme.GlassBright,
            Padding = new Padding(24, 22, 24, 20),
            Margin = Padding.Empty
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
        grid.Controls.Add(new LensArtControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) }, 0, 0);

        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, BackColor = Color.Transparent, Margin = Padding.Empty };
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 18f));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 17f));
        _heroTitle.Dock = DockStyle.Fill;
        _heroTitle.AutoSize = false;
        _heroTitle.TextAlign = ContentAlignment.BottomLeft;
        _heroTitle.Font = UiTheme.Display(25f);
        _heroDescription.Dock = DockStyle.Fill;
        _heroDescription.AutoSize = false;
        _heroDescription.TextAlign = ContentAlignment.MiddleLeft;
        _heroDescription.AutoEllipsis = true;
        _primaryAction.Dock = DockStyle.Left;
        _primaryAction.Width = 170;
        _primaryAction.Height = 44;
        _primaryAction.CornerRadius = 22;
        _primaryAction.Margin = new Padding(0, 2, 0, 2);
        _primaryHint.Dock = DockStyle.Fill;
        _primaryHint.AutoSize = false;
        _primaryHint.TextAlign = ContentAlignment.TopLeft;
        _primaryHint.AutoEllipsis = true;
        copy.Controls.Add(_heroTitle, 0, 0);
        copy.Controls.Add(new Label
        {
            Text = "韩文聊天 · 实时识别",
            Dock = DockStyle.Fill,
            Font = UiTheme.Body(11f),
            ForeColor = Color.FromArgb(69, 95, 121),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        copy.Controls.Add(_heroDescription, 0, 2);
        copy.Controls.Add(_primaryAction, 0, 3);
        copy.Controls.Add(_primaryHint, 0, 4);
        grid.Controls.Add(copy, 1, 0);
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildRecognitionCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 18,
            FillColor = Color.FromArgb(254, 250, 247),
            Padding = new Padding(22, 18, 22, 16),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        title.Controls.Add(Heading("识别区域", 18), 0, 0);
        _regionBadge.Dock = DockStyle.Fill;
        title.Controls.Add(_regionBadge, 1, 0);

        var preview = new CardPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(248, 248, 248),
            BorderColor = Color.FromArgb(224, 225, 226),
            CornerRadius = 14,
            Padding = new Padding(18, 16, 18, 12),
            Margin = new Padding(0, 6, 0, 6)
        };
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.Transparent };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        previewLayout.Controls.Add(MutedLabel("●  游戏聊天框"), 0, 0);
        _regionSummary.Dock = DockStyle.Fill;
        _regionSummary.AutoSize = false;
        _regionSummary.AutoEllipsis = true;
        _regionSummary.TextAlign = ContentAlignment.MiddleLeft;
        previewLayout.Controls.Add(_regionSummary, 0, 1);
        previewLayout.Controls.Add(new Label
        {
            Text = "适用于游戏、选人及直播画面",
            Dock = DockStyle.Fill,
            Font = UiTheme.Small(8.5f),
            ForeColor = Color.FromArgb(69, 95, 121),
            TextAlign = ContentAlignment.BottomCenter
        }, 0, 2);
        preview.Controls.Add(previewLayout);

        _presetSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetSelector.Dock = DockStyle.Fill;
        _presetSelector.Font = UiTheme.Caption(9f);
        _presetSelector.Margin = new Padding(0, 1, 0, 1);
        _presetSelector.SelectedIndexChanged += (_, _) => ApplySelectedPreset();
        PopulatePresets();

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        _regionAction.Dock = DockStyle.Fill;
        _regionAction.CornerRadius = 14;
        _regionAction.Margin = new Padding(0, 4, 8, 2);
        var test = QuietButton("测试识别", 100);
        test.Dock = DockStyle.Fill;
        test.Margin = new Padding(8, 4, 0, 2);
        test.Click += async (_, _) => await TestOcrAsync();
        actions.Controls.Add(_regionAction, 0, 0);
        actions.Controls.Add(test, 1, 0);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(preview, 0, 1);
        layout.Controls.Add(_presetSelector, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildRecentResultsCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 18,
            FillColor = UiTheme.GlassBright,
            Padding = new Padding(26, 20, 24, 18),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.Controls.Add(Heading("最近结果", 16), 0, 0);
        _recentOriginal.Dock = DockStyle.Fill;
        _recentOriginal.ForeColor = Color.FromArgb(100, 104, 127, 244);
        _recentOriginal.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_recentOriginal, 0, 1);
        _recentTranslation.Dock = DockStyle.Fill;
        _recentTranslation.AutoSize = false;
        _recentTranslation.ForeColor = UiTheme.MintDeep;
        _recentTranslation.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_recentTranslation, 0, 2);
        _recentMeta.Dock = DockStyle.Fill;
        _recentMeta.ForeColor = UiTheme.MintDeep;
        layout.Controls.Add(_recentMeta, 0, 3);
        var more = QuietButton("查看全部记录  ›", 132);
        more.Dock = DockStyle.Right;
        more.Click += (_, _) => ShowPage("history");
        layout.Controls.Add(more, 0, 4);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildQuickApiCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 18,
            FillColor = UiTheme.GlassBright,
            Padding = new Padding(24, 18, 24, 16),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        header.Controls.Add(Heading("DeepSeek API", 16), 0, 0);
        _apiBadge.Dock = DockStyle.Fill;
        header.Controls.Add(_apiBadge, 1, 0);
        layout.Controls.Add(header, 0, 0);
        _apiSummary.Dock = DockStyle.Fill;
        _apiSummary.AutoEllipsis = true;
        layout.Controls.Add(_apiSummary, 0, 1);
        _apiKey.Dock = DockStyle.Fill;
        _apiKey.Margin = new Padding(0, 3, 0, 3);
        _apiKey.PlaceholderText = "sk-••••••••••••";
        layout.Controls.Add(_apiKey, 0, 2);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var save = PrimaryButton("保存并连接", 130, 38);
        save.Dock = DockStyle.Fill;
        save.CornerRadius = 16;
        save.Margin = new Padding(0, 3, 8, 3);
        save.Click += async (_, _) => await SaveApiKeyAsync();
        var help = QuietButton("如何获取 API Key", 130);
        help.Dock = DockStyle.Fill;
        help.Margin = new Padding(8, 3, 0, 3);
        help.Click += (_, _) => ShowPage("advanced");
        actions.Controls.Add(save, 0, 0);
        actions.Controls.Add(help, 1, 0);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(MutedLabel("API Key 仅保存在 Windows 凭据管理器"), 0, 4);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildHomeStatusBar()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 76,
            CornerRadius = 18,
            FillColor = UiTheme.Glass,
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 14, 0, 0)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.Controls.Add(new VoiceAvatarControl { Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "晓晓 · 自然播报",
            Dock = DockStyle.Fill,
            Font = UiTheme.Body(10f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        _homeVolumeSlider.Dock = DockStyle.Fill;
        _homeVolumeSlider.Margin = new Padding(4, 10, 12, 8);
        layout.Controls.Add(_homeVolumeSlider, 2, 0);
        _homeVolumeValue.Dock = DockStyle.Fill;
        _homeVolumeValue.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(_homeVolumeValue, 3, 0);
        var ocr = new StatusBadge();
        ocr.SetStatus("本地 OCR", true);
        ocr.Dock = DockStyle.Fill;
        layout.Controls.Add(ocr, 4, 0);
        var ai = new StatusBadge();
        ai.SetStatus("AI 在线", true);
        ai.Dock = DockStyle.Fill;
        layout.Controls.Add(ai, 5, 0);
        layout.Controls.Add(new Label
        {
            Text = "@Nixz",
            Dock = DockStyle.Fill,
            Font = UiTheme.Body(10f),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleRight
        }, 6, 0);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildHistoryPage()
    {
        var scroll = PagePanel();
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(18, 18, 18, 28),
            BackColor = UiTheme.Canvas
        };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 52, ColumnCount = 2, BackColor = UiTheme.Canvas };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        header.Controls.Add(Heading("翻译记录", 22), 0, 0);
        var clear = QuietButton("清空记录", 98);
        clear.Dock = DockStyle.Fill;
        clear.Click += (_, _) => { _historyList.Controls.Clear(); _activity.Clear(); };
        header.Controls.Add(clear, 1, 0);
        stack.Controls.Add(header);
        var description = MutedLabel("滚动查看本次运行的玩家聊天翻译；主动开启的系统类别也会明确标注类型。");
        description.Margin = new Padding(0, 0, 0, 14);
        stack.Controls.Add(description);
        _historyList.Width = 1000;
        _historyList.SizeChanged += (_, _) => { };
        stack.Controls.Add(_historyList);
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildHeroCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 166,
            FillColor = UiTheme.Glass,
            BorderColor = UiTheme.Border,
            Padding = new Padding(30, 22, 26, 20),
            Margin = Padding.Empty
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _heroTitle.ForeColor = UiTheme.Text;
        _heroTitle.Dock = DockStyle.Fill;
        _heroTitle.AutoSize = false;
        _heroTitle.TextAlign = ContentAlignment.MiddleLeft;
        _heroDescription.ForeColor = UiTheme.TextMuted;
        _heroDescription.Dock = DockStyle.Fill;
        _heroDescription.AutoSize = false;
        _heroDescription.TextAlign = ContentAlignment.TopLeft;

        // 区域预设选择器
        _presetSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetSelector.Dock = DockStyle.Fill;
        _presetSelector.Font = UiTheme.Caption(9f);
        _presetSelector.Margin = new Padding(0, 2, 0, 2);
        _presetSelector.SelectedIndexChanged += (_, _) => ApplySelectedPreset();
        PopulatePresets();

        var assurance = new Label
        {
            Text = "内置韩英 OCR   ·   不注入游戏   ·   不自动发送",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent
        };
        copy.Controls.Add(_heroTitle, 0, 0);
        copy.Controls.Add(_heroDescription, 0, 1);
        copy.Controls.Add(assurance, 0, 2);
        grid.Controls.Add(copy, 0, 0);

        var action = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        action.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        action.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _primaryAction.Dock = DockStyle.Fill;
        _primaryAction.Margin = new Padding(20, 1, 0, 7);
        _primaryHint.ForeColor = UiTheme.TextMuted;
        _primaryHint.TextAlign = ContentAlignment.TopCenter;
        _primaryHint.Dock = DockStyle.Fill;
        _primaryHint.AutoSize = false;
        _primaryHint.Margin = new Padding(18, 0, 0, 0);
        action.Controls.Add(_primaryAction, 0, 0);
        action.Controls.Add(_primaryHint, 0, 1);
        grid.Controls.Add(action, 1, 0);
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildSetupCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 262,
            Padding = new Padding(24, 20, 20, 18),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        layout.Controls.Add(Heading("开始前检查", 15), 0, 0);
        layout.Controls.Add(MutedLabel("只需确认这三项。准备完成后，顶部主按钮会直接启用翻译。"), 0, 1);
        layout.Controls.Add(BuildSetupStep("01", "框选文字区域", _regionSummary, _regionBadge, _regionAction), 0, 2);
        layout.Controls.Add(BuildSetupStep("02", "应用内置 OCR", _ocrSummary, _ocrBadge, _ocrAction), 0, 3);
        layout.Controls.Add(BuildSetupStep("03", "连接 AI 翻译", _apiSummary, _apiBadge, _apiAction), 0, 4);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildQuickActionsCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 126,
            Padding = new Padding(24, 17, 20, 15),
            Margin = new Padding(0, 16, 0, 0),
            FillColor = UiTheme.Glass
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summary.Controls.Add(Heading("当前体验", 13), 0, 0);
        _modeSummary.Dock = DockStyle.Fill;
        _modeSummary.TextAlign = ContentAlignment.MiddleLeft;
        _placementSummary.Dock = DockStyle.Fill;
        summary.Controls.Add(_modeSummary, 0, 1);
        summary.Controls.Add(_placementSummary, 0, 2);
        grid.Controls.Add(summary, 0, 0);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent, Margin = new Padding(10, 8, 0, 8) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        var ocrTest = SecondaryButton("测试识别", 98);
        var overlayTest = SecondaryButton("预览显示", 98);
        var speechTest = SecondaryButton("试听晓晓", 98);
        ocrTest.Click += async (_, _) => await TestOcrAsync();
        overlayTest.Click += async (_, _) => await PreviewOverlayAsync();
        speechTest.Click += (_, _) => TestSpeech();
        foreach (var button in new[] { ocrTest, overlayTest, speechTest })
        {
            button.Anchor = AnchorStyles.None;
            button.Size = new Size(110, 38);
            button.Margin = new Padding(4);
        }
        actions.Controls.Add(ocrTest, 0, 0);
        actions.Controls.Add(overlayTest, 1, 0);
        actions.Controls.Add(speechTest, 2, 0);
        grid.Controls.Add(actions, 1, 0);
        card.Controls.Add(grid);
        return card;
    }

    private static Control BuildSetupStep(
        string number,
        string title,
        Label description,
        StatusBadge badge,
        UiButton action)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        var numberLabel = new Label
        {
            Text = number,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.Font(10, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = UiTheme.AccentSoft,
            Margin = new Padding(0, 6, 10, 6)
        };
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var titleLabel = BodyLabel(title, FontStyle.Bold);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.BottomLeft;
        description.Dock = DockStyle.Fill;
        description.AutoSize = false;
        description.AutoEllipsis = true;
        description.TextAlign = ContentAlignment.TopLeft;
        copy.Controls.Add(titleLabel, 0, 0);
        copy.Controls.Add(description, 0, 1);
        badge.Anchor = AnchorStyles.None;
        badge.Margin = new Padding(3, 4, 6, 4);
        action.Dock = DockStyle.Fill;
        action.Margin = new Padding(4, 7, 0, 7);
        row.Controls.Add(numberLabel, 0, 0);
        row.Controls.Add(copy, 1, 0);
        row.Controls.Add(badge, 2, 0);
        row.Controls.Add(action, 3, 0);
        return row;
    }

    private Control BuildPreviewCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 20, 22, 18),
            Margin = Padding.Empty,
            FillColor = Color.FromArgb(250, 251, 249)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(Heading("输出预览", 15), 0, 0);
        layout.Controls.Add(MutedLabel("实际播报会保留情绪，但不会逐字朗读辱骂。"), 0, 1);
        var preview = new CardPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(247, 247, 249),
            DrawBorder = false,
            CornerRadius = 13,
            Padding = new Padding(16, 15, 16, 12),
            Margin = new Padding(0, 6, 0, 8)
        };
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        var previewTitle = new Label
        {
            Text = "蕾欧娜有些着急，提醒上单及时支援并小心敌方打野。",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(10.5f, FontStyle.Bold),
            ForeColor = UiTheme.Text
        };
        var previewMeta = new Label
        {
            Text = "焦急  ·  战术提醒  ·  已过滤过激措辞",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(8),
            ForeColor = UiTheme.TextMuted
        };
        previewLayout.Controls.Add(previewTitle, 0, 0);
        previewLayout.Controls.Add(previewMeta, 0, 1);
        preview.Controls.Add(previewLayout);
        layout.Controls.Add(preview, 0, 2);
        layout.Controls.Add(_modeSummary, 0, 3);
        layout.Controls.Add(_placementSummary, 0, 4);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        var ocrTest = SecondaryButton("识别一次", 90);
        var overlayTest = SecondaryButton("预览悬浮", 90);
        var speechTest = SecondaryButton("试听语音", 90);
        ocrTest.Click += async (_, _) => await TestOcrAsync();
        overlayTest.Click += async (_, _) => await PreviewOverlayAsync();
        speechTest.Click += (_, _) => TestSpeech();
        foreach (var button in new[] { ocrTest, overlayTest, speechTest })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(2, 4, 2, 4);
        }
        actions.Controls.Add(ocrTest, 0, 0);
        actions.Controls.Add(overlayTest, 1, 0);
        actions.Controls.Add(speechTest, 2, 0);
        layout.Controls.Add(actions, 0, 5);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildActivityCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 164,
            Padding = new Padding(22, 17, 22, 15),
            Margin = new Padding(0, 16, 0, 0)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        header.Controls.Add(Heading("运行记录", 13), 0, 0);
        var clear = QuietButton("清空记录", 92);
        clear.Click += (_, _) => _activity.Clear();
        header.Controls.Add(clear, 1, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_activity, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildOutputPage()
    {
        var scroll = PagePanel();
        var stack = PageStack("翻译体验", "这里仅保留会明显改变使用体验的选项；精确数值已收进高级页面。", out var body);
        var sections = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Canvas };
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var overlaySettings = SettingsCard(
            "翻译悬浮层",
            "译文不会抢焦点，悬浮控制按钮可随时暂停。",
            ("输出方式", _outputMode),
            ("显示方位", _placement),
            ("原文", _showOriginal));
        overlaySettings.Dock = DockStyle.Top;
        overlaySettings.Height = CalculateSettingsCardHeight(3);
        overlaySettings.Margin = new Padding(0, 0, 0, 8);
        var speechSettings = SettingsCard(
            "总结式语音",
            "玩家聊天始终识别并翻译；以下系统类信息默认关闭，开启后只在玩家空闲时播报。",
            ("中文音色", BodyLabel(NaturalSpeechService.NeuralVoiceName)),
            ("播报音量", Inline(_speechVolume, Unit("%"))),
            ("玩家聊天播报", _speechFilter),
            ("战术信号", _tacticalSignals),
            ("击杀播报", _killEvents),
            ("地图资源与建筑", _objectiveEvents),
            ("客户端提醒", _clientReminders),
            ("对局流程与系统", _otherSystemEvents),
            ("运行习惯", _minimizeAfterStart),
            ("语音操作", ActionRow(("试听晓晓", new Action(TestSpeech)), ("立即静音", new Action(() => _speech.Clear())))));
        speechSettings.Dock = DockStyle.Top;
        speechSettings.Height = CalculateSettingsCardHeight(10);
        speechSettings.Margin = new Padding(0, 8, 0, 0);
        sections.Controls.Add(overlaySettings, 0, 0);
        sections.Controls.Add(speechSettings, 0, 1);
        body.Controls.Add(sections);

        var regionCard = new CardPanel { Dock = DockStyle.Top, Height = 124, Padding = new Padding(22, 18, 22, 16), Margin = new Padding(0, 16, 0, 0) };
        var regionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent };
        regionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        regionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        regionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        regionGrid.Controls.Add(SettingSummary("识别区域", _outputRegionSummary), 0, 0);
        regionGrid.Controls.Add(SettingSummary("固定显示区域", _displayRegionSummary), 1, 0);
        var regionActions = ActionRow(
            ("重新框选识别区", new Func<Task>(() => SelectRegionAsync(false))),
            ("框选显示区", new Func<Task>(() => SelectRegionAsync(true))));
        regionActions.Dock = DockStyle.Fill;
        regionGrid.Controls.Add(regionActions, 2, 0);
        regionCard.Controls.Add(regionGrid);
        body.Controls.Add(regionCard);
        body.Controls.Add(BuildSaveBar("设置会在下次开始翻译时生效。"));
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildAdvancedPage()
    {
        var scroll = PagePanel();
        var stack = PageStack("AI 与高级设置", "普通使用只需要填写一次 API Key；其余参数保持默认即可。", out var body);
        var sections = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Canvas };
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var keyActions = ActionRow(("安全保存 Key", new Func<Task>(SaveApiKeyAsync)), ("删除 Key", new Action(DeleteApiKey)));
        var aiSettings = SettingsCard(
            "AI 翻译服务",
            "兼容 Chat Completions 接口。DeepSeek 是默认值，但不被写死。",
            ("API 地址", _apiBaseUrl),
            ("模型名称", _apiModel),
            ("API Key", MutedLabel("在主界面的 DeepSeek API 卡片中快速配置")),
            ("密钥操作", keyActions));
        aiSettings.Dock = DockStyle.Top;
        aiSettings.Height = CalculateSettingsCardHeight(4);
        aiSettings.Margin = new Padding(0, 0, 0, 8);
        var ocrSettings = SettingsCard(
            "高级细节",
            "这些数值已针对轻量运行调好；只有明确需要时再修改。",
            ("检测间隔", Inline(_pollInterval, Unit("ms"))),
            ("OCR 放大", Inline(_ocrScale, Unit("倍"))),
            ("背景不透明度", Inline(_opacity, Unit("%"))),
            ("译文字号", Inline(_fontSize, Unit("px"))),
            ("停留时间", Inline(_duration, Unit("ms"))),
            ("语音速度", _speechRate),
            ("聚合窗口", Inline(_summaryWindow, Unit("ms"))));
        ocrSettings.Dock = DockStyle.Top;
        ocrSettings.Height = CalculateSettingsCardHeight(7);
        ocrSettings.Margin = new Padding(0, 8, 0, 0);
        sections.Controls.Add(aiSettings, 0, 0);
        sections.Controls.Add(ocrSettings, 0, 1);
        body.Controls.Add(sections);

        var privacy = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 126,
            FillColor = UiTheme.AccentSoft,
            BorderColor = Color.FromArgb(207, 223, 213),
            Padding = new Padding(22, 18, 22, 15),
            Margin = new Padding(0, 16, 0, 0)
        };
        privacy.Controls.Add(new Label
        {
            Text = "隐私与安全",
            Dock = DockStyle.Top,
            Height = 30,
            Font = UiTheme.Font(12, FontStyle.Bold),
            ForeColor = UiTheme.Text
        });
        privacy.Controls.Add(new Label
        {
            Text = "截图只在本地交给内置 OCR，不会上传图片。联网翻译只发送新增文字；API Key 保存在 Windows 凭据管理器。程序不读取游戏内存、不注入、不模拟输入。",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(9.5f),
            ForeColor = Color.FromArgb(61, 83, 70)
        });
        body.Controls.Add(privacy);
        body.Controls.Add(BuildSaveBar("API Key 需要单独点击“安全保存 Key”。"));
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildSaveBar(string helperText)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            ColumnCount = 2,
            BackColor = UiTheme.Canvas,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(4, 8, 0, 4)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        var helper = MutedLabel(helperText);
        helper.Dock = DockStyle.Fill;
        helper.TextAlign = ContentAlignment.MiddleLeft;
        var save = PrimaryButton("保存当前设置", 144, 40);
        save.Dock = DockStyle.Fill;
        save.Click += async (_, _) => await SaveSettingsWithFeedbackAsync();
        bar.Controls.Add(helper, 0, 0);
        bar.Controls.Add(save, 1, 0);
        return bar;
    }

    private static TableLayoutPanel PageStack(string title, string description, out TableLayoutPanel body)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(26, 12, 26, 30),
            BackColor = UiTheme.Canvas
        };
        stack.Controls.Add(Heading(title, 22));
        var subtitle = MutedLabel(description);
        subtitle.Margin = new Padding(1, 5, 0, 20);
        stack.Controls.Add(subtitle);
        body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = UiTheme.Canvas };
        stack.Controls.Add(body);
        return stack;
    }

    private static CardPanel SettingsCard(string title, string description, params (string Label, Control Control)[] rows)
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 18),
            Margin = new Padding(0, 0, 8, 0)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Length + 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var titleControl = Heading(title, 14);
        layout.Controls.Add(titleControl, 0, 0);
        layout.SetColumnSpan(titleControl, 2);
        var subtitle = MutedLabel(description);
        subtitle.Dock = DockStyle.Fill;
        layout.Controls.Add(subtitle, 0, 1);
        layout.SetColumnSpan(subtitle, 2);
        for (var index = 0; index < rows.Length; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = MutedLabel(rows[index].Label);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            rows[index].Control.Dock = DockStyle.Fill;
            layout.Controls.Add(label, 0, index + 2);
            layout.Controls.Add(rows[index].Control, 1, index + 2);
        }
        card.Controls.Add(layout);
        return card;
    }

    private static Control SettingSummary(string title, Label value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 12, 0),
            Margin = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = BodyLabel(title, FontStyle.Bold);
        heading.Dock = DockStyle.Fill;
        heading.TextAlign = ContentAlignment.BottomLeft;
        value.Dock = DockStyle.Fill;
        value.AutoSize = false;
        value.AutoEllipsis = true;
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private void WireEvents()
    {
        _homeNav.Click += (_, _) => ShowPage("home");
        _historyNav.Click += (_, _) => ShowPage("history");
        _outputNav.Click += (_, _) => ShowPage("output");
        _advancedNav.Click += (_, _) => ShowPage("advanced");
        _primaryAction.Click += async (_, _) => await HandlePrimaryActionAsync();
        _regionAction.Click += async (_, _) => await SelectRegionAsync(false);
        _apiAction.Click += (_, _) => ShowPage("advanced");
        _outputMode.SelectedIndexChanged += (_, _) => RefreshOutputSummary();
        _placement.SelectedIndexChanged += (_, _) => RefreshOutputSummary();
        _speechVolume.ValueChanged += (_, _) => ApplySpeechVolume((int)_speechVolume.Value, persist: false);
        _homeVolumeSlider.ValueChanged += volume => ApplySpeechVolume(volume, persist: true);
    }

    private void PopulateOptions()
    {
        _outputMode.Items.AddRange(new object[]
        {
            new SelectOption<OutputMode>("文字悬浮 + 语音播报", OutputMode.OverlayAndVoice),
            new SelectOption<OutputMode>("仅显示文字悬浮层", OutputMode.OverlayOnly),
            new SelectOption<OutputMode>("仅语音总结播报", OutputMode.VoiceOnly)
        });
        _placement.Items.AddRange(new object[]
        {
            new SelectOption<OverlayPlacement>("自动选择并锁定方位", OverlayPlacement.Auto),
            new SelectOption<OverlayPlacement>("识别区域上方", OverlayPlacement.Above),
            new SelectOption<OverlayPlacement>("识别区域下方", OverlayPlacement.Below),
            new SelectOption<OverlayPlacement>("识别区域左侧", OverlayPlacement.Left),
            new SelectOption<OverlayPlacement>("识别区域右侧", OverlayPlacement.Right),
            new SelectOption<OverlayPlacement>("使用单独框选的显示区", OverlayPlacement.Custom)
        });
        _speechFilter.Items.AddRange(new object[]
        {
            new SelectOption<SpeechFilterLevel>("默认：播报重点玩家聊天", SpeechFilterLevel.Standard),
            new SelectOption<SpeechFilterLevel>("精简：只播报战术意图", SpeechFilterLevel.Quiet),
            new SelectOption<SpeechFilterLevel>("完整：中性闲聊也播报", SpeechFilterLevel.Full)
        });
        _outputMode.SelectedIndex = 0;
        _placement.SelectedIndex = 0;
        _speechFilter.SelectedIndex = 0;
    }

    private async Task LoadSettingsAsync()
    {
        _heroLoadTask ??= HeroDictionary.LoadAsync();
        _settings = await _settingsStore.LoadAsync();
        Text = _settings.Language == AppLanguage.English
            ? "RiftLens · Korean-server screen translator"
            : "RiftLens · 韩服屏幕翻译";
        ApplyNativeLanguage();
        _apiKey.Text = _credentialStore.ReadApiKey() ?? string.Empty;
        _workspace.ApiKeyText = _apiKey.Text;
        ApplySettingsToControls();
        ApplySpeechVolume(_settings.SpeechVolume, persist: false);
        _ocrInitializing = true;
        _ocrError = null;
        UpdateReadiness();
        RefreshOutputSummary();
        SetStatus(L(
            "配置已载入；本地 OCR 正在后台初始化。",
            "Settings loaded. Local OCR is initializing in the background."));
        if (_settingsStore.LoadWarning is { } loadWarning)
        {
            SetStatus(loadWarning);
            Log(L("设置恢复", "Settings recovery"), loadWarning
                + (_settingsStore.RecoveryBackupPath is { } backup ? " " + backup : string.Empty));
        }
        if (_currentPage == "home")
        {
            _homeNav.Select();
        }

        _ocrWarmupTask ??= WarmUpOcrAsync();
        _ = FinishHeroLoadAsync(_heroLoadTask);
    }

    private async Task FinishHeroLoadAsync(Task<HeroDictionary> loadTask)
    {
        try
        {
            _heroes = await loadTask;
            Log("系统", $"英雄词典已载入 · {_heroes.Version}");
        }
        catch (Exception exception)
        {
            Log("英雄词典", "载入失败：" + exception.Message);
        }
    }

    private async Task WarmUpOcrAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                using var ocr = new BundledOcrService();
                ocr.WarmUp();
            });
            _ocrReady = true;
            _ocrError = null;
            _workspace.StatusMessage = L("本地 OCR 已就绪。", "Local OCR is ready.");
        }
        catch (Exception exception)
        {
            _ocrReady = false;
            _ocrError = exception.Message;
            _workspace.StatusMessage = L(
                "OCR 初始化失败，请重新下载完整便携版。",
                "OCR initialization failed. Download the complete portable package again.");
        }
        finally
        {
            _ocrInitializing = false;
            UpdateReadiness();
        }
    }

    private async Task HandlePrimaryActionAsync()
    {
        if (_coordinator is { IsRunning: true })
        {
            TogglePause();
            return;
        }

        if (!_settings.CaptureRegion.IsValid)
        {
            await SelectRegionAsync(false);
            return;
        }

        var visionMode = _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental;
        if (visionMode && !_settings.VisionPrivacyConsent)
        {
            ShowPage("advanced");
            SetStatus(L(
                "DeepSeek Vision 会发送用户框选区域截图。请先在更多设置中明确同意。",
                "DeepSeek Vision sends the user-selected crop. Confirm consent in More settings first."));
            return;
        }

        if (!visionMode && !_ocrReady)
        {
            SetStatus(_ocrInitializing
                ? L("本地 OCR 正在后台初始化，请稍等片刻。", "Local OCR is initializing. Please wait a moment.")
                : L("内置 OCR 尚未就绪：", "Bundled OCR is not ready: ")
                    + (_ocrError ?? L("请重新下载完整便携版", "download the complete portable package again")));
            return;
        }

        if (string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey()))
        {
            ShowPage("advanced");
            _apiKey.Focus();
            SetStatus(L(
                "请填写并安全保存 API Key，然后返回首页开始翻译。",
                "Enter and securely save an API key, then return home to start translating."));
            return;
        }

        await StartListeningAsync();
    }

    private async Task SelectRegionAsync(bool displayRegion)
    {
        await StopListeningAsync();
        var restoreMainWindow = Visible;
        var previousWindowState = WindowState;
        DialogResult selectionResult;
        ScreenRegion? selectedRegion;

        try
        {
            if (restoreMainWindow)
            {
                Hide();
                // 让 DWM 完成主窗口退场，避免它的最后一帧盖住全屏框选器。
                await Task.Delay(90);
            }

            using var selector = new RegionSelectorForm(displayRegion
                ? L("框选译文希望固定显示的区域", "Select the fixed translation display region")
                : L("框选韩服聊天等需要翻译识别的文字区域", "Select the Korean-server chat or text recognition region"),
                _settings.Language);
            selectionResult = selector.ShowDialog();
            selectedRegion = selector.SelectedRegion;
        }
        finally
        {
            if (restoreMainWindow && !IsDisposed)
            {
                Show();
                WindowState = previousWindowState == FormWindowState.Minimized
                    ? FormWindowState.Normal
                    : previousWindowState;
                Activate();
                BringToFront();
            }
        }

        if (selectionResult != DialogResult.OK || selectedRegion is null)
        {
            return;
        }

        GameWindowLocator.TryBind(selectedRegion);

        if (displayRegion)
        {
            _settings.DisplayRegion = selectedRegion;
            _settings.OverlayPlacement = OverlayPlacement.Custom;
            SelectValue(_placement, OverlayPlacement.Custom);
            SetStatus(L(
                "译文显示区已保存。悬浮翻译会固定显示在该区域。",
                "Translation display region saved. The overlay will stay in this region."));
        }
        else
        {
            _settings.CaptureRegion = selectedRegion;
            SetStatus(selectedRegion.HasRelative
                ? L(
                    "识别区已按游戏窗口保存。窗口移动后会跟着走。",
                    "Recognition region is saved relative to the game window and will follow it.")
                : L(
                    "翻译识别区已保存。开始翻译后可在记录页核对 OCR 原文。",
                    "Recognition region saved. After starting, verify OCR source text in History."));
        }
        await SaveSettingsAsync();
        RefreshRegionPreview();
        UpdateReadiness();
    }

    private void ApplySpeechPreset(int preset)
    {
        switch (Math.Clamp(preset, 0, 2))
        {
            case 0:
                _settings.SpeakUrgentPlayerChat = true;
                _settings.SpeakImportantPlayerChat = false;
                _settings.SpeakNeutralPlayerChat = false;
                break;
            case 2:
                _settings.SpeakUrgentPlayerChat = true;
                _settings.SpeakImportantPlayerChat = true;
                _settings.SpeakNeutralPlayerChat = true;
                break;
            default:
                _settings.SpeakUrgentPlayerChat = true;
                _settings.SpeakImportantPlayerChat = true;
                _settings.SpeakNeutralPlayerChat = false;
                break;
        }

        _workspace.SpeakUrgentPlayerChat = _settings.SpeakUrgentPlayerChat;
        _workspace.SpeakImportantPlayerChat = _settings.SpeakImportantPlayerChat;
        _workspace.SpeakNeutralPlayerChat = _settings.SpeakNeutralPlayerChat;
        SyncSpeechFilterFromPlayerSwitches();
    }

    private void RefreshRegionPreview()
    {
        try
        {
            if (!_settings.CaptureRegion.IsValid)
            {
                _workspace.RegionPreview = string.Empty;
                return;
            }

            using var bitmap = new ScreenCaptureService().Capture(_settings.CaptureRegion);
            var width = 480;
            var height = Math.Max(72, bitmap.Height * width / Math.Max(1, bitmap.Width));
            using var scaled = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(bitmap, 0, 0, width, height);
            }

            using var stream = new MemoryStream();
            scaled.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
            _workspace.RegionPreview = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            _workspace.RegionPreview = string.Empty;
        }
    }

    private async Task StartListeningAsync()
    {
        try
        {
            await StopListeningAsync();
            ReadControlsIntoSettings();
            await _settingsStore.SaveAsync(_settings);
            _heroes ??= await (_heroLoadTask ??= HeroDictionary.LoadAsync());
            var visionMode = _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental;
            if (!visionMode && !_ocrReady)
            {
                throw new InvalidOperationException(L(
                    "应用内置 OCR 模型不完整，请重新下载 RiftLens。",
                    "The bundled OCR model is incomplete. Download RiftLens again."));
            }

            var key = _credentialStore.ReadApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(L(
                    "请先安全保存 API Key。",
                    "Securely save an API key first."));
            }

            if (visionMode && !_settings.VisionPrivacyConsent)
            {
                throw new InvalidOperationException(L(
                    "请先同意仅将框选区域截图发送给 DeepSeek Vision。",
                    "Consent to sending only the selected crop to DeepSeek Vision first."));
            }

            ITranslationService translator = new OpenAiCompatibleTranslationService(
                _settings.ApiBaseUrl,
                _settings.ApiModel,
                key);
            IVisionOcrService? visionOcr = visionMode
                ? new DeepSeekVisionOcrService(_settings.ApiBaseUrl, key)
                : null;
            _coordinator = new CaptureCoordinator(_settings, _heroes, translator, visionOcr);
            _coordinator.EvaluationCaptureEnabled = _evaluationSession?.IsRecording == true;
            _diagnostics.Reset();
            _coordinator.StatusChanged += message => SafeUi(() =>
            {
                SetStatus(message);
                UpdateReadiness();
            });
            _coordinator.OcrRecognized += batch => SafeUi(() => Log("OCR", string.Join(" / ", batch.NewLines)));
            _coordinator.DiagnosticObserved += observation =>
            {
                _diagnostics.RecordClassification(observation);
                _evaluationSession?.RecordClassification(observation);
            };
            _coordinator.EvaluationFrameObserved += observation =>
            {
                var session = _evaluationSession;
                if (session?.IsRecording == true)
                {
                    session.RecordFrame(observation);
                }
                else
                {
                    observation.Screenshot.Dispose();
                }
            };
            _coordinator.TranslationReady += result => SafeUi(() => HandleTranslation(result));
            _coordinator.Start();
            _overlayControl.SetPaused(false);
            _overlayControl.SetVolume(_settings.SpeechVolume);
            var anchor = _settings.DisplayRegion.IsValid
                ? _settings.DisplayRegion.ToRectangle()
                : _settings.CaptureRegion.ToRectangle();
            if (_settings.ShowFloatingControl)
                _overlayControl.ShowNear(anchor);
            UpdateReadiness();
            Log(L("系统", "System"), L(
                "翻译已启用。切回游戏后，本窗口不会主动获得焦点。",
                "Translation enabled. This window will not take focus after you return to the game."));
            if (_settings.MinimizeAfterStart)
            {
                Hide();
            }
        }
        catch (Exception exception)
        {
            SetStatus(L("无法开始：", "Unable to start: ") + exception.Message);
            UpdateReadiness();
        }
    }

    private Task StopListeningAsync()
    {
        if (_stopListeningTask is { IsCompleted: false }) return _stopListeningTask;
        return _stopListeningTask = StopListeningCoreAsync();
    }

    private async Task StopListeningCoreAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        await _coordinator.DisposeAsync();
        _coordinator = null;
        await StopEvaluationRecordingAsync(showCompletionDialog: true);
        _speech.Clear();
        _overlay.HideImmediately();
        _overlayControl.Hide();
        SetStatus(L(
            "翻译已停止。修改设置后可以再次开始。",
            "Translation stopped. You can change settings and start again."));
        UpdateReadiness();
    }

    private async Task TestOcrAsync()
    {
        try
        {
            ReadControlsIntoSettings();
            if (!_settings.CaptureRegion.IsValid)
            {
                SetStatus(L("请先框选需要识别的文字区域。", "Select a text recognition region first."));
                ShowPage("home");
                return;
            }
            var visionMode = _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental;
            if (!visionMode && !_ocrReady)
            {
                SetStatus(_ocrInitializing
                    ? L("本地 OCR 正在初始化，请稍等片刻后再试。", "Local OCR is initializing. Try again in a moment.")
                    : L("内置 OCR 模型不完整，请重新下载 RiftLens。", "The bundled OCR model is incomplete. Download RiftLens again."));
                UpdateReadiness();
                return;
            }

            _heroes ??= await (_heroLoadTask ??= HeroDictionary.LoadAsync());
            IVisionOcrService? visionOcr = null;
            if (visionMode)
            {
                if (!_settings.VisionPrivacyConsent)
                {
                    SetStatus(L(
                        "请先同意仅将框选区域截图发送给 DeepSeek Vision。",
                        "Consent to sending only the selected crop to DeepSeek Vision first."));
                    return;
                }
                var key = _credentialStore.ReadApiKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    SetStatus(L("请先安全保存 API Key。", "Securely save an API key first."));
                    return;
                }
                visionOcr = new DeepSeekVisionOcrService(_settings.ApiBaseUrl, key);
            }
            await using var coordinator = new CaptureCoordinator(
                _settings,
                _heroes,
                new OcrOnlyTranslationService(),
                visionOcr);
            SetStatus(visionMode
                ? L("正在调用 DeepSeek Vision 识别当前框选区域…", "Calling DeepSeek Vision for the selected crop…")
                : L("正在使用本地 OCR 识别当前区域…", "Recognizing the selected region with local OCR…"));
            var text = await coordinator.CaptureAndRecognizeOnceAsync();
            Log(L("OCR 测试", "OCR test"), string.IsNullOrWhiteSpace(text)
                ? L("没有识别到文字。", "No text was recognized.")
                : text.Replace("\r\n", " / "));
            SetStatus(string.IsNullOrWhiteSpace(text)
                ? L("没有识别到文字。请重新框选，避免包含过多背景。", "No text was recognized. Select a tighter region with less background.")
                : L("OCR 测试成功，结果已写入运行记录。", "OCR test succeeded. The result was added to the activity log."));
        }
        catch (Exception exception)
        {
            SetStatus(L("OCR 测试失败：", "OCR test failed: ") + exception.Message);
        }
    }

    private async Task PreviewOverlayAsync()
    {
        ReadControlsIntoSettings();
        var preview = new TranslationResult
        {
            Speaker = L("蕾欧娜", "Leona"),
            DisplayTranslation = L(
                "蕾欧娜：请上路及时支援，并小心敌方打野。",
                "Leona: Please rotate top and watch for the enemy jungler."),
            DisplayLines = new List<string>
            {
                L("请上路及时支援。", "Please rotate top."),
                L("小心敌方打野。", "Watch for the enemy jungler.")
            },
            OriginalLines = new List<string> { "탑 합류 좀", "상대 정글 조심" },
            RawText = "레오나: 탑 합류 좀\n레오나: 상대 정글 조심",
            ExpiresAfterMs = _settings.OverlayDurationMs
        };
        try
        {
            await _overlay.ShowTranslationAsync(preview, _settings);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void TestSpeech()
    {
        _speech.Enqueue(
            L(
                "蕾欧娜有些着急，提醒上单及时支援并小心敌方打野。",
                "Leona urgently asks top to rotate and watch for the enemy jungler."),
            (int)_speechVolume.Value,
            (int)_speechRate.Value,
            8000,
            true,
            false,
            _settings.Language);
        SetStatus(L(
            "正在连接晓晓神经语音；网络不可用时会自动回退到本地语音。",
            "Connecting to Jenny Neural. If the network is unavailable, RiftLens will fall back to a local voice."));
    }

    private void HandleTranslation(TranslationResult result)
    {
        var shouldSpeakNow = FinalSpeechPolicy.ShouldSpeak(result, _settings);
        var shouldQueueSpeech = shouldSpeakNow
            && _settings.OutputMode is OutputMode.VoiceOnly or OutputMode.OverlayAndVoice;
        result.ShouldSpeak = shouldSpeakNow;
        if (!shouldSpeakNow)
        {
            result.SpeechSilenceReason = FinalSpeechPolicy.DescribeSilence(result, _settings)
                ?? result.SpeechSilenceReason;
        }
        else if (!shouldQueueSpeech)
        {
            result.ShouldSpeak = false;
            result.SpeechSilenceReason = SpeechSilenceReasons.OverlayOnly;
        }
        _diagnostics.RecordOutput(result, shouldQueueSpeech);
        _evaluationSession?.RecordOutput(result, shouldQueueSpeech);
        Log("翻译", $"[{result.Speaker}] {result.DisplayTranslation}");
        SafeUi(() => AddHistoryRecord(result));
        if (_settings.OutputMode is OutputMode.OverlayOnly or OutputMode.OverlayAndVoice)
        {
            _ = ShowOverlaySafelyAsync(result);
        }
        if (shouldQueueSpeech)
        {
            _speech.Enqueue(
                result.SpokenSummary,
                _settings.SpeechVolume,
                _settings.SpeechRate,
                result.ExpiresAfterMs,
                _settings.UseXiaoxiaoNeural,
                result.SourceKind is TranslationSourceKind.TacticalSignal
                    or TranslationSourceKind.SystemMessage,
                _settings.Language,
                result.Priority,
                () => FinalSpeechPolicy.ShouldSpeak(result, _settings)
                    && _settings.OutputMode is OutputMode.VoiceOnly or OutputMode.OverlayAndVoice,
                result.Lifetime);
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_diagnostics.Count == 0)
        {
            SetStatus(L(
                "当前还没有可导出的诊断记录，请先运行一段时间。",
                "There is no diagnostic trace to export yet. Run RiftLens for a while first."));
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = L("导出 RiftLens 脱敏诊断记录", "Export redacted RiftLens diagnostics"),
            Filter = "JSON (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            FileName = $"RiftLens-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _diagnostics.ExportAsync(dialog.FileName);
            SetStatus(L(
                "脱敏诊断记录已导出；不含截图、API Key 或原始玩家 ID。",
                "Redacted diagnostics exported without screenshots, API keys, or original player IDs."));
        }
        catch (Exception exception)
        {
            SetStatus(L(
                "导出诊断记录失败：",
                "Failed to export diagnostics: ") + exception.Message);
        }
    }

    private async Task ToggleEvaluationRecordingAsync()
    {
        if (_evaluationSession is not null)
        {
            await StopEvaluationRecordingAsync(showCompletionDialog: true);
            return;
        }

        ReadControlsIntoSettings();
        if (!_settings.CaptureRegion.IsValid)
        {
            SetStatus(L(
                "请先框选聊天识别区域，再开始真实评估记录。",
                "Select the chat recognition region before starting a real evaluation capture."));
            ShowPage("home");
            return;
        }

        var warning = L(
            "真实评估需要保存你框选聊天区域的原始截图，才能人工核对 OCR 错字和漏识别。\n\n"
            + "截图与文字可能包含玩家昵称和聊天内容；记录只写入你选择的本机文件夹，不会自动上传，也不会包含 API Key。分享前必须人工检查并脱敏。\n\n"
            + "是否继续？",
            "A real evaluation must save original screenshots of your selected chat region so OCR errors and misses can be annotated.\n\n"
            + "Screenshots and text may contain player names and chat. The capture is written only to a local folder you choose, is never uploaded automatically, and never contains your API key. Review and redact it before sharing.\n\n"
            + "Continue?");
        if (MessageBox.Show(
                this,
                warning,
                L("开始真实评估记录", "Start real evaluation capture"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = L(
                "选择用于保存真实评估记录的本机文件夹",
                "Choose a local folder for the real evaluation capture"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK
            || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            _evaluationSession = await RealEvaluationSessionRecorder.StartAsync(
                dialog.SelectedPath,
                _settings,
                Application.ProductVersion);
            if (_coordinator is not null)
            {
                _coordinator.EvaluationCaptureEnabled = true;
            }
            _workspace.EvaluationRecording = true;
            _workspace.Invalidate();
            SetStatus(L(
                "真实评估记录已开启；正在本地保存框选区域截图和实际处理结果。",
                "Real evaluation capture is active. Selected-region screenshots and actual processing results are being saved locally."));
        }
        catch (Exception exception)
        {
            SetStatus(L(
                "无法开始真实评估记录：",
                "Unable to start real evaluation capture: ") + exception.Message);
        }
    }

    private async Task StopEvaluationRecordingAsync(bool showCompletionDialog)
    {
        var session = Interlocked.Exchange(ref _evaluationSession, null);
        if (_coordinator is not null)
        {
            _coordinator.EvaluationCaptureEnabled = false;
        }
        _workspace.EvaluationRecording = false;
        _workspace.Invalidate();
        if (session is null)
        {
            return;
        }

        var path = session.SessionDirectory;
        try
        {
            await session.CompleteAsync();
            SetStatus(L(
                "真实评估记录已完成并保存在所选文件夹。",
                "Real evaluation capture completed and was saved in the selected folder."));
            if (showCompletionDialog && !_exiting)
            {
                MessageBox.Show(
                    this,
                    L(
                        "真实评估记录已安全收尾。请保留整个文件夹，之后交给 Codex 进行人工标注和重新评估。\n\n保存位置：",
                        "The real evaluation capture was finalized. Keep the entire folder for later annotation and evaluation with Codex.\n\nSaved at: ") + path,
                    L("评估记录已保存", "Evaluation capture saved"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            SetStatus(L(
                "真实评估记录收尾失败：",
                "Failed to finalize the real evaluation capture: ") + exception.Message);
        }
    }

    private void AddHistoryRecord(TranslationResult result)
    {
        var speaker = string.IsNullOrWhiteSpace(result.Speaker)
            ? L("玩家", "Player")
            : result.Speaker;
        _recentOriginal.Text = string.IsNullOrWhiteSpace(result.RawText)
            ? L("已识别新的玩家消息", "New player message recognized")
            : result.RawText;
        _recentTranslation.Text = result.DisplayTranslation.StartsWith(speaker, StringComparison.OrdinalIgnoreCase)
            ? result.DisplayTranslation
            : _settings.Language == AppLanguage.English
                ? $"{speaker}: {result.DisplayTranslation}"
                : $"{speaker}：{result.DisplayTranslation}";
        _recentMeta.Text = result.ShouldSpeak
            ? L("刚刚 · 已加入播报队列", "Just now · Queued for speech")
            : LocalSpeechPlanner.FormatReason(result.SpeechSilenceReason, _settings.Language);
        _workspace.RecentOriginal = _recentOriginal.Text;
        _workspace.RecentTranslation = _recentTranslation.Text;
        _workspace.RecentMeta = _recentMeta.Text;
        _workspace.RecentSilenceReason = result.SpeechSilenceReason;
        var historyItem = new SpatialHistoryItem(
            DateTime.Now,
            result.RawText,
            _recentTranslation.Text,
            result.ShouldSpeak,
            result.Priority,
            result.SourceKind switch
            {
                TranslationSourceKind.PlayerChat => L("玩家聊天", "Player chat"),
                TranslationSourceKind.TacticalSignal => L("战术信号", "Tactical signal"),
                TranslationSourceKind.SystemMessage when result.Intent == "kill_event" =>
                    L("击杀播报", "Kill feed"),
                TranslationSourceKind.SystemMessage when result.Intent == "objective_event" =>
                    L("地图资源与建筑", "Map objective & structure"),
                TranslationSourceKind.SystemMessage when result.Intent == "client_reminder" =>
                    L("客户端提醒", "Client reminder"),
                TranslationSourceKind.SystemMessage => L("系统通知", "System notice"),
                TranslationSourceKind.Unknown => L("未分类 · 仅显示", "Unclassified · Display only"),
                _ => string.Empty
            },
            result.ShouldSpeak
                ? L("已加入播报队列", "Queued for speech")
                : LocalSpeechPlanner.FormatReason(result.SpeechSilenceReason, _settings.Language),
            result.RecognitionEngine,
            result.SpeechSilenceReason);
        _workspace.AddHistory(historyItem);
        if (_settings.SaveTranslationHistory)
        {
            _ = AppendLocalHistoryAsync(historyItem);
        }

        var entry = BuildHistoryRecordCard(result, speaker);
        _historyList.SuspendLayout();
        _historyList.Controls.Add(entry);
        _historyList.Controls.SetChildIndex(entry, 0);
        while (_historyList.Controls.Count > 50)
            _historyList.Controls.RemoveAt(_historyList.Controls.Count - 1);
        _historyList.ResumeLayout();
    }

    private static Control BuildHistoryRecordCard(TranslationResult result, string speaker)
    {
        var card = new CardPanel
        {
            Width = 980,
            Height = 112,
            CornerRadius = 16,
            FillColor = UiTheme.GlassBright,
            Padding = new Padding(22, 16, 22, 14),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.Controls.Add(new Label
        {
            Text = $"{DateTime.Now:HH:mm}  ·  {speaker}",
            Dock = DockStyle.Fill,
            Font = UiTheme.Small(8.7f, FontStyle.Bold),
            ForeColor = UiTheme.MintDeep
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = result.DisplayTranslation,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UiTheme.Body(11f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = result.RawText,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UiTheme.Small(8.5f),
            ForeColor = UiTheme.TextMuted,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private async Task ShowOverlaySafelyAsync(TranslationResult result)
    {
        try
        {
            await _overlay.ShowTranslationAsync(result, _settings);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateReadiness()
    {
        var regionReady = _settings.CaptureRegion.IsValid;
        var keyReady = !string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey());
        var visionMode = _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental;
        var recognitionReady = visionMode
            ? keyReady && _settings.VisionPrivacyConsent
            : _ocrReady;
        _workspace.RegionReady = regionReady;
        _workspace.DisplayRegionReady = _settings.DisplayRegion.IsValid;
        _workspace.OcrReady = recognitionReady;
        _workspace.AiReady = keyReady;
        _workspace.IsRunning = _coordinator?.IsRunning ?? false;
        _workspace.IsPaused = _coordinator?.IsPaused ?? false;
        _workspace.Language = _settings.Language == AppLanguage.English ? "en-US" : "zh-CN";
        _workspace.RegionSummary = regionReady
            ? _settings.CaptureRegion.ToString()
            : L("尚未框选需要识别的文字区域", "No text recognition region selected");
        _workspace.ModelName = visionMode ? DeepSeekVisionOcrService.ModelName : _settings.ApiModel;
        _workspace.RecognitionMode = (int)_settings.RecognitionMode;
        _workspace.VisionPrivacyConsent = _settings.VisionPrivacyConsent;
        _workspace.AppVersion = DisplayVersion;
        _workspace.ActivePresetIndex = _settings.ActivePresetIndex;
        _workspace.PresetNames = _settings.Presets.Select(preset => preset.Name).ToArray();
        _regionBadge.SetStatus(regionReady ? "已完成" : "需要设置", regionReady);
        _ocrBadge.SetStatus(visionMode
            ? recognitionReady ? "Vision 已同意" : "等待同意"
            : _ocrReady ? "已内置" : _ocrInitializing ? "初始化中" : "文件缺失", recognitionReady);
        _apiBadge.SetStatus(keyReady ? "已连接" : "需要配置", keyReady);
        _regionSummary.Text = regionReady ? _settings.CaptureRegion.ToString() : "尚未框选需要识别的文字区域";
        _outputRegionSummary.Text = _regionSummary.Text;
        _displayRegionSummary.Text = _settings.DisplayRegion.IsValid
            ? _settings.DisplayRegion.ToString()
            : "不设置时会自动选择上、下、左、右";
        _ocrSummary.Text = visionMode
            ? _settings.VisionPrivacyConsent
                ? "DeepSeek V4 Vision 实验识别；失败时回退本地 OCR"
                : "Vision 模式需要明确同意发送框选区域截图"
            : _ocrReady
                ? "本地 PP-OCRv5 韩中英识别已就绪"
                : _ocrInitializing
                    ? "OCR 正在后台初始化，主界面可以继续操作"
                    : "OCR 初始化失败：" + (_ocrError ?? "请重新下载");
        _apiSummary.Text = keyReady
            ? $"{(visionMode ? DeepSeekVisionOcrService.ModelName : _settings.ApiModel)} · 密钥已安全保存"
            : "填写 API 地址、模型和 API Key";
        _regionAction.Text = regionReady ? "重选翻译识别区" : "框选翻译识别区";
        _ocrAction.Text = visionMode
            ? recognitionReady ? "Vision 已就绪" : "等待截图同意"
            : _ocrReady ? "无需安装" : _ocrInitializing ? "初始化中" : "模型缺失";
        _apiAction.Text = keyReady ? "查看设置" : "配置 AI";

        if (_coordinator is not null)
        {
            var paused = _coordinator.IsPaused;
            _appBadge.SetStatus(paused ? "已暂停" : "翻译中", !paused);
            _headerStatus.Text = paused ? "截图、识别和联网请求已暂停" : "正在监听框选区域";
            _heroTitle.Text = paused ? "翻译已暂停" : "翻译正在运行";
            _heroDescription.Text = paused
                ? "点击继续后会先重新建立聊天基线，不会补播暂停期间的旧消息。"
                : "悬浮小按钮可随时暂停或调整音量；右键可以显示主窗口或停止翻译。";
            _primaryAction.Text = paused ? "继续翻译" : "暂停翻译";
            _primaryAction.Kind = UiButtonKind.Primary;
            _primaryHint.Text = paused ? "继续时不会补播旧消息" : "停止功能位于悬浮按钮和托盘的右键菜单";
            _primaryAction.Invalidate();
            _workspace.Invalidate();
            return;
        }

        var ready = regionReady && recognitionReady && keyReady;
        _appBadge.SetStatus(ready ? "准备就绪" : "尚未就绪", ready);
        _headerStatus.Text = ready ? "所有必要设置已完成" : "按首页步骤完成设置";
        _heroTitle.Text = ready ? "可以开始翻译" : "准备开始屏幕翻译";
        _heroDescription.Text = ready
            ? "点击右侧按钮后切回游戏。程序只监听框选区域，不会主动激活窗口。"
            : "按照下面三步完成设置，然后点击右侧主按钮。每一步都会给出明确状态。";
        _primaryAction.Kind = UiButtonKind.Primary;
        _primaryAction.Text = !regionReady
            ? "先框选文字区域"
            : visionMode && !_settings.VisionPrivacyConsent
                ? "同意 Vision 截图发送"
            : !recognitionReady
                ? _ocrInitializing ? "OCR 初始化中" : "识别尚未就绪"
                : !keyReady
                    ? "配置 AI Key"
                    : "开始翻译";
        _primaryHint.Text = ready
            ? "首次识别只建立聊天基线，不会播报旧消息"
            : "主按钮会直接带你完成当前缺失的步骤";
        _primaryAction.Invalidate();
        _workspace.Invalidate();
    }

    private void PopulatePresets()
    {
        _presetSelector.Items.Clear();
        if (_settings.Presets.Count == 0)
            _settings.Presets = CapturePreset.CreateDefaults();
        foreach (var p in _settings.Presets)
            _presetSelector.Items.Add(p.Name);
        if (_settings.ActivePresetIndex >= 0 && _settings.ActivePresetIndex < _presetSelector.Items.Count)
            _presetSelector.SelectedIndex = _settings.ActivePresetIndex;
    }

    private void ApplySelectedPreset()
    {
        var idx = _presetSelector.SelectedIndex;
        if (idx < 0 || idx >= _settings.Presets.Count) return;
        var preset = _settings.Presets[idx];
        if (!preset.CaptureRegion.IsValid) return; // 尚未框选过的预设，忽略

        _settings.ActivePresetIndex = idx;
        _settings.CaptureRegion = preset.CaptureRegion;
        if (preset.DisplayRegion is { IsValid: true })
        {
            _settings.DisplayRegion = preset.DisplayRegion;
            _settings.OverlayPlacement = OverlayPlacement.Custom;
        }
        UpdateReadiness();
        SetStatus($"已切换到「{preset.Name}」");
        _ = SaveSettingsAsync();
    }

    public void SaveCurrentRegionToPreset(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= _settings.Presets.Count) return;
        _settings.Presets[presetIndex].CaptureRegion = _settings.CaptureRegion;
        _settings.Presets[presetIndex].DisplayRegion = _settings.DisplayRegion.IsValid ? _settings.DisplayRegion : null;
        _settings.ActivePresetIndex = presetIndex;
        SetStatus($"当前区域已保存到「{_settings.Presets[presetIndex].Name}」");
        _ = SaveSettingsAsync();
    }

    private void RefreshOutputSummary()
    {
        _modeSummary.Text = SelectedValue(_outputMode, OutputMode.OverlayAndVoice) switch
        {
            OutputMode.OverlayOnly => "当前输出：仅显示文字悬浮层",
            OutputMode.VoiceOnly => "当前输出：仅中文总结语音",
            _ => "当前输出：文字悬浮 + 中文总结语音"
        };
        _placementSummary.Text = SelectedValue(_placement, OverlayPlacement.Auto) switch
        {
            OverlayPlacement.Custom => "位置：使用单独框选的显示区域",
            OverlayPlacement.Above => "位置：识别区域上方",
            OverlayPlacement.Below => "位置：识别区域下方",
            OverlayPlacement.Left => "位置：识别区域左侧",
            OverlayPlacement.Right => "位置：识别区域右侧",
            _ => "位置：自动选择并在本次运行中锁定"
        };
    }

    private async Task AppendLocalHistoryAsync(SpatialHistoryItem item)
    {
        await _historySaveLock.WaitAsync();
        try
        {
            if (!_settings.SaveTranslationHistory)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_historySavePath))
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RiftLens",
                    "history");
                Directory.CreateDirectory(directory);
                _historySavePath = Path.Combine(
                    directory,
                    $"RiftLens-history-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            }
            var line = JsonSerializer.Serialize(new
            {
                time = item.Time,
                original = item.Original,
                translation = item.Translation,
                spoken = item.Spoken,
                priority = item.Priority,
                kind = item.Kind,
                silenceReason = item.SilenceReason,
                silenceReasonCode = item.SilenceReasonCode,
                recognitionEngine = item.RecognitionEngine
            });
            await File.AppendAllTextAsync(_historySavePath, line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            SafeUi(() => SetStatus(L(
                "保存本地翻译记录失败：",
                "Failed to save local translation history: ") + exception.Message));
        }
        finally
        {
            _historySaveLock.Release();
        }
    }

    private static string NormalizeInterfacePalette(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "mono" => "mono",
            "pearl" => "pearl",
            "coral" => "coral",
            "blue" => "blue",
            "teal" => "teal",
            "sage" => "sage",
            "sand" => "sand",
            "custom" => "custom",
            _ => "porcelain"
        };

    private static string NormalizeThemeColor(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 7
            && normalized[0] == '#'
            && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized.ToLowerInvariant()
            : "#6f7f91";
    }

    private void ReadControlsIntoSettings()
    {
        _settings.ApiBaseUrl = _apiBaseUrl.Text.Trim();
        _settings.ApiModel = _apiModel.Text.Trim();
        _settings.OutputMode = SelectedValue(_outputMode, OutputMode.OverlayAndVoice);
        _settings.OverlayPlacement = SelectedValue(_placement, OverlayPlacement.Auto);
        _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
        _settings.PollIntervalMs = (int)_pollInterval.Value;
        _settings.OverlayOpacity = (double)_opacity.Value / 100;
        _settings.OverlayFontSize = (int)_fontSize.Value;
        _settings.OverlayDurationMs = (int)_duration.Value;
        _settings.SpeechVolume = (int)_speechVolume.Value;
        _settings.SpeechRate = (int)_speechRate.Value;
        _settings.SummaryWindowMs = (int)_summaryWindow.Value;
        _settings.OcrScale = (int)_ocrScale.Value;
        _settings.ShowOriginalText = _showOriginal.Checked;
        _settings.TacticalSignalEnabled = _tacticalSignals.Checked;
        _settings.KillEventEnabled = _killEvents.Checked;
        _settings.ObjectiveEventEnabled = _objectiveEvents.Checked;
        _settings.ClientReminderEnabled = _clientReminders.Checked;
        _settings.OtherSystemEventEnabled = _otherSystemEvents.Checked;
        _settings.SignalVoiceEnabled = _settings.TacticalSignalEnabled;
        _settings.SystemMessageFilter = SystemMessageFilterMode.Strict;
        _settings.ShowFloatingControl = _workspace.FloatingButton;
        _settings.MinimizeAfterStart = _workspace.MinimizeAfterStart;
        _settings.CloseToTray = _workspace.CloseToTray;
        RefreshOutputSummary();
    }

    private void SyncSpeechFilterFromPlayerSwitches()
    {
        _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
        SelectValue(_speechFilter, _settings.SpeechFilter);
    }

    private async Task SaveSettingsAsync()
    {
        ReadControlsIntoSettings();
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task SaveSettingsWithFeedbackAsync()
    {
        await SaveSettingsAsync();
        SetStatus(L(
            "设置已保存。返回首页即可开始翻译或继续完成缺失步骤。",
            "Settings saved. Return home to start translating or finish any missing step."));
        UpdateReadiness();
    }

    private async Task SaveApiKeyAsync()
    {
        try
        {
            ReadControlsIntoSettings();
            if (string.IsNullOrWhiteSpace(_apiBaseUrl.Text)
                || !Uri.TryCreate(_apiBaseUrl.Text.Trim(), UriKind.Absolute, out _))
            {
                SetStatus(L(
                    "API 地址格式不正确，请填写完整的 https 地址。",
                    "The API address is invalid. Enter a complete HTTPS address."));
                return;
            }
            if (string.IsNullOrWhiteSpace(_apiModel.Text))
            {
                SetStatus(L("请填写模型名称。", "Enter a model name."));
                return;
            }
            if (string.IsNullOrWhiteSpace(_apiKey.Text))
            {
                SetStatus(L("请输入 API Key 后再保存。", "Enter an API key before saving."));
                return;
            }
            _credentialStore.SaveApiKey(_apiKey.Text.Trim());
            _workspace.ApiKeyText = _apiKey.Text.Trim();
            await _settingsStore.SaveAsync(_settings);
            SetStatus(L(
                "API Key 已保存到 Windows 凭据管理器。现在可以从首页开始翻译。",
                "The API key was saved to Windows Credential Manager. You can now start from Home."));
            UpdateReadiness();
        }
        catch (Exception exception)
        {
            SetStatus(L("保存 API Key 失败：", "Failed to save the API key: ") + exception.Message);
        }
    }

    private void DeleteApiKey()
    {
        _credentialStore.DeleteApiKey();
        _apiKey.Clear();
        _workspace.ApiKeyText = string.Empty;
        SetStatus(L("API Key 已删除。联网翻译已停用。", "The API key was deleted. Online translation is disabled."));
        UpdateReadiness();
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        _workspace.SetPage(page switch
        {
            "history" => SpatialPage.History,
            "output" => SpatialPage.Voice,
            "advanced" => SpatialPage.Settings,
            _ => SpatialPage.Home
        });
        _homeNav.Selected = page == "home";
        _historyNav.Selected = page == "history";
        _outputNav.Selected = page == "output";
        _advancedNav.Selected = page == "advanced";
        _homeNav.Invalidate();
        _historyNav.Invalidate();
        _outputNav.Invalidate();
        _advancedNav.Invalidate();
    }

    private static void ResetPageScroll(Control? page)
    {
        if (page is ScrollableControl scroll)
        {
            scroll.AutoScrollPosition = Point.Empty;
        }
    }

    private void EnableWindowDrag(Control root)
    {
        if (root is ButtonBase)
        {
            return;
        }

        root.MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
        };
        root.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ToggleMaximize();
            }
        };

        foreach (Control child in root.Controls)
        {
            EnableWindowDrag(child);
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        UpdateWindowChrome();
    }

    private void UpdateWindowChrome()
    {
        var maximized = WindowState == FormWindowState.Maximized;
        _maximizeWindow.Text = maximized ? "❐" : "□";
        _maximizeWindow.AccessibleName = maximized
            ? L("还原窗口", "Restore window")
            : L("最大化", "Maximize");
        Padding = Padding.Empty;
        ApplyWindowCorners();
    }

    private void ApplyWindowCorners()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var rounded = WindowState == FormWindowState.Normal;
        var cornerPreference = rounded ? 2 : 1; // DWMWCP_ROUND / DWMWCP_DONOTROUND
        _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
        // A WinForms Region is a 1-bit clipping mask and produces jagged black
        // corner pixels when combined with DWM and the WebUI's inner radius.
        // Let Windows own the outer window shape and keep CSS responsible only
        // for the inner application surface.
        var previousRegion = Region;
        Region = null;
        previousRegion?.Dispose();
    }

    private static string DisplayVersion
    {
        get
        {
            var version = typeof(MainForm).Assembly.GetName().Version;
            return version is null ? "2.3.0" : version.ToString(3);
        }
    }

    private void FitToWorkingArea()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var target = new Size(
            Math.Min(1240, Math.Max(1000, workingArea.Width - 36)),
            Math.Min(810, Math.Max(680, workingArea.Height - 36)));
        MinimumSize = new Size(Math.Min(1040, target.Width), Math.Min(680, target.Height));
        Size = target;
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == SingleInstance.WmShowExisting)
        {
            ShowMainWindow();
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
        if (message.Msg != WmNcHitTest || WindowState != FormWindowState.Normal || message.Result != (IntPtr)HtClient)
        {
            return;
        }

        var packedPoint = message.LParam.ToInt64();
        var screenPoint = new Point((short)(packedPoint & 0xffff), (short)((packedPoint >> 16) & 0xffff));
        var clientPoint = PointToClient(screenPoint);
        var grip = Math.Max(6, DeviceDpi * 7 / 96);
        var left = clientPoint.X <= grip;
        var right = clientPoint.X >= ClientSize.Width - grip;
        var top = clientPoint.Y <= grip;
        var bottom = clientPoint.Y >= ClientSize.Height - grip;

        message.Result = (left, right, top, bottom) switch
        {
            (true, _, true, _) => (IntPtr)HtTopLeft,
            (_, true, true, _) => (IntPtr)HtTopRight,
            (true, _, _, true) => (IntPtr)HtBottomLeft,
            (_, true, _, true) => (IntPtr)HtBottomRight,
            (true, _, _, _) => (IntPtr)HtLeft,
            (_, true, _, _) => (IntPtr)HtRight,
            (_, _, true, _) => (IntPtr)HtTop,
            (_, _, _, true) => (IntPtr)HtBottom,
            _ => message.Result
        };
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_shutdownComplete) return;
        if (!_exiting
            && _settings.CloseToTray
            && e.CloseReason is not CloseReason.WindowsShutDown
            && e.CloseReason is not CloseReason.TaskManagerClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        e.Cancel = true;
        if (_shutdownTask is not null) return;
        _exiting = true;
        Enabled = false;
        try
        {
            // Keep pumping the UI thread while the coordinator completes its awaits.
            _shutdownTask = ShutdownAsync();
            await _shutdownTask;
            _shutdownComplete = true;
            Close();
        }
        catch (Exception exception)
        {
            _shutdownTask = null;
            _exiting = false;
            Enabled = true;
            SetStatus(L("退出未完成，请重试：", "Could not finish closing. Try again: ") + exception.Message);
        }
    }

    private async Task ShutdownAsync()
    {
        // Leave the first FormClosing event before a possibly synchronous cleanup
        // calls Close again (for example when no listening session was started).
        await Task.Yield();
        var savePendingVolume = _volumeSaveTimer.Enabled;
        _volumeSaveTimer.Stop();
        _overlay.HideImmediately();
        _overlayControl.Hide();
        await StopListeningAsync();
        await StopEvaluationRecordingAsync(showCompletionDialog: false);
        if (savePendingVolume) await _settingsStore.SaveAsync(_settings);
        await _settingsStore.FlushAsync();
        _speech.Dispose();
        _volumeSaveTimer.Dispose();
        _overlayControl.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Icon?.Dispose();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示 RiftLens", null, (_, _) => ShowMainWindow());
        menu.Items.Add("暂停 / 继续翻译", null, (_, _) => TogglePause());
        menu.Items.Add("停止翻译", null, async (_, _) => await StopListeningAsync());
        menu.Items.Add("立即静音并隐藏", null, (_, _) => { _speech.Clear(); _overlay.HideImmediately(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _exiting = true;
            Close();
        });
        var icon = new NotifyIcon
        {
            Text = "RiftLens",
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowMainWindow();
        return icon;
    }

    private void ApplyNativeLanguage()
    {
        _overlayControl.SetLanguage(_settings.Language);
        _minimizeWindow.AccessibleName = L("最小化", "Minimize");
        _maximizeWindow.AccessibleName = WindowState == FormWindowState.Maximized
            ? L("还原窗口", "Restore window")
            : L("最大化", "Maximize");
        _closeWindow.AccessibleName = L("关闭", "Close");

        var items = _trayIcon.ContextMenuStrip?.Items;
        if (items is null || items.Count < 6)
        {
            return;
        }
        items[0].Text = L("显示 RiftLens", "Show RiftLens");
        items[1].Text = L("暂停 / 继续翻译", "Pause / resume translation");
        items[2].Text = L("停止翻译", "Stop translation");
        items[3].Text = L("立即静音并隐藏", "Mute now and hide");
        items[5].Text = L("退出", "Exit");
    }

    private void TogglePause()
    {
        if (_coordinator is null)
        {
            return;
        }

        var paused = !_coordinator.IsPaused;
        _coordinator.SetPaused(paused);
        _overlayControl.SetPaused(paused);
        if (paused)
        {
            _speech.Clear();
            _overlay.HideImmediately();
        }
        UpdateReadiness();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        _trayIcon.Visible = true;
        Hide();
    }

    private void ApplySpeechVolume(int volume, bool persist)
    {
        var next = Math.Clamp(volume, 0, 100);
        _settings.SpeechVolume = next;
        _speech.SetVolume(next);
        _overlayControl.SetVolume(next);
        _homeVolumeSlider.Value = next;
        _homeVolumeValue.Text = $"{next}%";
        _workspace.Volume = next;
        _workspace.Invalidate();
        if ((int)_speechVolume.Value != next)
        {
            _speechVolume.Value = next;
        }
        if (persist)
        {
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
        }
    }

    private string L(string chinese, string english) =>
        _settings.Language == AppLanguage.English ? english : chinese;

    private void SetStatus(string message)
    {
        _headerStatus.Text = message;
        _workspace.StatusMessage = message;
        _workspace.Invalidate();
        _trayIcon.Text = message.Length <= 63 ? message : message[..63];
        Log("状态", message);
    }

    private void Log(string source, string message)
    {
        _activity.AppendText($"[{DateTime.Now:HH:mm:ss}]  {source}  {message}{Environment.NewLine}");
    }

    private void SafeUi(Action action)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(action);
        }
    }

    private static int CalculateSettingsCardHeight(int contentRowCount)
    {
        // title(32) + subtitle(38) + padding(18+18=36) + 每个内容行 44px
        return 106 + contentRowCount * 44;
    }

    private static Panel PagePanel() => new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Canvas };

    private static Label Heading(string text, float size) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(size, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Margin = Padding.Empty
    };

    private static Label BodyLabel(string text, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(9.5f, style),
        ForeColor = UiTheme.Text,
        Margin = Padding.Empty
    };

    private static Label MutedLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(8.8f),
        ForeColor = UiTheme.TextMuted,
        Margin = Padding.Empty
    };

    private static Label Unit(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.TextMuted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 8, 5, 0)
    };

    private static TextBox TextInput() => new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.Text,
        Font = UiTheme.Font(9.2f),
        Margin = new Padding(0, 5, 0, 5)
    };

    private static ComboBox SelectInput() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.Text,
        Font = UiTheme.Font(9),
        Margin = new Padding(0, 5, 0, 5)
    };

    private static NumericUpDown NumberInput(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.Text,
        Font = UiTheme.Font(9),
        Width = 100,
        Margin = new Padding(0, 5, 0, 5)
    };

    private static CheckBox CheckInput(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Text,
        Font = UiTheme.Font(9),
        Margin = new Padding(0, 9, 0, 5)
    };

    private static UiButton PrimaryButton(string text, int width, int height)
    {
        var button = new UiButton { Text = text, Kind = UiButtonKind.Primary, Width = width, Height = height };
        return button;
    }

    private static UiButton SecondaryButton(string text, int width)
    {
        var button = new UiButton { Text = text, Kind = UiButtonKind.Secondary, Width = width, Height = 36, Margin = new Padding(0, 2, 8, 2) };
        return button;
    }

    private static UiButton QuietButton(string text, int width)
    {
        var button = new UiButton { Text = text, Kind = UiButtonKind.Quiet, Width = width, Height = 32 };
        return button;
    }

    private static UiButton NavButton(string text)
    {
        var button = new UiButton
        {
            Text = text,
            Kind = UiButtonKind.Quiet,
            Font = UiTheme.Body(10f),
            Width = 108,
            Height = 32,
            CornerRadius = 8,
            Margin = new Padding(1, 0, 1, 0)
        };
        return button;
    }


    private static FlowLayoutPanel Inline(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private static FlowLayoutPanel ActionRow(params (string Text, Delegate Action)[] actions)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        foreach (var (text, action) in actions)
        {
            var button = SecondaryButton(text, Math.Max(104, TextRenderer.MeasureText(text, UiTheme.Font(9)).Width + 26));
            button.Click += (_, _) =>
            {
                if (action is Func<Task> asyncAction) _ = asyncAction();
                else if (action is Action syncAction) syncAction();
            };
            row.Controls.Add(button);
        }
        return row;
    }

    private static decimal Clamp(decimal value, NumericUpDown input) => Math.Clamp(value, input.Minimum, input.Maximum);

    private static Icon CreateBrandIcon()
    {
        using (var stream = typeof(MainForm).Assembly
            .GetManifestResourceStream("RiftLens.Assets.RiftLens.ico"))
        {
            if (stream is not null)
            {
                using var embedded = new Icon(stream, new Size(32, 32));
                return (Icon)embedded.Clone();
            }
        }

        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(UiTheme.Accent);
        using var font = UiTheme.Font(29, FontStyle.Bold);
        TextRenderer.DrawText(
            graphics,
            "R",
            font,
            new Rectangle(0, 0, 64, 64),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void SelectValue<T>(ComboBox combo, T value) where T : struct, Enum
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is SelectOption<T> option && EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }

    private static T SelectedValue<T>(ComboBox combo, T fallback) where T : struct, Enum =>
        combo.SelectedItem is SelectOption<T> option ? option.Value : fallback;

    private sealed record SelectOption<T>(string Label, T Value) where T : struct, Enum
    {
        public override string ToString() => Label;
    }

    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int DwmWindowCornerPreference = 33;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, int wordParameter, IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
