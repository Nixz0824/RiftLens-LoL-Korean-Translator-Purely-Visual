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
        if (TryParseEmbeddedHudShare(normalized, out semanticEvent)
            || TryParseObjectiveRespawn(normalized, out semanticEvent)
            || TryParseSkillCooldown(normalized, out semanticEvent)
            || TryParseDamagedCooldownFragment(normalized, out semanticEvent)
            || TryParseSkillUnlearned(normalized, out semanticEvent)
            || TryParseHudResource(normalized, out semanticEvent)
            || TryParseWardCharges(normalized, out semanticEvent)
            || TryParseWardPurchase(normalized, out semanticEvent)
            || TryParseItemGoldNeed(normalized, out semanticEvent)
            || TryParseAbilityStateShare(normalized, out semanticEvent)
            || TryParseExplicitAbilityUse(normalized, out semanticEvent)
            || TryParseStructureStatus(normalized, out semanticEvent)
            || TryParseGeneratedTargetFragment(normalized, out semanticEvent)
            || TryParseObjectiveFragment(normalized, out semanticEvent)
            || TryParseSummonerSpellMarker(normalized, out semanticEvent)
            || TryParseAbilityMarker(normalized, out semanticEvent)
            || TryParseCountdown(normalized, out semanticEvent)
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
        if (TryParseEmbeddedHudShare(normalized, out semanticEvent)
            || TryParseExplicitSignal(normalized, out semanticEvent)
            || TryParseMarker(normalized, out semanticEvent)
            || TryParseObjectiveRespawn(normalized, out semanticEvent)
            || TryParseSkillCooldown(normalized, out semanticEvent)
            || TryParseDamagedCooldownFragment(normalized, out semanticEvent)
            || TryParseSkillUnlearned(normalized, out semanticEvent)
            || TryParseHudResource(normalized, out semanticEvent)
            || TryParseWardCharges(normalized, out semanticEvent)
            || TryParseWardPurchase(normalized, out semanticEvent)
            || TryParseItemGoldNeed(normalized, out semanticEvent)
            || TryParseAbilityStateShare(normalized, out semanticEvent)
            || TryParseExplicitAbilityUse(normalized, out semanticEvent)
            || TryParseStructureStatus(normalized, out semanticEvent)
            || TryParseGeneratedTargetFragment(normalized, out semanticEvent)
            || TryParseObjectiveFragment(normalized, out semanticEvent)
            || TryParseSummonerSpellMarker(normalized, out semanticEvent)
            || TryParseAbilityMarker(normalized, out semanticEvent)
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
        var hasKnownInitiator = !string.IsNullOrWhiteSpace(speaker);
        var resolvedSpeaker = hasKnownInitiator ? speaker.Trim() : string.Empty;
        var subject = LocalizeSubject(semanticEvent.Subject, language);

        if (language == AppLanguage.English)
        {
            return semanticEvent.SpeechAct switch
            {
                SemanticSpeechAct.Countdown when semanticEvent.Status == "objective_respawn"
                    && semanticEvent.DurationSeconds is int objectiveSeconds =>
                    $"{DefaultSubject(subject, "The objective")} respawns in {FormatEnglishDuration(objectiveSeconds)}.",
                SemanticSpeechAct.Countdown when semanticEvent.DurationSeconds is int seconds =>
                    $"{subject}: {seconds} seconds remaining.",
                SemanticSpeechAct.Status when semanticEvent.Status == "unlearned" =>
                    $"{DefaultSubject(subject, "That ability")} has not been learned yet.",
                SemanticSpeechAct.Status => FormatEnglishStatus(subject, semanticEvent.Status),
                SemanticSpeechAct.Marker when semanticEvent.Status == "summoner_spell" =>
                    FormatEnglishHudMarker(resolvedSpeaker, subject, semanticEvent.Target, "summoner spell"),
                SemanticSpeechAct.Marker when semanticEvent.Status == "ability" =>
                    FormatEnglishHudMarker(resolvedSpeaker, subject, semanticEvent.Target, "ability"),
                SemanticSpeechAct.Marker when hasKnownInitiator =>
                    $"{resolvedSpeaker} marked {DefaultSubject(subject, "that location")}.",
                SemanticSpeechAct.Marker => $"{DefaultSubject(subject, "A location")} was marked.",
                SemanticSpeechAct.Information when semanticEvent.Status == "resource_percent" =>
                    hasKnownInitiator ? $"{resolvedSpeaker}: {subject}." : $"{subject}.",
                SemanticSpeechAct.Information when semanticEvent.Status == "ward_charges" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker}'s {subject} has {semanticEvent.Target} charge remaining."
                        : $"{subject} has {semanticEvent.Target} charge remaining.",
                SemanticSpeechAct.Information when semanticEvent.Status == "gold_needed" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} needs {semanticEvent.Target} gold for {subject}."
                        : $"{semanticEvent.Target} gold is still needed for {subject}.",
                SemanticSpeechAct.Information when semanticEvent.Status == "ward_purchased" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} purchased a Control Ward."
                        : "A Control Ward was purchased.",
                SemanticSpeechAct.Information when semanticEvent.Status == "ward_purchase_available" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} can purchase a Control Ward."
                        : "A Control Ward is available for purchase.",
                SemanticSpeechAct.Information when semanticEvent.Status == "ability_used" =>
                    $"{DefaultSubject(subject, "A champion")} used {semanticEvent.Target}.",
                SemanticSpeechAct.Information when semanticEvent.Status == "summoner_spell_used" =>
                    $"{DefaultSubject(subject, "A champion")} used {LocalizeSubject(semanticEvent.Target, language)}.",
                SemanticSpeechAct.Information when semanticEvent.Status == "structure_status" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} marked {subject}."
                        : $"{subject} was marked.",
                SemanticSpeechAct.Request when semanticEvent.Status == "retreat" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} signals the team to fall back."
                        : "A retreat signal was received.",
                SemanticSpeechAct.Request when semanticEvent.Status == "assist" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} requests assistance."
                        : "An assistance request was received.",
                SemanticSpeechAct.Warning when semanticEvent.Status == "missing" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} signals that an enemy is missing."
                        : "An enemy-missing signal was received.",
                SemanticSpeechAct.Warning when semanticEvent.Status == "danger" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} sends a danger signal."
                        : "A danger signal was received.",
                SemanticSpeechAct.Plan when semanticEvent.Status == "on_my_way" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} signals they are on the way."
                        : "An on-my-way signal was received.",
                SemanticSpeechAct.Request when semanticEvent.Status == "vision" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} requests vision."
                        : "A vision request was received.",
                SemanticSpeechAct.Request when semanticEvent.Status == "all_in" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} signals an all-in attack."
                        : "An all-in signal was received.",
                SemanticSpeechAct.Plan when semanticEvent.Status == "push" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} signals a forward push."
                        : "A push-forward signal was received.",
                SemanticSpeechAct.Information when semanticEvent.Status == "enemy_vision" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} marked enemy vision here."
                        : "Enemy vision was marked here.",
                SemanticSpeechAct.Information when semanticEvent.Status == "no_enemy_vision" =>
                    hasKnownInitiator
                        ? $"{resolvedSpeaker} marked that the enemy has no vision here."
                        : "No enemy vision was marked here.",
                _ when hasKnownInitiator => $"{resolvedSpeaker} sent a tactical signal.",
                _ => "A tactical signal was received."
            };
        }

        return semanticEvent.SpeechAct switch
        {
            SemanticSpeechAct.Countdown when semanticEvent.Status == "objective_respawn"
                && semanticEvent.DurationSeconds is int objectiveSeconds =>
                $"{DefaultSubject(subject, "目标")}还有{FormatChineseDuration(objectiveSeconds)}刷新",
            SemanticSpeechAct.Countdown when semanticEvent.DurationSeconds is int seconds =>
                $"{DefaultSubject(subject, "技能")}还有{seconds}秒",
            SemanticSpeechAct.Status when semanticEvent.Status == "unlearned" =>
                $"{DefaultSubject(subject, "该技能")}尚未学习",
            SemanticSpeechAct.Status => FormatChineseStatus(subject, semanticEvent.Status),
            SemanticSpeechAct.Marker when semanticEvent.Status == "summoner_spell" =>
                FormatChineseHudMarker(resolvedSpeaker, subject, semanticEvent.Target, "召唤师技能"),
            SemanticSpeechAct.Marker when semanticEvent.Status == "ability" =>
                FormatChineseHudMarker(resolvedSpeaker, subject, semanticEvent.Target, "技能"),
            SemanticSpeechAct.Marker when hasKnownInitiator =>
                $"{resolvedSpeaker}标记了{DefaultSubject(subject, "该位置")}",
            SemanticSpeechAct.Marker => $"已标记{DefaultSubject(subject, "该位置")}",
            SemanticSpeechAct.Information when semanticEvent.Status == "resource_percent" =>
                hasKnownInitiator ? $"{resolvedSpeaker}{subject}" : subject,
            SemanticSpeechAct.Information when semanticEvent.Status == "ward_charges" =>
                hasKnownInitiator
                    ? $"{resolvedSpeaker}的{subject}还有{semanticEvent.Target}次充能"
                    : $"{subject}还有{semanticEvent.Target}次充能",
            SemanticSpeechAct.Information when semanticEvent.Status == "gold_needed" =>
                hasKnownInitiator
                    ? $"{resolvedSpeaker}还需要{semanticEvent.Target}金币购买{subject}"
                    : $"购买{subject}还需要{semanticEvent.Target}金币",
            SemanticSpeechAct.Information when semanticEvent.Status == "ward_purchased" =>
                hasKnownInitiator ? $"{resolvedSpeaker}购买了控制守卫" : "已购买控制守卫",
            SemanticSpeechAct.Information when semanticEvent.Status == "ward_purchase_available" =>
                hasKnownInitiator ? $"{resolvedSpeaker}可以购买控制守卫" : "控制守卫可以购买",
            SemanticSpeechAct.Information when semanticEvent.Status == "ability_used" =>
                $"{DefaultSubject(subject, "英雄")}使用了{semanticEvent.Target}",
            SemanticSpeechAct.Information when semanticEvent.Status == "summoner_spell_used" =>
                $"{DefaultSubject(subject, "英雄")}使用了{LocalizeSubject(semanticEvent.Target, language)}",
            SemanticSpeechAct.Information when semanticEvent.Status == "structure_status" =>
                hasKnownInitiator ? $"{resolvedSpeaker}标记了{subject}" : $"已标记{subject}",
            SemanticSpeechAct.Request when semanticEvent.Status == "retreat" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出撤退信号" : "收到撤退信号",
            SemanticSpeechAct.Request when semanticEvent.Status == "assist" =>
                hasKnownInitiator ? $"{resolvedSpeaker}请求支援" : "收到支援请求",
            SemanticSpeechAct.Warning when semanticEvent.Status == "missing" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出敌人消失信号" : "收到敌人消失信号",
            SemanticSpeechAct.Warning when semanticEvent.Status == "danger" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出危险信号" : "收到危险信号",
            SemanticSpeechAct.Plan when semanticEvent.Status == "on_my_way" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出正在路上的信号" : "收到正在路上的信号",
            SemanticSpeechAct.Request when semanticEvent.Status == "vision" =>
                hasKnownInitiator ? $"{resolvedSpeaker}请求补充视野" : "收到视野请求",
            SemanticSpeechAct.Request when semanticEvent.Status == "all_in" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出全力进攻信号" : "收到全力进攻信号",
            SemanticSpeechAct.Plan when semanticEvent.Status == "push" =>
                hasKnownInitiator ? $"{resolvedSpeaker}发出推进信号" : "收到推进信号",
            SemanticSpeechAct.Information when semanticEvent.Status == "enemy_vision" =>
                hasKnownInitiator ? $"{resolvedSpeaker}标记这里有敌方视野" : "已标记这里有敌方视野",
            SemanticSpeechAct.Information when semanticEvent.Status == "no_enemy_vision" =>
                hasKnownInitiator ? $"{resolvedSpeaker}标记这里没有敌方视野" : "已标记这里没有敌方视野",
            _ when hasKnownInitiator => $"{resolvedSpeaker}发出战术信号",
            _ => "收到战术信号"
        };
    }

    public static string Intent(GameSemanticEvent semanticEvent) =>
        semanticEvent.SpeechAct switch
        {
            SemanticSpeechAct.Countdown => "cooldown",
            SemanticSpeechAct.Request when semanticEvent.Status == "retreat" => "retreat",
            SemanticSpeechAct.Request when semanticEvent.Status == "assist" => "assist",
            SemanticSpeechAct.Request when semanticEvent.Status == "vision" => "resource",
            SemanticSpeechAct.Request when semanticEvent.Status == "all_in" => "all_in",
            SemanticSpeechAct.Plan when semanticEvent.Status == "push" => "push",
            SemanticSpeechAct.Information when semanticEvent.Status is "enemy_vision" or "no_enemy_vision" => "vision_status",
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

    private static bool TryParseEmbeddedHudShare(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = EmbeddedHudShareRegex().Match(normalized);
        if (match.Success
            && TryParsePlayerFeed(match.Groups["content"].Value, out semanticEvent))
        {
            semanticEvent = semanticEvent with
            {
                Confidence = Math.Min(semanticEvent.Confidence, 0.92),
                Evidence = normalized
            };
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseObjectiveRespawn(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = ObjectiveRespawnRegex().Match(normalized);
        if (!match.Success)
        {
            match = ObjectiveRespawnFragmentRegex().Match(normalized);
        }
        if (match.Success && TryParseDuration(match, out var seconds))
        {
            var subject = CleanSubject(match.Groups["subject"].Value);
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Countdown,
                Subject: string.IsNullOrWhiteSpace(subject) ? "目标" : subject,
                Status: "objective_respawn",
                DurationSeconds: seconds,
                Modality: SemanticModality.Asserted,
                Confidence: string.IsNullOrWhiteSpace(subject) ? 0.88 : 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseSkillCooldown(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = SkillCooldownRegex().Match(normalized);
        if (match.Success
            && TryParseOcrNumber(match.Groups["seconds"].Value, out var seconds)
            && seconds is >= 0 and <= 999)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Countdown,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Status: "cooldown",
                DurationSeconds: seconds,
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseDamagedCooldownFragment(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = DamagedCooldownFragmentRegex().Match(normalized);
        if (match.Success
            && TryParseOcrNumber(match.Groups["seconds"].Value, out var seconds))
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Noise,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Status: "fixed_feed_fragment",
                DurationSeconds: seconds,
                Modality: SemanticModality.Possible,
                Confidence: 0.82,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseSkillUnlearned(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = SkillUnlearnedRegex().Match(normalized);
        if (match.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Status,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Status: "unlearned",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseHudResource(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = HudResourceRegex().Match(normalized);
        if (match.Success)
        {
            var subject = $"{match.Groups["resource"].Value}{match.Groups["value"].Value}%";
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: subject,
                Status: "resource_percent",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseWardCharges(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = WardChargeRegex().Match(normalized);
        if (match.Success && TryParseOcrNumber(match.Groups["count"].Value, out var count))
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Target: count.ToString(CultureInfo.InvariantCulture),
                Status: "ward_charges",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseWardPurchase(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var available = WardPurchaseAvailableRegex().Match(normalized);
        if (available.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: "제어 와드",
                Status: "ward_purchase_available",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: available.Value);
            return true;
        }

        var purchase = WardPurchaseRegex().Match(normalized);
        if (purchase.Success)
        {
            var completed = purchase.Groups["completed"].Success;
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                completed ? SemanticSpeechAct.Information : SemanticSpeechAct.Noise,
                Subject: "제어 와드",
                Status: completed ? "ward_purchased" : "fixed_feed_fragment",
                Modality: completed ? SemanticModality.Asserted : SemanticModality.Possible,
                Confidence: completed ? 0.99 : 0.82,
                Evidence: purchase.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseItemGoldNeed(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = ItemGoldNeedRegex().Match(normalized);
        if (match.Success && TryParseOcrNumber(match.Groups["gold"].Value, out var gold))
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Target: gold.ToString(CultureInfo.InvariantCulture),
                Status: "gold_needed",
                Modality: SemanticModality.Asserted,
                Confidence: match.Groups["unit"].Value.Contains("콜드", StringComparison.Ordinal)
                    ? 0.91
                    : 0.99,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseAbilityStateShare(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = AbilityStateShareRegex().Match(normalized);
        if (!match.Success)
        {
            semanticEvent = Unknown(normalized);
            return false;
        }

        var status = NormalizeStatus(match.Groups["status"].Value);
        var ability = match.Groups["ability"].Value.ToUpperInvariant();
        var target = CleanSubject(match.Groups["target"].Value);
        var subject = string.IsNullOrWhiteSpace(target) ? ability : $"{target} {ability}";
        semanticEvent = new GameSemanticEvent(
            SemanticSourceType.HudShare,
            SemanticSpeechAct.Status,
            Subject: subject,
            Status: status,
            Modality: SemanticModality.Asserted,
            Confidence: 0.99,
            Evidence: match.Value);
        return !string.IsNullOrWhiteSpace(status);
    }

    private static bool TryParseExplicitAbilityUse(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var ability = AbilityUsedRegex().Match(normalized);
        if (ability.Success)
        {
            var key = ability.Groups["ability"].Value.ToUpperInvariant();
            var name = CleanSubject(ability.Groups["name"].Value);
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: CleanSubject(ability.Groups["subject"].Value),
                Target: string.IsNullOrWhiteSpace(name) ? $"{key}技能" : $"{key}技能{name}",
                Status: "ability_used",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: ability.Value);
            return true;
        }

        var spell = SummonerSpellUsedRegex().Match(normalized);
        if (spell.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: CleanSubject(spell.Groups["subject"].Value),
                Target: CleanSubject(spell.Groups["spell"].Value),
                Status: "summoner_spell_used",
                Modality: SemanticModality.Asserted,
                Confidence: 0.99,
                Evidence: spell.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseStructureStatus(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = StructureStatusRegex().Match(normalized);
        if (match.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Information,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Status: "structure_status",
                Modality: SemanticModality.Marked,
                Confidence: 0.96,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseGeneratedTargetFragment(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = GeneratedTargetFragmentRegex().Match(normalized);
        if (match.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.TacticalPing,
                SemanticSpeechAct.Noise,
                Subject: CleanSubject(match.Groups["target"].Value),
                Status: "fixed_feed_fragment",
                Modality: SemanticModality.Possible,
                Confidence: 0.82,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseObjectiveFragment(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = ObjectiveFragmentRegex().Match(normalized);
        if (match.Success)
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Noise,
                Subject: CleanSubject(match.Value),
                Status: "fixed_feed_fragment",
                Modality: SemanticModality.Possible,
                Confidence: 0.80,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseSummonerSpellMarker(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = SummonerSpellMarkerRegex().Match(normalized);
        if (match.Success && LooksLikeHudMarkerSubject(match.Groups["subject"].Value))
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Marker,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Target: CleanSubject(match.Groups["spell"].Value),
                Status: "summoner_spell",
                Modality: SemanticModality.Marked,
                Confidence: 0.98,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

    private static bool TryParseAbilityMarker(
        string normalized,
        out GameSemanticEvent semanticEvent)
    {
        var match = AbilityMarkerRegex().Match(normalized);
        if (match.Success && LooksLikeHudMarkerSubject(match.Groups["subject"].Value))
        {
            semanticEvent = new GameSemanticEvent(
                SemanticSourceType.HudShare,
                SemanticSpeechAct.Marker,
                Subject: CleanSubject(match.Groups["subject"].Value),
                Target: match.Groups["ability"].Value.ToUpperInvariant(),
                Status: "ability",
                Modality: SemanticModality.Marked,
                Confidence: 0.97,
                Evidence: match.Value);
            return true;
        }

        semanticEvent = Unknown(normalized);
        return false;
    }

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
            || IsExact(normalized, "후퇴", "撤退", "retreat", "fall back")
            || EndsWithAny(normalized, "모두 돌아가라고 신호를 보냄", "示意所有人后撤", "signals everyone to get back"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "retreat", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "도움", "지원", "协助", "支援", "assist", "help")
            || IsExact(normalized, "도움 요청", "지원 요청", "请求协助", "请求支援", "assist me", "need help")
            || EndsWithAny(normalized, "지원 요청을 보냄", "正在请求协助", "is asking for assistance"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "assist", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "적이 사라", "미아", "敌人不见", "敌人消失", "missing")
            || IsExact(normalized, "적이 사라짐", "미아", "敌人不见了", "敌人消失", "enemy missing", "missing enemy")
            || EndsWithAny(normalized, "적이 사라졌다고 알림", "示意敌人已不见踪影", "signals that enemies are missing"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Warning, "missing", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "위험", "조심", "危险", "小心", "danger", "be careful")
            || IsExact(normalized, "위험", "조심", "危险", "小心", "danger", "be careful")
            || EndsWithAny(normalized, "위험 신호를 보냄", "发出信号，请谨慎行事", "signals to be cautious"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Warning, "danger", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "가는 중", "이동 중", "正在路上", "on my way", "omw")
            || IsExact(normalized, "가는 중", "이동 중", "正在路上", "on my way", "omw")
            || EndsWithAny(normalized, "님이 가고 있음", "正在路上", "is on the way"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Plan, "on_my_way", normalized, 0.96);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "총공격", "全力进攻", "all in", "all-in")
            || IsExact(normalized, "총공격", "全力进攻", "all in", "all-in")
            || EndsWithAny(normalized, "총공격 신호를 보냄", "发出信号，请全力进攻", "signals to all in"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Request, "all_in", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "압박 요청", "向前推进", "push forward", "push")
            || IsExact(normalized, "압박 요청", "向前推进", "push forward", "push")
            || EndsWithAny(normalized, "님이 압박 요청", "想要向前推进", "wants to push forward"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Plan, "push", normalized, 0.97);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "적 와드를 확인", "敌方在这有视野", "enemy has vision here")
            || IsExact(normalized, "적 와드 확인", "敌方在这有视野", "enemy has vision here")
            || EndsWithAny(normalized, "적 와드를 확인하고 신호를 보냄", "示意敌方在这有视野", "signals enemy has vision here"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Information, "enemy_vision", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "적 와드가 없", "敌方在这无视野", "enemy has no vision here")
            || IsExact(normalized, "적 와드 없음", "敌方在这无视野", "enemy has no vision here")
            || EndsWithAny(normalized, "적 와드가 없음을 확인하고 신호를 보냄", "示意敌方在这无视野", "signals enemy has no vision here"))
        {
            semanticEvent = Signal(SemanticSpeechAct.Information, "no_enemy_vision", normalized, 0.98);
            return true;
        }
        if (hasSignalFrame && ContainsAny(normalized, "시야", "视野", "vision")
            || IsExact(normalized, "시야 필요", "시야 요청", "需要视野", "请求视野", "need vision", "vision needed")
            || EndsWithAny(normalized, "님이 시야 요청", "正在请求视野", "is asking for vision"))
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

    private static string FormatChineseStatus(string subject, string status) =>
        status switch
        {
            "alive" => $"{subject}仍然存活",
            "dead" => $"{subject}已经阵亡",
            "ready" => $"{subject}已就绪",
            "cooldown" => $"{subject}仍在冷却",
            _ => $"已标记{subject}的状态"
        };

    private static string FormatEnglishStatus(string subject, string status) =>
        status switch
        {
            "alive" => $"{subject} is still alive.",
            "dead" => $"{subject} is dead.",
            "ready" => $"{subject} is ready.",
            "cooldown" => $"{subject} is still on cooldown.",
            _ => $"{subject}'s status was marked."
        };

    private static string FormatChineseHudMarker(
        string speaker,
        string subject,
        string target,
        string fallbackTarget)
    {
        var localizedTarget = LocalizeSubject(target, AppLanguage.Chinese);
        var marked = $"{DefaultSubject(subject, "目标")}的{DefaultSubject(localizedTarget, fallbackTarget)}";
        return string.IsNullOrWhiteSpace(speaker)
            ? $"已标记{marked}"
            : $"{speaker}标记了{marked}";
    }

    private static string FormatEnglishHudMarker(
        string speaker,
        string subject,
        string target,
        string fallbackTarget)
    {
        var localizedTarget = LocalizeSubject(target, AppLanguage.English);
        var marked = $"{DefaultSubject(subject, "the target")}'s {DefaultSubject(localizedTarget, fallbackTarget)}";
        return string.IsNullOrWhiteSpace(speaker)
            ? $"{marked} was marked."
            : $"{speaker} marked {marked}.";
    }

    private static string FormatChineseDuration(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0
            ? seconds > 0 ? $"{minutes}分{seconds}秒" : $"{minutes}分钟"
            : $"{seconds}秒";
    }

    private static string FormatEnglishDuration(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        if (minutes == 0)
        {
            return $"{seconds} seconds";
        }
        return seconds == 0
            ? $"{minutes} minutes"
            : $"{minutes} minutes {seconds} seconds";
    }

    private static string LocalizeSubject(string subject, AppLanguage language)
    {
        var localized = GameTermGlossary.ReplaceTerms(CleanSubject(subject), language);
        if (language == AppLanguage.Chinese)
        {
            localized = Regex.Replace(localized, @"\b(?:enemy|opponent)\b", "敌方", RegexOptions.IgnoreCase);
            localized = localized
                .Replace("마나", "蓝量", StringComparison.Ordinal)
                .Replace("체력", "生命值", StringComparison.Ordinal)
                .Replace("기력", "能量", StringComparison.Ordinal)
                .Replace("분노", "怒气", StringComparison.Ordinal)
                .Replace("적 ", "敌方", StringComparison.Ordinal)
                .Replace("아군 ", "我方", StringComparison.Ordinal);
        }
        else
        {
            localized = localized
                .Replace("마나", "mana ", StringComparison.Ordinal)
                .Replace("체력", "health ", StringComparison.Ordinal)
                .Replace("기력", "energy ", StringComparison.Ordinal)
                .Replace("분노", "rage ", StringComparison.Ordinal)
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

    private static bool ContainsAny(string value, params string[] fragments)
    {
        var compact = RemoveSpaces(value);
        return fragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase)
            || compact.Contains(RemoveSpaces(fragment), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExact(string value, params string[] alternatives)
    {
        var normalized = value.Trim(' ', '\t', '.', '。', '!', '！', '?', '？').ToLowerInvariant();
        var compact = RemoveSpaces(normalized);
        return alternatives.Any(alternative =>
            string.Equals(normalized, alternative, StringComparison.OrdinalIgnoreCase)
            || string.Equals(compact, RemoveSpaces(alternative), StringComparison.OrdinalIgnoreCase));
    }

    private static bool EndsWithAny(string value, params string[] alternatives)
    {
        var normalized = value.Trim(' ', '\t', '.', '。', '!', '！', '?', '？');
        return alternatives.Any(alternative =>
            normalized.EndsWith(alternative, StringComparison.OrdinalIgnoreCase)
            || RemoveSpaces(normalized).EndsWith(
                RemoveSpaces(alternative),
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseDuration(Match match, out int totalSeconds)
    {
        totalSeconds = 0;
        if (match.Groups["minutes"].Success
            && TryParseOcrNumber(match.Groups["minutes"].Value, out var minutes)
            && TryParseOcrNumber(match.Groups["seconds"].Value, out var seconds)
            && seconds is >= 0 and <= 59)
        {
            totalSeconds = checked(minutes * 60 + seconds);
            return totalSeconds <= 3600;
        }
        if (match.Groups["flat"].Success
            && TryParseOcrNumber(match.Groups["flat"].Value, out var flatSeconds)
            && flatSeconds is >= 0 and <= 999)
        {
            totalSeconds = flatSeconds;
            return true;
        }
        return false;
    }

    private static bool TryParseOcrNumber(string value, out int number)
    {
        var corrected = value
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('|', '1');
        return int.TryParse(
            corrected,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static string RemoveSpaces(string value) =>
        Regex.Replace(value, @"\s+", string.Empty, RegexOptions.CultureInvariant);

    private static bool LooksLikeHudMarkerSubject(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (ContainsAny(normalized, "等", "我的", "需要", "用了", "没有", "请"))
        {
            return false;
        }

        return !Regex.IsMatch(
            normalized,
            @"\b(?:wait|my|our|your|their|need|use|used|has|have|had|no|without|please|pls|can|could|will|should)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        @"^(?:[0-9OolI|]{1,2})?[^:：\r\n]{1,64}[\(\[（][^\)\]）]{1,36}[\)\]）]\s*[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedHudShareRegex();

    [GeneratedRegex(
        @"^(?<subject>(?:내셔\s*남작|바론|장로\s*드래곤|바람의\s*드래곤|화염의\s*드래곤|대지의\s*드래곤|바다의\s*드래곤|화학공학\s*드래곤|마법공학\s*드래곤|드래곤|용|협곡의\s*전령|공허\s*유충|"
        + @"纳什男爵|男爵|远古巨龙|小龙|巨龙|峡谷先锋|虚空巢虫|"
        + @"baron(?:\s+nashor)?|elder\s+dragon|dragon|drake|rift\s+herald|void\s+grubs?))\s*[-:：]?\s*"
        + @"(?:(?<minutes>[0-9OolI|]{1,2})\s*[:：.]\s*(?<seconds>[0-9OolI|]{1,2})|(?<flat>[0-9OolI|]{1,3}))\s*"
        + @"(?:초|秒|s(?:ec(?:ond)?s?)?)?\s*(?:후|后|in)?\s*(?:재생성|刷新|重生|respawns?|respawning)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveRespawnRegex();

    [GeneratedRegex(
        @"^(?<subject>)(?<flat>[0-9OolI|]{1,3})\s*(?:초|秒|s(?:ec(?:ond)?s?)?)?\s*(?:후|后|in)?\s*(?:재생성|刷新|重生|respawns?|respawning)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveRespawnFragmentRegex();

    [GeneratedRegex(
        @"^(?<subject>(?:[QWER]|[^\r\n]{1,48}\s+[QWER]))\s*(?:대기\s*시간|쿨타임|冷却时间|冷却|cooldown)\s*[-:：]?\s*"
        + @"(?<seconds>[0-9OolI|]{1,3})\s*(?:s|sec(?:ond)?s?|초|秒)\s*(?:남음|剩余|후|left|remaining)?\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillCooldownRegex();

    [GeneratedRegex(
        @"^(?<subject>[^:：\r\n]{1,48})\s*(?:대기|쿨|기)?\s*시간\s*[-:：]?\s*(?<seconds>[0-9OolI|]{1,3})\s*(?:초|소)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamagedCooldownFragmentRegex();

    [GeneratedRegex(
        @"^(?<subject>[QWER])\s*[-:：]?\s*(?:아직\s*배우지\s*않음|尚未学习|还没学|not\s+learned(?:\s+yet)?)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillUnlearnedRegex();

    [GeneratedRegex(
        @"^(?<resource>마나|체력|기력|분노|法力值?|蓝量|生命值?|能量|怒气|mana|health|energy|rage)\s*[-:：]?\s*(?<value>\d{1,3})\s*%\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HudResourceRegex();

    [GeneratedRegex(
        @"^(?<subject>제어\s*와드|控制守卫|control\s*ward)\s*[-:：]?\s*(?<count>[0-9OolI|]{1,2})\s*(?:회|次|charges?)\s*(?:충전됨|充能(?:完毕)?|charged?)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WardChargeRegex();

    [GeneratedRegex(
        @"^(?<subject>제어\s*와드|控制守卫|control\s*ward)\s*[-:：]?\s*(?:구매\s*가능|可以购买|available\s+for\s+purchase)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WardPurchaseAvailableRegex();

    [GeneratedRegex(
        @"^(?:[^\r\n]{1,80}[\(\[（][^\)\]）]{1,36}[\)\]）]\s*님?\s*(?:이|가)?\s*)?"
        + @"(?:제어\s*와드|控制守卫|control\s*ward)\s*(?:아이템|item)?\s*(?:을|를)?\s*"
        + @"(?:(?<completed>구입했습니다|购买了|purchased))?\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WardPurchaseRegex();

    [GeneratedRegex(
        @"^(?<subject>.{1,80}?)\s*[-:：]\s*(?<gold>[0-9OolI|]{1,5})\s*(?<unit>골드|콜드|金币|gold)\s*(?:필요|需要|needed|required)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemGoldNeedRegex();

    [GeneratedRegex(
        @"^(?<ability>[QWER])\s*[-:：]\s*(?<status>준비됨|사용\s*가능|쿨타임|冷却中|就绪|可用|ready|on\s+cooldown)"
        + @"(?:\s*[\(（](?<target>[^\)）]{1,48})[\)）])?\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityStateShareRegex();

    [GeneratedRegex(
        @"^(?<subject>[^:：\r\n]{1,64}?)\s*님?\s*(?:이|가)\s*(?<ability>[QWER])\s*[-:：]\s*(?<name>[^:：\r\n]{0,64}?)\s*사용\s*[.!！。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityUsedRegex();

    [GeneratedRegex(
        @"^(?<subject>[^:：\r\n]{1,64}?)\s*님?\s*(?:이|가)\s*(?<spell>강력\s*순간이동|점멸|유체화|점화|회복|탈진|방어막|정화|강타)\s*사용\s*[.!！。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummonerSpellUsedRegex();

    [GeneratedRegex(
        @"^(?:[^\r\n]{1,80}[\(\[（][^\)\]）]{1,36}[\)\]）]\s*님?\s*(?:이|가)?\s*)?"
        + @"(?<subject>(?:억제기|외곽)?\s*포탑\s*[\(（]방패\s*\d\s*/\s*\d[\)）]\s*(?:보호\s*중|지목))\s*[.!！。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StructureStatusRegex();

    [GeneratedRegex(
        @"^[^:：\r\n]{1,80}[\(\[（][^\)\]）]{1,36}[\)\]）]\s*님?\s*(?:이|가)\s*"
        + @"(?<target>[^:：\r\n]{1,80}[\(\[（][^\)\]）]{1,36}[\)\]）])\s*[.!！。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedTargetFragmentRegex();

    [GeneratedRegex(
        @"^(?:협곡의|내셔\s*남작|장로\s*드래곤|바람의\s*드래곤|화염의\s*드래곤|대지의\s*드래곤|바다의\s*드래곤|공허\s*유충)\s*[\p{L}]{0,10}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveFragmentRegex();

    [GeneratedRegex(
        @"^(?<subject>[\p{L}\p{N}_'’·. ]{1,48}?)(?<spell>강력\s*순간이동|점멸|유체화|점화|회복|탈진|방어막|정화|강타|"
        + @"解封的传送|传送|闪现|幽灵疾步|引燃|治疗术?|虚弱|屏障|净化|惩戒|"
        + @"unleashed\s+teleport|teleport|flash|ghost|ignite|heal|exhaust|barrier|cleanse|smite)\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummonerSpellMarkerRegex();

    [GeneratedRegex(
        @"^(?<subject>[\p{L}\p{N}_'’·.()（）\[\] -]{1,48}?)\s+(?<ability>[QWER])\s*[.!。/]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbilityMarkerRegex();

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
        + @"(?:.+?님이\s+)?(?<target>[^:：]{1,80}?)\s*지목|"
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
