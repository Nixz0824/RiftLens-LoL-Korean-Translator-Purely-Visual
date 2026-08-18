using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Groups consecutive chat lines from one speaker. A speaker change completes the
/// previous turn immediately; an idle timeout is handled by CaptureCoordinator.
/// </summary>
public sealed class PlayerTurnAggregator
{
    private string _speakerKey = string.Empty;
    private string _speakerRaw = string.Empty;
    private string _speakerHeroId = string.Empty;
    private readonly List<string> _rawLines = new();
    private readonly List<string> _contents = new();
    private readonly List<string> _evaluationLineIds = new();
    private DateTimeOffset _lastUpdatedAt;

    public bool HasPending => _contents.Count > 0;

    public string LastContent => _contents.Count > 0 ? _contents[^1] : string.Empty;

    public PlayerSpeechTurn? Add(ClassifiedGameMessage message)
    {
        if (message.Kind != GameMessageKind.PlayerChat)
        {
            throw new ArgumentException("Only player chat can be added to a player turn.", nameof(message));
        }

        PlayerSpeechTurn? completed = null;
        if (HasPending && !AreSameSpeaker(_speakerKey, message.SpeakerKey))
        {
            completed = Flush();
        }

        if (!HasPending)
        {
            _speakerKey = message.SpeakerKey;
            _speakerRaw = message.SpeakerRaw;
            _speakerHeroId = message.SpeakerHeroId;
        }
        else if (!string.IsNullOrWhiteSpace(message.SpeakerHeroId))
        {
            // A later line in the same turn may provide the first clean champion
            // header, or may carry the session lock learned after earlier lines.
            _speakerRaw = message.SpeakerRaw;
            _speakerHeroId = message.SpeakerHeroId;
        }

        if (!_rawLines.Contains(message.RawText, StringComparer.Ordinal))
        {
            _rawLines.Add(message.RawText);
        }
        if (!_contents.Contains(message.Content, StringComparer.Ordinal))
        {
            _contents.Add(message.Content);
        }
        if (!string.IsNullOrWhiteSpace(message.EvaluationLineId)
            && !_evaluationLineIds.Contains(message.EvaluationLineId, StringComparer.Ordinal))
        {
            _evaluationLineIds.Add(message.EvaluationLineId);
        }
        _lastUpdatedAt = message.CapturedAt == default ? DateTimeOffset.UtcNow : message.CapturedAt;

        // A single turn cannot grow without bound when somebody floods chat.
        while (_rawLines.Count > 8) _rawLines.RemoveAt(0);
        while (_contents.Count > 8) _contents.RemoveAt(0);
        while (_evaluationLineIds.Count > 8) _evaluationLineIds.RemoveAt(0);
        return completed;
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
            _evaluationLineIds.ToArray());
        _speakerKey = string.Empty;
        _speakerRaw = string.Empty;
        _speakerHeroId = string.Empty;
        _rawLines.Clear();
        _contents.Clear();
        _evaluationLineIds.Clear();
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
