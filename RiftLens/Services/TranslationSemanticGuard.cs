using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Rejects unsupported semantic claims from free-form AI summaries. The overlay
/// still keeps the model's one-to-one literal translation; unsafe speech becomes
/// silent instead of guessing what a player or ping meant.
/// </summary>
public static partial class TranslationSemanticGuard
{
    private static readonly HashSet<string> AllowedSpeechActs = new(
        new[]
        {
            "marker", "status", "countdown", "request", "action", "plan",
            "warning", "question", "information", "hostility", "noise"
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedModalities = new(
        new[] { "asserted", "requested", "planned", "marked", "possible", "unknown" },
        StringComparer.OrdinalIgnoreCase);

    public static void Apply(TranslationResult result, TranslationRequest request)
    {
        result.SourceType = NormalizeToken(result.SourceType, "player_chat");
        result.SpeechAct = NormalizeToken(result.SpeechAct, "unknown");
        result.Modality = NormalizeToken(result.Modality, "unknown");
        result.Subject = result.Subject?.Trim() ?? string.Empty;
        result.Target = result.Target?.Trim() ?? string.Empty;
        result.Status = result.Status?.Trim() ?? string.Empty;
        result.Evidence = result.Evidence?.Trim() ?? string.Empty;
        result.SemanticConfidence = Math.Clamp(result.SemanticConfidence, 0, 1);

        var source = Normalize(string.Join("\n", request.Lines));
        var spoken = Normalize(result.SpokenSummary);
        var evidence = Normalize(result.Evidence);
        var evidenceSupported = evidence.Length >= 2
            && source.Contains(evidence, StringComparison.OrdinalIgnoreCase);
        var schemaSupported = string.Equals(
                result.SourceType,
                "player_chat",
                StringComparison.OrdinalIgnoreCase)
            && AllowedSpeechActs.Contains(result.SpeechAct)
            && AllowedModalities.Contains(result.Modality);
        var numbersSupported = NumbersArePreserved(source, spoken, result.DurationSeconds);
        var negationSupported = !ContainsNegation(source) || ContainsNegation(spoken);
        var actionSupported = !ClaimsOngoingAction(spoken)
            || ContainsExplicitActionEvidence(source);
        var separatorSupported = !NegativeCountdownRegex().IsMatch(spoken);

        result.SemanticValidationPassed = result.SemanticConfidence >= 0.62
            && schemaSupported
            && evidenceSupported
            && numbersSupported
            && negationSupported
            && actionSupported
            && separatorSupported;

        if (result.SemanticValidationPassed)
        {
            return;
        }

        // A failed semantic summary must never be spoken. The UI continues to
        // show displayLines, which are separately constrained to literal output.
        result.ShouldSpeak = false;
        if (string.IsNullOrWhiteSpace(result.SpeechSilenceReason))
        {
            result.SpeechSilenceReason = SpeechSilenceReasons.SemanticGuard;
        }
    }

    public static bool NumbersArePreserved(
        string source,
        string spoken,
        int? durationSeconds = null)
    {
        var sourceNumbers = NumberRegex().Matches(source)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
        var spokenNumbers = NumberRegex().Matches(spoken)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (spokenNumbers.Except(sourceNumbers).Any())
        {
            return false;
        }
        if (sourceNumbers.Except(spokenNumbers).Any())
        {
            return false;
        }
        return durationSeconds is null
            || sourceNumbers.Contains(durationSeconds.Value.ToString())
               && spokenNumbers.Contains(durationSeconds.Value.ToString());
    }

    private static bool ContainsNegation(string value) =>
        NegationRegex().IsMatch(value);

    private static bool ClaimsOngoingAction(string value) =>
        OngoingActionRegex().IsMatch(value);

    private static bool ContainsExplicitActionEvidence(string value) =>
        ExplicitActionEvidenceRegex().IsMatch(value);

    private static string NormalizeToken(string? value, string fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string Normalize(string? value) =>
        Regex.Replace(
                (value ?? string.Empty)
                    .Normalize(NormalizationForm.FormKC)
                    .Replace('−', '-')
                    .Replace('–', '-')
                    .Replace('—', '-'),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(
        @"(?:不|没|无|别|勿|不要|不能|不会|并非|未|안\s|못\s|없|아니|하지\s*마|말고|"
        + @"\b(?:no|not|never|without|don['’]?t|can['’]?t|won['’]?t|isn['’]?t|aren['’]?t)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegationRegex();

    [GeneratedRegex(
        @"(?:正在|已经|正(?:在)?)(?:打|做|拿|开|攻击|争夺|赶往|前往)|"
        + @"\b(?:is|are|we['’]?re|they['’]?re)\s+(?:currently\s+)?(?:doing|taking|fighting|attacking|killing|starting)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OngoingActionRegex();

    [GeneratedRegex(
        @"(?:正在|已经|在打|在做|在拿|开打|攻击|争夺|赶往|前往|"
        + @"치는\s*중|잡는\s*중|먹는\s*중|하고\s*있|가는\s*중|공격\s*중|"
        + @"\b(?:doing|taking|fighting|attacking|killing|starting|on\s+my\s+way)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitActionEvidenceRegex();

    [GeneratedRegex(
        @"(?:负\s*\d+\s*秒|-\s*\d+\s*(?:秒|seconds?|s\b))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegativeCountdownRegex();
}
