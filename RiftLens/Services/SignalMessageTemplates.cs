using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// 韩服 LOL 系统信号 / 模板消息本地翻译（无需调用 AI）。
/// 同时覆盖韩文原文 和 中文汉化客户端输出。
/// </summary>
public static partial class SignalMessageTemplates
{
    public static string? TryTranslate(string line) => TryTranslate(line, AppLanguage.Chinese);

    public static string? TryTranslate(string line, AppLanguage language)
    {
        var translated = TryTranslateChinese(line);
        return translated is null || language == AppLanguage.Chinese
            ? translated
            : TranslateChineseResultToEnglish(translated);
    }

    private static string? TryTranslateChinese(string line)
    {
        var normalized = Regex.Replace(line.Trim(), @"\s+", " ");

        // ═══════════════════════════════════════════════════
        // 智能 Ping（雷达盘）
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"신호\s*[:：]?\s*후퇴", out _)) return "撤退信号";
        if (TryMatch(normalized, @"(?:신호\s*[:：]?\s*)?(?:이동\s*중|가는\s*중)", out _)) return "正在路上";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*(도움|지원)\s*(요청)?", out _)) return "请求支援";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*적이?\s*사라짐", out _)) return "敌人不见了";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*(조심|위험)", out _)) return "小心危险";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*(전진|밀어)", out _)) return "推进前进";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*시야\s*(확보|필요)", out _)) return "需要视野";
        if (TryMatch(normalized, @"신호\s*[:：]?\s*적\s*시야", out _)) return "敌方有视野";

        // ── 中文（汉化客户端） ──
        if (TryMatch(normalized, @"信号\s*[:：]?\s*撤退", out _)) return "撤退信号";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*正在路上", out _)) return "正在路上";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*(?:请求)?(?:协助|支援|帮助)", out _)) return "请求支援";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*敌人(?:不见了?|消失)", out _)) return "敌人不见了";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*(?:小心|危险|警告)", out _)) return "小心危险";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*推进", out _)) return "推进前进";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*需要?视野", out _)) return "需要视野";
        if (TryMatch(normalized, @"信号\s*[:：]?\s*敌方?有?视野", out _)) return "敌方有视野";

        // ═══════════════════════════════════════════════════
        // 普通 Ping / 快捷聊天
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"후퇴하(?:라는|라는|세요|자)", out _)) return "请求撤退";
        if (TryMatch(normalized, @"도움을?\s*요청", out _)) return "请求协助";
        if (TryMatch(normalized, @"적이?\s*사라졌", out _)) return "敌人失踪";
        if (TryMatch(normalized, @"위험\s*(?:신호)?", out _)) return "危险警告";

        // ── 中文 ──
        if (TryMatch(normalized, @"请求撤退|请撤退|撤退一下|(?:发出)?撤退信号", out _)) return "请求撤退";
        if (TryMatch(normalized, @"请求协助|请求支援|帮一下|来人帮", out _)) return "请求协助";
        if (TryMatch(normalized, @"敌人失踪|敌人没了|人不见了", out _)) return "敌人失踪";
        if (TryMatch(normalized, @"危险警告|危险信号", out _)) return "危险警告";

        // ═══════════════════════════════════════════════════
        // 金币 / 装备
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"(\d{2,6})\s*골드", out var m)) return $"金币：{m.Groups[1].Value}";
        if (TryMatch(normalized, @"구매\w*\s*에\s*(\d+)\s*골드\s*(?:부족|필요)", out var m2)) return $"还差 {m2.Groups[1].Value} 金币";
        if (TryMatch(normalized, @"(\S+)\s*구매\s*완료", out var m3)) return $"已购买 {m3.Groups[1].Value}";
        if (TryMatch(normalized, @"골드\s*(?:부족|모자)", out _)) return "金币不足";

        // ── 中文 ──
        if (TryMatch(normalized, @"(\d{2,6})\s*金币", out var m4)) return $"金币：{m4.Groups[1].Value}";
        if (TryMatch(normalized, @"还差\s*(\d+)\s*金币", out var m5)) return $"还差 {m5.Groups[1].Value} 金币";
        if (TryMatch(normalized, @"购买.*需要.*(\d+)\s*金币", out var m6)) return $"还差 {m6.Groups[1].Value} 金币";
        if (TryMatch(normalized, @"已购买\s*(\S+)", out var m7)) return $"已购买 {m7.Groups[1].Value}";
        if (TryMatch(normalized, @"(?:金币|金钱)\s*(?:不足|不够)", out _)) return "金币不足";

        // ═══════════════════════════════════════════════════
        // 回城 / 传送
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"귀환\s*(?:시작|중|완료)?", out _)) return "回城中";
        if (TryMatch(normalized, @"순간이동\s*(?:시작|준비|완료)?", out _)) return "传送中";

        // ── 中文 ──
        if (TryMatch(normalized, @"回城\s*(?:中|开始|完成)?", out _)) return "回城中";
        if (TryMatch(normalized, @"传送\s*(?:中|开始|准备|完成)?", out _)) return "传送中";

        // ═══════════════════════════════════════════════════
        // 召唤师技能 / 大招提醒
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"(?:R\s*[:：]|궁극기).*(?:준비|대기|사용\s*가능)", out _)) return "大招已就绪";
        if (TryMatch(normalized, @"(?<skill>점멸|회복|점화|텔레포트|탈진|방어막|정화|강타).*(?:준비|대기|사용\s*가능)", out var skillMatch))
            return $"{TranslateSummonerSpell(skillMatch.Groups["skill"].Value)}已就绪";

        // ── 中文 ──
        if (TryMatch(normalized, @"(?:R\s*[:：]|大招|终极技能).*(?:就绪|可用|准备好了|冷却完毕)", out _)) return "大招已就绪";
        if (TryMatch(normalized, @"(?:闪现|治疗|点燃|传送|虚弱|屏障|净化|惩戒).*(?:就绪|可用|准备好了|冷却完毕)", out _)) return "召唤师技能就绪";

        // ═══════════════════════════════════════════════════
        // 游戏时长 / 防沉迷
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"(?:게임\s*)?플레이\s*시간[이은는]?\s*(\d+)\s*시간", out var m8)) return $"已连续游戏 {m8.Groups[1].Value} 小时，请注意休息";
        if (TryMatch(normalized, @"장시간\s*게임", out _)) return "长时间游戏提醒：请注意休息保护健康";

        // ── 中文 ──
        if (TryMatch(normalized, @"已(?:连续)?游戏\s*(\d+)\s*(?:个)?小时", out var m9)) return $"已连续游戏 {m9.Groups[1].Value} 小时，请注意休息";
        if (TryMatch(normalized, @"游戏时间已超过?\s*(\d+)\s*(?:个)?小时", out var m10)) return $"已连续游戏 {m10.Groups[1].Value} 小时，请注意休息";
        if (TryMatch(normalized, @"长时间游戏|请适当休息|健康游戏", out _)) return "长时间游戏提醒：请注意休息保护健康";
        if (TryMatch(normalized, @"(?:休息|健康)\s*(?:提醒|建议|提示)", out _)) return "系统建议适当休息";

        // ═══════════════════════════════════════════════════
        // 击杀/死亡通知 — 应过滤，不翻译
        // ═══════════════════════════════════════════════════
        if (TryMatch(normalized, @"(?:아군|적)\s*(?:챔피언|포탑|억제기|드래곤|내셔|전령|유충)", out _)) return null;
        if (TryMatch(normalized, @"처치(?:했|당했)", out _)) return null;
        if (TryMatch(normalized, @"(?:击杀了|被击杀|摧毁了|被摧毁)", out _)) return null;

        // ═══════════════════════════════════════════════════
        // 投降
        // ═══════════════════════════════════════════════════

        // ── 韩文 ──
        if (TryMatch(normalized, @"항복\s*투표", out _)) return "有人发起了投降投票";
        if (TryMatch(normalized, @"항복에\s*찬성", out _)) return "有人同意投降";

        // ── 中文 ──
        if (TryMatch(normalized, @"发起了?(?:提前)?投降", out _)) return "有人发起了投降投票";
        if (TryMatch(normalized, @"同意投降|赞成投降", out _)) return "有人同意投降";

        // ═══════════════════════════════════════════════════
        // 英文游戏缩写 / 常用语
        // ═══════════════════════════════════════════════════
        if (TryMatch(normalized, @"\b(?:ff|surr(?:ender)?)\b", out _)) return "有人发起投降 / 想投降了";
        if (TryMatch(normalized, @"\bgg\b", out _)) return "GG（游戏结束）";
        if (TryMatch(normalized, @"\bss\b|miss(?:ing)?", out _)) return "敌人不见了";
        if (TryMatch(normalized, @"\b(?:help|need\s*help)\b", out _)) return "请求协助";
        if (TryMatch(normalized, @"\bback\b|retreat|fall\s*back", out _)) return "请求撤退";
        if (TryMatch(normalized, @"\bpush\b", out _)) return "推进";
        if (TryMatch(normalized, @"\b(?:ward|vision|need\s*vision)\b", out _)) return "需要视野";
        if (TryMatch(normalized, @"\b(?:care(?:ful)?|danger|watch\s*out)\b", out _)) return "小心危险";
        if (TryMatch(normalized, @"\bon\s*(?:my\s*)?way|coming|omw\b", out _)) return "正在路上";
        if (TryMatch(normalized, @"\b(?:baron|nashor)\b", out _)) return "关注男爵";
        if (TryMatch(normalized, @"\b(?:drake|dragon)\b", out _)) return "关注小龙";
        if (TryMatch(normalized, @"\bgap\b|diff(?:erence)?", out _)) return "对位差距";
        if (TryMatch(normalized, @"\bfeed(?:ing)?|inting?\b", out _)) return "送人头/表现不佳";
        if (TryMatch(normalized, @"\b(?:mid|top|jgl?|jungle|bot|adc?|sup(?:port)?)\b", out _))
            return null; // 位置简称不做单独翻译，留给 AI 结合上下文处理
        if (TryMatch(normalized, @"\b(?:tp|teleport)\b", out _)) return "传送";

        return null;
    }

    public static bool IsSignalMessage(string line)
    {
        var normalized = Regex.Replace(line.Trim(), @"\s+", " ");
        return (SignalPatternRegex().IsMatch(normalized) || ExplicitSignalPrefixRegex().IsMatch(normalized))
            && !ExcludedNonSignalPatternRegex().IsMatch(normalized);
    }

    public static bool IsExplicitSignalMessage(string line)
    {
        var normalized = Regex.Replace(line.Trim(), @"\s+", " ");
        return ExplicitSignalPrefixRegex().IsMatch(normalized);
    }

    /// <summary>
    /// 仅返回可本地播报的固定队友信号。普通玩家聊天即使能被模板翻译，也应交由 AI 结合上下文处理。
    /// </summary>
    public static string? TryTranslateSignal(string line)
    {
        return IsSignalMessage(line) ? TryTranslate(line) : null;
    }

    public static string? TryTranslateSignal(string line, AppLanguage language)
    {
        return IsSignalMessage(line) ? TryTranslate(line, language) : null;
    }

    public static string TranslateResult(string translatedChinese, AppLanguage language) =>
        language == AppLanguage.English
            ? TranslateChineseResultToEnglish(translatedChinese)
            : translatedChinese;

    public static string FormatSpokenSignal(
        string translatedChinese,
        string speaker,
        AppLanguage language)
    {
        var resolvedSpeaker = string.IsNullOrWhiteSpace(speaker)
            ? language == AppLanguage.English ? "Teammate" : "队友"
            : speaker.Trim();
        var localizedAction = TranslateResult(translatedChinese, language);

        if (language == AppLanguage.English)
        {
            if (translatedChinese.EndsWith("信号", StringComparison.Ordinal))
            {
                return $"{resolvedSpeaker} signals {localizedAction.ToLowerInvariant()}.";
            }

            return $"{resolvedSpeaker} says {NaturalizeEnglishClause(localizedAction)}.";
        }

        if (translatedChinese.EndsWith("信号", StringComparison.Ordinal))
        {
            return $"{resolvedSpeaker}发出{localizedAction}";
        }

        var naturalAction = localizedAction switch
        {
            "请求协助" or "请求支援" => "需要协助",
            "请求撤退" => "请撤退",
            "危险警告" => "小心危险",
            _ => localizedAction
        };
        return $"{resolvedSpeaker}说，{naturalAction}";
    }

    private static string NaturalizeEnglishClause(string action)
    {
        if (TryMatch(action, @"^Gold: (?<amount>\d+)$", out var gold))
            return $"they have {gold.Groups["amount"].Value} gold";
        if (action.EndsWith(" is ready", StringComparison.OrdinalIgnoreCase))
            return "their " + char.ToLowerInvariant(action[0]) + action[1..];

        return action switch
        {
            "On my way" => "they're on the way",
            "Needs assistance" => "they need assistance",
            "Enemy missing" => "the enemy is missing",
            "Danger — be careful" => "be careful",
            "Push forward" => "push forward",
            "Needs vision" => "we need vision",
            "Enemy has vision here" => "the enemy has vision here",
            "Fall back" => "fall back",
            "Not enough gold" => "they don't have enough gold",
            "Recalling" => "they're recalling",
            "Teleporting" => "they're teleporting",
            _ => char.ToLowerInvariant(action[0]) + action[1..]
        };
    }

    private static string TranslateChineseResultToEnglish(string value)
    {
        if (TryMatch(value, @"^金币：(?<amount>\d+)$", out var gold))
            return $"Gold: {gold.Groups["amount"].Value}";
        if (TryMatch(value, @"^还差 (?<amount>\d+) 金币$", out var missingGold))
            return $"Needs {missingGold.Groups["amount"].Value} more gold";
        if (TryMatch(value, @"^已购买 (?<item>.+)$", out var purchase))
            return $"Purchased {purchase.Groups["item"].Value}";
        if (TryMatch(value, @"^已连续游戏 (?<hours>\d+) 小时", out var playTime))
            return $"You have played for {playTime.Groups["hours"].Value} hours. Consider taking a break.";
        if (value.EndsWith("已就绪", StringComparison.Ordinal) && value != "大招已就绪")
        {
            var ability = value[..^3] switch
            {
                "闪现" => "Flash",
                "治疗" => "Heal",
                "点燃" => "Ignite",
                "传送" => "Teleport",
                "虚弱" => "Exhaust",
                "屏障" => "Barrier",
                "净化" => "Cleanse",
                "惩戒" => "Smite",
                var other => other
            };
            return ability + " is ready";
        }

        return value switch
        {
            "撤退信号" => "Retreat",
            "正在路上" => "On my way",
            "请求支援" or "请求协助" => "Needs assistance",
            "敌人不见了" or "敌人失踪" => "Enemy missing",
            "小心危险" or "危险警告" => "Danger — be careful",
            "推进前进" or "推进" => "Push forward",
            "需要视野" => "Needs vision",
            "敌方有视野" => "Enemy has vision here",
            "请求撤退" => "Fall back",
            "金币不足" => "Not enough gold",
            "回城中" => "Recalling",
            "传送中" => "Teleporting",
            "大招已就绪" => "Ultimate is ready",
            "召唤师技能就绪" => "Summoner spell is ready",
            "长时间游戏提醒：请注意休息保护健康" or "系统建议适当休息" => "Long session reminder: consider taking a break.",
            "有人发起了投降投票" => "A surrender vote has started",
            "有人同意投降" => "A player voted to surrender",
            "有人发起投降 / 想投降了" => "A player wants to surrender",
            "GG（游戏结束）" => "GG",
            "关注男爵" => "Watch Baron",
            "关注小龙" => "Watch Dragon",
            "对位差距" => "Role gap",
            "送人头/表现不佳" => "Feeding / playing poorly",
            "传送" => "Teleport",
            _ => value
        };
    }

    private static bool TryMatch(string input, string pattern, out Match match)
    {
        match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success;
    }

    private static string TranslateSummonerSpell(string korean) => korean switch
    {
        "점멸" => "闪现",
        "회복" => "治疗",
        "점화" => "点燃",
        "텔레포트" => "传送",
        "탈진" => "虚弱",
        "방어막" => "屏障",
        "정화" => "净化",
        "강타" => "惩戒",
        _ => "召唤师技能"
    };

    [GeneratedRegex(
        @"(?:항복|플레이\s*시간|장시간\s*게임|휴식|건강\s*(?:권장|안내))"
        + @"|(?:\u53d1\u8d77\u4e86?(?:\u63d0\u524d)?\u6295\u964d|\u540c\u610f\u6295\u964d|\u8d5e\u6210\u6295\u964d|\u8fde\u7eed\u6e38\u620f|\u6e38\u620f\u65f6\u95f4|\u957f\u65f6\u95f4\u6e38\u620f|\u5065\u5eb7\u6e38\u620f)"
        + @"|\b(?:ff|surr(?:ender)?|gg|gap|diff(?:erence)?|feed(?:ing)?|inting?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExcludedNonSignalPatternRegex();

    [GeneratedRegex(
        @"^(?:\uC2E0\uD638|\u4FE1\u53F7)\s*[:\uFF1A]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSignalPrefixRegex();

    [GeneratedRegex(
        // 韩文 ping 信号
        @"신호\s*[:：]?\s*(?:후퇴|이동\s*중|가는\s*중|도움|지원|적이?\s*사라|조심|위험|전진|밀어|시야|적\s*시야)" +
        @"|후퇴하|도움을?\s*요청|적이?\s*사라졌|위험\s*신호|(?:이동\s*중|가는\s*중)" +
        @"|\d+\s*골드|골드\s*(?:부족|모자)|구매.*골드" +
        @"|귀환\s*(?:시작|중|완료)?" +
        @"|순간이동" +
        @"|(?:R\s*[:：]|궁극기).*(?:준비|대기|사용\s*가능)" +
        @"|(?:점멸|회복|점화|텔레포트|탈진|방어막|정화|강타).*(?:준비|대기|사용\s*가능)" +
        // 中文 ping 信号
        @"|信号\s*[:：]?\s*(?:撤退|正在路上|协助|支援|帮助|敌人|小心|危险|警告|推进|视野)" +
        @"|请求(?:撤退|协助|支援)|(?:发出)?撤退信号" +
        @"|\d+\s*金币|金币\s*(?:不足|不够)" +
        @"|回城\s*(?:中|开始|完成)?" +
        @"|传送\s*(?:中|开始|准备|完成)?" +
        @"|(?:R\s*[:：]|大招|终极技能).*(?:就绪|可用|准备好了|冷却完毕)" +
        @"|(?:闪现|治疗|点燃|传送|虚弱|屏障|净化|惩戒).*(?:就绪|可用|准备好了|冷却完毕)" +
        // 英文游戏术语
        @"|\b(?:ff|surr(?:ender)?|gg|ss|miss(?:ing)?|help|need\s*help|back|retreat|fall\s*back|push)\b" +
        @"|\b(?:ward|vision|need\s*vision|care(?:ful)?|danger|watch\s*out)\b" +
        @"|\bon\s*(?:my\s*)?way|coming|omw\b" +
        @"|\b(?:baron|nashor|drake|dragon)\b" +
        @"|\bgap\b|diff(?:erence)?\b" +
        @"|\bfeed(?:ing)?|inting?\b" +
        @"|\b(?:tp|teleport)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignalPatternRegex();
}
