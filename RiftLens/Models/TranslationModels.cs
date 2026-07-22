namespace RiftLens.Models;

public enum GameMessageKind
{
    PlayerChat,
    TacticalSignal,
    KillEvent,
    SystemEvent,
    Unknown
}

public enum TranslationSourceKind
{
    PlayerChat,
    TacticalSignal,
    Preview
}

public sealed record ClassifiedGameMessage(
    GameMessageKind Kind,
    string RawText,
    string Content,
    string SpeakerKey = "",
    string SpeakerRaw = "",
    string TacticalSummary = "",
    int Priority = 0,
    DateTimeOffset CapturedAt = default);

public sealed record PlayerSpeechTurn(
    string SpeakerKey,
    string SpeakerRaw,
    IReadOnlyList<string> RawLines,
    IReadOnlyList<string> Contents,
    DateTimeOffset LastUpdatedAt);

public sealed record HeroMention(
    string Id,
    string Key,
    string KoreanName,
    string ChineseOfficial,
    string ChinesePersonal,
    string SpokenName);

public sealed record TranslationRequest(
    IReadOnlyList<string> Lines,
    IReadOnlyList<HeroMention> Heroes,
    SpeechFilterLevel FilterLevel,
    int MaxSummaryChars,
    string SpeakerLabel = "队友",
    bool KeepEmotion = true,
    AppLanguage Language = AppLanguage.Chinese);

public sealed class TranslationResult
{
    public TranslationSourceKind SourceKind { get; set; } = TranslationSourceKind.PlayerChat;
    public bool ShouldSpeak { get; set; }
    public string Speaker { get; set; } = "队友";
    public string Intent { get; set; } = "message";
    public string Emotion { get; set; } = "neutral";
    public int EmotionIntensity { get; set; }
    public int Priority { get; set; } = 1;
    public string DisplayTranslation { get; set; } = string.Empty;
    public string SpokenSummary { get; set; } = string.Empty;
    public bool ToxicityFiltered { get; set; }
    public int ExpiresAfterMs { get; set; } = 6000;
    public string RawText { get; set; } = string.Empty;
}

public sealed record OcrBatch(string RawText, IReadOnlyList<string> NewLines, DateTimeOffset CapturedAt);
