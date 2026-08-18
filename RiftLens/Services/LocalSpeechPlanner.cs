using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Builds spoken text and a speak/silence decision from literal display lines.
/// The model is only asked to translate; this type owns narration policy.
/// </summary>
public static class LocalSpeechPlanner
{
    public static void Apply(TranslationResult result, TranslationRequest request)
    {
        result.Speaker = string.IsNullOrWhiteSpace(request.SpeakerLabel)
            ? request.Language == AppLanguage.English ? "Teammate" : "队友"
            : request.SpeakerLabel;

        var maximumDisplayLines = Math.Clamp(request.Lines.Count, 1, 8);
        result.DisplayLines = (result.DisplayLines ?? new List<string>())
            .Select(line => StripSpeakerPrefix(line, result.Speaker))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(maximumDisplayLines)
            .ToList();

        if (result.DisplayLines.Count == 0 && !string.IsNullOrWhiteSpace(result.DisplayTranslation))
        {
            result.DisplayLines = result.DisplayTranslation
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(line => StripSpeakerPrefix(line, result.Speaker))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(maximumDisplayLines)
                .ToList();
        }

        if (result.DisplayLines.Count > 0)
        {
            var body = string.Join(Environment.NewLine, result.DisplayLines);
            result.DisplayTranslation = request.Language == AppLanguage.English
                ? $"{result.Speaker}: {body}"
                : $"{result.Speaker}：{body}";
            result.SpokenSummary = string.Join(
                request.Language == AppLanguage.English ? ". " : "。",
                result.DisplayLines.Select(line => line.Trim().TrimEnd('.', '。', '!', '！')));
        }

        InferCategory(result, request);
        result.SourceType = "player_chat";
        result.SpeechAct = result.Priority >= 3 ? "warning" : result.Priority >= 2 ? "request" : "information";
        result.Modality = "asserted";
        result.Evidence = request.Lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .OrderByDescending(line => line.Length)
            .FirstOrDefault() ?? string.Empty;
        result.SemanticConfidence = 0.8;

        if (result.ToxicityFiltered || IsHostileSource(request.Lines))
        {
            result.ToxicityFiltered = true;
            result.ShouldSpeak = false;
            result.Intent = "hostility";
            result.SpeechSilenceReason = SpeechSilenceReasons.Toxicity;
        }
        else
        {
            result.ShouldSpeak = result.DisplayLines.Count > 0
                && !string.IsNullOrWhiteSpace(result.SpokenSummary);
            if (!result.ShouldSpeak)
            {
                result.SpeechSilenceReason = SpeechSilenceReasons.EmptySpeech;
            }
        }

        TranslationSemanticGuard.Apply(result, request);

        if (result.ShouldSpeak && !string.IsNullOrWhiteSpace(result.SpokenSummary)
            && !result.SpokenSummary.StartsWith(result.Speaker, StringComparison.OrdinalIgnoreCase))
        {
            result.SpokenSummary = request.Language == AppLanguage.English
                ? $"{result.Speaker}: {result.SpokenSummary}"
                : $"{result.Speaker}说，{result.SpokenSummary}";
        }

        if (result.SpokenSummary.Length > request.MaxSummaryChars + 16)
        {
            result.SpokenSummary = result.SpokenSummary[..Math.Min(result.SpokenSummary.Length, request.MaxSummaryChars + 4)]
                + (request.Language == AppLanguage.English ? "." : "。");
        }

        if (string.IsNullOrWhiteSpace(result.SpokenSummary))
        {
            result.ShouldSpeak = false;
            if (string.IsNullOrWhiteSpace(result.SpeechSilenceReason))
            {
                result.SpeechSilenceReason = SpeechSilenceReasons.EmptySpeech;
            }
        }
    }

    public static string FormatReason(string? reason, AppLanguage language) => reason switch
    {
        SpeechSilenceReasons.Toxicity => language == AppLanguage.English
            ? "Text only · abuse filtered"
            : "仅字幕 · 已过滤攻击",
        SpeechSilenceReasons.SemanticGuard => language == AppLanguage.English
            ? "Text only · speech did not pass the evidence check"
            : "仅字幕 · 语义未通过",
        SpeechSilenceReasons.UserFilter => language == AppLanguage.English
            ? "Text only · this speech tier is off"
            : "仅字幕 · 当前策略不播报",
        SpeechSilenceReasons.OverlayOnly => language == AppLanguage.English
            ? "Text only · speech output is off"
            : "仅字幕 · 未开语音",
        SpeechSilenceReasons.HudLeak => language == AppLanguage.English
            ? "Text only · fixed game text was blocked"
            : "仅字幕 · 固定 HUD 已拦截",
        SpeechSilenceReasons.EmptySpeech => language == AppLanguage.English
            ? "Text only · nothing safe to read"
            : "仅字幕 · 无可读摘要",
        _ => language == AppLanguage.English ? "Text only" : "仅字幕"
    };

    private static void InferCategory(TranslationResult result, TranslationRequest request)
    {
        var source = string.Join('\n', request.Lines);
        if (PlayerSpeechPolicy.IsLikelyUrgent(source))
        {
            result.Intent = "warning";
            result.Priority = 3;
            return;
        }

        if (LooksImportant(source))
        {
            result.Intent = "assist";
            result.Priority = 2;
            return;
        }

        result.Intent = string.IsNullOrWhiteSpace(result.Intent) ? "chitchat" : result.Intent;
        result.Priority = result.Priority > 0 ? result.Priority : 1;
    }

    private static bool LooksImportant(string source) =>
        source.Contains("합류", StringComparison.OrdinalIgnoreCase)
        || source.Contains("지원", StringComparison.OrdinalIgnoreCase)
        || source.Contains("미아", StringComparison.OrdinalIgnoreCase)
        || source.Contains("바론", StringComparison.OrdinalIgnoreCase)
        || source.Contains("용", StringComparison.Ordinal)
        || source.Contains("支援", StringComparison.Ordinal)
        || source.Contains("消失", StringComparison.Ordinal)
        || source.Contains("大龙", StringComparison.Ordinal)
        || source.Contains("小龙", StringComparison.Ordinal)
        || source.Contains("help", StringComparison.OrdinalIgnoreCase)
        || source.Contains("miss", StringComparison.OrdinalIgnoreCase)
        || source.Contains("baron", StringComparison.OrdinalIgnoreCase)
        || source.Contains("dragon", StringComparison.OrdinalIgnoreCase);

    private static bool IsHostileSource(IReadOnlyList<string> lines)
    {
        var source = string.Join('\n', lines);
        return source.Contains("욕설", StringComparison.Ordinal)
            || source.Contains("씨발", StringComparison.Ordinal)
            || source.Contains("병신", StringComparison.Ordinal)
            || source.Contains("fuck", StringComparison.OrdinalIgnoreCase)
            || source.Contains("shit", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripSpeakerPrefix(string? value, string speaker)
    {
        var line = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(speaker))
        {
            return line;
        }

        if (!line.StartsWith(speaker, StringComparison.OrdinalIgnoreCase))
        {
            return line.TrimStart('•', '·', '-', '—', ' ');
        }

        return line[speaker.Length..]
            .TrimStart(' ', '\t', ':', '：', '-', '—', '•', '·')
            .Trim();
    }
}

public static class SpeechSilenceReasons
{
    public const string Toxicity = "toxicity";
    public const string SemanticGuard = "semantic";
    public const string UserFilter = "policy";
    public const string OverlayOnly = "overlay_only";
    public const string HudLeak = "hud";
    public const string EmptySpeech = "empty";
}
