namespace RiftLens.Models;

public enum GameMessageKind
{
    PlayerChat,
    TacticalSignal,
    KillEvent,
    ObjectiveEvent,
    ClientReminder,
    OtherSystemEvent,
    Unknown
}

public enum TranslationSourceKind
{
    PlayerChat,
    TacticalSignal,
    SystemMessage,
    Unknown,
    Preview
}

public enum GameSystemEventType
{
    Unknown,
    ChampionKill,
    Execution,
    FirstBlood,
    DoubleKill,
    TripleKill,
    QuadraKill,
    PentaKill,
    KillingSpree,
    Rampage,
    Unstoppable,
    Dominating,
    Godlike,
    Legendary,
    Shutdown,
    Ace,
    ChampionRespawn,
    ObjectiveSpawn,
    ObjectiveSpawnSoon,
    ObjectiveSlain,
    ObjectiveStolen,
    HeraldCaptured,
    HeraldReturned,
    TerrainTransform,
    FirstTurret,
    TurretDestroyed,
    TurretPlateDestroyed,
    InhibitorDestroyed,
    InhibitorRespawned,
    NexusDestroyed,
    ObjectiveBountyClaimed,
    MinionsSpawned,
    PlayerDisconnected,
    PlayerReconnected,
    LoadingTimeout,
    SurrenderVote,
    RemakeVote,
    AfkSurrender,
    GamePaused,
    GameResumed,
    ChatRestricted,
    PingRateLimited,
    NetworkNotice,
    ChampionSpecialEvent,
    WellnessReminder
}

public enum SystemMessageRuleStatus
{
    Current,
    LegacyOrModeSpecific
}

public sealed record GameSystemEvent(
    GameSystemEventType Type,
    string Subject = "",
    string Target = "",
    string Objective = "",
    string Team = "",
    int? Count = null,
    int? DurationSeconds = null,
    double Confidence = 0,
    string Evidence = "",
    string RuleId = "",
    SystemMessageRuleStatus RuleStatus = SystemMessageRuleStatus.Current);

public enum SemanticSourceType
{
    PlayerChat,
    TacticalPing,
    HudShare,
    SystemFeed,
    Unknown
}

public enum SemanticSpeechAct
{
    Marker,
    Status,
    Countdown,
    Request,
    Action,
    Plan,
    Warning,
    Question,
    Information,
    Hostility,
    Noise,
    Unknown
}

public enum SemanticModality
{
    Asserted,
    Requested,
    Planned,
    Marked,
    Possible,
    Unknown
}

public sealed record GameSemanticEvent(
    SemanticSourceType SourceType,
    SemanticSpeechAct SpeechAct,
    string Subject = "",
    string Target = "",
    string Status = "",
    int? DurationSeconds = null,
    SemanticModality Modality = SemanticModality.Unknown,
    double Confidence = 0,
    string Evidence = "");

public sealed record OcrColorEvidence(
    int SampleCount,
    float CyanRatio,
    float NeutralRatio,
    float WarningRatio,
    float PrefixCyanRatio,
    float ContentNeutralRatio)
{
    public bool IsAvailable => SampleCount >= 12;

    public bool SupportsPlayerChat =>
        IsAvailable
        && PrefixCyanRatio >= 0.16f
        && ContentNeutralRatio >= 0.28f
        && ContentNeutralRatio >= PrefixCyanRatio * 0.75f;

    public bool SupportsSystemEvent =>
        IsAvailable
        && WarningRatio >= 0.06f
        && !SupportsPlayerChat;
}

public sealed record OcrRecognizedLine(
    string Text,
    Rectangle Bounds,
    float Confidence,
    OcrColorEvidence? ColorEvidence = null);

public sealed record OcrDocument(
    string RawText,
    IReadOnlyList<OcrRecognizedLine> Lines)
{
    public static OcrDocument FromText(string? text)
    {
        var normalizedLines = (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new OcrRecognizedLine(line, Rectangle.Empty, 0f))
            .ToArray();
        return new OcrDocument(
            string.Join(Environment.NewLine, normalizedLines.Select(line => line.Text)),
            normalizedLines);
    }
}

public sealed record ClassifiedGameMessage(
    GameMessageKind Kind,
    string RawText,
    string Content,
    string SpeakerKey = "",
    string SpeakerRaw = "",
    string TacticalSummary = "",
    int Priority = 0,
    DateTimeOffset CapturedAt = default,
    OcrColorEvidence? ColorEvidence = null,
    string SpeakerHeroRaw = "",
    string SpeakerHeroId = "",
    GameSemanticEvent? SemanticEvent = null,
    GameSystemEvent? SystemEvent = null,
    double ClassificationConfidence = 0,
    string EvaluationLineId = "");

public sealed record PlayerSpeechTurn(
    string SpeakerKey,
    string SpeakerRaw,
    IReadOnlyList<string> RawLines,
    IReadOnlyList<string> Contents,
    DateTimeOffset LastUpdatedAt,
    string SpeakerHeroId = "",
    IReadOnlyList<string>? EvaluationLineIds = null);

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
    [System.Text.Json.Serialization.JsonIgnore]
    public GameMessageKind RoutedMessageKind { get; set; } = GameMessageKind.PlayerChat;
    public double ClassificationConfidence { get; set; } = 1;
    public string ClassificationRuleId { get; set; } = string.Empty;
    public string Speaker { get; set; } = "队友";
    public string Intent { get; set; } = "message";
    public string Emotion { get; set; } = "neutral";
    public int EmotionIntensity { get; set; }
    public int Priority { get; set; } = 1;
    public string SourceType { get; set; } = "player_chat";
    public string SpeechAct { get; set; } = "unknown";
    public string Subject { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DurationSeconds { get; set; }
    public string Modality { get; set; } = "unknown";
    public double SemanticConfidence { get; set; }
    public string Evidence { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SemanticValidationPassed { get; set; }
    public string DisplayTranslation { get; set; } = string.Empty;
    public List<string> DisplayLines { get; set; } = new();
    public string SpokenSummary { get; set; } = string.Empty;
    public bool ToxicityFiltered { get; set; }
    public string SpeechSilenceReason { get; set; } = string.Empty;
    public int ExpiresAfterMs { get; set; } = 6000;
    public string RawText { get; set; } = string.Empty;
    public List<string> OriginalLines { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> EvaluationLineIds { get; set; } = new();
}

public sealed record OcrBatch(string RawText, IReadOnlyList<string> NewLines, DateTimeOffset CapturedAt);
