using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public enum PlayerSpeechCategory
{
    Urgent,
    Important,
    Neutral
}

/// <summary>
/// Converts AI intent and priority into the three player-facing speech switches.
/// Translation and overlay display remain independent and are never disabled here.
/// </summary>
public static partial class PlayerSpeechPolicy
{
    public static PlayerSpeechCategory Classify(TranslationResult result)
    {
        var intent = result.Intent?.Trim().ToLowerInvariant() ?? string.Empty;
        if (result.Priority >= 3
            || intent is "warning" or "retreat" or "engage")
        {
            return PlayerSpeechCategory.Urgent;
        }

        if (result.Priority >= 2
            || intent is "assist" or "missing" or "objective" or "resource"
                or "cooldown" or "plan" or "question")
        {
            return PlayerSpeechCategory.Important;
        }

        return PlayerSpeechCategory.Neutral;
    }

    public static bool ShouldSpeak(TranslationResult result, AppSettings settings)
    {
        if (!result.ShouldSpeak
            || string.Equals(result.Intent?.Trim(), "hostility", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Classify(result) switch
        {
            PlayerSpeechCategory.Urgent => settings.SpeakUrgentPlayerChat,
            PlayerSpeechCategory.Important => settings.SpeakImportantPlayerChat,
            _ => settings.SpeakNeutralPlayerChat
        };
    }

    public static SpeechFilterLevel GetModelFilter(AppSettings settings) =>
        settings.SpeakNeutralPlayerChat
            ? SpeechFilterLevel.Full
            : settings.SpeakImportantPlayerChat
                ? SpeechFilterLevel.Standard
                : SpeechFilterLevel.Quiet;

    /// <summary>
    /// Uses only conspicuous player-written danger calls to skip the normal turn
    /// wait. The AI still makes the final category decision.
    /// </summary>
    public static bool IsLikelyUrgent(string content) =>
        !string.IsNullOrWhiteSpace(content) && UrgentCueRegex().IsMatch(content);

    [GeneratedRegex(
        @"(?:조심|위험|후퇴|도망|빼(?:자|요|세요)?|오고\s*있|온다|하지\s*마|들어가(?:지)?\s*마)"
        + @"|(?:小心|危险|撤退|快跑|快走|来了|别打|不要打|别进|不要进)"
        + @"|(?:\bcare(?:ful)?\b|\bdanger\b|\bretreat\b|\bfall\s*back\b|\brun\b|\bincoming\b|\bdon'?t\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrgentCueRegex();
}

public static partial class PlayerSpeechTextSanitizer
{
    public static string Clean(string? text, PlayerSpeechTurn turn)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = LeadingTimestampRegex().Replace(text.Trim(), string.Empty);
        var playerName = ExtractPlayerName(turn.SpeakerRaw);
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            result = Regex.Replace(
                result,
                $@"^\s*{Regex.Escape(playerName)}\s*(?:说|says?)?\s*[,，:：\-–—]?\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!string.IsNullOrWhiteSpace(turn.SpeakerKey)
            && !string.Equals(turn.SpeakerKey, playerName, StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(
                result,
                $@"^\s*{Regex.Escape(turn.SpeakerKey)}\s*(?:说|says?)?\s*[,，:：\-–—]?\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return MultiSpaceRegex()
            .Replace(result, " ")
            .Trim(' ', '\t', ':', '：', ',', '，', '-', '–', '—');
    }

    private static string ExtractPlayerName(string speakerRaw)
    {
        var openBracket = speakerRaw.LastIndexOfAny(new[] { '(', '[', '（' });
        return openBracket > 0
            ? speakerRaw[..openBracket].Trim()
            : speakerRaw.Trim();
    }

    [GeneratedRegex(
        @"^\s*(?:(?:[\[\(]\d{1,2}:\d{2}[\]\)])|(?:\d{1,2}:\d{2}(?=\s)))\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTimestampRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();
}
