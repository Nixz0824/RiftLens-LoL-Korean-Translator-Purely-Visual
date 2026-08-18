using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Holds a player turn one extra aggregation window when the last OCR line
/// looks cut off, so a mid-scroll fragment is less likely to go to translation.
/// </summary>
public static partial class PartialLineWait
{
    public static bool LooksIncomplete(PlayerSpeechTurn? turn)
    {
        if (turn is null || turn.Contents.Count == 0)
        {
            return false;
        }

        return LooksIncomplete(turn.Contents[^1]);
    }

    public static bool LooksIncomplete(string? line)
    {
        var text = (line ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            return true;
        }

        if (IncompleteJamoRegex().IsMatch(text))
        {
            return true;
        }

        if (DanglingSeparatorRegex().IsMatch(text))
        {
            return true;
        }

        if (text.Length < 8 && !TerminalPunctuationRegex().IsMatch(text))
        {
            return true;
        }

        return UnbalancedBrackets(text);
    }

    private static bool UnbalancedBrackets(string text)
    {
        var square = 0;
        var round = 0;
        foreach (var ch in text)
        {
            square += ch switch { '[' => 1, ']' => -1, _ => 0 };
            round += ch switch { '(' or '（' => 1, ')' or '）' => -1, _ => 0 };
            if (square < 0 || round < 0)
            {
                return true;
            }
        }

        return square > 0 || round > 0;
    }

    [GeneratedRegex(@"[\u1100-\u11FF\u3130-\u318F]$", RegexOptions.CultureInvariant)]
    private static partial Regex IncompleteJamoRegex();

    [GeneratedRegex(@"[:：]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DanglingSeparatorRegex();

    [GeneratedRegex(@"[.!?。！？…~]$", RegexOptions.CultureInvariant)]
    private static partial Regex TerminalPunctuationRegex();
}
