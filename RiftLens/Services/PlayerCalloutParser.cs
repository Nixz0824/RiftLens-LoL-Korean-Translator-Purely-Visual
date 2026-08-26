using System.Globalization;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public enum PlayerCalloutKind
{
    PassThrough,
    Formatted,
    Silence
}

/// <summary>
/// Local meaning for a few player-authored callouts that the translation model
/// routinely mangles: flash-timer slang, lane flash status, and enemy item-bar
/// inspect shares. Unresolved OCR that only looks like those patterns is silenced
/// instead of being guessed into unknown champion or item names.
/// </summary>
public static partial class PlayerCalloutParser
{
    public static PlayerCalloutKind TryFormat(
        string? content,
        AppLanguage language,
        HeroDictionary? heroes,
        bool preferPersonalName,
        out string displayLine)
    {
        displayLine = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return PlayerCalloutKind.PassThrough;
        }

        var normalized = Normalize(content);
        if (TryFormatFlashTimers(normalized, language, out displayLine)
            || TryFormatFlashStatus(normalized, language, out displayLine))
        {
            return PlayerCalloutKind.Formatted;
        }

        var inspect = TryFormatItemInspect(
            normalized,
            language,
            heroes,
            preferPersonalName,
            out displayLine);
        if (inspect != PlayerCalloutKind.PassThrough)
        {
            return inspect;
        }

        if (LooksLikeUnresolvedStructuredShare(normalized))
        {
            return PlayerCalloutKind.Silence;
        }

