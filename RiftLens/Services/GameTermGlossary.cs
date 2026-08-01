using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Aligns official Korean, Simplified Chinese and English League terminology
/// before text reaches the user's translation model. The embedded catalog is
/// generated from matching Data Dragon IDs, while manual rows cover common
/// player shorthand that does not exist in Riot's structured data.
/// </summary>
public static class GameTermGlossary
{
    private static readonly Lazy<GlossaryIndex> Index = new(LoadIndex);

    public static string Version => Index.Value.Version;
    public static int OfficialTermCount => Index.Value.OfficialTermCount;

    public static string ReplaceTerms(
        string line,
        AppLanguage language = AppLanguage.Chinese)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line ?? string.Empty;
        }

        var source = NormalizeText(line);
        var result = new StringBuilder(source.Length + 16);
        for (var index = 0; index < source.Length;)
        {
            var key = char.ToUpperInvariant(source[index]);
            if (Index.Value.ByFirstCharacter.TryGetValue(key, out var candidates))
            {
                Replacement? match = null;
                foreach (var candidate in candidates)
                {
                    if (MatchesAt(source, index, candidate))
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match is not null)
                {
                    var target = language == AppLanguage.English
                        ? match.English
                        : match.Chinese;
                    if (language == AppLanguage.English
                        && result.Length > 0
                        && char.IsLetterOrDigit(result[^1])
                        && char.IsLetterOrDigit(target[0]))
                    {
                        result.Append(' ');
                    }
                    result.Append(target);
                    index += match.Source.Length;
                    if (language == AppLanguage.English
                        && index < source.Length
                        && char.IsLetterOrDigit(target[^1])
                        && char.IsLetterOrDigit(source[index]))
                    {
                        result.Append(' ');
                    }
                    continue;
                }
            }

            result.Append(source[index]);
            index++;
        }

        return result.ToString();
    }

    private static GlossaryIndex LoadIndex()
    {
        GameTermCatalog? catalog = null;
        using (var stream = typeof(GameTermGlossary).Assembly
            .GetManifestResourceStream("RiftLens.Assets.lol_terms.json"))
        {
            if (stream is not null)
            {
                catalog = JsonSerializer.Deserialize<GameTermCatalog>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        catalog ??= new GameTermCatalog();
        var replacements = new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase);
        var ambiguousOfficialAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in catalog.Terms)
        {
            AddOfficialAliases(replacements, ambiguousOfficialAliases, term);
        }

        // Common player vocabulary intentionally wins over an ambiguous official
        // name. These phrases are written by players and are not Data Dragon IDs.
        foreach (var term in ManualTerms)
        {
            foreach (var alias in term.Aliases)
            {
                AddReplacement(
                    replacements,
                    ambiguousOfficialAliases,
                    alias,
                    term.Chinese,
                    term.English,
                    priority: 2,
                    allowShort: true);
            }
        }

        var byFirstCharacter = replacements.Values
            .GroupBy(replacement => char.ToUpperInvariant(replacement.Source[0]))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Replacement>)group
                    .OrderByDescending(replacement => replacement.Source.Length)
                    .ThenByDescending(replacement => replacement.Priority)
                    .ToArray());
        return new GlossaryIndex(
            catalog.Version,
            catalog.Terms.Count,
            byFirstCharacter);
    }

    private static void AddOfficialAliases(
        IDictionary<string, Replacement> replacements,
        ISet<string> ambiguousOfficialAliases,
        GameTermEntry term)
    {
        foreach (var alias in new[] { term.Korean, term.Chinese, term.English })
        {
            AddReplacement(
                replacements,
                ambiguousOfficialAliases,
                alias,
                term.Chinese,
                term.English,
                priority: 1,
                allowShort: false);

            // Small OCR text often loses spaces inside Hangul or Han names.
            // English spaces remain meaningful word boundaries and are not removed.
            if (!ContainsAsciiLetter(alias) && alias.Any(char.IsWhiteSpace))
            {
                AddReplacement(
                    replacements,
                    ambiguousOfficialAliases,
                    string.Concat(alias.Where(character => !char.IsWhiteSpace(character))),
                    term.Chinese,
                    term.English,
                    priority: 1,
                    allowShort: false);
            }
        }
    }

    private static void AddReplacement(
        IDictionary<string, Replacement> replacements,
        ISet<string> ambiguousOfficialAliases,
        string? source,
        string chinese,
        string english,
        int priority,
        bool allowShort)
    {
        if (string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(chinese)
            || string.IsNullOrWhiteSpace(english))
        {
            return;
        }

        var normalized = NormalizeText(source).Trim();
        var meaningfulLength = normalized.Count(char.IsLetterOrDigit);
        var minimumLength = ContainsAsciiLetter(normalized) ? 3 : 2;
        if (!allowShort && meaningfulLength < minimumLength)
        {
            return;
        }

        var replacement = new Replacement(normalized, chinese, english, priority);
        if (!replacements.TryGetValue(normalized, out var existing))
        {
            if (priority > 1 || !ambiguousOfficialAliases.Contains(normalized))
            {
                replacements[normalized] = replacement;
            }
            return;
        }

        if (string.Equals(existing.Chinese, chinese, StringComparison.Ordinal)
            && string.Equals(existing.English, english, StringComparison.Ordinal))
        {
            if (priority > existing.Priority)
            {
                replacements[normalized] = replacement;
            }
            return;
        }

        if (priority > existing.Priority)
        {
            replacements[normalized] = replacement;
            return;
        }
        if (priority < existing.Priority)
        {
            return;
        }

        // Two official IDs with the same visible name but different locale
        // targets are unsafe without structured game context, so neither wins.
        replacements.Remove(normalized);
        if (priority == 1)
        {
            ambiguousOfficialAliases.Add(normalized);
        }
    }

    private static bool MatchesAt(string source, int index, Replacement candidate)
    {
        if (index + candidate.Source.Length > source.Length
            || string.Compare(
                source,
                index,
                candidate.Source,
                0,
                candidate.Source.Length,
                StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        if (!ContainsAsciiLetter(candidate.Source))
        {
            return true;
        }

        var beforeIsWord = index > 0 && char.IsLetterOrDigit(source[index - 1]);
        var afterIndex = index + candidate.Source.Length;
        var afterIsWord = afterIndex < source.Length && char.IsLetterOrDigit(source[afterIndex]);
        return !beforeIsWord && !afterIsWord;
    }

    private static bool ContainsAsciiLetter(string value) =>
        value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static string NormalizeText(string value) =>
        Regex.Replace(
                value
                    .Normalize(NormalizationForm.FormKC)
                    .Replace('’', '\'')
                    .Replace('‘', '\'')
                    .Replace('–', '-')
                    .Replace('—', '-'),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();

    private static readonly ManualTerm[] ManualTerms =
    {
        // Roles and lanes
        new("上路", "top", "탑", "上路", "top lane", "top"),
        new("中路", "mid", "미드", "中路", "mid lane", "mid"),
        new("下路", "bot", "바텀", "봇", "下路", "bot lane", "bottom", "bot"),
        new("打野", "jungle", "정글", "打野", "jungler", "jungle", "jg"),
        new("辅助", "support", "서폿", "서포터", "辅助", "support", "sup"),
        new("ADC", "ADC", "원딜", "射手", "ADC", "ad carry"),
        new("坦克", "tank", "탱커", "坦克", "tank"),
        new("输出位", "damage dealer", "딜러", "输出位", "damage dealer"),

        // Map objectives and camps
        new("小龙", "Dragon", "용", "드래곤", "小龙", "元素龙", "Dragon", "drake"),
        new("远古巨龙", "Elder Dragon", "장로 드래곤", "장로", "远古巨龙", "Elder Dragon"),
        new("纳什男爵", "Baron Nashor", "내셔 남작", "내셔", "바론", "纳什男爵", "大龙", "Baron Nashor", "Baron"),
        new("峡谷先锋", "Rift Herald", "협곡의 전령", "전령", "峡谷先锋", "Rift Herald", "Herald"),
        new("虚空巢虫", "Void Grubs", "공허 유충", "유충", "虚空巢虫", "Void Grubs", "Grubs"),
        new("阿塔坎", "Atakhan", "아타칸", "阿塔坎", "Atakhan"),
        new("防御塔", "turret", "포탑", "타워", "防御塔", "turret", "tower"),
        new("召唤水晶", "inhibitor", "억제기", "召唤水晶", "inhibitor"),
        new("水晶枢纽", "Nexus", "넥서스", "水晶枢纽", "Nexus"),
        new("蓝 buff", "blue buff", "블루", "蓝 buff", "蓝BUFF", "blue buff"),
        new("红 buff", "red buff", "레드", "红 buff", "红BUFF", "red buff"),
        new("魔沼蛙", "Gromp", "두꺼비", "魔沼蛙", "蛤蟆", "Gromp"),
        new("暗影狼", "Murk Wolves", "늑대", "暗影狼", "三狼", "Murk Wolves", "wolves"),
        new("锋喙鸟", "Raptors", "칼날부리", "칼부", "锋喙鸟", "F6", "Raptors"),
        new("石甲虫", "Krugs", "돌거북", "石甲虫", "石头人", "Krugs"),
        new("峡谷迅捷蟹", "Rift Scuttler", "바위 게", "河道蟹", "峡谷迅捷蟹", "Rift Scuttler", "Scuttle"),

        // Summoner-spell shorthand and cooldown calls
        new("闪现", "Flash", "점멸", "플", "闪现", "Flash"),
        new("解封的传送", "Unleashed Teleport", "강력 순간이동", "강력순간이동", "解封的传送", "Unleashed Teleport"),
        new("传送", "Teleport", "순간이동", "텔레포트", "텔포", "텔", "传送", "Teleport", "TP"),
        new("引燃", "Ignite", "점화", "点燃", "引燃", "Ignite"),
        new("治疗术", "Heal", "회복", "힐", "治疗", "治疗术", "Heal"),
        new("幽灵疾步", "Ghost", "유체화", "疾跑", "幽灵疾步", "Ghost"),
        new("净化", "Cleanse", "정화", "净化", "Cleanse"),
        new("虚弱", "Exhaust", "탈진", "虚弱", "Exhaust"),
        new("屏障", "Barrier", "방어막", "屏障", "Barrier"),
        new("惩戒", "Smite", "강타", "惩戒", "Smite"),
        new("没有闪现", "no Flash", "노플", "플 없음", "플없", "플 빠짐", "没闪", "无闪现", "no flash"),
        new("没有大招", "no ultimate", "노궁", "궁 없음", "궁없", "没大", "没有大招", "no ult"),

        // Combat and game mechanics
        new("召唤师技能", "summoner spell", "스펠", "召唤师技能", "summoner spell"),
        new("终极技能", "ultimate", "궁극기", "궁", "大招", "终极技能", "ultimate", "ult"),
        new("被动技能", "passive", "패시브", "被动", "被动技能", "passive"),
        new("冷却时间", "cooldown", "대기시간", "대기 시간", "쿨타임", "쿨", "冷却", "冷却时间", "CD", "cooldown"),
        new("控制", "crowd control", "군중 제어", "씨씨", "控制", "CC", "crowd control"),
        new("伤害", "damage", "딜", "伤害", "damage", "dmg"),
        new("坦度", "tankiness", "탱", "坦度", "tankiness"),
        new("仇恨", "aggro", "어그로", "仇恨", "aggro"),
        new("经验值", "experience", "경험치", "经验", "经验值", "experience", "XP"),
        new("金币", "gold", "골드", "金币", "gold"),
        new("蓝量", "mana", "마나", "蓝量", "法力", "法力值", "mana"),
        new("生命值", "health", "체력", "生命值", "血量", "health", "HP"),
        new("等级", "level", "레벨", "等级", "level", "lvl"),
        new("补刀", "CS", "CS", "cs", "补刀", "CS"),
        new("小兵", "minion", "미니언", "小兵", "minion"),
        new("炮车兵", "cannon minion", "대포 미니언", "炮车兵", "cannon minion"),
        new("兵线", "wave", "웨이브", "兵线", "wave"),
        new("视野", "vision", "시야", "视野", "vision"),
        new("侦查守卫", "ward", "와드", "眼", "侦查守卫", "ward"),
        new("控制守卫", "Control Ward", "제어 와드", "제어와드", "핑와", "真眼", "控制守卫", "Control Ward", "pink ward"),
        new("神谕透镜", "Oracle Lens", "렌즈", "扫描", "神谕透镜", "Oracle Lens", "sweeper"),
        new("草丛", "brush", "부쉬", "수풀", "草丛", "brush", "bush"),

        // Player shot-calling vocabulary
        new("对线期", "laning phase", "라인전", "对线期", "laning phase"),
        new("前期", "early game", "초반", "前期", "early game"),
        new("中期", "mid game", "중반", "中期", "mid game"),
        new("后期", "late game", "후반", "后期", "late game"),
        new("团战", "teamfight", "한타", "团战", "teamfight"),
        new("龙团", "Dragon fight", "용 한타", "小龙团", "龙团", "Dragon fight"),
        new("大龙团", "Baron fight", "바론 한타", "男爵团", "大龙团", "Baron fight"),
        new("资源目标", "objective", "오브젝트", "资源目标", "objective"),
        new("支援", "rotate", "합류", "支援", "集合", "rotate"),
        new("游走", "roam", "로밍", "游走", "roam"),
        new("抓人", "gank", "갱킹", "갱", "抓人", "gank"),
        new("反蹲", "counter-gank", "역갱", "反蹲", "counter-gank"),
        new("越塔", "dive", "다이브", "越塔", "dive"),
        new("控线", "freeze", "프리징", "控线", "freeze"),
        new("推线", "push", "푸쉬", "밀기", "推线", "push"),
        new("清线", "waveclear", "라인 클리어", "清线", "waveclear"),
        new("分带", "split push", "스플릿", "사이드", "分带", "split push"),
        new("偷家", "backdoor", "백도어", "偷家", "backdoor"),
        new("开团", "engage", "이니시", "이니시에이팅", "开团", "engage"),
        new("消耗", "poke", "포킹", "消耗", "poke"),
        new("换血", "trade", "딜교", "换血", "trade"),
        new("拉扯", "kite", "카이팅", "拉扯", "kite"),
        new("入侵野区", "invade", "인베이드", "入侵野区", "invade"),
        new("反野", "counter-jungle", "카정", "反野", "counter-jungle"),
        new("帮开野", "leash", "리쉬", "帮开野", "leash"),
        new("回城", "recall", "귀환", "回城", "recall", "back"),
        new("敌人消失", "missing", "미아", "사라짐", "敌人消失", "miss", "missing", "mia"),
        new("支援掩护", "cover", "백업", "커버", "支援掩护", "cover", "backup"),
        new("选取", "pick", "픽", "选取", "pick"),
        new("禁用", "ban", "밴", "禁用", "ban"),
        new("滚雪球", "snowball", "스노우볼", "滚雪球", "snowball"),
        new("带领取胜", "carry", "캐리", "带领取胜", "carry"),

        // Common item shorthand whose official full names are in the data catalog
        new("中娅沙漏", "Zhonya's Hourglass", "존야", "金身", "中娅", "中娅沙漏", "Zhonya's", "Zhonya's Hourglass"),
        new("守护天使", "Guardian Angel", "가엔", "复活甲", "守护天使", "GA", "Guardian Angel"),
        new("无尽之刃", "Infinity Edge", "무대", "无尽", "无尽之刃", "IE", "Infinity Edge"),
        new("灭世者的死亡之帽", "Rabadon's Deathcap", "라바돈", "帽子", "大帽", "灭世者的死亡之帽", "Rabadon's", "Deathcap"),
        new("虚空之杖", "Void Staff", "보이드", "法穿杖", "虚空之杖", "Void Staff"),
        new("莫雷洛秘典", "Morellonomicon", "모렐로", "鬼书", "莫雷洛秘典", "Morellonomicon"),
        new("幽梦之灵", "Youmuu's Ghostblade", "요우무", "幽梦", "幽梦之灵", "Youmuu's", "Ghostblade"),
        new("无限火力", "URF", "우르프", "无限火力", "URF")
    };

    private sealed record ManualTerm(
        string Chinese,
        string English,
        params string[] Aliases);

    private sealed record Replacement(
        string Source,
        string Chinese,
        string English,
        int Priority);

    private sealed record GlossaryIndex(
        string Version,
        int OfficialTermCount,
        IReadOnlyDictionary<char, IReadOnlyList<Replacement>> ByFirstCharacter);
}
