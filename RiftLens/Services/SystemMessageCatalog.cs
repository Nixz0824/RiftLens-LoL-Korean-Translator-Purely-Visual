using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Version-scoped, deterministic catalog for LoL's fixed-format system feed.
/// Rules are anchored to official 26.15 / CommunityDragon 16.15 client strings.
/// Deprecated and mode-specific rules are kept separate and never participate
/// in the live Summoner's Rift classifier.
/// </summary>
public static class SystemMessageCatalog
{
    public const double SpeechConfidenceThreshold = 0.85;

    private const string Objective =
        @"(?:장로\s*드래곤|드래곤|용|내셔\s*남작|바론|협곡의\s*전령|전령|공허\s*유충|유충|"
        + @"远古巨龙|远古龙|巨龙|小龙|元素龙|[\p{L}]{1,8}亚龙|纳什男爵|男爵|峡谷先锋|虚空巢虫|巢虫|"
        + @"elder\s+dragon|(?:elemental\s+)?dragon|drake|baron\s+nashor|baron|rift\s+herald|void\s*grubs?)";

    private static readonly IReadOnlyList<SystemRule> CurrentRules = new[]
    {
        Exact("combat.first_blood", GameMessageKind.KillEvent, GameSystemEventType.FirstBlood, 3,
            @"(?:선취점|첫\s*번째\s*킬|第一滴血|first\s+blood)"),
        Rule("combat.first_blood_feed", GameMessageKind.KillEvent, GameSystemEventType.FirstBlood, 3,
            @"^.{0,96}?(?:刚刚)?(?:拿下了|拿到了)?第一滴血[.!！。]?\s*$"),
        Exact("combat.double_kill", GameMessageKind.KillEvent, GameSystemEventType.DoubleKill, 2,
            @"(?:더블\s*킬|双杀|double\s+kill)"),
        Rule("combat.double_kill_feed", GameMessageKind.KillEvent, GameSystemEventType.DoubleKill, 2,
            @"^.{0,120}?完成了一次双杀[.!！。]?\s*$"),
        Exact("combat.triple_kill", GameMessageKind.KillEvent, GameSystemEventType.TripleKill, 3,
            @"(?:트리플\s*킬|三杀|triple\s+kill)"),
        Exact("combat.quadra_kill", GameMessageKind.KillEvent, GameSystemEventType.QuadraKill, 3,
            @"(?:쿼드라\s*킬|四杀|quadra\s+kill)"),
        Exact("combat.penta_kill", GameMessageKind.KillEvent, GameSystemEventType.PentaKill, 3,
            @"(?:펜타\s*킬|五杀|penta\s*kill|pentakill)"),
        Exact("combat.shutdown", GameMessageKind.KillEvent, GameSystemEventType.Shutdown, 2,
            @"(?:연속\s*킬\s*차단|终结|shut\s*down|shutdown)"),
        Exact("combat.ace", GameMessageKind.KillEvent, GameSystemEventType.Ace, 3,
            @"(?:마지막\s*적\s*처치|团灭|ace)"),

        Rule("combat.spree_legendary", GameMessageKind.KillEvent, GameSystemEventType.Legendary, 3,
            @"^(?:전설의\s+.+?님|.+?(?:已经超越神了|\bis\s+legendary))\s*[.!！]?$"),
        Rule("combat.spree_godlike", GameMessageKind.KillEvent, GameSystemEventType.Godlike, 3,
            @"^(?:전장의\s*화신\s+.+?님|.+?(?:已经接近神(?:了|的境界(?:啦)?)|\bis\s+godlike))\s*[.!！]?$"),
        Rule("combat.spree_dominating", GameMessageKind.KillEvent, GameSystemEventType.Dominating, 2,
            @"^(?:전장의\s*지배자\s+.+?님|.+?(?:已经主宰比赛了|\bis\s+dominating))\s*[.!！]?$"),
        Rule("combat.spree_unstoppable", GameMessageKind.KillEvent, GameSystemEventType.Unstoppable, 2,
            @"^.+?(?:님을\s*도저히\s*막을\s*수\s*없습니다|已经无人能挡(?:了)?|\bis\s+unstoppable)\s*[.!！]?$"),
        Rule("combat.spree_rampage", GameMessageKind.KillEvent, GameSystemEventType.Rampage, 2,
            @"^.+?(?:님이\s*미쳐\s*날뛰고\s*있습니다|已经(?:接近)?暴走了|\bis\s+on\s+a\s+rampage)\s*[.!！]?$"),
        Rule("combat.spree_killing", GameMessageKind.KillEvent, GameSystemEventType.KillingSpree, 2,
            @"^.+?(?:님이\s*학살\s*중입니다|正在大杀特杀|\bis\s+on\s+a\s+killing\s+spree)\s*[.!！]?$"),
        Rule("combat.execution", GameMessageKind.KillEvent, GameSystemEventType.Execution, 1,
            @"^(?:미니언|포탑|a\s+minion|a\s+turret|一名小兵|一座防御塔).{0,80}(?:처치했습니다|击杀了|has\s+slain).{0,48}[.!！]?$|^.+?(?:was|has\s+been)\s+executed[.!！]?$"),
        Rule("combat.champion_kill", GameMessageKind.KillEvent, GameSystemEventType.ChampionKill, 2,
            $@"^(?!.*{Objective}).{{1,80}}?(?:(?:님)?(?:이|가)\s+.{{1,80}}?(?:님)?(?:을|를)\s*처치했습니다|刚刚击杀了.{{1,80}}|has\s+slain\s+.{{1,80}})[.!！]?$"),
        Rule("combat.respawn", GameMessageKind.KillEvent, GameSystemEventType.ChampionRespawn, 1,
            @"^.{1,80}?(?:님이\s*부활했습니다|已经复活|has\s+respawned)[.!！]?$"),
        Rule("combat.partial_streak_feed", GameMessageKind.KillEvent, GameSystemEventType.Shutdown, 1,
            @"^.{1,80}?님이.{1,80}?님의\s*연속\s*$",
            confidence: 0.78),

        Rule("objective.stolen", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveStolen, 3,
            $@"^(?<subject>.{{1,80}}?)(?:이|가|은|는|军团|team)?\s*(?:{Objective})(?:을|를)?\s*(?:빼앗았습니다|抢夺了|抢到了|has\s+stolen)[.!！]?$"
            + $@"|^(?<subject>.{{1,80}}?)\s+has\s+stolen\s+(?:the\s+)?(?<objective>{Objective})[.!！]?$"),
        Rule("objective.herald_returned", GameMessageKind.ObjectiveEvent, GameSystemEventType.HeraldReturned, 1,
            @"^(?:협곡의\s*전령이\s*공허로\s*돌아갔습니다|峡谷先锋已返回虚空|rift\s+herald\s+has\s+returned\s+to\s+the\s+void)[.!！]?$"),
        Rule("objective.spawn_soon", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveSpawnSoon, 2,
            $@"^(?<objective>{Objective})(?:이|가)?\s*(?:(?:곧|即将|soon)\s*)?(?:(?<seconds>\d{{1,3}})\s*(?:초|秒|seconds?)\s*(?:후|后|in)?\s*)?(?:곧\s*)?(?:생성|등장|刷新|出现|spawn|respawn).*(?:예정|됩니다|即将|soon|in\s+\d{{1,3}}\s+seconds?)[.!！]?$"
            + $@"|^(?<objective>{Objective})\s*即将\s*在?\s*(?<seconds>\d{{1,3}})\s*秒后\s*(?:刷新|出现)[.!！]?$"),
        Rule("objective.spawn", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveSpawn, 2,
            $@"^(?:the\s+)?(?<objective>{Objective})(?:이|가)?\s*(?:생성되었습니다|등장했습니다|공허에서\s*솟아올랐습니다|已经登场|已从虚空中现身|已经刷新|已刷新|has\s+spawned|have\s+spawned|has\s+emerged\s+from\s+the\s+void)[.!！]?$"),
        Rule("objective.slain", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveSlain, 3,
            $@"^(?<subject>.{{1,96}}?)(?:이|가|은|는|军团|team)?\s*(?:已经|刚刚|has)?\s*(?:처치했습니다|已?击杀(?:了)?|消灭了|slain|defeated)\s*(?:the\s+)?(?<objective>{Objective})[.!！]?$|^(?<subject>.{{1,96}}?)(?:이|가)\s*(?<objective>{Objective})(?:을|를)\s*처치했습니다[.!！]?$"),
        Rule("objective.herald_captured", GameMessageKind.ObjectiveEvent, GameSystemEventType.HeraldCaptured, 2,
            @"^.{1,96}?(?:협곡의\s*전령을\s*획득|已捕获(?:了)?峡谷先锋|has\s+captured\s+rift\s+herald)[.!！]?$"),
        Rule("objective.terrain", GameMessageKind.ObjectiveEvent, GameSystemEventType.TerrainTransform, 1,
            @"^(?:화염의\s*협곡|대지의\s*협곡|바람의\s*협곡|화공의\s*협곡|炼狱峡谷|山脉峡谷|云霄峡谷|炼金峡谷|the\s+inferno\s+blazes|the\s+mountains\s+rise|the\s+cloud\s+winds\s+quicken|the\s+chemplants\s+bloom)[.!！]?$"),

        Rule("structure.first_turret", GameMessageKind.ObjectiveEvent, GameSystemEventType.FirstTurret, 2,
            @"^.{1,100}?(?:첫\s*번째\s*포탑을\s*파괴했습니다|已摧毁第一座塔|has\s+destroyed\s+the\s+first\s+turret)[.!！]?$"),
        Rule("structure.inhibitor_respawn", GameMessageKind.ObjectiveEvent, GameSystemEventType.InhibitorRespawned, 2,
            @"^(?:.+?억제기가\s*재생성되었습니다|一座.+?召唤水晶已经重生|.+?inhibitor\s+(?:has\s+)?respawned)[.!！]?$"),
        Rule("structure.inhibitor", GameMessageKind.ObjectiveEvent, GameSystemEventType.InhibitorDestroyed, 2,
            @"^.{1,100}?(?:억제기를\s*파괴했습니다|摧毁了(?:一座)?.{0,12}召唤水晶|has\s+destroyed\s+(?:an?|the)\s+.{0,12}inhibitor)[.!！]?$"),
        Rule("structure.nexus", GameMessageKind.ObjectiveEvent, GameSystemEventType.NexusDestroyed, 3,
            @"^.{1,100}?(?:넥서스를\s*파괴했습니다|摧毁了.{0,20}水晶枢纽|has\s+destroyed\s+.{0,20}(?:team'?s\s+)?nexus)[.!！]?$"),
        Rule("structure.turret_plate", GameMessageKind.ObjectiveEvent, GameSystemEventType.TurretPlateDestroyed, 1,
            @"^.{1,100}?(?:포탑\s*방패|镀层|turret\s+plating).*(?:파괴|摧毁|destroyed)[.!！]?$"),
        Rule("structure.turret", GameMessageKind.ObjectiveEvent, GameSystemEventType.TurretDestroyed, 2,
            @"^.{1,100}?(?:포탑을\s*파괴했습니다|摧毁了(?:一座)?防御塔|has\s+destroyed\s+(?:an?|the)\s+.{0,12}turret)[.!！]?$"),
        Rule("objective.bounty", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveBountyClaimed, 1,
            @"^(?:목표물\s*현상금을\s*획득했습니다|一个战略点悬赏已被夺取|an\s+objective\s+bounty\s+has\s+been\s+claimed)[.!！]?$"),

        Exact("flow.minions_spawned", GameMessageKind.OtherSystemEvent, GameSystemEventType.MinionsSpawned, 1,
            @"(?:미니언이\s*생성되었습니다|双方军团开始出兵了|小兵已经生成|minions\s+have\s+spawned)"),
        Rule("flow.loading_timeout", GameMessageKind.OtherSystemEvent, GameSystemEventType.LoadingTimeout, 1,
            @"^.{1,80}?(?:로딩\s*시간이\s*초과되어\s*연결이\s*끊겼습니다|载入时超时.{0,16}已断开连[结接]|was\s+disconnected\s+due\s+to\s+loading\s+time\s*out)[.!！]?$"),
        Rule("flow.reconnected", GameMessageKind.OtherSystemEvent, GameSystemEventType.PlayerReconnected, 1,
            @"^.{1,80}?(?:님이\s*다시\s*연결되었습니다|已重新连接|has\s+reconnected)[.!！]?$"),
        Rule("flow.disconnected", GameMessageKind.OtherSystemEvent, GameSystemEventType.PlayerDisconnected, 1,
            @"^.{1,80}?(?:님의\s*연결이\s*종료되었습니다|已断开连接|has\s+disconnected|left\s+the\s+game|게임에서\s*나갔)[.!！]?$"),
        Rule("flow.remake", GameMessageKind.OtherSystemEvent, GameSystemEventType.RemakeVote, 1,
            @"^(?:다시하기\s*투표가\s*시작|重开投票已开始|a\s+remake\s+vote\s+has\s+been\s+started).*$"),
        Rule("flow.surrender", GameMessageKind.OtherSystemEvent, GameSystemEventType.SurrenderVote, 1,
            @"^(?:항복\s*투표가\s*시작|发起了投降(?:投票)?|投降投票已开始|a\s+surrender\s+vote\s+has\s+been\s+started).*$"),
        Rule("flow.surrender_result", GameMessageKind.OtherSystemEvent, GameSystemEventType.SurrenderVote, 1,
            @"^아군이\s*찬성\s*\d+\s*표\s*반대\s*\d+\s*표로\s*항복에\s*찬성했습니다[.!！]?$"),
        Rule("flow.afk_surrender", GameMessageKind.OtherSystemEvent, GameSystemEventType.AfkSurrender, 1,
            @"^.*(?:팀원이\s*자리\s*비움|玩家.{0,20}(?:挂机|离开).{0,20}(?:投降|认输)|player\s+on\s+your\s+team\s+is\s+afk).*$"),
        Exact("flow.paused", GameMessageKind.OtherSystemEvent, GameSystemEventType.GamePaused, 1,
            @"(?:게임이\s*정지되었습니다|游戏暂停了|the\s+game\s+has\s+been\s+paused)"),
        Rule("flow.resumed", GameMessageKind.OtherSystemEvent, GameSystemEventType.GameResumed, 1,
            @"^(?:정지\s*상태가\s*해제되었습니다|游戏已经解除暂停了|the\s+game\s+has\s+been\s+unpaused).*$"),
        Rule("flow.ping_throttled", GameMessageKind.OtherSystemEvent, GameSystemEventType.PingRateLimited, 1,
            @"^(?:추가\s*신호를\s*보내려면\s*기다려야\s*합니다|目前暂不可发送信号|你必须先等一会才能发送更多该类型的信号|you\s+must\s+wait\s+before\s+issuing\s+more\s+pings).*$"),
        Rule("flow.chat_restricted", GameMessageKind.OtherSystemEvent, GameSystemEventType.ChatRestricted, 1,
            @"^.*(?:채팅\s*제한|聊天(?:受到|已被)限制|chat\s+(?:is\s+)?restricted).*$"),
        Rule("flow.network", GameMessageKind.OtherSystemEvent, GameSystemEventType.NetworkNotice, 1,
            @"^.*(?:서버\s*(?:연결|상태)|네트워크\s*연결|服务器连接|网络连接|server\s+connection|network\s+connection).*$"),

        Rule("special.sylas_ultimate_stolen", GameMessageKind.OtherSystemEvent, GameSystemEventType.ChampionSpecialEvent, 1,
            @"^.{1,64}?(?:이|가).{1,64}?의\s*궁극기를\s*강탈했습니다[.!！]?$"),
        Rule("special.lane_quest_completed", GameMessageKind.OtherSystemEvent, GameSystemEventType.ChampionSpecialEvent, 1,
            @"^.{1,96}?[\(（].{1,36}?[\)）]\s*(?:상단|중단|하단)\s*퀘스트\s*완료[.!！]?$|^.{1,96}?[\(（].{1,36}?[\)）]\s*(?:上路|中路|下路|野区|打野|守卫)任务完成[.!！]?$"),
        Rule("special.epic_monster_milestone", GameMessageKind.OtherSystemEvent, GameSystemEventType.ChampionSpecialEvent, 1,
            @"^.{1,96}?已达到.*(?:击杀|已击杀).*史诗野怪.*新?里程碑?\s*[:：]?\s*[\d.,，]*[.!！]?$"),

        Rule("wellness.play_time", GameMessageKind.ClientReminder, GameSystemEventType.WellnessReminder, 1,
            @"^.*(?:(?:게임|플레이).*(?:시간|휴식|건강)|(?:游戏|游玩|连续游戏).*(?:时间|小时|休息|健康|防沉迷)|(?:play|gaming).*(?:hours?|break|healthy|wellness)).*$")
    };

