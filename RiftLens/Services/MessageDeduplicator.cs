using System.Text;
using System.Text.RegularExpressions;

namespace RiftLens.Services;

public sealed partial class MessageDeduplicator
{
    private static readonly TimeSpan RecentFallbackWindow = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _clock;
    private IReadOnlyList<string>? _previousEventKeys;
    private readonly List<RecentEventKey> _recent = new();

    public MessageDeduplicator(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<string> FindNewLines(string recognizedText) =>
        FindNewLineResults(recognizedText)
            .Select(line => line.NormalizedText)
            .ToArray();

    public IReadOnlyList<DeduplicatedMessageLine> FindNewLineResults(string recognizedText) =>
        EvaluateLineResults(recognizedText, commit: true);

    /// <summary>
    /// Computes which lines would be treated as new without mutating the
    /// deduplication baseline or recent history. Used when an asynchronous
    /// Vision pass will supply the authoritative transcription for this frame.
    /// </summary>
    public IReadOnlyList<DeduplicatedMessageLine> PeekNewLineResults(string recognizedText) =>
        EvaluateLineResults(recognizedText, commit: false);

    private IReadOnlyList<DeduplicatedMessageLine> EvaluateLineResults(string recognizedText, bool commit)
    {
        var current = SplitLineResults(recognizedText);
        var currentKeys = current.Select(line => CreateStableEventKey(line.NormalizedText)).ToArray();
        if (current.Count == 0)
        {
            if (commit)
            {
                _previousEventKeys ??= Array.Empty<string>();
            }
            return Array.Empty<DeduplicatedMessageLine>();
        }

        var observedAt = _clock.GetUtcNow();
        if (_previousEventKeys is null)
        {
            if (commit)
            {
                _previousEventKeys = currentKeys;
                Remember(currentKeys, observedAt);
            }
            return Array.Empty<DeduplicatedMessageLine>();
        }

        var overlap = FindLargestOverlap(_previousEventKeys, currentKeys);
        var candidates = overlap > 0
            ? current.Skip(overlap)
            : current.Where((_, index) => !WasRecentlySeen(currentKeys[index], observedAt));
        var result = candidates.ToArray();
        if (commit)
        {
            _previousEventKeys = currentKeys;
            Remember(currentKeys, observedAt);
        }
        return result;
    }

    public void Reset()
    {
        _previousEventKeys = null;
        _recent.Clear();
    }

    public static IReadOnlyList<string> SplitLines(string text) =>
        SplitLineResults(text)
            .Select(line => line.NormalizedText)
            .ToArray();

    private static IReadOnlyList<DeduplicatedMessageLine> SplitLineResults(string text) => text
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(CreateLineResult)
        .Where(line => line.NormalizedText.Length >= 2)
        .ToArray();

    private static int FindLargestOverlap(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var maximum = Math.Min(previous.Count, current.Count);
        for (var length = maximum; length > 0; length--)
        {
            var matches = true;
            for (var index = 0; index < length; index++)
            {
                if (!AreEquivalentEventKeys(
                        previous[previous.Count - length + index],
                        current[index]))
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

    private bool WasRecentlySeen(string eventKey, DateTimeOffset observedAt)
    {
        PruneRecent(observedAt);
        return _recent.Any(item => AreEquivalentEventKeys(item.Key, eventKey));
    }

    private void Remember(IEnumerable<string> eventKeys, DateTimeOffset observedAt)
    {
        PruneRecent(observedAt);
        foreach (var eventKey in eventKeys)
        {
            var existingIndex = _recent.FindIndex(item =>
                AreEquivalentEventKeys(item.Key, eventKey));
            if (existingIndex >= 0)
            {
                _recent[existingIndex] = new RecentEventKey(eventKey, observedAt);
                continue;
            }

            _recent.Add(new RecentEventKey(eventKey, observedAt));
        }

        while (_recent.Count > 80)
        {
            _recent.RemoveAt(0);
        }
    }

    private void PruneRecent(DateTimeOffset observedAt) =>
        _recent.RemoveAll(item => observedAt - item.ObservedAt > RecentFallbackWindow);

    private static string CreateStableEventKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private static bool AreEquivalentEventKeys(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        if (Math.Min(left.Length, right.Length) < 10
            || Math.Abs(left.Length - right.Length) > 2)
        {
            return false;
        }

        var maximumDistance = Math.Max(1, Math.Min(2, Math.Max(left.Length, right.Length) / 24));
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1]
                    + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }
            if (rowMinimum > maximumDistance)
            {
                return false;
            }
            previous = current;
        }
        return previous[right.Length] <= maximumDistance;
    }

    private static DeduplicatedMessageLine CreateLineResult(string value)
    {
        var raw = value.Trim();
        var timestamp = GameTimestampPrefixParser.Parse(raw);
        var normalized = MultiSpaceRegex()
            .Replace(timestamp.TextWithoutTimestamp, " ")
            .Normalize(NormalizationForm.FormKC);
        return new DeduplicatedMessageLine(raw, normalized, timestamp);
    }

    /// <summary>
    /// 去除 LOL 游戏内聊天时间戳。韩服常见格式：
    /// [12:34]  /  (12:34)  /  [0:05]  /  [12:34][팀]  /  [12:34] 플레이어: 메시지
    /// </summary>
    public static string StripGameTimestamp(string line)
    {
        return GameTimestampPrefixParser.Strip(line);
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();

    private sealed record RecentEventKey(string Key, DateTimeOffset ObservedAt);
}

public sealed record DeduplicatedMessageLine(
    string RawText,
    string NormalizedText,
    GameTimestampParseResult Timestamp);
