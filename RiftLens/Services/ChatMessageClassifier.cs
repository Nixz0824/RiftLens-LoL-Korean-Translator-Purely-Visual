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
    public ClassifiedGameMessage Classify(
        string line,
        OcrColorEvidence? colorEvidence = null)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unknown(line ?? string.Empty, colorEvidence);
        }

        var normalized = NormalizeLine(line);

        if (NonGameDateTimeStatRegex().IsMatch(normalized))
        {
            return Unknown(normalized, colorEvidence);
        }

        // The "(champion):" shape is shared by real player chat and a small set
        // of client-generated ping/HUD-share lines. Parse those deterministic
        // payloads first so a marker or cooldown cannot become an inferred action.
        var playerWithHero = PlayerWithHeroRegex().Match(normalized);
        if (playerWithHero.Success && !IsReservedSpeaker(playerWithHero.Groups["speaker"].Value))
        {
            if (TryPlayerFeed(normalized, playerWithHero, colorEvidence, includeHero: true, speakItemInspect: true, out var feed))
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
            if (TryPlayerFeed(normalized, channelPlayer, colorEvidence, includeHero: false, speakItemInspect: true, out var feed))
            {
                return feed;
            }
            return Player(normalized, channelPlayer, colorEvidence, includeHero: false);
        }

        if (TrySpeakerHeroPayload(normalized, colorEvidence, out var generatedPayload))
        {
            return generatedPayload;
        }

        // OCR sometimes drops parentheses or the channel label. Recover the
        // remaining "name: content" form before keyword checks only when the
        // LoL cyan-metadata + neutral-content palette supports player chat.
        // Reserved system labels can never pass this path.
        if (colorEvidence?.SupportsPlayerChat == true
            && !GameTimestampPrefixParser.StartsWithTimestampLikePrefix(normalized))
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
            TryReadGeneratedFeedSpeaker(
                normalized,
                out var speakerKey,
                out var speakerRaw,
                out var speakerHeroRaw);
            return new ClassifiedGameMessage(
                GameMessageKind.TacticalSignal,
                normalized,
                normalized,
                SpeakerKey: speakerKey,
                SpeakerRaw: speakerRaw,
                TacticalSummary: GameSemanticEventParser.Format(
                    semanticEvent,
                    string.Empty,
                    AppLanguage.Chinese),
                Priority: GameSemanticEventParser.Priority(semanticEvent),
                ColorEvidence: colorEvidence,
                SpeakerHeroRaw: speakerHeroRaw,
                SemanticEvent: semanticEvent,
                ClassificationConfidence: semanticEvent.Confidence);
        }

        if (SystemMessageCatalog.TryClassify(
                normalized,
                out var kind,
                out var priority,
                out var systemEvent))
        {
            return SystemMessage(kind, normalized, priority, colorEvidence, systemEvent);
        }

        // Keep the legacy template fallback only for an explicit "Signal:" frame.
        // Broad terms such as gg/help/dragon are player language, not proof of a Ping.
        var tacticalSummary = SignalMessageTemplates.IsExplicitSignalMessage(normalized)
            ? SignalMessageTemplates.TryTranslateSignal(normalized)
            : null;
        if (!string.IsNullOrWhiteSpace(tacticalSummary))
        {
            return new ClassifiedGameMessage(
                GameMessageKind.TacticalSignal,
                normalized,
                normalized,
                TacticalSummary: tacticalSummary,
                Priority: GetSignalPriority(tacticalSummary),
                ColorEvidence: colorEvidence,
                ClassificationConfidence: 0.9);
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
        var normalized = Regex.Replace(
                OfficialPhraseNormalizer.Normalize(line).Trim(),
                @"\s+",
                " ")
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
            SpeakerHeroRaw: hero,
            ClassificationConfidence: includeHero ? 1 : 0.98);
    }

    private static bool TryPlayerFeed(
        string normalized,
        Match match,
        OcrColorEvidence? colorEvidence,
        bool includeHero,
        bool speakItemInspect,
        out ClassifiedGameMessage message)
    {
        var content = match.Groups["content"].Value.Trim();
        if (!GameSemanticEventParser.TryParsePlayerFeed(content, out var semanticEvent))
        {
            message = Unknown(normalized, colorEvidence);
            return false;
        }

        // Chat-colon item-bar clicks are teammate communication. Colon-less
        // generated HUD shares stay on the tactical path.
        if (speakItemInspect
            && string.Equals(semanticEvent.Status, "item_marker", StringComparison.Ordinal))
        {
            message = Player(normalized, match, colorEvidence, includeHero) with
            {
                SemanticEvent = semanticEvent,
                ClassificationConfidence = semanticEvent.Confidence
            };
            return true;
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
            SemanticEvent: semanticEvent,
            ClassificationConfidence: semanticEvent.Confidence);
        return true;
    }

    private static bool TrySpeakerHeroPayload(
        string normalized,
        OcrColorEvidence? colorEvidence,
        out ClassifiedGameMessage message)
    {
        var match = SpeakerHeroPayloadRegex().Match(normalized);
        if (!match.Success || IsReservedSpeaker(match.Groups["speaker"].Value))
        {
            message = Unknown(normalized, colorEvidence);
            return false;
        }

        var content = match.Groups["content"].Value.Trim();
        if (OfficialPhraseNormalizer.IsTruncatedGeneratedPayload(content))
        {
            message = Unknown(normalized, colorEvidence);
            return true;
        }

        if (TryPlayerFeed(normalized, match, colorEvidence, includeHero: true, speakItemInspect: false, out message))
        {
            return true;
        }

        if (SystemMessageCatalog.TryClassify(
                normalized,
                out var kind,
                out var priority,
                out var systemEvent)
            || SystemMessageCatalog.TryClassify(content, out kind, out priority, out systemEvent))
        {
            message = SystemMessage(kind, normalized, priority, colorEvidence, systemEvent);
            return true;
        }

        message = Unknown(normalized, colorEvidence);
        return false;
    }

    private static ClassifiedGameMessage SystemMessage(
        GameMessageKind kind,
        string normalized,
        int priority,
        OcrColorEvidence? colorEvidence,
        GameSystemEvent systemEvent) =>
        new(
            kind,
            normalized,
            normalized,
            Priority: priority,
            ColorEvidence: colorEvidence,
            SystemEvent: systemEvent,
            ClassificationConfidence: systemEvent.Confidence);

    private static ClassifiedGameMessage Unknown(
        string line,
        OcrColorEvidence? colorEvidence) =>
        new(
            GameMessageKind.Unknown,
            line,
            line,
            ColorEvidence: colorEvidence,
            ClassificationConfidence: colorEvidence?.SupportsSystemEvent == true ? 0.45 : 0.2);

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

    private static bool TryReadGeneratedFeedSpeaker(
        string normalized,
        out string speakerKey,
        out string speakerRaw,
        out string speakerHeroRaw)
    {
        var match = GeneratedFeedSpeakerRegex().Match(normalized);
        if (!match.Success)
        {
            speakerKey = string.Empty;
            speakerRaw = string.Empty;
            speakerHeroRaw = string.Empty;
            return false;
        }

        var speaker = match.Groups["speaker"].Value.Trim();
        speakerHeroRaw = match.Groups["hero"].Value.Trim();
        speakerKey = Regex.Replace(speaker, @"\s+", string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        speakerRaw = string.IsNullOrWhiteSpace(speakerHeroRaw)
            ? speaker
            : $"{speaker} ({speakerHeroRaw})";
        return !string.IsNullOrWhiteSpace(speakerKey);
    }

    [GeneratedRegex(
        @"^\s*(?:\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*)?"
        + @"(?<speaker>[^:：\r\n\(\[（]{1,64}?)\s*[\(\[（]\s*"
        + @"(?<hero>[^:：\r\n\)\]）]{1,36})\s*[\)\]）]\s*[:：·•]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerWithHeroRegex();

    [GeneratedRegex(
        @"^\s*\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*"
        + @"(?<speaker>[^:：\]\r\n]{1,64})[:：·•]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPlayerRegex();

    [GeneratedRegex(
        @"^\s*(?<speaker>[^:：\[\]\r\n]{1,64})[:：]\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorBackedPlayerRegex();

    [GeneratedRegex(
        @"^\s*(?<speaker>[^\(\[（]{1,64})\s*[\(\[（]\s*(?<hero>[^\)\]）]{1,36})[\)\]）]\s*"
        + @"(?:님?\s*(?:이|가)?(?:\s|(?=[\p{L}]))|"
        + @"(?=(?:要求队友|发出信号|已把|已购买|购买了|已击杀|击杀了|打野任务|已使用|使用了|标记了|已达到|已将目标定|正在防守)))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedFeedSpeakerRegex();

    [GeneratedRegex(
        @"^\s*(?:\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*)?"
        + @"(?<speaker>[^:：\r\n\(\[（]{1,64}?)\s*[\(\[（]\s*"
        + @"(?<hero>[^:：\r\n\)\]）]{1,36})\s*[\)\]）]\s*"
        + @"(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerHeroPayloadRegex();

    [GeneratedRegex(
        @"^\s*\d{1,2}\s*[-/.]\s*\d{1,2}\s+\d{1,2}\s*[:：]\s*\d{2}\s*(?:胜|负|勝|負)\s*\d+\s*/\s*\d+\s*/\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonGameDateTimeStatRegex();

}
