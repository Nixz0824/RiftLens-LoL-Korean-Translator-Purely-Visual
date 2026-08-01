using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Last policy gate before audio playback. It intentionally reads the current
/// settings again so a queued item cannot bypass a switch changed after OCR.
/// </summary>
public static class FinalSpeechPolicy
{
    public static bool ShouldSpeak(TranslationResult result, AppSettings settings)
    {
        if (!result.ShouldSpeak || string.IsNullOrWhiteSpace(result.SpokenSummary))
        {
            return false;
        }

        return result.SourceKind switch
        {
            TranslationSourceKind.PlayerChat =>
                !ContainsDeterministicNonPlayerContent(result)
                && PlayerSpeechPolicy.ShouldSpeak(result, settings),
            TranslationSourceKind.TacticalSignal =>
                settings.TacticalSignalEnabled
                && result.RoutedMessageKind == GameMessageKind.TacticalSignal
                && result.ClassificationConfidence >= SystemMessageCatalog.SpeechConfidenceThreshold,
            TranslationSourceKind.SystemMessage =>
                settings.IsSystemMessageEnabled(result.RoutedMessageKind)
                && result.ClassificationConfidence >= SystemMessageCatalog.SpeechConfidenceThreshold,
            _ => false
        };
    }

    /// <summary>
    /// Fail closed when a fixed LoL HUD/ping feed accidentally reaches the AI
    /// player-chat path. Re-routing the already generated AI sentence would
    /// preserve invented actions, so the safe fallback is silence.
    /// </summary>
    private static bool ContainsDeterministicNonPlayerContent(TranslationResult result)
    {
        var candidates = (result.OriginalLines ?? new List<string>())
            .Concat((result.RawText ?? string.Empty).Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        foreach (var candidate in candidates)
        {
            var normalized = GameTimestampPrefixParser.Strip(candidate);
            if (GameSemanticEventParser.TryParsePlayerFeed(normalized, out _)
                || GameSemanticEventParser.TryParseSystemFeed(normalized, out _)
                || SystemMessageCatalog.TryClassify(normalized, out _, out _, out _))
            {
                return true;
            }

            var classified = new ChatMessageClassifier().Classify(normalized);
            if (classified.Kind is not GameMessageKind.PlayerChat and not GameMessageKind.Unknown)
            {
                return true;
            }
        }

        return false;
    }
}
