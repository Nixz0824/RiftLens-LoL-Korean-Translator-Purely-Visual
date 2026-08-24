using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RiftLens.Models;

namespace RiftLens.UI;

/// <summary>
/// WebView2 承载的高保真主工作区。OCR、翻译、语音、悬浮层仍由原生代码负责；
/// 此控件只承担主窗口的绘制、输入和状态映射。
/// </summary>
internal sealed class WebWorkspace : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    // WebView2 needs a short initialization window before the product-owned
    // welcome screen can render. Keep that interval visually neutral instead
    // of showing a second, competing loading page.
    private readonly Panel _loadingSurface = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(249, 248, 247),
        TabStop = false
    };
    private readonly List<SpatialHistoryItem> _history = new();
    private bool _webReady;
    private bool _initializing;
    private bool _suspendPending;

    public event Action<SpatialAction, object?>? ActionRequested;

    public SpatialPage Page { get; private set; } = SpatialPage.Home;
    public bool RegionReady { get; set; }
    public bool DisplayRegionReady { get; set; }
    public bool OcrReady { get; set; }
    public bool AiReady { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public bool SpeechAvailable { get; set; } = true;
    public string StatusMessage { get; set; } = "正在读取配置…";
    public string RegionSummary { get; set; } = "尚未框选需要识别的文字区域";
    public string ModelName { get; set; } = "deepseek-chat";
    public string RecentOriginal { get; set; } = string.Empty;
    public string RecentTranslation { get; set; } = string.Empty;
    public string RecentMeta { get; set; } = string.Empty;
    public string RecentSilenceReason { get; set; } = string.Empty;
    public string RegionPreview { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "2.2.0";
    public int ActivePresetIndex { get; set; } = -1;
    public IReadOnlyList<string> PresetNames { get; set; } = Array.Empty<string>();
    public int Volume { get; set; } = 65;
    public int SpeechRate { get; set; } = 1;
    public int OutputModeIndex { get; set; } = 2;
    public bool AutoSpeech { get; set; } = true;
    public bool ChangeOnly { get; set; } = true;
    public bool TacticalSignals { get; set; }
    public bool KillEvents { get; set; }
    public bool ObjectiveEvents { get; set; }
    public bool ClientReminders { get; set; }
    public bool OtherSystemEvents { get; set; }
    public bool SpeakUrgentPlayerChat { get; set; } = true;
    public bool SpeakImportantPlayerChat { get; set; } = true;
    public bool KeepEmotion { get; set; } = true;
    public bool SpeakNeutralPlayerChat { get; set; }
    public bool FloatingButton { get; set; } = true;
    public bool LowUsage { get; set; } = true;
    public bool MinimizeAfterStart { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public int OverlayOpacity { get; set; } = 68;
    public int OverlayPlacementIndex { get; set; }
    public string Language { get; set; } = "zh-CN";
    public bool OnboardingCompleted { get; set; }
    public string InterfacePalette { get; set; } = "porcelain";
    public string CustomThemeColor { get; set; } = "#6f7f91";
    public bool SaveTranslationHistory { get; set; }
    public string ApiKeyText { get; set; } = string.Empty;
    public bool EvaluationRecording { get; set; }

    public WebWorkspace()
    {
        BackColor = Color.FromArgb(249, 248, 247);
        Controls.Add(_webView);
        Controls.Add(_loadingSurface);
        _loadingSurface.BringToFront();
        VisibleChanged += async (_, _) => await UpdateSuspensionAsync();
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode)
        {
            await InitializeAsync();
        }
    }

    public void SetPage(SpatialPage page)
    {
        Page = page;
        PushState();
    }

    public void ApplySettings(AppSettings settings, bool minimizeAfterStart)
    {
        Volume = settings.SpeechVolume;
        SpeechRate = settings.SpeechRate;
        OutputModeIndex = (int)settings.OutputMode;
        AutoSpeech = settings.OutputMode != OutputMode.OverlayOnly;
        ChangeOnly = settings.ChangeThreshold > 0;
        TacticalSignals = settings.TacticalSignalEnabled;
        KillEvents = settings.KillEventEnabled;
        ObjectiveEvents = settings.ObjectiveEventEnabled;
        ClientReminders = settings.ClientReminderEnabled;
        OtherSystemEvents = settings.OtherSystemEventEnabled;
        SpeakUrgentPlayerChat = settings.SpeakUrgentPlayerChat;
        SpeakImportantPlayerChat = settings.SpeakImportantPlayerChat;
        KeepEmotion = settings.KeepEmotionSummary;
        SpeakNeutralPlayerChat = settings.SpeakNeutralPlayerChat;
        FloatingButton = settings.ShowFloatingControl;
        LowUsage = settings.PollIntervalMs >= 500;
        MinimizeAfterStart = minimizeAfterStart;
        CloseToTray = settings.CloseToTray;
        OverlayOpacity = (int)Math.Round(settings.OverlayOpacity * 100);
        OverlayPlacementIndex = (int)settings.OverlayPlacement;
        ModelName = settings.ApiModel;
        Language = settings.Language == AppLanguage.English ? "en-US" : "zh-CN";
        OnboardingCompleted = settings.OnboardingCompleted;
        InterfacePalette = settings.InterfacePalette;
        CustomThemeColor = settings.CustomThemeColor;
        SaveTranslationHistory = settings.SaveTranslationHistory;
        PushState();
    }

    public void AddHistory(SpatialHistoryItem item)
    {
        _history.Add(item);
        if (_history.Count > 150)
        {
            _history.RemoveRange(0, _history.Count - 150);
        }
        PushState();
    }

    public void ClearHistory()
    {
        _history.Clear();
        PushState();
    }

    public void FocusApiKey()
    {
        SetPage(SpatialPage.Home);
        if (_webReady)
        {
            _ = _webView.ExecuteScriptAsync("document.getElementById('api-key')?.focus()");
        }
    }

    public new void Invalidate()
    {
        base.Invalidate();
        PushState();
    }

    private async Task InitializeAsync()
    {
        if (_initializing || _webView.CoreWebView2 is not null)
        {
            return;
        }

        _initializing = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RiftLens",
                "WebView2");
            var options = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments: "--disable-background-networking --disable-sync");
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 Runtime 初始化后未返回浏览器实例。");
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.WebMessageReceived += OnWebMessageReceived;

            var webUiFolder = Path.Combine(AppContext.BaseDirectory, "WebUI");
            if (!Directory.Exists(webUiFolder))
            {
                throw new DirectoryNotFoundException("找不到本地主界面资源：" + webUiFolder);
            }
            core.SetVirtualHostNameToFolderMapping(
                "riftlens.local",
                webUiFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _webView.DefaultBackgroundColor = Color.FromArgb(249, 248, 247);
            core.Navigate("https://riftlens.local/index.html");
        }
        catch (Exception exception)
        {
            ShowFallback(exception);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WebMessage>(e.WebMessageAsJson, JsonOptions);
        }
        catch
        {
            return;
        }
        if (message is null || string.IsNullOrWhiteSpace(message.Action))
        {
            return;
        }

        var action = message.Action;
        if (action == "ready")
        {
            _webReady = true;
            _loadingSurface.Hide();
            PushState();
            return;
        }

        if (action == "saveApiKey" && TryGetString(message.Payload, "apiKey", out var apiKey))
        {
            ApiKeyText = apiKey;
        }

        var mapped = action switch
        {
            "navigate" => SpatialAction.Navigate,
            "minimize" => SpatialAction.Minimize,
            "maximize" => SpatialAction.Maximize,
            "close" => SpatialAction.Close,
            "beginWindowDrag" => SpatialAction.BeginWindowDrag,
            "togglePrimary" => SpatialAction.TogglePrimary,
            "selectRegion" => SpatialAction.SelectRegion,
            "selectDisplayRegion" => SpatialAction.SelectDisplayRegion,
            "testOcr" => SpatialAction.TestOcr,
            "saveApiKey" => SpatialAction.SaveApiKey,
            "showApiHelp" => SpatialAction.ShowApiHelp,
            "clearHistory" => SpatialAction.ClearHistory,
            "exportDiagnostics" => SpatialAction.ExportDiagnostics,
            "toggleEvaluationRecording" => SpatialAction.ToggleEvaluationRecording,
            "testSpeech" => SpatialAction.TestSpeech,
            "previewOverlay" => SpatialAction.PreviewOverlay,
            "restoreDefaults" => SpatialAction.RestoreDefaults,
            "saveSettings" => SpatialAction.SaveSettings,
            "manageApiKey" => SpatialAction.ManageApiKey,
            "checkUpdate" => SpatialAction.CheckUpdate,
            "showAbout" => SpatialAction.ShowAbout,
            "volumeChanged" => SpatialAction.VolumeChanged,
            "speechRateChanged" => SpatialAction.SpeechRateChanged,
            "speechFilterChanged" => SpatialAction.SpeechFilterChanged,
            "urgentPlayerSpeechChanged" => SpatialAction.UrgentPlayerSpeechChanged,
            "importantPlayerSpeechChanged" => SpatialAction.ImportantPlayerSpeechChanged,
            "neutralPlayerSpeechChanged" => SpatialAction.NeutralPlayerSpeechChanged,
            "toggleAutoSpeech" => SpatialAction.ToggleAutoSpeech,
            "outputModeChanged" => SpatialAction.OutputModeChanged,
            "systemFilterChanged" => SpatialAction.SystemFilterChanged,
            "changeOnlyChanged" => SpatialAction.ChangeOnlyChanged,
            "signalVoiceChanged" => SpatialAction.SignalVoiceChanged,
            "tacticalSignalsChanged" => SpatialAction.TacticalSignalsChanged,
            "killEventsChanged" => SpatialAction.KillEventsChanged,
            "objectiveEventsChanged" => SpatialAction.ObjectiveEventsChanged,
            "clientRemindersChanged" => SpatialAction.ClientRemindersChanged,
            "otherSystemEventsChanged" => SpatialAction.OtherSystemEventsChanged,
            "keepEmotionChanged" => SpatialAction.KeepEmotionChanged,
            "floatingButtonChanged" => SpatialAction.FloatingButtonChanged,
            "lowUsageChanged" => SpatialAction.LowUsageChanged,
            "minimizeAfterStartChanged" => SpatialAction.MinimizeAfterStartChanged,
            "closeToTrayChanged" => SpatialAction.CloseToTrayChanged,
            "overlayOpacityChanged" => SpatialAction.OverlayOpacityChanged,
            "overlayPlacementChanged" => SpatialAction.OverlayPlacementChanged,
            "languageChanged" => SpatialAction.LanguageChanged,
            "interfacePaletteChanged" => SpatialAction.InterfacePaletteChanged,
            "customThemeColorChanged" => SpatialAction.CustomThemeColorChanged,
            "onboardingCompleted" => SpatialAction.OnboardingCompleted,
            "saveTranslationHistoryChanged" => SpatialAction.SaveTranslationHistoryChanged,
            "applyPreset" => SpatialAction.ApplyPreset,
            "speechPresetChanged" => SpatialAction.SpeechPresetChanged,
            _ => (SpatialAction?)null
        };
        if (mapped is null)
        {
            return;
        }

        object? value = action switch
        {
            "navigate" => ReadPage(message.Payload),
            "volumeChanged" or "speechRateChanged" or "speechFilterChanged" or "outputModeChanged"
                or "systemFilterChanged" or "overlayOpacityChanged" or "overlayPlacementChanged"
                or "applyPreset" or "speechPresetChanged" => ReadInt(message.Payload),
            "toggleAutoSpeech" or "changeOnlyChanged" or "signalVoiceChanged"
                or "urgentPlayerSpeechChanged" or "importantPlayerSpeechChanged" or "neutralPlayerSpeechChanged"
                or "tacticalSignalsChanged" or "killEventsChanged" or "objectiveEventsChanged"
                or "clientRemindersChanged" or "otherSystemEventsChanged" or "keepEmotionChanged"
                or "floatingButtonChanged" or "lowUsageChanged" or "minimizeAfterStartChanged"
                or "closeToTrayChanged" or "onboardingCompleted" or "saveTranslationHistoryChanged" => ReadBool(message.Payload),
            "languageChanged" or "interfacePaletteChanged" or "customThemeColorChanged" => ReadString(message.Payload),
            _ => null
        };
        ActionRequested?.Invoke(mapped.Value, value);
    }

    private void PushState()
    {
        if (!_webReady || _webView.CoreWebView2 is null || IsDisposed)
        {
            return;
        }
        var page = Page switch
        {
            SpatialPage.History => "history",
            SpatialPage.Voice => "voice",
            SpatialPage.Settings => "settings",
            _ => "home"
        };
        var envelope = new
        {
            type = "state",
            data = new
            {
                page,
                regionReady = RegionReady,
                displayRegionReady = DisplayRegionReady,
                ocrReady = OcrReady,
                aiReady = AiReady,
                isRunning = IsRunning,
                isPaused = IsPaused,
                speechAvailable = SpeechAvailable,
                statusMessage = StatusMessage,
                regionSummary = RegionSummary,
                modelName = ModelName,
                recentOriginal = RecentOriginal,
                recentTranslation = RecentTranslation,
                recentMeta = RecentMeta,
                recentSilenceReason = RecentSilenceReason,
                regionPreview = RegionPreview,
                appVersion = AppVersion,
                activePresetIndex = ActivePresetIndex,
                presets = PresetNames,
                volume = Volume,
                speechRate = SpeechRate,
                outputModeIndex = OutputModeIndex,
                autoSpeech = AutoSpeech,
                changeOnly = ChangeOnly,
                tacticalSignals = TacticalSignals,
                killEvents = KillEvents,
                objectiveEvents = ObjectiveEvents,
                clientReminders = ClientReminders,
                otherSystemEvents = OtherSystemEvents,
                speakUrgentPlayerChat = SpeakUrgentPlayerChat,
                speakImportantPlayerChat = SpeakImportantPlayerChat,
                keepEmotion = KeepEmotion,
                speakNeutralPlayerChat = SpeakNeutralPlayerChat,
                floatingButton = FloatingButton,
                lowUsage = LowUsage,
                minimizeAfterStart = MinimizeAfterStart,
                closeToTray = CloseToTray,
                overlayOpacity = OverlayOpacity,
                overlayPlacementIndex = OverlayPlacementIndex,
                language = Language,
                onboardingCompleted = OnboardingCompleted,
                interfacePalette = InterfacePalette,
                customThemeColor = CustomThemeColor,
                saveTranslationHistory = SaveTranslationHistory,
                evaluationRecording = EvaluationRecording,
                filteredCount = 0,
                history = _history.ToArray()
            }
        };
        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(envelope, JsonOptions));
        }
        catch (InvalidOperationException)
        {
            _webReady = false;
        }
    }

    private async Task UpdateSuspensionAsync()
    {
        if (_suspendPending || _webView.CoreWebView2 is null)
        {
            return;
        }
        _suspendPending = true;
        try
        {
            if (Visible)
            {
                if (_webView.CoreWebView2.IsSuspended)
                {
                    _webView.CoreWebView2.Resume();
                    PushState();
                }
            }
            else if (!_webView.CoreWebView2.IsSuspended)
            {
                await _webView.CoreWebView2.TrySuspendAsync();
            }
        }
        catch
        {
            // 窗口快速显示/隐藏时 Runtime 可能正在切换状态，下次事件会再次同步。
        }
        finally
        {
            _suspendPending = false;
        }
    }

    private void ShowFallback(Exception exception)
    {
        _loadingSurface.Hide();
        _webView.Visible = false;
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 12f),
            ForeColor = Color.FromArgb(27, 66, 91),
            BackColor = Color.FromArgb(249, 248, 247),
            Text = "RiftLens 主界面初始化失败\r\n\r\n" + exception.Message + "\r\n\r\n请确认 Microsoft Edge WebView2 Runtime 已安装。"
        };
        Controls.Add(label);
        label.BringToFront();
    }

    private static SpatialPage ReadPage(JsonElement? payload)
    {
        var page = payload is { ValueKind: JsonValueKind.String } ? payload.Value.GetString() : null;
        return page switch
        {
            "history" => SpatialPage.History,
            "voice" => SpatialPage.Voice,
            "settings" => SpatialPage.Settings,
            _ => SpatialPage.Home
        };
    }

    private static int ReadInt(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Number } && payload.Value.TryGetInt32(out var value) ? value : 0;

    private static bool ReadBool(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.True or JsonValueKind.False } && payload.Value.GetBoolean();

    private static string ReadString(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.String } ? payload.Value.GetString() ?? string.Empty : string.Empty;

    private static bool TryGetString(JsonElement? payload, string property, out string value)
    {
        value = string.Empty;
        if (payload is not { ValueKind: JsonValueKind.Object }
            || !payload.Value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private sealed record WebMessage(string Action, JsonElement? Payload);
}
