using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed partial class ChatMessageClassifier
{
    private static readonly string[] SystemFragments =
    {
        "[시스템]", "게임에서 나갔", "게임에 참가했", "재접속했",
        "항복 투표", "항복에 찬성", "항복에 반대",
        "플레이 시간", "장시간 게임", "서버 점검",
        "[系统]", "重新连接", "重新连入", "离开了游戏",
        "发起了投降", "同意投降", "赞成投降", "连续游戏", "健康游戏",
        "购买了", "已购买"
    };

    public ClassifiedGameMessage Classify(string line, SystemMessageFilterMode mode)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unknown(line ?? string.Empty);
        }

        var normalized = Regex.Replace(line.Trim(), @"\s+", " ")
            .Normalize(NormalizationForm.FormKC);

        // A channel prefix is the strongest evidence of player-authored text. Check it
        // before event keywords, because a player may discuss a kill or destroyed tower.
        var strongPlayer = StrongPlayerChatRegex().Match(normalized);
        if (strongPlayer.Success)
        {
            return Player(normalized, strongPlayer);
        }

        if (KillEventRegex().IsMatch(normalized))
        {
            return new ClassifiedGameMessage(GameMessageKind.KillEvent, normalized, normalized);
        }

        if (SystemFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            || SystemPrefixRegex().IsMatch(normalized)
            || SystemEventRegex().IsMatch(normalized))
        {
            return new ClassifiedGameMessage(GameMessageKind.SystemEvent, normalized, normalized);
        }

        // Balanced accepts the common "name: message" form. Strict requires an explicit
        // team/all/party channel prefix; Off retains unknown text for explicit opt-in use.
        if (mode != SystemMessageFilterMode.Strict)
        {
            var weakPlayer = WeakPlayerChatRegex().Match(normalized);
            if (weakPlayer.Success && !IsReservedSpeaker(weakPlayer.Groups["speaker"].Value))
            {
                return Player(normalized, weakPlayer);
            }
        }

        var tacticalSummary = SignalMessageTemplates.TryTranslateSignal(normalized);
        if (!string.IsNullOrWhiteSpace(tacticalSummary))
        {
            return new ClassifiedGameMessage(
                GameMessageKind.TacticalSignal,
                normalized,
                normalized,
                TacticalSummary: tacticalSummary,
                Priority: GetSignalPriority(tacticalSummary));
        }

        return Unknown(normalized);
    }

    public bool IsPlayerMessage(string line, SystemMessageFilterMode mode, bool signalVoiceEnabled = false)
    {
        var classified = Classify(line, mode);
        return classified.Kind switch
        {
            GameMessageKind.PlayerChat => true,
            GameMessageKind.TacticalSignal => signalVoiceEnabled,
            GameMessageKind.Unknown => mode == SystemMessageFilterMode.Off,
            _ => false
        };
    }

    private static ClassifiedGameMessage Player(string normalized, Match match)
    {
        var speakerRaw = match.Groups["speaker"].Value.Trim();
        var content = match.Groups["content"].Value.Trim();
        var speakerKey = Regex.Replace(speakerRaw, @"\s+", string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        return new ClassifiedGameMessage(
            GameMessageKind.PlayerChat,
            normalized,
            content,
            speakerKey,
            speakerRaw,
            Priority: 3);
    }

    private static ClassifiedGameMessage Unknown(string line) =>
        new(GameMessageKind.Unknown, line, line);

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
        @"^\s*(?:\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*)(?<speaker>[^:：\]]{1,48})[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StrongPlayerChatRegex();

    [GeneratedRegex(
        @"^\s*(?<speaker>[^:：\[\]]{1,48})[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeakPlayerChatRegex();

    [GeneratedRegex(
        @"(?:처치했|처치당했|연속\s*킬|더블\s*킬|트리플\s*킬|쿼드라\s*킬|펜타\s*킬|킬을\s*달성)"
        + @"|(?:击杀了|被击杀|双杀|三杀|四杀|五杀|大杀特杀|主宰比赛|超神|终结了|已被终结)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KillEventRegex();

    [GeneratedRegex(
        @"(?:포탑|억제기).*(?:파괴|무너)|(?:드래곤|내셔\s*남작|협곡의\s*전령|공허\s*유충).*(?:처치|사냥)"
        + @"|(?:摧毁了|被摧毁|防御塔|兵营|水晶|巨龙|男爵|先锋|巢虫)"
        + @"|(?:게임에서\s*나갔|게임에\s*참가했|재접속했|서버\s*(?:연결|상태|점검))"
        + @"|(?:重新连接|重新连入|离开了游戏|服务器|网络连接)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemEventRegex();

    [GeneratedRegex(
        @"^\s*(?:\[(?:시스템|系统|SYSTEM)\]|(?:시스템|알림|안내|공지|系统|通知|提示|SYSTEM|NOTICE)\s*[:：])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemPrefixRegex();
}
