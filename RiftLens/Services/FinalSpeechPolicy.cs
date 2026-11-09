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
        if (result.Lifetime is { IsExpired: true }
            || !result.ShouldSpeak || string.IsNullOrWhiteSpace(result.SpokenSummary))
        {
            return false;
        }

        return result.SourceKind switch
        {
            TranslationSourceKind.PlayerChat =>
                !ContainsDeterministicNonPlayerContent(result)
                && PassesPlayerSpeechQuality(result, settings.Language)
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

    public static string? DescribeSilence(TranslationResult result, AppSettings settings)
    {
        if (result.Lifetime is { IsExpired: true }) return SpeechSilenceReasons.Expired;
        if (settings.OutputMode == OutputMode.OverlayOnly)
        {
            return SpeechSilenceReasons.OverlayOnly;
        }

        if (!string.IsNullOrWhiteSpace(result.SpeechSilenceReason))
        {
            return result.SpeechSilenceReason;
        }

        if (result.ToxicityFiltered)
        {
            return SpeechSilenceReasons.Toxicity;
        }

        if (result.SourceKind == TranslationSourceKind.PlayerChat
            && ContainsDeterministicNonPlayerContent(result))
        {
            return SpeechSilenceReasons.HudLeak;
        }

        if (result.SourceKind == TranslationSourceKind.PlayerChat
            && !PassesPlayerSpeechQuality(result, settings.Language))
        {
            return SpeechSilenceReasons.TranslationQuality;
        }

        if (!result.ShouldSpeak || string.IsNullOrWhiteSpace(result.SpokenSummary))
        {
            return SpeechSilenceReasons.UserFilter;
        }

        return null;
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
            if ((GameSemanticEventParser.TryParsePlayerFeed(normalized, out var playerFeed)
                    && !string.Equals(playerFeed.Status, "item_marker", StringComparison.Ordinal))
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

        return candidates.Any() && candidates.All(NonPlayerContentGate.IsFixedNonChatPayload);
    }

    private static bool PassesPlayerSpeechQuality(
        TranslationResult result,
        AppLanguage language)
    {
        var source = string.Join(
            " ",
            (result.OriginalLines ?? new List<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        var readableSource = source.Count(char.IsLetterOrDigit);
        if (readableSource < 3 || source.All(character => !char.IsLetter(character)))
        {
            return false;
        }

        if (language == AppLanguage.Chinese)
        {
            var residualHangul = result.SpokenSummary.Count(character =>
                character is >= '\uAC00' and <= '\uD7A3'
                or >= '\u3130' and <= '\u318F');
            if (residualHangul >= 2)
            {
                return false;
            }
        }

        return result.SpokenSummary.Count(char.IsLetterOrDigit) >= 2;
    }
}
