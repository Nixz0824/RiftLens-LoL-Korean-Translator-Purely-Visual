using System.Text.RegularExpressions;

namespace RiftLens.Services;

/// <summary>
/// Filters obvious OCR noise and non-chat streaming-tool UI fragments before
/// lines enter classification or translation.
/// </summary>
internal static partial class ChatNoiseFilter
{
    private static readonly string[] ExactIgnoredLines =
    {
        "单/双排积分",
        "法师之靴",
        "魔法穿透"
    };

    private static readonly string[] IgnoredStreamerUiFragments =
    {
        "直播设置",
        "中控台",
        "素材库",
        "绿幕直播",
        "互动工具",
        "直播流量",
        "精品母带"
    };

    public static bool IsPlausibleGameChat(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
        {
            return false;
        }

        if (!line.Any(char.IsLetter))
        {
            return false;
        }

        var simplifiedLine = OfficialPhraseNormalizer.Normalize(line).Trim();
        foreach (var exact in ExactIgnoredLines)
        {
            if (string.Equals(simplifiedLine, exact, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var fragment in IgnoredStreamerUiFragments)
        {
            if (simplifiedLine.Contains(fragment, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (OfficialPhraseNormalizer.IsTruncatedGeneratedPayload(simplifiedLine))
        {
            return false;
        }

        if (MatchHistoryScoreboardRegex().IsMatch(simplifiedLine))
        {
            return false;
        }

        var readable = 0;
        var total = 0;
        foreach (var ch in line)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            total++;
            if (IsReadableGameChar(ch))
            {
                readable++;
            }
        }

        if (total == 0)
        {
            return false;
        }

        if (readable / (double)total < 0.40)
        {
            return false;
        }

        var distinctCount = line.Where(c => !char.IsWhiteSpace(c)).Distinct().Count();
        if (distinctCount <= 2 && line.Length > 4)
        {
            return false;
        }

        return true;
    }

    private static bool IsReadableGameChar(char ch) =>
        char.IsLetterOrDigit(ch)
        || ch >= 0xAC00
        || (ch >= 0x3130 && ch <= 0x318F)
        || (ch >= 0x4E00 && ch <= 0x9FFF)
        || (ch >= 0x3000 && ch <= 0x303F)
        || (ch >= 0xFF00 && ch <= 0xFFEF)
        || ch == ':' || ch == '：'
        || ch == '[' || ch == ']'
        || ch == '!' || ch == '？' || ch == '?';

    [GeneratedRegex(
        @"^\s*\d{1,2}\s*[-/.]\s*\d{1,2}\s+\d{1,2}\s*[:：]\s*\d{2}\s*(?:胜|负)\s*\d+\s*/\s*\d+\s*/\s*\d+",
        RegexOptions.CultureInvariant)]
    private static partial Regex MatchHistoryScoreboardRegex();
}
