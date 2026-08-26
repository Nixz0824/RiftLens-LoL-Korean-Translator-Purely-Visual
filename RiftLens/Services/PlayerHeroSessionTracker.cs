using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Learns the stable player-ID-to-champion relationship from repeated, valid
/// in-game chat headers. OCR guesses that do not resolve through the official
/// champion dictionary never receive a vote.
/// </summary>
public sealed class PlayerHeroSessionTracker
{
    private const int LockVoteThreshold = 3;

    private readonly HeroDictionary _heroes;
    private readonly List<PlayerState> _players = new();

    public PlayerHeroSessionTracker(HeroDictionary heroes)
    {
        _heroes = heroes;
    }

    public void Reset() => _players.Clear();

    public ClassifiedGameMessage Observe(ClassifiedGameMessage message)
    {
        if (message.Kind is not (GameMessageKind.PlayerChat or GameMessageKind.TacticalSignal)
            || string.IsNullOrWhiteSpace(message.SpeakerKey))
        {
            return message;
        }

        var state = FindPlayer(message.SpeakerKey);
        if (state is null)
        {
            state = new PlayerState(
                message.SpeakerKey,
                ExtractPlayerName(message),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            _players.Add(state);
        }

        var observedHeroId = _heroes.FindOfficialHeroId(message.SpeakerHeroRaw);
        if (observedHeroId is null
            && _heroes.TryCorrectChineseHeroPrefix(
                message.SpeakerHeroRaw,
                out var correctedHeroId,
                out _,
                out _))
        {
            observedHeroId = correctedHeroId;
        }

        if (observedHeroId is not null && state.LockedHeroId is null)
        {
            state.Votes.TryGetValue(observedHeroId, out var count);
            state.Votes[observedHeroId] = count + 1;
            TryLockMajority(state);
        }

        // A valid current header may be used immediately. Once the session has
        // enough repeated evidence, the locked champion overrides every later
        // OCR fluctuation, including another valid-but-wrong champion token.
        var stableHeroId = state.LockedHeroId ?? observedHeroId ?? string.Empty;
        var stableSpeakerRaw = state.PlayerNameRaw;
        if (!string.IsNullOrWhiteSpace(stableHeroId))
        {
            var hero = _heroes.FindById(stableHeroId, preferPersonalName: false);
            if (hero is not null)
            {
                stableSpeakerRaw = $"{state.PlayerNameRaw} ({hero.KoreanName})";
            }
        }

        var stableContent = string.IsNullOrWhiteSpace(stableHeroId)
            ? _heroes.CorrectStructuredHeroPrefix(
                message.Content,
                preferPersonalName: true,
                AppLanguage.Chinese)
            : _heroes.StabilizeHeroTokens(
                message.Content,
                stableHeroId,
                AppLanguage.Chinese);

        return message with
        {
            SpeakerKey = state.SpeakerKey,
            SpeakerRaw = stableSpeakerRaw,
            SpeakerHeroId = stableHeroId,
            Content = stableContent
        };
    }

    private PlayerState? FindPlayer(string speakerKey)
    {
        var exact = _players.FirstOrDefault(player =>
            string.Equals(player.SpeakerKey, speakerKey, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        if (speakerKey.Length < 6)
        {
            return null;
        }

        var fuzzyMatches = _players
            .Where(player =>
                player.SpeakerKey.Length >= 6
                && Math.Abs(player.SpeakerKey.Length - speakerKey.Length) <= 1
                && EditDistanceAtMostOne(player.SpeakerKey, speakerKey))
            .Take(2)
            .ToArray();
        return fuzzyMatches.Length == 1 ? fuzzyMatches[0] : null;
    }

    private static void TryLockMajority(PlayerState state)
    {
        var leaders = state.Votes
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (leaders.Length == 0 || leaders[0].Value < LockVoteThreshold)
        {
            return;
        }

        if (leaders.Length == 1 || leaders[0].Value > leaders[1].Value)
        {
            state.LockedHeroId = leaders[0].Key;
        }
    }

    private static string ExtractPlayerName(ClassifiedGameMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.SpeakerHeroRaw))
        {
            return message.SpeakerRaw.Trim();
        }

        var openBracket = message.SpeakerRaw.LastIndexOfAny(new[] { '(', '[', '（' });
        return openBracket > 0
            ? message.SpeakerRaw[..openBracket].Trim()
            : message.SpeakerRaw.Trim();
    }

    private static bool EditDistanceAtMostOne(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        var leftIndex = 0;
        var rightIndex = 0;
        var edits = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (left[leftIndex] == right[rightIndex])
            {
                leftIndex++;
                rightIndex++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            if (left.Length > right.Length)
            {
                leftIndex++;
            }
            else if (right.Length > left.Length)
            {
                rightIndex++;
            }
            else
            {
                leftIndex++;
                rightIndex++;
            }
        }

        if (leftIndex < left.Length || rightIndex < right.Length)
        {
            edits++;
        }
        return edits <= 1;
    }

    private sealed class PlayerState(
        string speakerKey,
        string playerNameRaw,
        Dictionary<string, int> votes)
    {
        public string SpeakerKey { get; } = speakerKey;
        public string PlayerNameRaw { get; } = playerNameRaw;
        public Dictionary<string, int> Votes { get; } = votes;
        public string? LockedHeroId { get; set; }
    }
}
