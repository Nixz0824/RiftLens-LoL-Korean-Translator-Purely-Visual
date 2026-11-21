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
    ApiEndpointChanged,
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
    OutputDeviceChanged,
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
    RecognitionModeChanged,
    VisionPrivacyConsentChanged,
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
    string? SilenceReason = null,
    string RecognitionEngine = "",
    string SilenceReasonCode = "");

internal sealed record ApiEndpointUpdate(string BaseUrl, string Model);
