namespace RiftLens.Models;

public enum AppLanguage
{
    Chinese,
    English
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

    public bool IsValid => Width >= 16 && Height >= 12;

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static ScreenRegion FromRectangle(Rectangle rectangle) => new()
    {
        X = rectangle.X,
        Y = rectangle.Y,
        Width = rectangle.Width,
        Height = rectangle.Height
    };

    public override string ToString() => IsValid ? $"{Width} × {Height} @ ({X}, {Y})" : "尚未选择";
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 4;
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
    public int OverlayDurationMs { get; set; } = 5200;
    public bool ShowOriginalText { get; set; }
    public int SpeechVolume { get; set; } = 65;
    public int SpeechRate { get; set; } = 1;
    public SpeechFilterLevel SpeechFilter { get; set; } = SpeechFilterLevel.Standard;
    public SystemMessageFilterMode SystemMessageFilter { get; set; } = SystemMessageFilterMode.Balanced;
    public bool UseXiaoxiaoNeural { get; set; } = true;
    public int SummaryWindowMs { get; set; } = 900;
    public int MaxSummaryChars { get; set; } = 64;
    public bool KeepEmotionSummary { get; set; } = true;
    public bool PreferChampionPersonalName { get; set; } = true;
    public bool SignalVoiceEnabled { get; set; } = true;
    public bool ShowFloatingControl { get; set; } = true;
    public bool MinimizeAfterStart { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public int ActivePresetIndex { get; set; } = -1;
    public List<CapturePreset> Presets { get; set; } = new();
}

public sealed class CapturePreset
{
    public string Name { get; set; } = string.Empty;
    public ScreenRegion CaptureRegion { get; set; } = new();
    public ScreenRegion? DisplayRegion { get; set; }

    public static List<CapturePreset> CreateDefaults() => new()
    {
        new CapturePreset { Name = "游戏内聊天 · 右下角聊天框" },
        new CapturePreset { Name = "英雄选择 · 中间聊天区域" },
        new CapturePreset { Name = "直播字幕 · 画面底部" },
        new CapturePreset { Name = "自定义 1" },
        new CapturePreset { Name = "自定义 2" },
    };
}
