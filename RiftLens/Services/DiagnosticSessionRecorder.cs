using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Keeps a bounded diagnostic trace in memory. Nothing is persisted until the
/// user explicitly exports it; screenshots and credentials are never recorded.
/// </summary>
public sealed class DiagnosticSessionRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly List<DiagnosticClassificationRecord> _classifications = new();
    private readonly List<DiagnosticOutputRecord> _outputs = new();
    private readonly Dictionary<string, string> _speakerAliases = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _classifications.Count + _outputs.Count;
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _classifications.Clear();
            _outputs.Clear();
            _speakerAliases.Clear();
        }
    }

    public void RecordClassification(MessageDiagnosticObservation observation)
    {
        lock (_sync)
        {
            var message = observation.Message;
            var alias = GetSpeakerAlias(message.SpeakerKey);
            var rawRedacted = RedactLine(observation.Line.RawText, message, alias);
            var normalizedRedacted = RedactLine(observation.Line.NormalizedText, message, alias);
            var subtype = message.SystemEvent?.Type.ToString()
                ?? message.SemanticEvent?.SpeechAct.ToString()
                ?? "Unknown";
            var ruleId = message.SystemEvent?.RuleId
                ?? (message.SemanticEvent is not null ? "signal.semantic" : string.Empty);

            _classifications.Add(new DiagnosticClassificationRecord(
                observation.CapturedAt,
                observation.FrameId,
                observation.LineId,
                rawRedacted,
                normalizedRedacted,
                observation.Line.Timestamp.Matched,
                GameTimestampPrefixParser.Format(observation.Line.Timestamp.Timestamp),
                observation.Line.Timestamp.OriginalPrefix,
                observation.Line.Timestamp.Evidence,
                observation.Line.Timestamp.Confidence,
                message.Kind.ToString(),
                subtype,
                ResolveOrigin(message),
                message.SemanticEvent?.SpeechAct.ToString() ?? "",
                ruleId,
                message.ClassificationConfidence,
                observation.CategoryEnabled,
                message.Kind == GameMessageKind.Unknown
                    ? "display_only_unknown"
                    : observation.CategoryEnabled ? "routed" : "disabled_by_category_switch"));
            TrimToLimit(_classifications, 1200);
        }
    }

    public void RecordOutput(TranslationResult result, bool finalSpeechAllowed)
    {
        lock (_sync)
        {
            _outputs.Add(new DiagnosticOutputRecord(
                DateTimeOffset.Now,
                result.EvaluationLineIds.Take(8).ToArray(),
                result.SourceKind.ToString(),
                result.RoutedMessageKind.ToString(),
                result.Intent,
                result.SourceType,
                result.SpeechAct,
                result.ClassificationRuleId,
                result.ClassificationConfidence,
                result.SemanticConfidence,
                result.OriginalLines.Take(8).ToArray(),
                result.DisplayLines.Count > 0
                    ? result.DisplayLines.Take(8).ToArray()
                    : new[] { result.DisplayTranslation },
                finalSpeechAllowed ? result.SpokenSummary : string.Empty,
                finalSpeechAllowed,
                finalSpeechAllowed
                    ? "spoken"
                    : result.SourceKind == TranslationSourceKind.Unknown
                        ? "silent_unknown"
                        : "silent_by_policy_or_switch"));
            TrimToLimit(_outputs, 600);
        }
    }

    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        DiagnosticExport export;
        lock (_sync)
        {
            export = new DiagnosticExport(
                "RiftLens diagnostic trace",
                "2.1",
                DateTimeOffset.Now,
                "User-initiated local export. No screenshots, API keys, credentials, or game-memory data are included. Player IDs are replaced with session aliases.",
                _classifications.ToArray(),
                _outputs.ToArray());
        }

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private string GetSpeakerAlias(string speakerKey)
    {
        if (string.IsNullOrWhiteSpace(speakerKey))
        {
            return string.Empty;
        }
        if (_speakerAliases.TryGetValue(speakerKey, out var existing))
        {
            return existing;
        }
        var alias = $"Player-{_speakerAliases.Count + 1:00}";
        _speakerAliases[speakerKey] = alias;
        return alias;
    }

    private static string RedactLine(
        string line,
        ClassifiedGameMessage message,
        string alias)
    {
        var redacted = line;
        if (!string.IsNullOrWhiteSpace(alias)
            && !string.IsNullOrWhiteSpace(message.SpeakerRaw))
        {
            var replacement = string.IsNullOrWhiteSpace(message.SpeakerHeroRaw)
                ? alias
                : $"{alias} ({message.SpeakerHeroRaw})";
            redacted = Regex.Replace(
                redacted,
                Regex.Escape(message.SpeakerRaw),
                _ => replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (message.SystemEvent is { Subject.Length: > 0 } systemEvent
            && systemEvent.Subject.Length >= 3)
        {
            redacted = redacted.Replace(
                systemEvent.Subject,
                "Player",
                StringComparison.OrdinalIgnoreCase);
        }
        return redacted;
    }

    private static string ResolveOrigin(ClassifiedGameMessage message) => message.Kind switch
    {
        GameMessageKind.PlayerChat => "player_authored_chat",
        GameMessageKind.TacticalSignal when !string.IsNullOrWhiteSpace(message.SpeakerKey) =>
            "player_initiated_game_signal",
        GameMessageKind.TacticalSignal => "game_generated_signal",
        GameMessageKind.Unknown => "unknown",
        _ => "game_system"
    };

    private static void TrimToLimit<T>(List<T> items, int limit)
    {
        if (items.Count > limit)
        {
            items.RemoveRange(0, items.Count - limit);
        }
    }

    private sealed record DiagnosticExport(
        string Format,
        string SchemaVersion,
        DateTimeOffset ExportedAt,
        string Privacy,
        IReadOnlyList<DiagnosticClassificationRecord> Classifications,
        IReadOnlyList<DiagnosticOutputRecord> Outputs);

    private sealed record DiagnosticClassificationRecord(
        DateTimeOffset CapturedAt,
        string FrameId,
        string LineId,
        string RawOcrRedacted,
        string TextAfterTimestampRedacted,
        bool TimestampRemoved,
        string? Timestamp,
        string TimestampPrefix,
        string TimestampEvidence,
        double TimestampConfidence,
        string Category,
        string Subtype,
        string Origin,
        string SpeechAct,
        string RuleId,
        double ClassificationConfidence,
        bool CategorySwitchEnabled,
        string RouteDecision);

    private sealed record DiagnosticOutputRecord(
        DateTimeOffset ProducedAt,
        IReadOnlyList<string> EvaluationLineIds,
        string SourceKind,
        string RoutedCategory,
        string Intent,
        string SourceType,
        string SpeechAct,
        string RuleId,
        double ClassificationConfidence,
        double SemanticConfidence,
        IReadOnlyList<string> OriginalContentWithoutPlayerId,
        IReadOnlyList<string> DisplayLines,
        string SpokenText,
        bool SpeechAllowed,
        string FinalDecision);
}

public sealed record MessageDiagnosticObservation(
    DateTimeOffset CapturedAt,
    DeduplicatedMessageLine Line,
    ClassifiedGameMessage Message,
    bool CategoryEnabled,
    string FrameId = "",
    string LineId = "");