    private static readonly IReadOnlyList<SystemRule> LegacyRules = new[]
    {
        Rule("legacy.objective.atakhan", GameMessageKind.ObjectiveEvent, GameSystemEventType.ObjectiveSpawn, 1,
            @"^.*(?:아타칸|阿塔坎|厄塔汗|atakhan).*$", SystemMessageRuleStatus.LegacyOrModeSpecific),
        Rule("legacy.ping.bait_hold", GameMessageKind.TacticalSignal, GameSystemEventType.Unknown, 1,
            @"^.*(?:유인|诱敌|bait|사수|坚守|hold).*$", SystemMessageRuleStatus.LegacyOrModeSpecific),
        Exact("legacy.combat.hexakill", GameMessageKind.KillEvent, GameSystemEventType.PentaKill, 1,
            @"(?:헥사\s*킬|六杀|hexakill)", SystemMessageRuleStatus.LegacyOrModeSpecific)
    };

    public static bool TryClassify(
        string line,
        out GameMessageKind kind,
        out int priority,
        out GameSystemEvent systemEvent)
    {
        var normalized = Normalize(line);
        foreach (var rule in CurrentRules)
        {
            var match = rule.Pattern.Match(normalized);
            if (!match.Success)
            {
                continue;
            }

            kind = rule.Kind;
            priority = rule.Priority;
            systemEvent = BuildEvent(rule, match, normalized);
            return true;
        }

        kind = GameMessageKind.Unknown;
        priority = 0;
        systemEvent = new GameSystemEvent(
            GameSystemEventType.Unknown,
            Confidence: 0,
            Evidence: normalized);
        return false;
    }

