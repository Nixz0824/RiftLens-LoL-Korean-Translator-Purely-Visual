using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RiftLens.Services;

/// <summary>
/// Parses only the leading in-game chat timestamp. OCR damage is accepted only
/// when the candidate is a valid clock value and the remaining text still has
/// a strong chat or fixed-feed structure.
/// </summary>
public static partial class GameTimestampPrefixParser
{
    // LoL displays elapsed game time, not a wall-clock value. Long games can
    // legitimately pass 24:00, so the first component is minutes.
    private const int MaxGameMinutes = 199;

    public static GameTimestampParseResult Parse(string? line)
    {
        var original = line ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return GameTimestampParseResult.NoMatch(original);
        }

        var normalized = original.TrimStart().Normalize(NormalizationForm.FormKC);

        var bracketed = BracketedTimestampRegex().Match(normalized);
        if (TryCreateResult(original, bracketed, requireFollowingEvidence: false,
                "timestamp.bracketed", 0.99, out var result))
        {
            return result;
        }

        var damagedBracket = DamagedBracketTimestampRegex().Match(normalized);
        if (TryCreateResult(original, damagedBracket, requireFollowingEvidence: true,
                "timestamp.ocr_damaged_bracket", 0.90, out result))
        {
            return result;
        }

        var plain = PlainTimestampRegex().Match(normalized);
        if (TryCreateResult(original, plain, requireFollowingEvidence: true,
                "timestamp.plain", 0.92, out result))
        {
            return result;
        }

        var missingColon = MissingColonTimestampRegex().Match(normalized);
        if (TryCreateResult(original, missingColon, requireFollowingEvidence: true,
                "timestamp.ocr_missing_colon", 0.82, out result))
        {
            return result;
        }

