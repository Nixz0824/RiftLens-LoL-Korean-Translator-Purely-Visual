using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Short deterministic summaries for optional LoL system-feed categories.
/// These messages never need to be sent to the user's translation provider.
/// </summary>
public static partial class SystemMessageTemplates
{
    public static string Format(
        ClassifiedGameMessage message,
        AppLanguage language)
    {
        if (message.Kind == GameMessageKind.TacticalSignal)
        {
            var signal = SignalMessageTemplates.FormatSpokenSignal(
                message.TacticalSummary,
                language == AppLanguage.English ? "Teammate" : "队友",
                language);
            return signal;
        }

        return language == AppLanguage.English
            ? FormatEnglish(message)
            : FormatChinese(message);
    }

    private static string FormatChinese(ClassifiedGameMessage message) =>
        message.Kind switch
        {
            GameMessageKind.KillEvent => FormatKillChinese(message.RawText),
            GameMessageKind.ObjectiveEvent => FormatObjectiveChinese(message.RawText),
            GameMessageKind.ClientReminder => "客户端提醒：请注意游戏时长并适当休息。",
            GameMessageKind.OtherSystemEvent => FormatOtherChinese(message.RawText),
            _ => "游戏系统发出了一条提醒。"
        };

    private static string FormatEnglish(ClassifiedGameMessage message) =>
        message.Kind switch
        {
            GameMessageKind.KillEvent => FormatKillEnglish(message.RawText),
            GameMessageKind.ObjectiveEvent => FormatObjectiveEnglish(message.RawText),
            GameMessageKind.ClientReminder => "Client reminder: take regular breaks and mind your play time.",
            GameMessageKind.OtherSystemEvent => FormatOtherEnglish(message.RawText),
            _ => "The game posted a system notice."
        };

    private static string FormatKillChinese(string text)
    {
        if (ContainsAny(text, "펜타", "五杀", "penta")) return "击杀播报：五杀。";
        if (ContainsAny(text, "쿼드라", "四杀", "quadra")) return "击杀播报：四杀。";
        if (ContainsAny(text, "트리플", "三杀", "triple")) return "击杀播报：三杀。";
        if (ContainsAny(text, "더블", "双杀", "double")) return "击杀播报：双杀。";
        return "发生了一条击杀播报。";
    }

    private static string FormatKillEnglish(string text)
    {
        if (ContainsAny(text, "펜타", "五杀", "penta")) return "Kill feed: pentakill.";
        if (ContainsAny(text, "쿼드라", "四杀", "quadra")) return "Kill feed: quadra kill.";
        if (ContainsAny(text, "트리플", "三杀", "triple")) return "Kill feed: triple kill.";
        if (ContainsAny(text, "더블", "双杀", "double")) return "Kill feed: double kill.";
        return "A champion kill was announced.";
    }

    private static string FormatObjectiveChinese(string text)
    {
        var objective = ObjectiveName(text, AppLanguage.Chinese);
        if (SpawnedRegex().IsMatch(text)) return $"中立资源提醒：{objective}已经刷新。";
        if (SoonRegex().IsMatch(text)) return $"中立资源提醒：{objective}即将刷新。";
        if (KilledRegex().IsMatch(text)) return $"中立资源提醒：{objective}已被击杀。";
        return $"中立资源提醒：请留意{objective}。";
    }

    private static string FormatObjectiveEnglish(string text)
    {
        var objective = ObjectiveName(text, AppLanguage.English);
        if (SpawnedRegex().IsMatch(text)) return $"Objective alert: {objective} has spawned.";
        if (SoonRegex().IsMatch(text)) return $"Objective alert: {objective} will spawn soon.";
        if (KilledRegex().IsMatch(text)) return $"Objective alert: {objective} was slain.";
        return $"Objective alert: watch {objective}.";
    }

    private static string FormatOtherChinese(string text)
    {
        if (ContainsAny(text, "항복", "投降", "surrender")) return "系统提醒：出现了投降相关通知。";
        if (ContainsAny(text, "나갔", "离开", "left the game")) return "系统提醒：有玩家离开了游戏。";
        if (ContainsAny(text, "재접속", "重新连接", "reconnect")) return "系统提醒：有玩家重新连接。";
        if (ContainsAny(text, "포탑", "억제기", "防御塔", "兵营", "水晶", "turret", "inhibitor"))
            return "系统提醒：防御塔或高地建筑状态发生变化。";
        return "游戏系统发出了一条客户端通知。";
    }

    private static string FormatOtherEnglish(string text)
    {
        if (ContainsAny(text, "항복", "投降", "surrender")) return "System notice: a surrender update was posted.";
        if (ContainsAny(text, "나갔", "离开", "left the game")) return "System notice: a player left the game.";
        if (ContainsAny(text, "재접속", "重新连接", "reconnect")) return "System notice: a player reconnected.";
        if (ContainsAny(text, "포탑", "억제기", "防御塔", "兵营", "水晶", "turret", "inhibitor"))
            return "System notice: a turret or inhibitor state changed.";
        return "The game posted a client notification.";
    }

    private static string ObjectiveName(string text, AppLanguage language)
    {
        if (ContainsAny(text, "내셔", "바론", "男爵", "baron", "nashor"))
            return language == AppLanguage.English ? "Baron Nashor" : "男爵";
        if (ContainsAny(text, "전령", "峡谷先锋", "rift herald"))
            return language == AppLanguage.English ? "Rift Herald" : "峡谷先锋";
        if (ContainsAny(text, "유충", "巢虫", "void grub"))
            return language == AppLanguage.English ? "Void Grubs" : "虚空巢虫";
        if (ContainsAny(text, "아타칸", "阿塔坎", "atakhan"))
            return language == AppLanguage.English ? "Atakhan" : "阿塔坎";
        if (ContainsAny(text, "장로", "远古龙", "elder"))
            return language == AppLanguage.English ? "Elder Dragon" : "远古龙";
        return language == AppLanguage.English ? "Dragon" : "小龙";
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(
        @"(?:생성|등장|부활했습니다|已经刷新|已刷新|已经出现|has spawned|has respawned)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpawnedRegex();

    [GeneratedRegex(
        @"(?:곧|초\s*후|即将|秒后|soon|in\s+\d+\s+seconds?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoonRegex();

    [GeneratedRegex(
        @"(?:처치|사냥|击杀|消灭|slain|killed)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KilledRegex();
}
