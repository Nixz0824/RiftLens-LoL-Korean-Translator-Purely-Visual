using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Local, deterministic speech for classified LoL system events. Player IDs and
/// timestamps are intentionally omitted because they add no tactical value.
/// </summary>
public static class SystemMessageTemplates
{
    public static string Format(
        ClassifiedGameMessage message,
        AppLanguage language)
    {
        if (message.Kind == GameMessageKind.TacticalSignal)
        {
            return SignalMessageTemplates.FormatSpokenSignal(
                message.TacticalSummary,
                string.Empty,
                language,
                hasKnownInitiator: false);
        }

        if (message.SystemEvent is not { } systemEvent)
        {
            return language == AppLanguage.English
                ? "Unclassified game notice."
                : "未确认类型的游戏通知。";
        }

        return language == AppLanguage.English
            ? FormatEnglish(systemEvent)
            : FormatChinese(systemEvent);
    }

    private static string FormatChinese(GameSystemEvent systemEvent)
    {
        var objective = ObjectiveName(systemEvent.Objective, systemEvent.Evidence, AppLanguage.Chinese);
        return systemEvent.Type switch
        {
            GameSystemEventType.ChampionKill => "击杀播报：发生一次英雄击杀。",
            GameSystemEventType.Execution => "击杀播报：有英雄被处决。",
            GameSystemEventType.FirstBlood => "击杀播报：第一滴血。",
            GameSystemEventType.DoubleKill => "击杀播报：双杀。",
            GameSystemEventType.TripleKill => "击杀播报：三杀。",
            GameSystemEventType.QuadraKill => "击杀播报：四杀。",
            GameSystemEventType.PentaKill => "击杀播报：五杀。",
            GameSystemEventType.KillingSpree => "击杀播报：大杀特杀。",
            GameSystemEventType.Rampage => "击杀播报：暴走。",
            GameSystemEventType.Unstoppable => "击杀播报：无人能挡。",
            GameSystemEventType.Dominating => "击杀播报：主宰比赛。",
            GameSystemEventType.Godlike => "击杀播报：接近神。",
            GameSystemEventType.Legendary => "击杀播报：超越神。",
            GameSystemEventType.Shutdown => "击杀播报：终结连杀。",
            GameSystemEventType.Ace => "击杀播报：团灭。",
            GameSystemEventType.ChampionRespawn => "击杀播报：有英雄已经复活。",

            GameSystemEventType.ObjectiveSpawn => $"地图资源提醒：{objective}已经刷新。",
            GameSystemEventType.ObjectiveSpawnSoon when systemEvent.DurationSeconds is int seconds =>
                $"地图资源提醒：{objective}还有{seconds}秒刷新。",
            GameSystemEventType.ObjectiveSpawnSoon => $"地图资源提醒：{objective}即将刷新。",
            GameSystemEventType.ObjectiveSlain => $"地图资源提醒：{objective}已被击杀。",
            GameSystemEventType.ObjectiveStolen => $"地图资源提醒：{objective}被抢夺。",
            GameSystemEventType.HeraldCaptured => "地图资源提醒：峡谷先锋已被捕获。",
            GameSystemEventType.HeraldReturned => "地图资源提醒：峡谷先锋已返回虚空。",
            GameSystemEventType.TerrainTransform => "地图资源提醒：元素峡谷地形已经变化。",
            GameSystemEventType.FirstTurret => "建筑提醒：第一座防御塔已被摧毁。",
            GameSystemEventType.TurretDestroyed => "建筑提醒：一座防御塔已被摧毁。",
            GameSystemEventType.TurretPlateDestroyed => "建筑提醒：防御塔镀层已被摧毁。",
            GameSystemEventType.InhibitorDestroyed => "建筑提醒：一座召唤水晶已被摧毁。",
            GameSystemEventType.InhibitorRespawned => "建筑提醒：一座召唤水晶已经重生。",
            GameSystemEventType.NexusDestroyed => "建筑提醒：一座水晶枢纽已被摧毁。",
            GameSystemEventType.ObjectiveBountyClaimed => "地图提醒：一个战略点悬赏已被夺取。",

            GameSystemEventType.MinionsSpawned => "对局流程：双方开始出兵。",
            GameSystemEventType.PlayerDisconnected => "对局流程：有玩家断开连接。",
            GameSystemEventType.PlayerReconnected => "对局流程：有玩家重新连接。",
            GameSystemEventType.LoadingTimeout => "对局流程：有玩家载入超时并断开连接。",
            GameSystemEventType.SurrenderVote => "对局流程：投降投票已经开始。",
            GameSystemEventType.RemakeVote => "对局流程：重开投票已经开始。",
            GameSystemEventType.AfkSurrender => "对局流程：因队友离开，可以提前投降。",
            GameSystemEventType.GamePaused => "对局流程：游戏已暂停。",
            GameSystemEventType.GameResumed => "对局流程：游戏即将恢复。",
            GameSystemEventType.ChatRestricted => "系统提醒：聊天功能受到限制。",
            GameSystemEventType.PingRateLimited => "系统提醒：信号发送过于频繁。",
            GameSystemEventType.NetworkNotice => "系统提醒：网络连接状态发生变化。",
            GameSystemEventType.ChampionSpecialEvent => "对局流程：发生英雄专属事件。",
            GameSystemEventType.WellnessReminder => "防沉迷提示：请注意游戏时长并适当休息。",
            _ => "未确认类型的游戏通知。"
        };
    }

