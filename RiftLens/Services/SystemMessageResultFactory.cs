using RiftLens.Models;

namespace RiftLens.Services;

public static class SystemMessageResultFactory
{
    public static TranslationResult Create(
        ClassifiedGameMessage message,
        AppSettings settings)
    {
        if (message.Kind == GameMessageKind.Unknown)
        {
            return CreateUnknown(message, settings.Language);
        }
        if (message.Kind is GameMessageKind.PlayerChat or GameMessageKind.TacticalSignal)
        {
            throw new ArgumentException("Expected a fixed system message or Unknown.", nameof(message));
        }

        var spoken = SystemMessageTemplates.Format(message, settings.Language);
        return new TranslationResult
        {
            RecognitionEngine = message.RecognitionEngine,
            SourceKind = TranslationSourceKind.SystemMessage,
            RoutedMessageKind = message.Kind,
            ClassificationConfidence = message.ClassificationConfidence,
            ClassificationRuleId = message.SystemEvent?.RuleId ?? string.Empty,
            Speaker = settings.Language == AppLanguage.English ? "Game" : "系统",
            Intent = message.Kind switch
            {
                GameMessageKind.KillEvent => "kill_event",
                GameMessageKind.ObjectiveEvent => "objective_event",
                GameMessageKind.ClientReminder => "client_reminder",
                _ => "system_event"
            },
            SourceType = "system_feed",
            SpeechAct = "information",
            Modality = "asserted",
            SemanticConfidence = message.ClassificationConfidence,
            Evidence = message.SystemEvent?.Evidence ?? message.Content,
            SemanticValidationPassed = message.SystemEvent is not null,
            Priority = message.Priority,
            DisplayTranslation = spoken,
            DisplayLines = new List<string> { spoken },
            SpokenSummary = spoken,
            RawText = message.RawText,
            OriginalLines = new List<string> { message.Content },
            EvaluationLineIds = string.IsNullOrWhiteSpace(message.EvaluationLineId)
                ? new List<string>()
                : new List<string> { message.EvaluationLineId },
            ShouldSpeak = message.ClassificationConfidence
                >= SystemMessageCatalog.SpeechConfidenceThreshold,
            ExpiresAfterMs = 3500
        };
    }

    private static TranslationResult CreateUnknown(
        ClassifiedGameMessage message,
        AppLanguage language)
    {
        var label = language == AppLanguage.English ? "Unclassified" : "未分类";
        return new TranslationResult
        {
            RecognitionEngine = message.RecognitionEngine,
            SourceKind = TranslationSourceKind.Unknown,
            RoutedMessageKind = GameMessageKind.Unknown,
            ClassificationConfidence = message.ClassificationConfidence,
            Speaker = label,
            Intent = "unknown",
            SourceType = "unknown",
            SpeechAct = "unknown",
            Modality = "unknown",
            Evidence = message.Content,
            Priority = 0,
            DisplayTranslation = message.Content,
            DisplayLines = new List<string> { message.Content },
            SpokenSummary = string.Empty,
            RawText = message.RawText,
            OriginalLines = new List<string> { message.Content },
            EvaluationLineIds = string.IsNullOrWhiteSpace(message.EvaluationLineId)
                ? new List<string>()
                : new List<string> { message.EvaluationLineId },
            ShouldSpeak = false,
            ExpiresAfterMs = 3500
        };
    }
}
