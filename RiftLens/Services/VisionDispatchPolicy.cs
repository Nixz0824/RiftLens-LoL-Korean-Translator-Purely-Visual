using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Decides whether a locally classified frame still needs DeepSeek Vision.
/// HUD, kills, and other deterministic feed lines stay local so they do not
/// wait on the network. Free-form player chat still goes to Vision.
/// </summary>
public static class VisionDispatchPolicy
{
    public static bool NeedsVision(IEnumerable<ClassifiedGameMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Kind == GameMessageKind.PlayerChat)
            {
                return true;
            }

            if (message.Kind == GameMessageKind.Unknown
                && LooksLikeUnresolvedPlayerLine(message.RawText))
            {
                return true;
            }
        }

        return false;
    }

    public static bool PublishImmediately(ClassifiedGameMessage message) =>
        message.Kind is not GameMessageKind.PlayerChat
        && (message.Kind != GameMessageKind.Unknown
            || !LooksLikeUnresolvedPlayerLine(message.RawText));

    public static bool LooksLikeUnresolvedPlayerLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = OfficialPhraseNormalizer.Normalize(raw).Trim();
        if (OfficialPhraseNormalizer.IsTruncatedGeneratedPayload(normalized))
        {
            return false;
        }

        var stripped = MessageDeduplicator.StripGameTimestamp(normalized);
        if (stripped.Contains(':') || stripped.Contains('：'))
        {
            return true;
        }

        var close = stripped.LastIndexOfAny(new[] { ')', '）' });
        return close > 0 && close < stripped.Length - 4;
    }
}