    private static string FormatEnglish(GameSystemEvent systemEvent)
    {
        var objective = ObjectiveName(systemEvent.Objective, systemEvent.Evidence, AppLanguage.English);
        return systemEvent.Type switch
        {
            GameSystemEventType.ChampionKill => "Kill feed: a champion was slain.",
            GameSystemEventType.Execution => "Kill feed: a champion was executed.",
            GameSystemEventType.FirstBlood => "Kill feed: first blood.",
            GameSystemEventType.DoubleKill => "Kill feed: double kill.",
            GameSystemEventType.TripleKill => "Kill feed: triple kill.",
            GameSystemEventType.QuadraKill => "Kill feed: quadra kill.",
            GameSystemEventType.PentaKill => "Kill feed: pentakill.",
            GameSystemEventType.KillingSpree => "Kill feed: killing spree.",
            GameSystemEventType.Rampage => "Kill feed: rampage.",
            GameSystemEventType.Unstoppable => "Kill feed: unstoppable.",
            GameSystemEventType.Dominating => "Kill feed: dominating.",
            GameSystemEventType.Godlike => "Kill feed: godlike.",
            GameSystemEventType.Legendary => "Kill feed: legendary.",
            GameSystemEventType.Shutdown => "Kill feed: shutdown.",
            GameSystemEventType.Ace => "Kill feed: ace.",
            GameSystemEventType.ChampionRespawn => "Kill feed: a champion has respawned.",

            GameSystemEventType.ObjectiveSpawn => $"Map objective: {objective} has spawned.",
            GameSystemEventType.ObjectiveSpawnSoon when systemEvent.DurationSeconds is int seconds =>
                $"Map objective: {objective} spawns in {seconds} seconds.",
            GameSystemEventType.ObjectiveSpawnSoon => $"Map objective: {objective} will spawn soon.",
            GameSystemEventType.ObjectiveSlain => $"Map objective: {objective} was slain.",
            GameSystemEventType.ObjectiveStolen => $"Map objective: {objective} was stolen.",
            GameSystemEventType.HeraldCaptured => "Map objective: Rift Herald was captured.",
            GameSystemEventType.HeraldReturned => "Map objective: Rift Herald returned to the Void.",
            GameSystemEventType.TerrainTransform => "Map objective: the Elemental Rift terrain changed.",
            GameSystemEventType.FirstTurret => "Structure: the first turret was destroyed.",
            GameSystemEventType.TurretDestroyed => "Structure: a turret was destroyed.",
            GameSystemEventType.TurretPlateDestroyed => "Structure: turret plating was destroyed.",
            GameSystemEventType.InhibitorDestroyed => "Structure: an inhibitor was destroyed.",
            GameSystemEventType.InhibitorRespawned => "Structure: an inhibitor respawned.",
            GameSystemEventType.NexusDestroyed => "Structure: a nexus was destroyed.",
            GameSystemEventType.ObjectiveBountyClaimed => "Map objective: an Objective Bounty was claimed.",

            GameSystemEventType.MinionsSpawned => "Match flow: minions have spawned.",
            GameSystemEventType.PlayerDisconnected => "Match flow: a player disconnected.",
            GameSystemEventType.PlayerReconnected => "Match flow: a player reconnected.",
            GameSystemEventType.LoadingTimeout => "Match flow: a player timed out while loading.",
            GameSystemEventType.SurrenderVote => "Match flow: a surrender vote started.",
            GameSystemEventType.RemakeVote => "Match flow: a remake vote started.",
            GameSystemEventType.AfkSurrender => "Match flow: early surrender is available because a player is AFK.",
            GameSystemEventType.GamePaused => "Match flow: the game is paused.",
            GameSystemEventType.GameResumed => "Match flow: the game will resume shortly.",
            GameSystemEventType.ChatRestricted => "System notice: chat is restricted.",
            GameSystemEventType.PingRateLimited => "System notice: pings are temporarily rate-limited.",
            GameSystemEventType.NetworkNotice => "System notice: the network connection changed.",
            GameSystemEventType.ChampionSpecialEvent => "Match flow: a champion-specific event occurred.",
            GameSystemEventType.WellnessReminder => "Wellness reminder: mind your play time and take regular breaks.",
            _ => "Unclassified game notice."
        };
    }

    private static string ObjectiveName(string objective, string evidence, AppLanguage language)
    {
        var text = string.IsNullOrWhiteSpace(objective) ? evidence : objective;
        if (ContainsAny(text, "내셔", "바론", "男爵", "baron", "nashor"))
            return language == AppLanguage.English ? "Baron Nashor" : "男爵";
        if (ContainsAny(text, "전령", "峡谷先锋", "rift herald"))
            return language == AppLanguage.English ? "Rift Herald" : "峡谷先锋";
        if (ContainsAny(text, "유충", "巢虫", "voidgrub", "void grub"))
            return language == AppLanguage.English ? "Void Grubs" : "虚空巢虫";
        if (ContainsAny(text, "장로", "远古", "elder"))
            return language == AppLanguage.English ? "Elder Dragon" : "远古巨龙";
        return language == AppLanguage.English ? "Dragon" : "巨龙";
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
