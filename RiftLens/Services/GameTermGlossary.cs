using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// LOL 游戏术语表：韩→中固定映射，防止 AI 误译游戏专有名词。
/// 在 AI 翻译前将原文中的游戏术语替换为中文标签，确保翻译一致性。
/// 术语来源：LunaTranslator 术语表机制 + Riot Data Dragon 16.14.1。
/// </summary>
public static partial class GameTermGlossary
{
    /// <summary>
    /// 将原文中的韩文游戏术语替换为中文，降低 AI 误解概率。
    /// </summary>
    public static string ReplaceTerms(string line, AppLanguage language = AppLanguage.Chinese)
    {
        var result = line;
        foreach (var (korean, chinese) in Terms.OrderByDescending(term => term.Korean.Length))
        {
            var idx = result.IndexOf(korean, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            // 只替换独立的词（前后为空格/标点/行首行尾）
            result = GetBoundaryRegex(korean).Replace(result, m =>
            {
                var bef = m.Groups[1].Value;
                var aft = m.Groups[3].Value;
                return bef + (language == AppLanguage.English ? ToEnglish(chinese) : chinese) + aft;
            });
        }
        return result;
    }

    /// <summary>
    /// 韩文→中文固定映射（地图位置/装备/游戏机制等通用术语）。
    /// 英雄名由 HeroDictionary 处理，此处不重复。
    /// </summary>
    private static readonly (string Korean, string Chinese)[] Terms =
    {
        // ── 地图位置 ──
        ("탑",       "上路"),     ("미드",     "中路"),
        ("바텀",     "下路"),     ("봇",       "下路"),
        ("정글",     "打野"),     ("서폿",     "辅助"),
        ("원딜",     "ADC"),      ("탱커",     "坦克"),

        // ── 野怪 / 目标 ──
        ("드래곤",   "小龙"),     ("내셔",     "大龙/男爵"),
        ("전령",     "峡谷先锋"), ("유충",     "虚空巢虫"),
        ("바론",     "男爵"),     ("블루",     "蓝buff"),
        ("레드",     "红buff"),   ("칼날부리", "F6/鸟"),
        ("두꺼비",   "蛤蟆"),     ("늑대",     "三狼"),
        ("돌거북",   "石头人"),

        // ── 装备 ──
        ("존야",     "金身/中亚"), ("루덴",    "卢登"),
        ("리안드리", "大面具"),    ("라바돈",  "帽子/大帽"),
        ("공허의 지팡이", "法穿杖"), ("모렐로", "鬼书"),
        ("그림자 검", "幽梦"),     ("드락사르", "暗爪"),
        ("스태틱",   "电刀"),      ("무한의 대검", "无尽"),
        ("피바라기", "饮血"),      ("정수 약탈자", "吸蓝刀"),
        ("수호 천사", "复活甲"),   ("가엔",    "复活甲"),
        ("신발",     "鞋子"),

        // ── 召唤师技能 ──
        ("점멸",     "闪现"),     ("회복",     "治疗"),
        ("점화",     "点燃"),     ("텔레포트", "传送"),
        ("탈진",     "虚弱"),     ("방어막",   "屏障"),
        ("정화",     "净化"),     ("강타",     "惩戒"),

        // ── 符文/机制 ──
        ("룬",       "符文"),     ("특성",     "天赋"),
        ("스펠",     "召唤师技能"), ("궁",      "大招"),
        ("패시브",   "被动"),     ("쿨타임",   "CD/冷却"),
        ("딜",       "伤害"),     ("탱",       "肉/坦度"),
        ("CC",       "控制"),     ("cc",       "控制"),

        // ── 游戏进度 ──
        ("초반",     "前期"),     ("중반",     "中期"),
        ("후반",     "后期"),     ("라인전",   "对线期"),
        ("한타",     "团战"),     ("용 한타",  "龙团"),
        ("바론 한타","大龙团"),   ("오브젝트", "资源/目标"),
        ("시야",     "视野"),     ("와드",     "眼"),

        // ── 战术用语 ──
        ("갱",       "gank"),     ("로밍",     "游走"),
        ("다이브",   "越塔"),     ("프리징",   "控线"),
        ("푸쉬",     "推线"),     ("라인 클리어", "清线"),
        ("스플릿",   "分带"),     ("백도어",   "偷家"),
        ("이니시",   "开团"),     ("어그로",   "仇恨/吸引火力"),
        ("딜교",     "换血"),     ("포킹",     "poke/消耗"),
        ("버프",     "buff/增益"), ("디버프",  "debuff/减益"),
        ("리쉬",     "帮打野开野"),
    };

    private static readonly Dictionary<string, Regex> BoundaryCache = new();

    private static string ToEnglish(string chinese) => chinese switch
    {
        "上路" => "top", "中路" => "mid", "下路" => "bot", "打野" => "jungle",
        "辅助" => "support", "ADC" => "ADC", "坦克" => "tank",
        "小龙" => "Dragon", "大龙/男爵" or "男爵" => "Baron", "峡谷先锋" => "Rift Herald",
        "虚空巢虫" => "Void Grubs", "蓝buff" => "blue buff", "红buff" => "red buff",
        "F6/鸟" => "raptors", "蛤蟆" => "Gromp", "三狼" => "wolves", "石头人" => "Krugs",
        "金身/中亚" => "Zhonya's", "卢登" => "Luden's", "大面具" => "Liandry's",
        "帽子/大帽" => "Rabadon's", "法穿杖" => "Void Staff", "鬼书" => "Morellonomicon",
        "幽梦" => "Youmuu's", "暗爪" => "Duskblade", "电刀" => "Statikk Shiv",
        "无尽" => "Infinity Edge", "饮血" => "Bloodthirster", "吸蓝刀" => "Essence Reaver",
        "复活甲" => "Guardian Angel", "鞋子" => "boots",
        "闪现" => "Flash", "治疗" => "Heal", "点燃" => "Ignite", "传送" => "Teleport",
        "虚弱" => "Exhaust", "屏障" => "Barrier", "净化" => "Cleanse", "惩戒" => "Smite",
        "符文" => "runes", "天赋" => "runes", "召唤师技能" => "summoner spell",
        "大招" => "ultimate", "被动" => "passive", "CD/冷却" => "cooldown",
        "伤害" => "damage", "肉/坦度" => "tankiness", "控制" => "CC",
        "前期" => "early game", "中期" => "mid game", "后期" => "late game",
        "对线期" => "laning phase", "团战" => "teamfight", "龙团" => "Dragon fight",
        "大龙团" => "Baron fight", "资源/目标" => "objective", "视野" => "vision", "眼" => "ward",
        "gank" => "gank", "游走" => "roam", "越塔" => "dive", "控线" => "freeze",
        "推线" => "push", "清线" => "waveclear", "分带" => "split push", "偷家" => "backdoor",
        "开团" => "engage", "仇恨/吸引火力" => "aggro", "换血" => "trade",
        "poke/消耗" => "poke", "buff/增益" => "buff", "debuff/减益" => "debuff",
        "帮打野开野" => "leash",
        _ => chinese
    };

    private static Regex GetBoundaryRegex(string term)
    {
        if (!BoundaryCache.TryGetValue(term, out var regex))
        {
            var escaped = Regex.Escape(term);
            regex = new Regex(
                $@"(^|[\s,，.。!！?？:：\[\(])({escaped})(\s|$|[,，.。!！?？:：\]\)])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            BoundaryCache[term] = regex;
        }
        return regex;
    }
}