    public static bool MatchesLegacyOrModeSpecific(string line, out string ruleId)
    {
        var normalized = Normalize(line);
        var rule = LegacyRules.FirstOrDefault(item => item.Pattern.IsMatch(normalized));
        ruleId = rule?.Id ?? string.Empty;
        return rule is not null;
    }

    private static GameSystemEvent BuildEvent(SystemRule rule, Match match, string evidence) =>
        new(
            rule.Type,
            Subject: Group(match, "subject"),
            Target: Group(match, "target"),
            Objective: Group(match, "objective"),
            Team: Group(match, "team"),
            Count: ParseInt(Group(match, "count")),
            DurationSeconds: ParseInt(Group(match, "seconds")),
            Confidence: rule.Confidence,
            Evidence: evidence,
            RuleId: rule.Id,
            RuleStatus: rule.Status);

    private static string Group(Match match, string name) =>
        match.Groups[name].Success ? match.Groups[name].Value.Trim() : string.Empty;

    private static int? ParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static SystemRule Exact(
        string id,
        GameMessageKind kind,
        GameSystemEventType type,
        int priority,
        string body,
        SystemMessageRuleStatus status = SystemMessageRuleStatus.Current) =>
        Rule(id, kind, type, priority, $@"^(?:{body})\s*[.!！。]?$", status, 0.99);

    private static SystemRule Rule(
        string id,
        GameMessageKind kind,
        GameSystemEventType type,
        int priority,
        string pattern,
        SystemMessageRuleStatus status = SystemMessageRuleStatus.Current,
        double confidence = 0.96) =>
        new(
            id,
            kind,
            type,
            priority,
            confidence,
            status,
            new Regex(
                pattern,
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant
                | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(80)));

    private static string Normalize(string value)
    {
        var normalized = Regex.Replace(
                ChineseTextNormalizer.ToSimplifiedCharacters(value)
                    .Normalize(NormalizationForm.FormKC)
                    .Trim(),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim(' ', '°', '·', '•');
        normalized = Regex.Replace(
            normalized,
            @"\s+([.!！。])$",
            "$1",
            RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"(?<=[\u3400-\u9FFF])\s+(?=[\u3400-\u9FFF])",
            string.Empty,
            RegexOptions.CultureInvariant);
        return normalized
            .Replace('別', '别')
            .Replace('毀', '毁')
            .Replace(',', '，');
    }

    private sealed record SystemRule(
        string Id,
        GameMessageKind Kind,
        GameSystemEventType Type,
        int Priority,
        double Confidence,
        SystemMessageRuleStatus Status,
        Regex Pattern);
}
