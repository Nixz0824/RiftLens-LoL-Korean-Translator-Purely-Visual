using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Last check before a supposed player turn is sent to the text translator.
/// HUD, shop chrome, and match-history rows must not become "teammate said".
/// </summary>
public static partial class NonPlayerContentGate
{
    public static bool IsBlockedPlayerPayload(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var normalized = OfficialPhraseNormalizer.Normalize(line).Trim();
        return normalized.Length < 3
            || !normalized.Any(char.IsLetter)
            || IsFixedNonChatPayload(normalized);
    }

    public static bool IsFixedNonChatPayload(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var normalized = OfficialPhraseNormalizer.Normalize(line).Trim();
        if (OfficialPhraseNormalizer.IsTruncatedGeneratedPayload(normalized)
            || normalized is "单/双排积分" or "法师之靴" or "魔法穿透"
            || MatchHistoryRegex().IsMatch(normalized)
            || DamagedHudShareRegex().IsMatch(normalized))
        {
            return true;
        }

        if ((GameSemanticEventParser.TryParsePlayerFeed(normalized, out var playerFeed)
                && !string.Equals(playerFeed.Status, "item_marker", StringComparison.Ordinal))
            || GameSemanticEventParser.TryParseSystemFeed(normalized, out _)
            || SystemMessageCatalog.TryClassify(normalized, out _, out _, out _))
        {
            return true;
        }

        var classified = new ChatMessageClassifier().Classify(normalized);
        return classified.Kind is not GameMessageKind.PlayerChat
            and not GameMessageKind.Unknown;
    }

    public static bool ShouldSkipPlayerTranslation(IEnumerable<string> lines)
    {
        var material = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return material.Length > 0 && material.All(IsBlockedPlayerPayload);
    }

    [GeneratedRegex(
        @"^\s*\d{1,2}\s*[-/.]\s*\d{1,2}\s+\d{1,2}\s*[:：]\s*\d{2}\s*(?:胜|负)\s*\d+\s*/\s*\d+\s*/\s*\d+",
        RegexOptions.CultureInvariant)]
    private static partial Regex MatchHistoryRegex();

    [GeneratedRegex(
        @"(?:재생성|재생정|구입했습니다|와드\s*아이템을|퀘스트\s*(?:완료|압박)|任务(?:完成|压迫))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamagedHudShareRegex();
}
