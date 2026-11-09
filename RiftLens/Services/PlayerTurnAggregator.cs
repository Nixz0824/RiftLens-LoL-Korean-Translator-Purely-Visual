using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Groups consecutive chat lines from one speaker. A speaker change completes the
/// previous turn immediately; an idle timeout is handled by CaptureCoordinator.
/// </summary>
public sealed class PlayerTurnAggregator
{
    public const int MaximumLines = 8;
    public const int MaximumWaitMs = 2500;
    private readonly TimeProvider _clock;
    private long _startedAt;
    private long _lastAddedAt;
    private string _speakerKey = string.Empty;
    private string _speakerRaw = string.Empty;
    private string _speakerHeroId = string.Empty;
    private string _recognitionEngine = string.Empty;
    private readonly List<string> _rawLines = new();
    private readonly List<string> _contents = new();
    private readonly List<string> _evaluationLineIds = new();
    private readonly List<ClassifiedGameMessage> _messages = new();
    private DateTimeOffset _lastUpdatedAt;

    public bool HasPending => _contents.Count > 0;

    public string LastContent => _contents.Count > 0 ? _contents[^1] : string.Empty;

    public PlayerTurnAggregator(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public bool HasReachedMaximumWait => HasPending
        && _clock.GetElapsedTime(_startedAt).TotalMilliseconds >= MaximumWaitMs;

    public int GetWaitMilliseconds(int idleTimeoutMs)
    {
        if (!HasPending) return Timeout.Infinite;
        var remaining = Math.Min(
            MaximumWaitMs - _clock.GetElapsedTime(_startedAt).TotalMilliseconds,
            idleTimeoutMs - _clock.GetElapsedTime(_lastAddedAt).TotalMilliseconds);
        return (int)Math.Max(0, Math.Ceiling(remaining));
    }

    public PlayerSpeechTurn? Add(ClassifiedGameMessage message)
    {
        if (message.Kind != GameMessageKind.PlayerChat)
        {
            throw new ArgumentException("Only player chat can be added to a player turn.", nameof(message));
        }

        PlayerSpeechTurn? completed = null;
        if (HasPending && (!AreSameSpeaker(_speakerKey, message.SpeakerKey) || HasReachedMaximumWait))
        {
            completed = Flush();
        }

        if (!HasPending)
        {
            _startedAt = _clock.GetTimestamp();
            _speakerKey = message.SpeakerKey;
            _speakerRaw = message.SpeakerRaw;
            _speakerHeroId = message.SpeakerHeroId;
            _recognitionEngine = message.RecognitionEngine;
        }
        else if (!string.IsNullOrWhiteSpace(message.SpeakerHeroId))
        {
            // A later line in the same turn may provide the first clean champion
            // header, or may carry the session lock learned after earlier lines.
            _speakerRaw = message.SpeakerRaw;
            _speakerHeroId = message.SpeakerHeroId;
        }
        if (!string.IsNullOrWhiteSpace(message.RecognitionEngine))
        {
            _recognitionEngine = string.IsNullOrWhiteSpace(_recognitionEngine)
                ? message.RecognitionEngine
                : string.Equals(_recognitionEngine, message.RecognitionEngine, StringComparison.Ordinal)
                    ? _recognitionEngine
                    : "mixed";
        }

        if (!_contents.Contains(message.Content, StringComparer.Ordinal))
        {
            _contents.Add(message.Content);
            _rawLines.Add(message.RawText);
            _evaluationLineIds.Add(message.EvaluationLineId);
            _messages.Add(message);
        }
        _lastUpdatedAt = message.CapturedAt == default ? DateTimeOffset.UtcNow : message.CapturedAt;
        _lastAddedAt = _clock.GetTimestamp();

        // Complete the full turn before accepting more lines; never discard its beginning.
        return completed ?? (_contents.Count >= MaximumLines ? Flush() : null);
    }

    public PlayerSpeechTurn? Flush()
    {
        if (!HasPending)
        {
            return null;
        }

        var turn = new PlayerSpeechTurn(
            _speakerKey,
            _speakerRaw,
            _rawLines.ToArray(),
            _contents.ToArray(),
            _lastUpdatedAt,
            _speakerHeroId,
            _evaluationLineIds.ToArray(),
            _recognitionEngine,
            _messages.ToArray());
        _speakerKey = string.Empty;
        _speakerRaw = string.Empty;
        _speakerHeroId = string.Empty;
        _recognitionEngine = string.Empty;
        _rawLines.Clear();
        _contents.Clear();
        _evaluationLineIds.Clear();
        _messages.Clear();
        _lastUpdatedAt = default;
        return turn;
    }

    private static bool AreSameSpeaker(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        // Player names are frequently the noisiest part of a small OCR line. Permit one
        // recognition error for longer names so a single glyph fluctuation does not create
        // a fake speaker change and duplicate summary.
        if (Math.Min(left.Length, right.Length) < 6 || Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            var rowMinimum = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum > 1)
            {
                return false;
            }
            previous = current;
        }
        return previous[right.Length] <= 1;
    }
}