        return GameTimestampParseResult.NoMatch(original);
    }

    public static string Strip(string? line) => Parse(line).TextWithoutTimestamp;

    public static string? Format(TimeSpan? timestamp) => timestamp is null
        ? null
        : $"{(int)timestamp.Value.TotalMinutes:00}:{timestamp.Value.Seconds:00}";

    /// <summary>
    /// Final speech output must never start with an in-game clock value. At this
    /// stage the timestamp is always non-semantic, so a valid clock can be
    /// removed without requiring the original chat-structure evidence.
    /// </summary>
    public static string StripForSpeech(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parsed = Parse(text);
        if (parsed.Matched)
        {
            return parsed.TextWithoutTimestamp;
        }

        var normalized = text.TrimStart().Normalize(NormalizationForm.FormKC);
        var match = SpeechTimestampRegex().Match(normalized);
        if (!match.Success
            || !TryParseDigitPart(match.Groups["minutes"].Value, out var gameMinutes)
            || !TryParseDigitPart(match.Groups["seconds"].Value, out var seconds)
            || gameMinutes is < 0 or > MaxGameMinutes
            || seconds is < 0 or > 59)
        {
            return normalized;
        }
        return match.Groups["rest"].Value.TrimStart();
    }

    /// <summary>
    /// Protects the colour-backed "name: content" fallback from treating the
    /// colon inside an unhandled clock prefix as the player/content delimiter.
    /// </summary>
    public static bool StartsWithTimestampLikePrefix(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return TimestampLikePrefixRegex().IsMatch(
            line.TrimStart().Normalize(NormalizationForm.FormKC));
    }

    private static bool TryCreateResult(
        string original,
        Match match,
        bool requireFollowingEvidence,
        string evidence,
        double confidence,
        out GameTimestampParseResult result)
    {
        if (!match.Success
            || !TryParseDigitPart(match.Groups["minutes"].Value, out var gameMinutes)
            || !TryParseDigitPart(match.Groups["seconds"].Value, out var seconds)
            || gameMinutes is < 0 or > MaxGameMinutes
            || seconds is < 0 or > 59)
        {
            result = GameTimestampParseResult.NoMatch(original);
            return false;
        }

        var remainder = match.Groups["rest"].Value.TrimStart();
        if (requireFollowingEvidence && !HasStrongFollowingEvidence(remainder))
        {
            result = GameTimestampParseResult.NoMatch(original);
            return false;
        }

        result = new GameTimestampParseResult(
            true,
            TimeSpan.FromMinutes(gameMinutes) + TimeSpan.FromSeconds(seconds),
            match.Value[..(match.Value.Length - match.Groups["rest"].Value.Length)].TrimEnd(),
            remainder,
            confidence,
            evidence);
        return true;
    }

    private static bool HasStrongFollowingEvidence(string remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return true;
        }

        if (ChannelPrefixRegex().IsMatch(remainder)
            || PlayerWithChampionRegex().IsMatch(remainder)
            || GeneratedHudPrefixRegex().IsMatch(remainder)
            || NameContentRegex().IsMatch(remainder)
            || ReservedSystemPrefixRegex().IsMatch(remainder)
            || FixedFeedSuffixRegex().IsMatch(remainder))
        {
            return true;
        }

        return GameSemanticEventParser.TryParseSystemFeed(remainder, out _)
            || SystemMessageCatalog.TryClassify(remainder, out _, out _, out _);
    }

    private static bool TryParseDigitPart(string value, out int number)
    {
        var corrected = value
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('|', '1');
        return int.TryParse(
            corrected,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    [GeneratedRegex(
        @"^\s*[\[\(（【]\s*(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*[\]\)）】]\s*(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BracketedTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?:[\[\(（【Il|]\s*)?(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*(?:[\]\)）】'’""”\-–—])+\s*(?<rest>.*)$"
        + @"|^\s*[\[\(（【Il|]\s*(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*[Il|1]+\s*(?<rest>.*)$"
        + @"|^\s*[\[\(（【Il|]\s*(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*(?<rest>\S.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DamagedBracketTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*(?:[\]\)）】'’""”\-–—]+\s*)?(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlainTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?:[\[\(（【Il|]\s*)?(?<minutes>[0-9OolI|]{1,3})\s+(?<seconds>[0-9OolI|]{2})\s*(?:[\]\)）】Il|1]\s*)?(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MissingColonTimestampRegex();

    [GeneratedRegex(
        @"^\s*(?:[\[\(（【Il|]\s*)?[0-9OolI|]{1,3}\s*[:：.·]\s*[0-9OolI|]{2}",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampLikePrefixRegex();

    [GeneratedRegex(
        @"^\s*(?:[\[\(（【Il|]\s*)?(?<minutes>[0-9OolI|]{1,3})\s*[:：.·]\s*(?<seconds>[0-9OolI|]{2})\s*(?:[\]\)）】'’""”\-–—]+\s*)?(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SpeechTimestampRegex();

    [GeneratedRegex(
        @"^\s*\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPrefixRegex();

    [GeneratedRegex(
        @"^.{1,64}?[\(\[（].{1,36}?[\)\]）]\s*[:：]\s*\S",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlayerWithChampionRegex();

    [GeneratedRegex(
        @"^[^:：\r\n\(\[（]{1,64}\s*[\(\[（][^:：\r\n\)\]）]{1,36}[\)\]）]",
        RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedHudPrefixRegex();

    [GeneratedRegex(
        @"^[^:：\r\n]{1,64}[:：]\s*\S",
        RegexOptions.CultureInvariant)]
    private static partial Regex NameContentRegex();

    [GeneratedRegex(
        @"^(?:시스템|알림|안내|공지|신호|系统|通知|提示|信号|SYSTEM|NOTICE|SIGNAL)\s*[:：]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedSystemPrefixRegex();

    [GeneratedRegex(
        @"(?:님?(?:이|가)|처치했|파괴했|강탈했|퀘스트\s*완료|알림|신호|아이템|지목|사용|재생성|대기\s*시간|마나\s*\d{1,3}\s*%|가고\s*있음)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FixedFeedSuffixRegex();
}

public sealed record GameTimestampParseResult(
    bool Matched,
    TimeSpan? Timestamp,
    string OriginalPrefix,
    string TextWithoutTimestamp,
    double Confidence,
    string Evidence)
{
    public static GameTimestampParseResult NoMatch(string original) =>
        new(false, null, string.Empty, original.TrimStart(), 0, string.Empty);
}
