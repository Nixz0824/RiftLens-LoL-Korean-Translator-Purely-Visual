using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RiftLens.Models;
using RiftLens.Services;

namespace RiftLens.UI;

public sealed class MainForm : Form
{
    private const string ReleasesUrl =
        "https://github.com/Nixz0824/RiftLens-LoL-Korean-Translator-Purely-Visual/releases";

    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore = new();
    private readonly BundledOcrService _ocr = new();
    private readonly TranslationOverlayForm _overlay = new();
    private readonly OverlayControlForm _overlayControl = new();
    private readonly NaturalSpeechService _speech = new();
    private readonly DiagnosticSessionRecorder _diagnostics = new();
    private readonly System.Windows.Forms.Timer _volumeSaveTimer = new() { Interval = 500 };
    private readonly SemaphoreSlim _historySaveLock = new(1, 1);
    private readonly NotifyIcon _trayIcon;
    private readonly WebWorkspace _workspace;

    private RealEvaluationSessionRecorder? _evaluationSession;
    private AppSettings _settings = new();
    private HeroDictionary? _heroes;
    private Task<HeroDictionary>? _heroLoadTask;
    private Task? _ocrWarmupTask;
    private CaptureCoordinator? _coordinator;
    private bool _exiting;
    private bool _shutdownComplete;
    private Task? _shutdownTask;
    private Task? _stopListeningTask;
    private bool _hasApiKey;
    private bool _ocrReady;
    private bool _ocrInitializing;
    private string? _ocrError;
    private string? _historySavePath;

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
                SetStatus(L("保存设置失败：", "Failed to save settings: ") + exception.Message);
            }
        };

        _workspace.ActionRequested += async (action, value) => await HandleSpatialActionAsync(action, value);
        _workspace.Dock = DockStyle.Fill;
        Controls.Add(_workspace);

        HandleCreated += (_, _) => ApplyWindowCorners();
        Resize += (_, _) => ApplyWindowCorners();
        Shown += async (_, _) =>
        {
            FitToWorkingArea();
            await LoadSettingsAsync();
        };
        FormClosing += OnFormClosing;
    }

    public void SaveCurrentRegionToPreset(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= _settings.Presets.Count)
        {
            return;
        }

        _settings.Presets[presetIndex].CaptureRegion = _settings.CaptureRegion;
        _settings.Presets[presetIndex].DisplayRegion = _settings.DisplayRegion.IsValid
            ? _settings.DisplayRegion
            : null;
        _settings.ActivePresetIndex = presetIndex;
        SetStatus(L(
            $"当前区域已保存到「{_settings.Presets[presetIndex].Name}」",
            $"Current region saved to \"{_settings.Presets[presetIndex].Name}\"."));
        _ = _settingsStore.SaveAsync(_settings);
    }

    private async Task HandleSpatialActionAsync(SpatialAction action, object? value)
    {
        switch (action)
        {
            case SpatialAction.Navigate when value is SpatialPage page:
                ShowPage(page switch
                {
                    SpatialPage.History => "history",
                    SpatialPage.Voice => "output",
                    SpatialPage.Settings => "advanced",
                    _ => "home"
                });
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
                await SelectRegionAsync(displayRegion: false);
                break;
            case SpatialAction.SelectDisplayRegion:
                await SelectRegionAsync(displayRegion: true);
                break;
            case SpatialAction.TestOcr:
                await TestOcrAsync();
                break;
            case SpatialAction.SaveApiKey:
                await SaveApiKeyAsync();
                break;
            case SpatialAction.ApiEndpointChanged when value is ApiEndpointUpdate endpoint:
                await SaveApiEndpointAsync(endpoint);
                break;
            case SpatialAction.ShowApiHelp:
                SetStatus(L(
                    "在 DeepSeek 或其他 OpenAI 兼容平台创建 API Key，粘贴到输入框后点击“保存密钥”。密钥仅写入 Windows 凭据管理器。",
                    "Create an API key on DeepSeek or any OpenAI-compatible provider, paste it here, and select Save key. It is stored only in Windows Credential Manager."));
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
                SyncWorkspaceFromSettings();
                await _settingsStore.SaveAsync(_settings);
                SetStatus(L("已恢复默认设置，并保留当前框选区域。", "Defaults restored. Your selected regions were kept."));
                break;
            case SpatialAction.SaveSettings:
                await _settingsStore.SaveAsync(_settings);
                SetStatus(L("设置已保存。", "Settings saved."));
                ShowPage("home");
                break;
            case SpatialAction.ManageApiKey:
                ShowPage("home");
                _workspace.FocusApiKey();
                break;
            case SpatialAction.CheckUpdate:
                OpenReleasesPage();
                break;
            case SpatialAction.ShowAbout:
                SetStatus(L(
                    $"RiftLens {DisplayVersion} · MIT 开源 · 本地 OCR、韩服聊天翻译与自然语音播报 · @Nixz0824",
                    $"RiftLens {DisplayVersion} · MIT License · Local OCR, LoL KR chat translation & neural speech · @Nixz0824"));
                break;
            case SpatialAction.VolumeChanged when value is int volume:
                ApplySpeechVolume(volume, persist: true);
                break;
            case SpatialAction.SpeechRateChanged when value is int speechRate:
                _settings.SpeechRate = Math.Clamp(speechRate, -5, 5);
                _workspace.SpeechRate = _settings.SpeechRate;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OutputDeviceChanged when value is int deviceIndex:
                _settings.OutputDeviceIndex = deviceIndex < 0 ? -1 : deviceIndex;
                _speech.SetOutputDevice(_settings.OutputDeviceIndex);
                _workspace.OutputDeviceIndex = _settings.OutputDeviceIndex;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.SpeechFilterChanged when value is int speechFilter:
                _settings.SpeakNeutralPlayerChat = speechFilter >= 2;
                _workspace.SpeakNeutralPlayerChat = _settings.SpeakNeutralPlayerChat;
                _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.UrgentPlayerSpeechChanged when value is bool urgentPlayerSpeech:
                _settings.SpeakUrgentPlayerChat = urgentPlayerSpeech;
                _workspace.SpeakUrgentPlayerChat = urgentPlayerSpeech;
                _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.ImportantPlayerSpeechChanged when value is bool importantPlayerSpeech:
                _settings.SpeakImportantPlayerChat = importantPlayerSpeech;
                _workspace.SpeakImportantPlayerChat = importantPlayerSpeech;
                _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.NeutralPlayerSpeechChanged when value is bool neutralPlayerSpeech:
                _settings.SpeakNeutralPlayerChat = neutralPlayerSpeech;
                _workspace.SpeakNeutralPlayerChat = neutralPlayerSpeech;
                _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OutputModeChanged when value is int outputMode:
                _settings.OutputMode = (OutputMode)Math.Clamp(outputMode, 0, 2);
                _workspace.OutputModeIndex = (int)_settings.OutputMode;
                _workspace.AutoSpeech = _settings.OutputMode != OutputMode.OverlayOnly;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.ChangeOnlyChanged when value is bool changeOnly:
                _workspace.ChangeOnly = changeOnly;
                _settings.ChangeThreshold = changeOnly ? 0.035 : 0;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.SignalVoiceChanged or SpatialAction.TacticalSignalsChanged when value is bool tacticalSignals:
                _settings.TacticalSignalEnabled = tacticalSignals;
                _settings.SignalVoiceEnabled = tacticalSignals;
                _workspace.TacticalSignals = tacticalSignals;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.KillEventsChanged when value is bool killEvents:
                _settings.KillEventEnabled = killEvents;
                _workspace.KillEvents = killEvents;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.ObjectiveEventsChanged when value is bool objectiveEvents:
                _settings.ObjectiveEventEnabled = objectiveEvents;
                _workspace.ObjectiveEvents = objectiveEvents;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.ClientRemindersChanged when value is bool clientReminders:
                _settings.ClientReminderEnabled = clientReminders;
                _workspace.ClientReminders = clientReminders;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OtherSystemEventsChanged when value is bool otherSystemEvents:
                _settings.OtherSystemEventEnabled = otherSystemEvents;
                _workspace.OtherSystemEvents = otherSystemEvents;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.KeepEmotionChanged when value is bool keepEmotion:
                _settings.KeepEmotionSummary = keepEmotion;
                _workspace.KeepEmotion = keepEmotion;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.FloatingButtonChanged when value is bool floating:
                _settings.ShowFloatingControl = floating;
                _workspace.FloatingButton = floating;
                if (!floating)
                {
                    _overlayControl.Hide();
                }
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.LowUsageChanged when value is bool lowUsage:
                _settings.PollIntervalMs = lowUsage ? 600 : 350;
                _workspace.LowUsage = lowUsage;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.MinimizeAfterStartChanged when value is bool minimizeAfterStart:
                _settings.MinimizeAfterStart = minimizeAfterStart;
                _workspace.MinimizeAfterStart = minimizeAfterStart;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.CloseToTrayChanged when value is bool closeToTray:
                _settings.CloseToTray = closeToTray;
                _workspace.CloseToTray = closeToTray;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OverlayOpacityChanged when value is int opacity:
                var clampedOpacity = Math.Clamp(opacity, 20, 90);
                _settings.OverlayOpacity = clampedOpacity / 100.0;
                _workspace.OverlayOpacity = clampedOpacity;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.OverlayPlacementChanged when value is int placement:
                placement = Math.Clamp(placement, 0, Enum.GetValues<OverlayPlacement>().Length - 1);
                _settings.OverlayPlacement = (OverlayPlacement)placement;
                _workspace.OverlayPlacementIndex = placement;
                await _settingsStore.SaveAsync(_settings);
                break;
            case SpatialAction.ApplyPreset when value is int presetIndex:
                ApplyPreset(presetIndex);
                break;
            case SpatialAction.SpeechPresetChanged when value is int speechPreset:
                ApplySpeechPreset(speechPreset);
                await _settingsStore.SaveAsync(_settings);
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
                _settings.OutputMode = autoSpeech ? OutputMode.OverlayAndVoice : OutputMode.OverlayOnly;
                _workspace.AutoSpeech = autoSpeech;
                _workspace.OutputModeIndex = (int)_settings.OutputMode;
                await _settingsStore.SaveAsync(_settings);
                break;
        }
        _workspace.Invalidate();
    }

    private void SyncWorkspaceFromSettings()
    {
        if (_settings.Presets.Count == 0)
        {
            _settings.Presets = CapturePreset.CreateDefaults();
        }
        _speech.SetOutputDevice(_settings.OutputDeviceIndex);
        _workspace.OutputDevices = NaturalSpeechService.GetOutputDevices();
        _overlayControl.SetTheme(_settings.InterfacePalette, _settings.CustomThemeColor);
        _overlayControl.SetPreferredPosition(_settings.FloatingControlX, _settings.FloatingControlY);
        _workspace.ApplySettings(_settings, _settings.MinimizeAfterStart);
    }

    private async Task LoadSettingsAsync()
    {
        _heroLoadTask ??= HeroDictionary.LoadAsync();
        _settings = await _settingsStore.LoadAsync();
        Text = _settings.Language == AppLanguage.English
            ? "RiftLens · Korean-server screen translator"
            : "RiftLens · 韩服屏幕翻译";
        ApplyNativeLanguage();
        _hasApiKey = !string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey());
        SyncWorkspaceFromSettings();
        ApplySpeechVolume(_settings.SpeechVolume, persist: false);
        _ocrInitializing = true;
        _ocrError = null;
        UpdateReadiness();
        SetStatus(L(
            "配置已载入；本地 OCR 正在后台初始化。",
            "Settings loaded. Local OCR is initializing in the background."));
        if (_settingsStore.LoadWarning is { } loadWarning)
        {
            SetStatus(loadWarning);
        }

        _ocrWarmupTask ??= WarmUpOcrAsync();
        _ = FinishHeroLoadAsync(_heroLoadTask);
    }

    private async Task FinishHeroLoadAsync(Task<HeroDictionary> loadTask)
    {
        try
        {
            _heroes = await loadTask;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[RiftLens] 英雄词典载入失败：{exception}");
        }
    }

    private async Task WarmUpOcrAsync()
    {
        try
        {
            // Warm up the shared BundledOcrService instance so the loaded ONNX
            // sessions stay resident and are reused when listening begins.
            await Task.Run(() => _ocr.WarmUp());
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
        if (_workspace.PrimaryActionPending) return;
        _workspace.PrimaryActionPending = true;
        _workspace.Invalidate();
        try
        {
            await HandlePrimaryActionCoreAsync();
        }
        finally
        {
            _workspace.PrimaryActionPending = false;
            _workspace.Invalidate();
        }
    }

    private async Task HandlePrimaryActionCoreAsync()
    {
        if (_coordinator is { IsRunning: true })
        {
            TogglePause();
            return;
        }

        if (!_settings.CaptureRegion.IsValid)
        {
            await SelectRegionAsync(displayRegion: false);
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

        if (!_hasApiKey && string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey()))
        {
            _workspace.FocusApiKey();
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
            _workspace.OverlayPlacementIndex = (int)OverlayPlacement.Custom;
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
        await _settingsStore.SaveAsync(_settings);
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
        _settings.SpeechFilter = PlayerSpeechPolicy.GetModelFilter(_settings);
    }

    private async Task StartListeningAsync()
    {
        try
        {
            await StopListeningAsync();
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
            _hasApiKey = !string.IsNullOrWhiteSpace(key);
            if (!_hasApiKey)
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
                key!);
            IVisionOcrService? visionOcr = visionMode
                ? new DeepSeekVisionOcrService(_settings.ApiBaseUrl, key!)
                : null;
            _coordinator = new CaptureCoordinator(
                _settings,
                _heroes,
                translator,
                visionOcr,
                ocr: _ocr);
            _coordinator.EvaluationCaptureEnabled = _evaluationSession?.IsRecording == true;
            _diagnostics.Reset();
            _coordinator.StatusChanged += message => SafeUi(() =>
            {
                SetStatus(message);
                UpdateReadiness();
            });
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
                ? _settings.DisplayRegion.Resolve()
                : _settings.CaptureRegion.Resolve();
            if (_settings.ShowFloatingControl)
            {
                _overlayControl.ShowNear(anchor);
            }
            UpdateReadiness();
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
        if (_stopListeningTask is { IsCompleted: false })
        {
            return _stopListeningTask;
        }
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
                visionOcr,
                ocr: _ocr);
            SetStatus(visionMode
                ? L("正在调用 DeepSeek Vision 识别当前框选区域…", "Calling DeepSeek Vision for the selected crop…")
                : L("正在使用本地 OCR 识别当前区域…", "Recognizing the selected region with local OCR…"));
            var text = await coordinator.CaptureAndRecognizeOnceAsync();
            SetStatus(string.IsNullOrWhiteSpace(text)
                ? L("没有识别到文字。请重新框选，避免包含过多背景。", "No text was recognized. Select a tighter region with less background.")
                : L("OCR 测试成功：", "OCR test succeeded: ") + text.Replace("\r\n", " / "));
        }
        catch (Exception exception)
        {
            SetStatus(L("OCR 测试失败：", "OCR test failed: ") + exception.Message);
        }
    }

    private async Task PreviewOverlayAsync()
    {
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
            _settings.SpeechVolume,
            _settings.SpeechRate,
            8000,
            true,
            false,
            _settings.Language);
        SetStatus(L(
            "正在连接晓晓神经语音；连接失败时会跳过本次试听，稍后可重试。",
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
                        "真实评估记录已安全保存。请保留整个文件夹用于后续离线标注和准确率评估。\n\n保存位置：",
                        "The real evaluation capture was finalized. Keep the entire folder for offline annotation and accuracy evaluation.\n\nSaved at: ") + path,
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
        var recentOriginal = string.IsNullOrWhiteSpace(result.RawText)
            ? L("已识别新的玩家消息", "New player message recognized")
            : result.RawText;
        var recentTranslation = result.DisplayTranslation.StartsWith(speaker, StringComparison.OrdinalIgnoreCase)
            ? result.DisplayTranslation
            : _settings.Language == AppLanguage.English
                ? $"{speaker}: {result.DisplayTranslation}"
                : $"{speaker}：{result.DisplayTranslation}";
        var recentMeta = result.ShouldSpeak
            ? L("刚刚 · 已加入播报队列", "Just now · Queued for speech")
            : LocalSpeechPlanner.FormatReason(result.SpeechSilenceReason, _settings.Language);

        _workspace.RecentOriginal = recentOriginal;
        _workspace.RecentTranslation = recentTranslation;
        _workspace.RecentMeta = recentMeta;
        _workspace.RecentSilenceReason = result.SpeechSilenceReason;

        var historyItem = new SpatialHistoryItem(
            DateTime.Now,
            result.RawText,
            recentTranslation,
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
        var visionMode = _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental;
        var recognitionReady = visionMode
            ? _hasApiKey && _settings.VisionPrivacyConsent
            : _ocrReady;

        _workspace.RegionReady = regionReady;
        _workspace.DisplayRegionReady = _settings.DisplayRegion.IsValid;
        _workspace.OcrReady = recognitionReady;
        _workspace.AiReady = _hasApiKey;
        _workspace.IsRunning = _coordinator?.IsRunning ?? false;
        _workspace.IsPaused = _coordinator?.IsPaused ?? false;
        _workspace.Language = _settings.Language == AppLanguage.English ? "en-US" : "zh-CN";
        _workspace.RegionSummary = regionReady
            ? _settings.CaptureRegion.ToString()
            : L("尚未框选需要识别的文字区域", "No text recognition region selected");
        _workspace.ApiBaseUrl = _settings.ApiBaseUrl;
        _workspace.ModelName = visionMode ? DeepSeekVisionOcrService.ModelName : _settings.ApiModel;
        _workspace.RecognitionMode = (int)_settings.RecognitionMode;
        _workspace.VisionPrivacyConsent = _settings.VisionPrivacyConsent;
        _workspace.AppVersion = DisplayVersion;
        _workspace.ActivePresetIndex = _settings.ActivePresetIndex;
        _workspace.PresetNames = _settings.Presets.Select(preset => preset.Name).ToArray();
        _workspace.Invalidate();
    }

    private void ApplyPreset(int index)
    {
        if (index < 0 || index >= _settings.Presets.Count)
        {
            return;
        }
        var preset = _settings.Presets[index];
        if (!preset.CaptureRegion.IsValid)
        {
            return;
        }

        _settings.ActivePresetIndex = index;
        _settings.CaptureRegion = preset.CaptureRegion;
        if (preset.DisplayRegion is { IsValid: true })
        {
            _settings.DisplayRegion = preset.DisplayRegion;
            _settings.OverlayPlacement = OverlayPlacement.Custom;
            _workspace.OverlayPlacementIndex = (int)OverlayPlacement.Custom;
        }
        UpdateReadiness();
        SetStatus(L($"已切换到「{preset.Name}」", $"Switched to \"{preset.Name}\"."));
        _ = _settingsStore.SaveAsync(_settings);
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

    private async Task SaveApiEndpointAsync(ApiEndpointUpdate endpoint)
    {
        var baseUrl = endpoint.BaseUrl.Trim();
        var model = endpoint.Model.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("https" or "http"))
        {
            SetStatus(L(
                "API 地址格式不正确，请填写完整的 https 地址。",
                "Invalid API base URL. Enter a complete HTTP/HTTPS URL."));
            UpdateReadiness();
            return;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            SetStatus(L("请填写模型名称。", "Enter a model name."));
            UpdateReadiness();
            return;
        }

        _settings.ApiBaseUrl = baseUrl;
        _settings.ApiModel = model;
        await _settingsStore.SaveAsync(_settings);
        UpdateReadiness();
        SetStatus(L(
            $"已更新翻译端点与模型（{model}）；下次开始翻译时生效。",
            $"Updated API endpoint and model ({model}). Applies on next start."));
    }

    private async Task SaveApiKeyAsync()
    {
        if (_workspace.ApiKeySavePending) return;
        _workspace.ApiKeySavePending = true;
        _workspace.ApiKeySaveError = string.Empty;
        _workspace.Invalidate();
        try
        {
            var baseUrl = _settings.ApiBaseUrl.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            {
                SetApiKeySaveError(L(
                    "API 地址格式不正确，请填写完整的 https 地址。",
                    "The API address is invalid. Enter a complete HTTPS address."));
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.ApiModel))
            {
                SetApiKeySaveError(L("请填写模型名称。", "Enter a model name."));
                return;
            }
            var apiKey = _workspace.ApiKeyText.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetApiKeySaveError(L("请输入 API Key 后再保存。", "Enter an API key before saving."));
                return;
            }

            _credentialStore.SaveApiKey(apiKey);
            _hasApiKey = true;
            _workspace.ApiKeyText = string.Empty;
            await _settingsStore.SaveAsync(_settings);
            SetStatus(L(
                "API Key 已保存到 Windows 凭据管理器。现在可以从首页开始翻译。",
                "The API key was saved to Windows Credential Manager. You can now start from Home."));
            UpdateReadiness();
        }
        catch (Exception exception)
        {
            SetApiKeySaveError(L("保存 API Key 失败：", "Failed to save the API key: ") + exception.Message);
        }
        finally
        {
            _workspace.ApiKeySavePending = false;
            _workspace.Invalidate();
        }
    }

    private void SetApiKeySaveError(string message)
    {
        _workspace.ApiKeySaveError = message;
        SetStatus(message);
    }

    private void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleasesUrl,
                UseShellExecute = true
            });
            SetStatus(L(
                $"已在浏览器中打开发布页（当前版本 v{DisplayVersion}）。",
                $"Opened the GitHub Releases page (current version v{DisplayVersion})."));
        }
        catch
        {
            SetStatus(L(
                $"当前版本 v{DisplayVersion}；请前往 GitHub Releases 查看最新版本。",
                $"Current version v{DisplayVersion}. Check GitHub Releases for updates."));
        }
    }

    private void ShowPage(string page)
    {
        _workspace.SetPage(page switch
        {
            "history" => SpatialPage.History,
            "output" => SpatialPage.Voice,
            "advanced" => SpatialPage.Settings,
            _ => SpatialPage.Home
        });
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
        ApplyWindowCorners();
    }

    private void ApplyWindowCorners()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var rounded = WindowState == FormWindowState.Normal;
        var cornerPreference = rounded ? 2 : 1;
        _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
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
        _ocr.Dispose();
        _volumeSaveTimer.Dispose();
        _historySaveLock.Dispose();
        _overlay.Dispose();
        _overlayControl.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
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
        _workspace.Volume = next;
        _workspace.Invalidate();
        if (persist)
        {
            _volumeSaveTimer.Stop();
            _volumeSaveTimer.Start();
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

    private string L(string chinese, string english) =>
        _settings.Language == AppLanguage.English ? english : chinese;

    private void SetStatus(string message)
    {
        _workspace.StatusMessage = message;
        _workspace.Invalidate();
        _trayIcon.Text = message.Length <= 63 ? message : message[..63];
    }

    private void SafeUi(Action action)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(action);
        }
    }

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
