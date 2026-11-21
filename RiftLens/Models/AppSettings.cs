using RiftLens.Services;

namespace RiftLens.Models;

public enum AppLanguage
{
    Chinese,
    English
}

public enum RecognitionPipelineMode
{
    LocalOcr,
    DeepSeekVisionExperimental
}

public enum OutputMode
{
    OverlayOnly,
    VoiceOnly,
    OverlayAndVoice
}

public enum OverlayPlacement
{
    Auto,
    Above,
    Below,
    Left,
    Right,
    Custom
}

public enum SpeechFilterLevel
{
    Quiet,
    Standard,
    Full
}

public enum SystemMessageFilterMode
{
    Balanced,
    Strict,
    Off
}

public sealed class ScreenRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? AnchorProcess { get; set; }
    public double RelX { get; set; }
    public double RelY { get; set; }
    public double RelWidth { get; set; }
    public double RelHeight { get; set; }

    public bool HasRelative => RelWidth >= 0.02 && RelHeight >= 0.02;
    public bool IsValid => (Width >= 16 && Height >= 12) || HasRelative;

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public Rectangle Resolve()
    {
        if (HasRelative)
        {
            var client = GameWindowLocator.TryGetClientBounds(AnchorProcess, out var found)
                ? found
                : Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
            var resolved = new Rectangle(
                client.X + (int)Math.Round(RelX * client.Width),
                client.Y + (int)Math.Round(RelY * client.Height),
                Math.Max(16, (int)Math.Round(RelWidth * client.Width)),
                Math.Max(12, (int)Math.Round(RelHeight * client.Height)));
            return Rectangle.Intersect(resolved, SystemInformation.VirtualScreen);
        }

        return ToRectangle();
    }

    public void BindToClient(Rectangle client, string? processName)
    {
        if (client.Width < 16 || client.Height < 12)
        {
            return;
        }

        var area = ToRectangle();
        if (area.Width < 16 || area.Height < 12 || !client.IntersectsWith(area))
        {
            return;
        }

        AnchorProcess = processName;
        RelX = Math.Clamp((area.X - client.X) / (double)client.Width, 0, 1);
        RelY = Math.Clamp((area.Y - client.Y) / (double)client.Height, 0, 1);
        RelWidth = Math.Clamp(area.Width / (double)client.Width, 0.02, 1);
        RelHeight = Math.Clamp(area.Height / (double)client.Height, 0.02, 1);
    }

    public static ScreenRegion FromRectangle(Rectangle rectangle) => new()
    {
        X = rectangle.X,
        Y = rectangle.Y,
        Width = rectangle.Width,
        Height = rectangle.Height
    };

    public static ScreenRegion FromRelative(
        string nameHint,
        double relX,
        double relY,
        double relWidth,
        double relHeight,
        int fallbackX,
        int fallbackY,
        int fallbackWidth,
        int fallbackHeight) => new()
    {
        AnchorProcess = nameHint,
        RelX = relX,
        RelY = relY,
        RelWidth = relWidth,
        RelHeight = relHeight,
        X = fallbackX,
        Y = fallbackY,
        Width = fallbackWidth,
        Height = fallbackHeight
    };

    public override string ToString()
    {
        if (!IsValid)
        {
            return "尚未选择";
        }

        if (HasRelative)
        {
            var process = string.IsNullOrWhiteSpace(AnchorProcess) ? "窗口" : AnchorProcess;
            return $"{process} · {RelWidth:P0} × {RelHeight:P0}";
        }

        return $"{Width} × {Height} @ ({X}, {Y})";
    }
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 11;
    public AppLanguage Language { get; set; } = AppLanguage.Chinese;
    public ScreenRegion CaptureRegion { get; set; } = new();
    public ScreenRegion DisplayRegion { get; set; } = new();
    public int PollIntervalMs { get; set; } = 500;
    public double ChangeThreshold { get; set; } = 0.035;
    public int StabilizationDelayMs { get; set; } = 180;
    public int OcrScale { get; set; } = 3;
    public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiModel { get; set; } = "deepseek-v4-flash";
    public bool TranslationEnabled { get; set; } = true;
    public OutputMode OutputMode { get; set; } = OutputMode.OverlayAndVoice;
    public OverlayPlacement OverlayPlacement { get; set; } = OverlayPlacement.Auto;
    public double OverlayOpacity { get; set; } = 0.68;
    public int OverlayFontSize { get; set; } = 16;
    public int OverlayDurationMs { get; set; } = 7000;
    public bool ShowOriginalText { get; set; }
    public int SpeechVolume { get; set; } = 65;
    public int SpeechRate { get; set; } = 1;
    public int OutputDeviceIndex { get; set; } = -1;
    public SpeechFilterLevel SpeechFilter { get; set; } = SpeechFilterLevel.Standard;
    [System.Text.Json.Serialization.JsonIgnore]
    public SystemMessageFilterMode SystemMessageFilter { get; set; } = SystemMessageFilterMode.Balanced;
    public bool UseXiaoxiaoNeural { get; set; } = true;
    public int SummaryWindowMs { get; set; } = 900;
    public int MaxSummaryChars { get; set; } = 64;
    public bool KeepEmotionSummary { get; set; } = true;
    public bool PreferChampionPersonalName { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SignalVoiceEnabled { get; set; }
    public bool TacticalSignalEnabled { get; set; }
    public bool KillEventEnabled { get; set; }
    public bool ObjectiveEventEnabled { get; set; }
    public bool ClientReminderEnabled { get; set; }
    public bool OtherSystemEventEnabled { get; set; }
    public bool SpeakUrgentPlayerChat { get; set; } = true;
    public bool SpeakImportantPlayerChat { get; set; } = true;
    public bool SpeakNeutralPlayerChat { get; set; }
    public bool ShowFloatingControl { get; set; } = true;
    public int? FloatingControlX { get; set; }
    public int? FloatingControlY { get; set; }
    public bool MinimizeAfterStart { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool OnboardingCompleted { get; set; }
    public string InterfacePalette { get; set; } = "porcelain";
    public string CustomThemeColor { get; set; } = "#6f7f91";
    public bool SaveTranslationHistory { get; set; }
    public RecognitionPipelineMode RecognitionMode { get; set; } = RecognitionPipelineMode.LocalOcr;
    public bool VisionPrivacyConsent { get; set; }
    public int ActivePresetIndex { get; set; } = -1;
    public List<CapturePreset> Presets { get; set; } = new();

    public bool IsSystemMessageEnabled(GameMessageKind kind) => kind switch
    {
        GameMessageKind.TacticalSignal => TacticalSignalEnabled,
        GameMessageKind.KillEvent => KillEventEnabled,
        GameMessageKind.ObjectiveEvent => ObjectiveEventEnabled,
        GameMessageKind.ClientReminder => ClientReminderEnabled,
        GameMessageKind.OtherSystemEvent => OtherSystemEventEnabled,
        _ => false
    };
}

public sealed class CapturePreset
{
    public string Name { get; set; } = string.Empty;
    public ScreenRegion CaptureRegion { get; set; } = new();
    public ScreenRegion? DisplayRegion { get; set; }

    public static List<CapturePreset> CreateDefaults() => new()
    {
        new CapturePreset
        {
            Name = "游戏内聊天 · 左下角聊天框",
            CaptureRegion = ScreenRegion.FromRelative(
                "League of Legends",
                0.006, 0.668, 0.228, 0.238,
                12, 721, 438, 257)
        },
        new CapturePreset
        {
            Name = "英雄选择 · 中间聊天区域",
            CaptureRegion = ScreenRegion.FromRelative(
                "LeagueClientUx",
                0.278, 0.704, 0.444, 0.208,
                533, 760, 853, 225)
        },
        new CapturePreset
        {
            Name = "直播字幕 · 画面底部",
            CaptureRegion = ScreenRegion.FromRelative(
                "",
                0.148, 0.824, 0.704, 0.138,
                284, 889, 1351, 149)
        },
        new CapturePreset { Name = "自定义 1" },
        new CapturePreset { Name = "自定义 2" },
    };
}
