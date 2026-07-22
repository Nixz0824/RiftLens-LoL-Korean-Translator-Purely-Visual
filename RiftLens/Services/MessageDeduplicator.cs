using System.Text.RegularExpressions;
using System.Text;

namespace RiftLens.Services;

public sealed partial class MessageDeduplicator
{
    private IReadOnlyList<string>? _previousLines;
    private readonly Queue<string> _recentOrder = new();
    private readonly HashSet<string> _recent = new(StringComparer.Ordinal);

    public IReadOnlyList<string> FindNewLines(string recognizedText)
    {
        var current = SplitLines(recognizedText);
        if (current.Count == 0)
        {
            _previousLines ??= Array.Empty<string>();
            return Array.Empty<string>();
        }

        if (_previousLines is null)
        {
            _previousLines = current;
            Remember(current);
            return Array.Empty<string>();
        }

        var overlap = FindLargestOverlap(_previousLines, current);
        var candidates = overlap > 0 ? current.Skip(overlap) : current.Where(line => !_recent.Contains(line));
        var result = candidates.Where(line => !_recent.Contains(line)).ToArray();
        _previousLines = current;
        Remember(current);
        return result;
    }

    public void Reset()
    {
        _previousLines = null;
        _recent.Clear();
        _recentOrder.Clear();
    }

    public static IReadOnlyList<string> SplitLines(string text) => text
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Normalize)
        .Where(line => line.Length >= 2)
        .ToArray();

    private static int FindLargestOverlap(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var maximum = Math.Min(previous.Count, current.Count);
        for (var length = maximum; length > 0; length--)
        {
            var matches = true;
            for (var index = 0; index < length; index++)
            {
                if (!string.Equals(previous[previous.Count - length + index], current[index], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return length;
            }
        }

        return 0;
    }

    private void Remember(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!_recent.Add(line))
            {
                continue;
            }

            _recentOrder.Enqueue(line);
            while (_recentOrder.Count > 160)
            {
                _recent.Remove(_recentOrder.Dequeue());
            }
        }
    }

    private static string Normalize(string value)
    {
        var trimmed = StripGameTimestamp(value.Trim());
        return MultiSpaceRegex()
            .Replace(trimmed, " ")
            .Normalize(NormalizationForm.FormKC);
    }

    /// <summary>
    /// 去除 LOL 游戏内聊天时间戳。韩服常见格式：
    /// [12:34]  /  (12:34)  /  [0:05]  /  [12:34][팀]  /  [12:34] 플레이어: 메시지
    /// </summary>
    public static string StripGameTimestamp(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;

        // [12:34] 或 (12:34) — 可能出现在行首，可选后面跟着 [팀]/[전체]/[파티]
        var result = TimeBracketRegex().Replace(line, string.Empty, 1);
        // 纯数字时间戳 12:34 在行首（较少见但也可能）
        result = PlainTimeRegex().Replace(result, string.Empty, 1);
        return result.TrimStart();
    }

    [GeneratedRegex(@"^[\[\(]\d{1,2}:\d{2}[\]\)]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex TimeBracketRegex();

    [GeneratedRegex(@"^\d{1,2}:\d{2}\s+", RegexOptions.CultureInvariant)]
    private static partial Regex PlainTimeRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();
}
