using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Separates player-authored chat from LoL's templated system feed.
/// Structure is authoritative; colour evidence only recovers a player line
/// when OCR dropped its channel or hero brackets.
/// </summary>
public sealed partial class ChatMessageClassifier
{
    private static readonly string[] ClientReminderFragments =
    {
        "플레이 시간", "장시간 게임", "휴식을 취", "건강", "청소년",
        "连续游戏", "游戏时间", "适当休息", "健康游戏", "防沉迷",
        "play time", "take a break", "healthy gaming", "wellness"
    };

    private static readonly string[] OtherSystemFragments =
    {
        "[시스템]", "게임에서 나갔", "게임에 참가했", "재접속했",
        "항복 투표", "항복에 찬성", "항복에 반대", "서버 점검",
        "[系统]", "重新连接", "重新连入", "离开了游戏", "加入了游戏",
        "发起了投降", "同意投降", "赞成投降", "服务器",
        "[system]", "reconnected", "left the game", "joined the game",
        "surrender vote", "server maintenance"
    };

    public ClassifiedGameMessage Classify(
        string line,
        OcrColorEvidence? colorEvidence = null)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unknown(line ?? string.Empty, colorEvidence);
        }

        var normalized = NormalizeLine(line);

        // The "(champion):" shape is shared by real player chat and a small set
        // of client-generated ping/HUD-share lines. Parse those deterministic
        // payloads first so a marker or cooldown cannot become an inferred action.
        var playerWithHero = PlayerWithHeroRegex().Match(normalized);
        if (playerWithHero.Success && !IsReservedSpeaker(playerWithHero.Groups["speaker"].Value))
        {
            if (TryPlayerFeed(normalized, playerWithHero, colorEvidence, includeHero: true, out var feed))
            {
                return feed;
            }
            return Player(normalized, playerWithHero, colorEvidence, includeHero: true);
        }

        // Champion select and a few HUD variants use an explicit channel without
        // displaying a champion. The channel marker remains strong structural evidence.
        var channelPlayer = ChannelPlayerRegex().Match(normalized);
        if (channelPlayer.Success && !IsReservedSpeaker(channelPlayer.Groups["speaker"].Value))
        {
            if (TryPlayerFeed(normalized, channelPlayer, colorEvidence, includeHero: false, out var feed))
            {
                return feed;
            }
            return Player(normalized, channelPlayer, colorEvidence, includeHero: false);
        }

        // OCR sometimes drops parentheses or the channel label. Recover the
        // remaining "name: content" form before keyword checks only when the
        // LoL cyan-metadata + neutral-content palette supports player chat.
        // Reserved system labels can never pass this path.
        if (colorEvidence?.SupportsPlayerChat == true)
        {
            var colorBackedPlayer = ColorBackedPlayerRegex().Match(normalized);
            if (colorBackedPlayer.Success
                && !IsReservedSpeaker(colorBackedPlayer.Groups["speaker"].Value))
            {
                return Player(normalized, colorBackedPlayer, colorEvidence, includeHero: false);
            }
        }

        if (GameSemanticEventParser.TryParseSystemFeed(normalized, out var semanticEvent))
        {
            return new ClassifiedGameMessage(
                GameMessageKind.TacticalSignal,
                normalized,
                normalized,
                TacticalSummary: GameSemanticEventParser.Format(
                    semanticEvent,
                    "队友",
                    AppLanguage.Chinese),
                Priority: GameSemanticEventParser.Priority(semanticEvent),
                ColorEvidence: colorEvidence,
                SemanticEvent: semanticEvent);
        }

        var tacticalSummary = SignalMessageTemplates.TryTranslateSignal(normalized);
        if (!string.IsNullOrWhiteSpace(tacticalSummary))
        {
            return new ClassifiedGameMessage(
                GameMessageKind.TacticalSignal,
                normalized,
                normalized,
                TacticalSummary: tacticalSummary,
                Priority: GetSignalPriority(tacticalSummary),
                ColorEvidence: colorEvidence);
        }

        if (ObjectiveEventRegex().IsMatch(normalized))
        {
            return SystemMessage(GameMessageKind.ObjectiveEvent, normalized, 2, colorEvidence);
        }

        if (KillEventRegex().IsMatch(normalized))
        {
            return SystemMessage(GameMessageKind.KillEvent, normalized, 1, colorEvidence);
        }

        if (ClientReminderFragments.Any(fragment =>
                normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            || ClientReminderRegex().IsMatch(normalized))
        {
            return SystemMessage(GameMessageKind.ClientReminder, normalized, 1, colorEvidence);
        }

        if (OtherSystemFragments.Any(fragment =>
                normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            || SystemPrefixRegex().IsMatch(normalized)
            || OtherSystemEventRegex().IsMatch(normalized))
        {
            return SystemMessage(GameMessageKind.OtherSystemEvent, normalized, 1, colorEvidence);
        }

        return Unknown(normalized, colorEvidence);
    }

    // Compatibility overload for older callers and saved filter settings. The old
    // relaxed/strict switch no longer changes classification because it could turn
    // system notices into player chat.
    public ClassifiedGameMessage Classify(string line, SystemMessageFilterMode _) =>
        Classify(line);

    public bool IsPlayerMessage(
        string line,
        SystemMessageFilterMode _,
        bool signalVoiceEnabled = false)
    {
        var classified = Classify(line);
        return classified.Kind == GameMessageKind.PlayerChat
            || classified.Kind == GameMessageKind.TacticalSignal && signalVoiceEnabled;
    }

    private static string NormalizeLine(string line)
    {
        var normalized = Regex.Replace(line.Trim(), @"\s+", " ")
            .Normalize(NormalizationForm.FormKC);
        return MessageDeduplicator.StripGameTimestamp(normalized);
    }

    private static ClassifiedGameMessage Player(
        string normalized,
        Match match,
        OcrColorEvidence? colorEvidence,
        bool includeHero)
    {
        var speaker = match.Groups["speaker"].Value.Trim();
        var hero = includeHero ? match.Groups["hero"].Value.Trim() : string.Empty;
        var content = match.Groups["content"].Value.Trim();
        var speakerKey = Regex.Replace(speaker, @"\s+", string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        var speakerRaw = string.IsNullOrWhiteSpace(hero)
            ? speaker
            : $"{speaker} ({hero})";
        return new ClassifiedGameMessage(
            GameMessageKind.PlayerChat,
            normalized,
            content,
            speakerKey,
            speakerRaw,
            Priority: 3,
            ColorEvidence: colorEvidence,
            SpeakerHeroRaw: hero);
    }

    private static bool TryPlayerFeed(
        string normalized,
        Match match,
        OcrColorEvidence? colorEvidence,
        bool includeHero,
        out ClassifiedGameMessage message)
    {
        var content = match.Groups["content"].Value.Trim();
        if (!GameSemanticEventParser.TryParsePlayerFeed(content, out var semanticEvent))
        {
            message = Unknown(normalized, colorEvidence);
            return false;
        }

        var speaker = match.Groups["speaker"].Value.Trim();
        var hero = includeHero ? match.Groups["hero"].Value.Trim() : string.Empty;
        var speakerKey = Regex.Replace(speaker, @"\s+", string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        var speakerRaw = string.IsNullOrWhiteSpace(hero)
            ? speaker
            : $"{speaker} ({hero})";
        message = new ClassifiedGameMessage(
            GameMessageKind.TacticalSignal,
            normalized,
            content,
            speakerKey,
            speakerRaw,
            TacticalSummary: GameSemanticEventParser.Format(
                semanticEvent,
                "队友",
                AppLanguage.Chinese),
            Priority: GameSemanticEventParser.Priority(semanticEvent),
            ColorEvidence: colorEvidence,
            SpeakerHeroRaw: hero,
            SemanticEvent: semanticEvent);
        return true;
    }

    private static ClassifiedGameMessage SystemMessage(
        GameMessageKind kind,
        string normalized,
        int priority,
        OcrColorEvidence? colorEvidence) =>
        new(kind, normalized, normalized, Priority: priority, ColorEvidence: colorEvidence);

    private static ClassifiedGameMessage Unknown(
        string line,
        OcrColorEvidence? colorEvidence) =>
        new(GameMessageKind.Unknown, line, line, ColorEvidence: colorEvidence);

    private static bool IsReservedSpeaker(string speaker)
    {
        var normalized = Regex.Replace(speaker.Trim(), @"\s+", string.Empty);
        return normalized is "시스템" or "알림" or "안내" or "공지" or "신호"
            or "系统" or "通知" or "提示" or "信号"
            or "SYSTEM" or "NOTICE" or "SIGNAL";
    }

    private static int GetSignalPriority(string summary) =>
        summary.Contains("撤退", StringComparison.Ordinal)
        || summary.Contains("危险", StringComparison.Ordinal)
        || summary.Contains("小心", StringComparison.Ordinal)
        || summary.Contains("支援", StringComparison.Ordinal)
        || summary.Contains("协助", StringComparison.Ordinal)
        || summary.Contains("失踪", StringComparison.Ordinal)
        || summary.Contains("不见", StringComparison.Ordinal)
            ? 2
            : 1;

    [GeneratedRegex(
        @"^\s*(?:\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*)?"
        + @"(?<speaker>[^:：\r\n\(\[（]{1,64}?)\s*[\(\[（]\s*"
        + @"(?<hero>[^:：\r\n\)\]）]{1,36})\s*[\)\]）]\s*[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerWithHeroRegex();

    [GeneratedRegex(
        @"^\s*\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*"
        + @"(?<speaker>[^:：\]\r\n]{1,64})[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPlayerRegex();

    [GeneratedRegex(
        @"^\s*(?<speaker>[^:：\[\]\r\n]{1,64})[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorBackedPlayerRegex();

    [GeneratedRegex(
        @"(?:더블\s*킬|트리플\s*킬|쿼드라\s*킬|펜타\s*킬|연속\s*킬|킬을\s*달성)"
        + @"|(?:[^:：]{1,48})(?:이|가)\s*(?:[^:：]{1,48})(?:을|를)\s*처치"
        + @"|(?:击杀了|被击杀|双杀|三杀|四杀|五杀|大杀特杀|主宰比赛|超神|终结了|已被终结)"
        + @"|(?:has slain|was slain|double kill|triple kill|quadra kill|penta kill|killing spree)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KillEventRegex();

    [GeneratedRegex(
        @"(?:(?:드래곤|용|내셔\s*남작|바론|협곡의\s*전령|전령|공허\s*유충|유충|아타칸).*(?:생성|등장|부활|처치|사냥|곧|초\s*후))"
        + @"|(?:(?:巨龙|小龙|元素龙|远古龙|男爵|峡谷先锋|虚空巢虫|巢虫|阿塔坎).*(?:刷新|复活|生成|出现|击杀|消灭|秒后))"
        + @"|(?:(?:dragon|drake|baron|nashor|rift herald|void grub|atakhan).*(?:spawn|respawn|slain|killed|seconds?))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveEventRegex();

    [GeneratedRegex(
        @"(?:게임|플레이).*(?:시간|휴식|건강)"
        + @"|(?:游戏|游玩).*(?:时间|小时|休息|健康|防沉迷)"
        + @"|(?:play|gaming).*(?:hours?|break|healthy|wellness)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClientReminderRegex();

    [GeneratedRegex(
        @"(?:포탑|억제기).*(?:파괴|무너)|(?:摧毁了|被摧毁|防御塔|兵营|水晶)"
        + @"|(?:게임에서\s*나갔|게임에\s*참가했|재접속했|서버\s*(?:연결|상태|점검))"
        + @"|(?:重新连接|重新连入|离开了游戏|加入了游戏|服务器|网络连接)"
        + @"|(?:left the game|joined the game|reconnect|server|network connection)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OtherSystemEventRegex();

    [GeneratedRegex(
        @"^\s*(?:\[(?:시스템|系统|SYSTEM)\]|(?:시스템|알림|안내|공지|系统|通知|提示|SYSTEM|NOTICE)\s*[:：])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemPrefixRegex();
}