        return PlayerCalloutKind.PassThrough;
    }

    public static bool IsLaneToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return LaneTokenRegex().IsMatch(Normalize(value));
    }

    private static bool TryFormatFlashTimers(
        string normalized,
        AppLanguage language,
        out string displayLine)
    {
        displayLine = string.Empty;
        var matches = TimeLaneRegex().Matches(normalized);
        if (matches.Count == 0)
        {
            return false;
        }

        var remainder = FlashNoiseRegex().Replace(TimeLaneRegex().Replace(normalized, string.Empty), string.Empty)
            .Trim(' ', '.', '。', '!', '！', '/', '-', '_');
        if (remainder.Length > 0)
        {
            return false;
        }

        var parts = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            if (!TryParseClock(match.Groups["time"].Value, out var minutes, out var seconds)
                || !TryLocalizeLane(match.Groups["lane"].Value, language, out var lane))
            {
                return false;
            }

            parts.Add(language == AppLanguage.English
                ? $"{lane} flashed at {FormatEnglishClock(minutes, seconds)}"
                : $"{lane}{FormatChineseClock(minutes, seconds)}交了闪现");
        }

        displayLine = language == AppLanguage.English
            ? string.Join(". ", parts)
            : string.Join("。", parts);
        return parts.Count > 0;
    }

    private static bool TryFormatFlashStatus(
        string normalized,
        AppLanguage language,
        out string displayLine)
    {
        displayLine = string.Empty;
        var match = FlashStatusRegex().Match(normalized);
        if (!match.Success || !TryLocalizeLane(match.Groups["lane"].Value, language, out var lane))
        {
            return false;
        }

        var negation = match.Groups["negation"].Value + match.Groups["negation2"].Value;
        var state = match.Groups["state"].Value;
        if (string.IsNullOrWhiteSpace(state) && string.IsNullOrWhiteSpace(negation))
        {
            displayLine = language == AppLanguage.English ? $"{lane} Flash" : $"{lane}闪现";
            return true;
        }

        var available = IsFlashAvailable(state, negation);
        displayLine = language == AppLanguage.English
            ? available ? $"{lane} still has Flash" : $"{lane} has no Flash"
            : available ? $"{lane}还有闪现" : $"{lane}没有闪现";
        return true;
    }

    private static PlayerCalloutKind TryFormatItemInspect(
        string normalized,
        AppLanguage language,
        HeroDictionary? heroes,
        bool preferPersonalName,
        out string displayLine)
    {
        displayLine = string.Empty;
        if (!TryResolveItemInspect(normalized, out var subject, out var item))
        {
            if (LooksLikeSmashedHeroTerm(normalized, heroes))
            {
                return PlayerCalloutKind.Silence;
            }

            return PlayerCalloutKind.PassThrough;
        }

        var hero = ResolveHeroSpokenName(subject, language, heroes, preferPersonalName);
        if (string.IsNullOrWhiteSpace(hero))
        {
            return PlayerCalloutKind.Silence;
        }

        var itemName = language == AppLanguage.English ? item.English : item.Chinese;
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return PlayerCalloutKind.Silence;
        }

        displayLine = language == AppLanguage.English
            ? $"{hero} bought {itemName}"
            : $"{hero}买了{itemName}";
        return PlayerCalloutKind.Formatted;
    }

    private static bool TryResolveItemInspect(
        string normalized,
        out string subject,
        out GameTermResolution item)
    {
        if (GameTermGlossary.TryResolveOfficialSuffix(normalized, "item", requireBoundary: true, out subject, out item)
            || GameTermGlossary.TryResolveOfficialSuffix(normalized, "item", requireBoundary: false, out subject, out item)
            || GameTermGlossary.TryResolveNearMissOfficialSuffix(normalized, "item", out subject, out item))
        {
            subject = subject.Trim(' ', '-', ':', '：', '.', '·');
            return subject.Length is >= 2 and <= 24
                && !ContainsSentenceNoise(subject);
        }

        subject = string.Empty;
        item = GameTermResolution.Empty;
        return false;
    }

    private static string ResolveHeroSpokenName(
        string subject,
        AppLanguage language,
        HeroDictionary? heroes,
        bool preferPersonalName)
    {
        if (heroes is null)
        {
            return string.Empty;
        }

        var exactId = heroes.FindOfficialHeroId(subject);
        if (exactId is not null)
        {
            return heroes.FindById(exactId, preferPersonalName, language)?.SpokenName ?? string.Empty;
        }

        var corrected = heroes.CorrectStructuredHeroPrefix(subject, preferPersonalName, language);
        if (!string.Equals(corrected, subject, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(corrected))
        {
            var spoken = corrected.Trim();
            return spoken.Length > 0 ? spoken : string.Empty;
        }

        if (heroes.TryCorrectKoreanHeroPrefix(subject, out var koreanName, out var consumed)
            && consumed >= subject.Trim().Length - 1)
        {
            var heroId = heroes.FindOfficialHeroId(koreanName);
            return heroes.FindById(heroId, preferPersonalName, language)?.SpokenName ?? string.Empty;
        }

        if (heroes.TryCorrectChineseHeroPrefix(subject, out var chineseId, out _, out var chineseConsumed)
            && chineseConsumed >= subject.Trim().Length - 1)
        {
            return heroes.FindById(chineseId, preferPersonalName, language)?.SpokenName ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool LooksLikeSmashedHeroTerm(string normalized, HeroDictionary? heroes)
    {
        var compact = Compact(normalized);
        if (compact.Length is < 5 or > 24 || compact.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (heroes is not null
            && heroes.TryCorrectKoreanHeroPrefix(compact, out _, out var consumed)
            && consumed is >= 2
            && compact.Length - consumed is >= 3 and <= 12
            && compact[consumed..].All(IsHangul))
        {
            return true;
        }

        return GameTermGlossary.TryResolveOfficialSuffix(
                   compact,
                   "item",
                   requireBoundary: false,
                   out _,
                   out _)
               || GameTermGlossary.TryResolveNearMissOfficialSuffix(compact, "item", out _, out _);
    }

    private static bool LooksLikeUnresolvedStructuredShare(string normalized)
    {
        return DamagedHudShareRegex().IsMatch(normalized);
    }

    private static bool TryParseClock(string value, out int minutes, out int seconds)
    {
        minutes = 0;
        seconds = 0;
        var clock = ClockSplitRegex().Match(value);
        if (clock.Success)
        {
            return int.TryParse(clock.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
                && int.TryParse(clock.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
                && minutes is >= 0 and <= 99
                && seconds is >= 0 and <= 59;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var packed)
            || packed < 0)
        {
            return false;
        }

        return value.Length switch
        {
            2 => AssignClock(packed, 0, out minutes, out seconds),
            3 => AssignClock(packed / 100, packed % 100, out minutes, out seconds),
            4 => AssignClock(packed / 100, packed % 100, out minutes, out seconds),
            _ => false
        };
    }

    private static bool AssignClock(int minutes, int seconds, out int resolvedMinutes, out int resolvedSeconds)
    {
        resolvedMinutes = minutes;
        resolvedSeconds = seconds;
        return minutes is >= 0 and <= 99 && seconds is >= 0 and <= 59;
    }

    private static bool TryLocalizeLane(string raw, AppLanguage language, out string lane)
    {
        var token = Compact(raw).ToLowerInvariant().Replace('0', 'o');
        lane = token switch
        {
            "mid" or "middle" or "midlane" or "미드" or "중" or "중로" or "中路" or "中单" =>
                language == AppLanguage.English ? "mid" : "中路",
            "top" or "toplane" or "t0p" or "탑" or "상" or "上路" or "上单" =>
                language == AppLanguage.English ? "top" : "上单",
            "jg" or "jng" or "jung" or "jungle" or "정글" or "打野" =>
                language == AppLanguage.English ? "jungle" : "打野",
            "bot" or "bottom" or "botlane" or "adc" or "ad" or "봇" or "바텀" or "원딜" or "下路" =>
                language == AppLanguage.English ? "bot" : "下路",
            "sup" or "supp" or "support" or "서폿" or "서포터" or "辅助" =>
                language == AppLanguage.English ? "support" : "辅助",
            _ => string.Empty
        };
        return lane.Length > 0;
    }

    private static bool IsFlashAvailable(string state, string negation)
    {
        if (!string.IsNullOrWhiteSpace(negation)
            || StatusMeansMissing(state))
        {
            return false;
        }

        return true;
    }

    private static bool StatusMeansMissing(string state)
    {
        var token = Compact(state).ToLowerInvariant();
        return token is "off" or "no" or "cd" or "out" or "used" or "gone" or "down"
            or "없음" or "빠짐" or "노" or "无" or "没";
    }

    private static string FormatChineseClock(int minutes, int seconds) =>
        seconds == 0 ? $"{minutes}分" : $"{minutes}分{seconds}秒";

    private static string FormatEnglishClock(int minutes, int seconds) =>
        seconds == 0
            ? $"{minutes}:00"
            : $"{minutes}:{seconds.ToString("00", CultureInfo.InvariantCulture)}";

    private static bool ContainsSentenceNoise(string subject) =>
        subject.IndexOfAny(['?', '？', '!', '！']) >= 0
        || subject.Contains("구입", StringComparison.Ordinal)
        || subject.Contains("구매", StringComparison.Ordinal)
        || subject.Contains("필요", StringComparison.Ordinal)
        || subject.Contains("买", StringComparison.Ordinal)
        || subject.Contains("需要", StringComparison.Ordinal);

    private static string Normalize(string value) =>
        Regex.Replace(
                ChineseTextNormalizer.ToSimplifiedCharacters(value)
                    .Normalize(System.Text.NormalizationForm.FormKC)
                    .Replace('−', '-')
                    .Replace('–', '-')
                    .Replace('—', '-'),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim()
            .Trim('"', '\'', '“', '”');

    private static string Compact(string value) =>
        Regex.Replace(value, @"\s+", string.Empty, RegexOptions.CultureInvariant);

    private static bool IsHangul(char character) =>
        character is >= '\uAC00' and <= '\uD7A3';

    [GeneratedRegex(
        @"(?<time>\d{1,2}[:.：]\d{2}|\d{2,4})\s*(?<lane>midlane|middle|toplane|botlane|jungle|jung|support|supp|bottom|adc|mid|top|t0p|jg|jng|bot|ad|sup|미드|정글|바텀|원딜|서포터|서폿|중로|탑|봇|중|상|하|中路|上路|下路|打野|辅助|中单|上单)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeLaneRegex();

    [GeneratedRegex(
        @"^(?<negation>no|노|没|无)?\s*(?<lane>midlane|middle|toplane|botlane|jungle|jung|support|supp|bottom|adc|mid|top|t0p|jg|jng|bot|ad|sup|미드|정글|바텀|원딜|서포터|서폿|중로|탑|봇|중|상|中路|上路|下路|打野|辅助|中单|上单)\s*(?<negation2>no|노|没|无)?\s*(?:flash|점멸|플래시|\bfl\b|\b플\b|\bf\b)\s*(?<state>on|off|no|cd|out|used|has|gone|down|있음|없음|빠짐|有|无|没)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlashStatusRegex();

    [GeneratedRegex(
        @"^(?:midlane|middle|toplane|botlane|jungle|jung|support|supp|bottom|adc|mid|top|t0p|jg|jng|bot|ad|sup|미드|정글|바텀|원딜|서포터|서폿|중로|탑|봇|중|상|中路|上路|下路|打野|辅助|中单|上单)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaneTokenRegex();

    [GeneratedRegex(@"^(?<minutes>\d{1,2})[:.：](?<seconds>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockSplitRegex();

    [GeneratedRegex(
        @"(?:flash|점멸|플래시|\bfl\b|\b플\b|\bf\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlashNoiseRegex();

    [GeneratedRegex(
        @"(?:재생성|재생정|구입했습니다|와드\s*아이템|퀘스트\s*(?:완료|압박)|任务(?:完成|压迫))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamagedHudShareRegex();
}
