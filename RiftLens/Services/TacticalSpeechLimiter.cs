using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Keeps tactical/system recognition intact while preventing the same ping or
/// HUD share from being spoken repeatedly. Different signal types can still
/// pre-empt after a short global gap.
/// </summary>
public sealed class TacticalSpeechLimiter
{
    private static readonly TimeSpan SameKeyCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GlobalMinGap = TimeSpan.FromMilliseconds(1200);

    private readonly Dictionary<string, DateTimeOffset> _spokenAt = new(StringComparer.Ordinal);
    private DateTimeOffset _lastSpokenAt = DateTimeOffset.MinValue;

    public void Reset()
    {
        _spokenAt.Clear();
        _lastSpokenAt = DateTimeOffset.MinValue;
    }

    public bool CanSpeak(ClassifiedGameMessage message, DateTimeOffset now)
    {
        if (now - _lastSpokenAt < GlobalMinGap)
        {
            return false;
        }

        return !_spokenAt.TryGetValue(Key(message), out var last)
            || now - last >= SameKeyCooldown;
    }

    public void MarkSpoken(ClassifiedGameMessage message, DateTimeOffset now)
    {
        _lastSpokenAt = now;
        _spokenAt[Key(message)] = now;
        if (_spokenAt.Count <= 64)
        {
            return;
        }

        var stale = _spokenAt
            .Where(pair => now - pair.Value > TimeSpan.FromSeconds(30))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in stale)
        {
            _spokenAt.Remove(key);
        }
    }

    private static string Key(ClassifiedGameMessage message) =>
        string.Join(
            '|',
            message.SpeakerKey,
            message.Kind.ToString(),
            message.SemanticEvent?.Status
            ?? message.SystemEvent?.Type.ToString()
            ?? message.Content);
}
