using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Parses the small set of deterministic LoL ping and HUD-share messages that
/// must not be interpreted as free-form player chat. A separator dash in these
/// messages is structural and is never treated as a negative sign.
/// </summary>
public static partial class GameSemanticEventParser
{
    public static bool TryParsePlayerFeed(string content, out GameSemanticEvent semanticEvent)
    {
        var normalized = Normalize(content);
        if (TryParseCountdown(normalized, out semanticEvent)
            || TryParseStatus(normalized, out semanticEvent)
            || TryParseExplicitSignal(normalized, out semanticEvent)
            || TryParseMarker(normalized, out semanticEvent))
        {
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    public static bool TryParseSystemFeed(string line, out GameSemanticEvent semanticEvent)
    {
        var normalized = Normalize(line);
        if (TryParseExplicitSignal(normalized, out semanticEvent)
            || TryParseMarker(normalized, out semanticEvent)
            || TryParseCountdown(normalized, out semanticEvent)
            || TryParseStatus(normalized, out semanticEvent))
        {
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    public static string Format(
        GameSemanticEvent semanticEvent,
        string speaker,
        AppLanguage language)
    {
        var resolvedSpeaker = string.IsNullOrWhiteSpace(speaker)
            ? language == AppLanguage.English ? "Teammate" : "队友"
            : speaker.Trim();
        var subject = LocalizeSubject(semanticEvent.Subject, language);

        if (language == AppLanguage.English)
        {
            return semanticEvent.SpeechAct switch
            {
                SemanticSpeechAct.Countdown when semanticEvent.DurationSeconds is int seconds =>
                    $"{subject}: {seconds} seconds remaining.",
                SemanticSpeechAct.Status => FormatEnglishStatus(subject, semanticEvent.Status, resolvedSpeaker),
                SemanticSpeechAct.Marker => $"{resolvedSpeaker} marked {DefaultSubject(subject, "that location")}.",
                SemanticSpeechAct.Request when semanticEvent.Status == "retreat" =>
                    $"{resolvedSpeaker} asks the team to fall back.",
                SemanticSpeechAct.Request when semanticEvent.Status == "assist" =>
                    $"{resolvedSpeaker} asks for assistance.",
                SemanticSpeechAct.Warning when semanticEvent.Status == "missing" =>
                    $"{resolvedSpeaker} warns that an enemy is missing.",
                SemanticSpeechAct.Warning when semanticEvent.Status == "danger" =>
                    $"{resolvedSpeaker} warns the team to be careful.",
                SemanticSpeechAct.Plan when semanticEvent.Status == "on_my_way" =>
                    $"{resolvedSpeaker} signals they are on the way.",
                SemanticSpeechAct.Request when semanticEvent.Status == "vision" =>
                    $"{resolvedSpeaker} asks for vision.",
                _ => $"{resolvedSpeaker} marked {DefaultSubject(subject, "a tactical signal")}."
            };
        }

        return semanticEvent.SpeechAct switch
        {
            SemanticSpeechAct.Countdown when semanticEvent.DurationSeconds is int seconds =>
                $"{DefaultSubject(subject, "技能")}还有{seconds}秒",
            SemanticSpeechAct.Status => FormatChineseStatus(subject, semanticEvent.Status, resolvedSpeaker),
            SemanticSpeechAct.Marker => $"{resolvedSpeaker}标记了{DefaultSubject(subject, "该位置")}",
            SemanticSpeechAct.Request when semanticEvent.Status == "retreat" =>
                $"{resolvedSpeaker}请求撤退",
            SemanticSpeechAct.Request when semanticEvent.Status == "assist" =>
                $"{resolvedSpeaker}请求支援",
            SemanticSpeechAct.Warning when semanticEvent.Status == "missing" =>
                $"{resolvedSpeaker}提醒敌人消失",
            SemanticSpeechAct.Warning when semanticEvent.Status == "danger" =>
                $"{resolvedSpeaker}提醒小心危险",
            SemanticSpeechAct.Plan when semanticEvent.Status == "on_my_way" =>
                $"{resolvedSpeaker}发出正在路上的信号",
            SemanticSpeechAct.Request when semanticEvent.Status == "vision" =>
                $"{resolvedSpeaker}请求补充视野",
            _ => $"{resolvedSpeaker}发出战术标记"
        };
    }

    public static string Intent(GameSemanticEvent semanticEvent) =>
        semanticEvent.SpeechAct switch
        {
            SemanticSpeechAct.Countdown => "cooldown",
            SemanticSpeechAct.Request when semanticEvent.Status == "retreat" => "retreat",
            SemanticSpeechAct.Request when semanticEvent.Status == "assist" => "assist",
            SemanticSpeechAct.Request when semanticEvent.Status == "vision" => "resource",
            SemanticSpeechAct.Warning when semanticEvent.Status == "missing" => "missing",
            SemanticSpeechAct.Warning => "warning",
            SemanticSpeechAct.Plan => "plan",
            SemanticSpeechAct.Marker => "marker",
            SemanticSpeechAct.Status => "status",
            _ => "tactical_signal"
        };

    public static int Priority(GameSemanticEvent semanticEvent) =>
        semanticEvent.SpeechAct switch
        {
            SemanticSpeechAct.Warning => 3,
            SemanticSpeechAct.Request when semanticEvent.Status is "retreat" or "assist" => 3,
            SemanticSpeechAct.Countdown or SemanticSpeechAct.Plan => 2,
            _ => 1
        };

    private static bool TryParseCountdown(string normalized, out GameSemanticEvent semanticEvent)
    {
        var match = CountdownRegex().Match(normalized);
        if (match.Success
            && int.TryParse(
                match.Groups["seconds"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds)
            && seconds is >= 0 and <= 999)
        {
            var subject = CleanSubject(match.Groups["subject"].Value);
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Countdown,
                Subject: subject,
                Status: "cooldown",
                DurationSeconds: seconds,
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: match.Value);
            return !string.IsNullOrWhiteSpace(subject);
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseStatus(string normalized, out GameSemanticEvent semanticEvent)
    {
        var match = StatusRegex().Match(normalized);
        if (!match.Success)
        {
            semanticEvent = Unknown(normalized);
            return false;
        }

        var status = NormalizeStatus(match.Groups["status"].Value);
        var subject = CleanSubject(match.Groups["subject"].Value);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(status))
        {
            semanticEvent = Unknown(normalized);
            return false;
        }

        semanticEvent = new GameSemanticEvent(
            SemanticSourceType.HudShare,
            SemanticSpeechAct.Status,
            Subject: subject,
            Status: status,
            Modality: status is "alive" or "dead" ? SemanticModality.Marked : SemanticModality.Asserted,
            Confidence: 0.98,
            Evidence: match.Value);
        return true;
    }

    private static bool TryParseExplicitSignal(string normalized, out GameSemanticEvent semanticEvent)
    {
        var hasSignalFrame = SignalFrameRegex().IsMatch(normalized);
        if (hasSignalFrame && ContainsAny(normalized, "후퇴", "撤退", "retreat", "fall back")
            || IsExact(normalized, "후퇴", "撤退", "retreat", "fall back"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "retreat", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "도움", "지원", "协助", "支援", "assist", "help")
            || IsExact(normalized, "도움 요청", "지원 요청", "请求协助", "请求支援", "assist me", "need help"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "assist", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "적이 사라", "미아", "敌人不见", "敌人消失", "missing")
            || IsExact(normalized, "적이 사라짐", "미아", "敌人不见了", "敌人消失", "enemy missing", "missing enemy"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Warning, "missing", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "위험", "조심", "危险", "小心", "danger", "be careful")
            || IsExact(normalized, "위험", "조심", "危险", "小心", "danger", "be careful"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Warning, "danger", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "가는 중", "이동 중", "正在路上", "on my way", "omw")
            || IsExact(normalized, "가는 중", "이동 중", "正在路上", "on my way", "omw"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Plan, "on_my_way", normalized, 0.96);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "시야", "视野", "vision")
            || IsExact(normalized, "시야 필요", "시야 요청", "需要视野", "请求视野", "need vision", "vision needed"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "vision", normalized, 0.96);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseMarker(string normalized, out GameSemanticEvent semanticEvent)
    {
        var match = MarkerRegex().Match(normalized);
        if (!match.Success)
        {
            semanticEvent = Unknown(normalized);
            return false;
        }

        var target = CleanSubject(match.Groups["target"].Value);
        if (string.IsNullOrWhiteSpace(target))
        {
            semanticEvent = Unknown(normalized);
            return false;
        }

        semanticEvent = new GameSemanticEvent(
            SemanticSourceType.TacticalPing,
            SemanticSpeechAct.Marker,
            Subject: target,
            Modality: SemanticModality.Marked,
            Confidence: 0.95,
            Evidence: match.Value);
        return true;
    }

    private static GameSemanticEvent Signal(
        SemanticSpeechAct speechAct,
        string status,
        string evidence,
        double confidence) =>
        new(
            SemanticSourceType.TacticalPing,
            speechAct,
            Status: status,
            Modality: speechAct switch
            {
                SemanticSpeechAct.Request => SemanticModality.Requested,
                SemanticSpeechAct.Plan => SemanticModality.Planned,
                _ => SemanticModality.Marked
            },
            Confidence: confidence,
            Evidence: evidence);

    private static string FormatChineseStatus(string subject, string status, string speaker) =>
        status switch
        {
            "alive" => $"{speaker}提醒：{subject}仍然存活",
            "dead" => $"{subject}已经阵亡",
            "ready" => $"{subject}已就绪",
            "cooldown" => $"{subject}仍在冷却",
            _ => $"{speaker}标记了{subject}的状态"
        };

    private static string FormatEnglishStatus(string subject, string status, string speaker) =>
        status switch
        {
            "alive" => $"{speaker} notes that {subject} is still alive.",
            "dead" => $"{subject} is dead.",
            "ready" => $"{subject} is ready.",
            "cooldown" => $"{subject} is still on cooldown.",
            _ => $"{speaker} marked {subject}'s status."
        };

    private static string LocalizeSubject(string subject, AppLanguage language)
    {
        var localized = GameTermGlossary.ReplaceTerms(CleanSubject(subject), language);
        if (language == AppLanguage.Chinese)
        {
            localized = Regex.Replace(localized, @"\b(?:enemy|opponent)\b", "敌方", RegexOptions.IgnoreCase);
            localized = localized
                .Replace("적 ", "敌方", StringComparison.Ordinal)
                .Replace("아군 ", "我方", StringComparison.Ordinal);
        }
        else
        {
            localized = localized
                .Replace("적 ", "enemy ", StringComparison.Ordinal)
                .Replace("아군 ", "ally ", StringComparison.Ordinal);
        }
        return localized.Trim();
    }

    private static string CleanSubject(string value)
    {
        var cleaned = value
            .Trim(' ', '\t', '-', '–', '—', '|', ':', '：', '.', '。')
            .Normalize(NormalizationForm.FormKC);
        cleaned = SpeakerSignalPrefixRegex().Replace(cleaned, string.Empty);
        return cleaned.Trim(' ', '\t', '-', '–', '—', '|', ':', '：', '.', '。');
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("alive", StringComparison.Ordinal)
            || normalized.Contains("생존", StringComparison.Ordinal)
            || normalized.Contains("살아", StringComparison.Ordinal)
            || normalized.Contains("存活", StringComparison.Ordinal))
        {
            return "alive";
        }
        if (normalized.Contains("dead", StringComparison.Ordinal)
            || normalized.Contains("사망", StringComparison.Ordinal)
            || normalized.Contains("죽", StringComparison.Ordinal)
            || normalized.Contains("阵亡", StringComparison.Ordinal)
            || normalized.Contains("死亡", StringComparison.Ordinal))
        {
            return "dead";
        }
        if (normalized.Contains("ready", StringComparison.Ordinal)
            || normalized.Contains("준비", StringComparison.Ordinal)
            || normalized.Contains("사용 가능", StringComparison.Ordinal)
            || normalized.Contains("就绪", StringComparison.Ordinal)
            || normalized.Contains("可用", StringComparison.Ordinal))
        {
            return "ready";
        }
        if (normalized.Contains("cooldown", StringComparison.Ordinal)
            || normalized.Contains("쿨", StringComparison.Ordinal)
            || normalized.Contains("冷却", StringComparison.Ordinal))
        {
            return "cooldown";
        }
        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsExact(string value, params string[] alternatives)
    {
        var normalized = value.Trim(' ', '\t', '.', '。', '!', '！', '?', '？').ToLowerInvariant();
        return alternatives.Any(alternative =>
            string.Equals(normalized, alternative, StringComparison.OrdinalIgnoreCase));
    }

    private static string DefaultSubject(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Normalize(string value) =>
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

    private static GameSemanticEvent Unknown(string evidence) =>
        new(
            SemanticSourceType.Unknown,
            SemanticSpeechAct.Unknown,
            Confidence: 0,
            Evidence: evidence);

    [GeneratedRegex(
        @"^(?<subject>[\p{L}\p{N}_'’·./()（）\[\] :：-]{1,96}?)\s*(?:[-|:：]\s*)?"
        + @"(?<seconds>\d{1,3})\s*(?:s|sec(?:ond)?s?|초|秒)\s*(?:남음|剩余|후|left|remaining)?[.!。]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountdownRegex();

    [GeneratedRegex(
        @"^(?<subject>.+?)\s*(?:[-|:：]\s*)?"
        + @"(?<status>생존|살아\s*있음|사망|죽음|준비됨|사용\s*가능|쿨타임|"
        + @"存活(?:中)?|阵亡|死亡|就绪|可用|冷却中|alive|dead|ready|on\s+cooldown)[.!。]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(
        @"(?:신호\s*[:：]\s*(?<target>[^:：]{1,80})|"
        + @"(?<target>[^:：]{1,80}?)(?:에게|에)?\s*(?:신호를\s*보냄|핑(?:을)?\s*찍(?:음|었음))|"
        + @"(?:信号\s*[:：]\s*(?<target>[^:：]{1,80})|(?:标记了|向)\s*(?<target>[^:：]{1,80}?)(?:发出|发送)?(?:了)?(?:信号|ping))|"
        + @"(?:pinged|marked|signals?)\s+(?<target>[^:：]{1,80}))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(
        @"^(?:(?:.+?\s+)?(?:signals?\s+that|pinged|marked)\s+|"
        + @"(?:.+?님?이\s+)|(?:.+?)(?:标记了|提醒)\s*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerSignalPrefixRegex();

    [GeneratedRegex(
        @"(?:신호|信号|\b(?:ping(?:ed)?|signals?|marked)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignalFrameRegex();
}
