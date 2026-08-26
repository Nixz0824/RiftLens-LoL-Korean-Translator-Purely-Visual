using System.Text.Json;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class HeroDictionary
{
    private readonly IReadOnlyList<HeroEntry> _entries;
    private readonly IReadOnlyDictionary<string, HeroEntry> _entriesById;
    private readonly IReadOnlyDictionary<string, HeroEntry> _speakerAliases;

    public string Version { get; }

    private HeroDictionary(HeroCatalog catalog)
    {
        Version = catalog.Version;
        _entries = catalog.Heroes
            .Where(entry => !string.IsNullOrWhiteSpace(entry.KoreanName))
            .OrderByDescending(entry => entry.KoreanName.Length)
            .ToArray();
        _entriesById = _entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        var aliases = new Dictionary<string, HeroEntry>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            AddSpeakerAlias(aliases, entry, entry.KoreanName);
            AddSpeakerAlias(aliases, entry, entry.ChineseOfficial);
            AddSpeakerAlias(aliases, entry, entry.ChinesePersonal);
            AddSpeakerAlias(aliases, entry, entry.ChineseTraditionalName);
            AddSpeakerAlias(aliases, entry, entry.ChineseTraditionalTitle);
            AddSpeakerAlias(
                aliases,
                entry,
                ChineseTextNormalizer.ToSimplifiedCharacters(entry.ChineseTraditionalName));
            AddSpeakerAlias(
                aliases,
                entry,
                ChineseTextNormalizer.ToSimplifiedCharacters(entry.ChineseTraditionalTitle));
            AddSpeakerAlias(aliases, entry, entry.EnglishName);
            foreach (var alias in entry.ChineseAliases)
            {
                AddSpeakerAlias(aliases, entry, alias);
            }

            // Jarvan's numeral is especially vulnerable to spacing, Arabic/Roman
            // numeral and Korean suffix fluctuations in small chat text.
            if (entry.KoreanName.Contains("4세", StringComparison.Ordinal))
            {
                AddSpeakerAlias(aliases, entry, entry.KoreanName.Replace("4세", "IV", StringComparison.Ordinal));
                AddSpeakerAlias(aliases, entry, entry.KoreanName.Replace("4세", "4", StringComparison.Ordinal));
            }
            if (entry.EnglishName.Contains(" IV", StringComparison.OrdinalIgnoreCase))
            {
                AddSpeakerAlias(aliases, entry, entry.EnglishName.Replace(" IV", " 4", StringComparison.OrdinalIgnoreCase));
            }
        }
        _speakerAliases = aliases;
    }

    public static async Task<HeroDictionary> LoadAsync(string? path = null)
    {
        Stream? stream = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            stream = File.OpenRead(path);
        }
        else
        {
            stream = typeof(HeroDictionary).Assembly.GetManifestResourceStream("RiftLens.Assets.heroes.json");
        }

        if (stream is null)
        {
            return new HeroDictionary(CreateFallbackCatalog());
        }

        await using (stream)
        {
            var catalog = await JsonSerializer.DeserializeAsync<HeroCatalog>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return new HeroDictionary(catalog ?? CreateFallbackCatalog());
        }
    }

    public IReadOnlyList<HeroMention> FindMentions(
        IEnumerable<string> lines,
        bool preferPersonalName,
        AppLanguage language = AppLanguage.Chinese)
    {
        var text = string.Join('\n', lines);
        // 文本太短（< 8 字符）时不足以可靠识别英雄，跳过避免噪声误匹配
        if (text.Length < 8) return Array.Empty<HeroMention>();

        return _entries
            .Where(entry =>
                // 韩文原名（至少 2 个字符以避免单字误匹配）
                (entry.KoreanName.Length >= 2
                    && text.Contains(entry.KoreanName, StringComparison.OrdinalIgnoreCase))
                // 中文官方名（至少 3 个字符，如"曙光女神"）
                || (entry.ChineseOfficial is { Length: >= 3 } official
                    && text.Contains(official, StringComparison.OrdinalIgnoreCase))
                // 中文常用名（至少 2 个汉字，如"蕾欧娜""阿狸"）
                || (entry.ChinesePersonal is { Length: >= 2 } personal
                    && text.Contains(personal, StringComparison.OrdinalIgnoreCase))
                || (entry.ChineseTraditionalName is { Length: >= 2 } traditionalName
                    && (text.Contains(traditionalName, StringComparison.OrdinalIgnoreCase)
                        || text.Contains(
                            ChineseTextNormalizer.ToSimplifiedCharacters(traditionalName),
                            StringComparison.OrdinalIgnoreCase)))
                || (entry.ChineseTraditionalTitle is { Length: >= 2 } traditionalTitle
                    && (text.Contains(traditionalTitle, StringComparison.OrdinalIgnoreCase)
                        || text.Contains(
                            ChineseTextNormalizer.ToSimplifiedCharacters(traditionalTitle),
                            StringComparison.OrdinalIgnoreCase)))
                // 中文别名（至少 2 个字符）
                || entry.ChineseAliases.Any(alias =>
                    alias is { Length: >= 2 }
                    && text.Contains(alias, StringComparison.OrdinalIgnoreCase))
                // 英文名过短时很容易命中普通单词，正文只接受至少 3 个字符；
                // 括号内的说话英雄仍通过完整 token 精确识别。
                || (entry.EnglishName is { Length: >= 3 } english
                    && text.Contains(english, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => CreateMention(entry, preferPersonalName, language))
            .DistinctBy(mention => mention.Id)
            .ToArray();
    }

    public string? FindOfficialHeroId(string? heroToken)
    {
        if (string.IsNullOrWhiteSpace(heroToken))
        {
            return null;
        }

        var normalized = NormalizeHeroToken(heroToken);
        return _speakerAliases.TryGetValue(normalized, out var entry)
            ? entry.Id
            : null;
    }

    /// <summary>
    /// Repairs only a leading Korean champion token and only when exactly one
    /// official name is within one character. Callers must keep this scoped to
    /// structured HUD fields rather than free-form player chat.
    /// </summary>
    public bool TryCorrectKoreanHeroPrefix(
        string? text,
        out string correctedKoreanName,
        out int consumedCharacters)
    {
        correctedKoreanName = string.Empty;
        consumedCharacters = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var source = text.TrimStart();
        var candidates = new List<(HeroEntry Entry, int Consumed, int Distance)>();
        foreach (var entry in _entries)
        {
            var expected = NormalizeHeroToken(entry.KoreanName);
            if (expected.Length < 2 || !expected.All(IsHangul))
            {
                continue;
            }

            var builder = new System.Text.StringBuilder(expected.Length);
            var consumed = 0;
            while (consumed < source.Length && builder.Length < expected.Length)
            {
                var character = source[consumed++];
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }
                if (!IsHangul(character))
                {
                    builder.Clear();
                    break;
                }
                builder.Append(character);
            }
            if (builder.Length != expected.Length)
            {
                continue;
            }

            var distance = EditDistance(builder.ToString(), expected);
            if (distance <= 1)
            {
                candidates.Add((entry, consumed, distance));
            }
        }

        var bestDistance = candidates.Count == 0 ? int.MaxValue : candidates.Min(item => item.Distance);
        var best = candidates
            .Where(item => item.Distance == bestDistance)
            .GroupBy(item => item.Entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(2)
            .ToArray();
        if (best.Length != 1)
        {
            return false;
        }

        correctedKoreanName = best[0].Entry.KoreanName;
        consumedCharacters = best[0].Consumed;
        return true;
    }

    /// <summary>
    /// Resolves a leading Simplified/Traditional Chinese champion token when
    /// exactly one official hero is within one Han-character edit. This is for
    /// fixed HUD/Ping fields only; free-form chat must not be fuzzily rewritten.
    /// </summary>
    public bool TryCorrectChineseHeroPrefix(
        string? text,
        out string heroId,
        out string correctedSimplifiedName,
        out int consumedCharacters)
    {
        heroId = string.Empty;
        correctedSimplifiedName = string.Empty;
        consumedCharacters = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var source = text.TrimStart();
        var candidates = new List<(HeroEntry Entry, int Consumed, int Distance)>();
        foreach (var entry in _entries)
        {
            foreach (var alias in ChineseHeroAliases(entry))
            {
                var expected = NormalizeHeroToken(alias);
                if (expected.Length < 2 || expected.Length > 8 || !expected.All(IsHan))
                {
                    continue;
                }

                var builder = new System.Text.StringBuilder(expected.Length);
                var consumed = 0;
                while (consumed < source.Length && builder.Length < expected.Length)
                {
                    var character = source[consumed++];
                    if (char.IsWhiteSpace(character))
                    {
                        continue;
                    }
                    if (!IsHan(character))
                    {
                        builder.Clear();
                        break;
                    }
                    builder.Append(character);
                }
                if (builder.Length != expected.Length)
                {
                    continue;
                }

                var distance = EditDistance(builder.ToString(), expected);
                if (distance <= 1)
                {
                    candidates.Add((entry, consumed, distance));
                }
            }
        }

        var bestDistance = candidates.Count == 0 ? int.MaxValue : candidates.Min(item => item.Distance);
        var best = candidates
            .Where(item => item.Distance == bestDistance)
            .GroupBy(item => item.Entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(2)
            .ToArray();
        if (best.Length != 1)
        {
            return false;
        }

        heroId = best[0].Entry.Id;
        correctedSimplifiedName = !string.IsNullOrWhiteSpace(best[0].Entry.ChinesePersonal)
            ? best[0].Entry.ChinesePersonal
            : best[0].Entry.ChineseOfficial;
        consumedCharacters = best[0].Consumed;
        return true;
    }

    public HeroMention? FindById(
        string? heroId,
        bool preferPersonalName,
        AppLanguage language = AppLanguage.Chinese)
    {
        return !string.IsNullOrWhiteSpace(heroId)
            && _entriesById.TryGetValue(heroId, out var entry)
                ? CreateMention(entry, preferPersonalName, language)
                : null;
    }

    public string CorrectStructuredHeroPrefix(
        string subject,
        bool preferPersonalName,
        AppLanguage language)
    {
        if (TryCorrectChineseHeroPrefix(
                subject,
                out var chineseHeroId,
                out _,
                out var chineseConsumed))
        {
            var mention = FindById(chineseHeroId, preferPersonalName, language);
            if (mention is not null)
            {
                return mention.SpokenName + subject.TrimStart()[chineseConsumed..];
            }
        }

        if (TryCorrectKoreanHeroPrefix(subject, out var koreanName, out var koreanConsumed))
        {
            var heroId = FindOfficialHeroId(koreanName);
            var mention = FindById(heroId, preferPersonalName, language);
            if (mention is not null)
            {
                return mention.SpokenName + subject.TrimStart()[koreanConsumed..];
            }
        }

        return subject;
    }

    public string StabilizeHeroTokens(
        string text,
        string? lockedHeroId,
        AppLanguage language)
    {
        var stabilized = CorrectStructuredHeroPrefix(text, preferPersonalName: true, language);
        if (string.IsNullOrWhiteSpace(lockedHeroId)
            || !_entriesById.TryGetValue(lockedHeroId, out var locked))
        {
            return stabilized;
        }

        var spoken = CreateMention(locked, preferPersonalName: true, language).SpokenName;
        foreach (var alias in ChineseHeroAliases(locked)
                     .Concat(new[] { locked.KoreanName, locked.EnglishName })
                     .Where(alias => !string.IsNullOrWhiteSpace(alias) && alias.Length >= 2)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(alias => alias.Length))
        {
            if (string.Equals(alias, spoken, StringComparison.Ordinal))
            {
                continue;
            }

            stabilized = stabilized.Replace(
                ChineseTextNormalizer.ToSimplifiedCharacters(alias),
                spoken,
                StringComparison.OrdinalIgnoreCase);
            stabilized = stabilized.Replace(alias, spoken, StringComparison.OrdinalIgnoreCase);
        }

        return ReplaceUniqueNearMissRuns(stabilized, locked.Id, spoken);
    }

    private string ReplaceUniqueNearMissRuns(string text, string lockedHeroId, string spokenName)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!IsHan(text[index]))
            {
                index++;
                continue;
            }

            var end = index;
            while (end < text.Length && IsHan(text[end]) && end - index < 8)
            {
                end++;
            }

            var run = text[index..end];
            if (run.Length >= 2
                && !string.Equals(run, spokenName, StringComparison.Ordinal)
                && TryCorrectChineseHeroPrefix(run, out var heroId, out _, out _)
                && string.Equals(heroId, lockedHeroId, StringComparison.OrdinalIgnoreCase))
            {
                text = string.Concat(text.AsSpan(0, index), spokenName, text.AsSpan(end));
                index += spokenName.Length;
                continue;
            }

            index = end;
        }

        return text;
    }

    public HeroMention? FindSpeakerMention(
        string speakerText,
        bool preferPersonalName,
        AppLanguage language = AppLanguage.Chinese)
    {
        if (string.IsNullOrWhiteSpace(speakerText))
        {
            return null;
        }

        var normalized = speakerText.Trim();
        var exactId = FindOfficialHeroId(normalized);
        if (exactId is not null)
        {
            return FindById(exactId, preferPersonalName, language);
        }

        var bracketTokens = Regex.Matches(normalized, @"[\(\[（]\s*(?<hero>[^\)\]）]+)\s*[\)\]）]")
            .Select(match => match.Groups["hero"].Value);
        foreach (var token in bracketTokens)
        {
            var bracketId = FindOfficialHeroId(token);
            if (bracketId is not null)
            {
                return FindById(bracketId, preferPersonalName, language);
            }
        }

        var entry = _entries.FirstOrDefault(candidate =>
            IsSpeakerToken(normalized, candidate.KoreanName)
            || IsSpeakerToken(normalized, candidate.ChineseOfficial)
            || IsSpeakerToken(normalized, candidate.ChinesePersonal)
            || IsSpeakerToken(normalized, candidate.ChineseTraditionalName)
            || IsSpeakerToken(normalized, candidate.ChineseTraditionalTitle)
            || IsSpeakerToken(
                normalized,
                ChineseTextNormalizer.ToSimplifiedCharacters(candidate.ChineseTraditionalName))
            || IsSpeakerToken(
                normalized,
                ChineseTextNormalizer.ToSimplifiedCharacters(candidate.ChineseTraditionalTitle))
            || IsSpeakerToken(normalized, candidate.EnglishName)
            || candidate.ChineseAliases.Any(alias => IsSpeakerToken(normalized, alias)));
        return entry is null
            ? null
            : CreateMention(entry, preferPersonalName, language);
    }

    public IReadOnlyList<string> CanonicalizeLines(
        IEnumerable<string> lines,
        IReadOnlyList<HeroMention> mentions)
    {
        return lines.Select(line =>
        {
            var result = line;
            foreach (var hero in mentions)
            {
                if (!_entriesById.TryGetValue(hero.Id, out var entry))
                {
                    continue;
                }

                var tag = $"<英雄 id=\"{hero.Id}\">{hero.SpokenName}</英雄>";
                var aliases = new[]
                    {
                        entry.KoreanName,
                        entry.ChineseOfficial,
                        entry.ChinesePersonal,
                        entry.ChineseTraditionalName,
                        entry.ChineseTraditionalTitle,
                        ChineseTextNormalizer.ToSimplifiedCharacters(entry.ChineseTraditionalName),
                        ChineseTextNormalizer.ToSimplifiedCharacters(entry.ChineseTraditionalTitle),
                        entry.EnglishName
                    }
                    .Concat(entry.ChineseAliases)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias) && alias.Length >= 2)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(alias => alias.Length);
                foreach (var alias in aliases)
                {
                    result = result.Replace(alias, tag, StringComparison.OrdinalIgnoreCase);
                }
            }

            return result;
        }).ToArray();
    }

    private static bool IsSpeakerToken(string speakerText, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return string.Equals(speakerText, candidate, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(
                speakerText,
                $@"(?:^|[\s\(\[<·-]){Regex.Escape(candidate)}(?:$|[\s\)\]>·-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void AddSpeakerAlias(
        IDictionary<string, HeroEntry> aliases,
        HeroEntry entry,
        string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        aliases.TryAdd(NormalizeHeroToken(alias), entry);
    }

    private static string NormalizeHeroToken(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormKC)
            .Replace("Ⅳ", "IV", StringComparison.Ordinal)
            .ToUpperInvariant();
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsHangul(char character) =>
        character is >= '\uAC00' and <= '\uD7A3';

    private static bool IsHan(char character) =>
        character is >= '\u3400' and <= '\u9FFF';

    private static IEnumerable<string> ChineseHeroAliases(HeroEntry entry) => new[]
        {
            entry.ChinesePersonal,
            entry.ChineseOfficial,
            entry.ChineseTraditionalName,
            entry.ChineseTraditionalTitle
        }
        .Concat(entry.ChineseAliases)
        .Where(alias => !string.IsNullOrWhiteSpace(alias));

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            }
            previous = current;
        }
        return previous[right.Length];
    }

    private static HeroMention CreateMention(
        HeroEntry entry,
        bool preferPersonalName,
        AppLanguage language) =>
        new(
            entry.Id,
            entry.Key,
            entry.KoreanName,
            entry.ChineseOfficial,
            entry.ChinesePersonal,
            GetSpokenName(entry, preferPersonalName, language),
            entry.ChineseTraditionalName,
            entry.ChineseTraditionalTitle);

    private static string GetSpokenName(
        HeroEntry entry,
        bool preferPersonalName,
        AppLanguage language)
    {
        if (language == AppLanguage.English)
        {
            return !string.IsNullOrWhiteSpace(entry.EnglishName)
                ? entry.EnglishName
                : entry.Id switch
            {
                "AurelionSol" => "Aurelion Sol",
                "Belveth" => "Bel'Veth",
                "Chogath" => "Cho'Gath",
                "DrMundo" => "Dr. Mundo",
                "JarvanIV" => "Jarvan IV",
                "Kaisa" => "Kai'Sa",
                "Khazix" => "Kha'Zix",
                "KogMaw" => "Kog'Maw",
                "KSante" => "K'Sante",
                "Leblanc" => "LeBlanc",
                "LeeSin" => "Lee Sin",
                "MasterYi" => "Master Yi",
                "MissFortune" => "Miss Fortune",
                "MonkeyKing" => "Wukong",
                "Nunu" => "Nunu & Willump",
                "RekSai" => "Rek'Sai",
                "Renata" => "Renata Glasc",
                "TahmKench" => "Tahm Kench",
                "TwistedFate" => "Twisted Fate",
                "Velkoz" => "Vel'Koz",
                "XinZhao" => "Xin Zhao",
                _ => entry.Id
            };
        }

        return preferPersonalName && !string.IsNullOrWhiteSpace(entry.ChinesePersonal)
            ? entry.ChinesePersonal
            : entry.ChineseOfficial;
    }

    private static HeroCatalog CreateFallbackCatalog() => new()
    {
        Version = "fallback",
        Heroes =
        {
            new HeroEntry
            {
                Id = "Leona", Key = "89", KoreanName = "레오나", KoreanTitle = "여명의 빛",
                ChineseOfficial = "曙光女神", ChinesePersonal = "蕾欧娜", EnglishName = "Leona",
                ChineseAliases = { "日女" }
            },
            new HeroEntry
            {
                Id = "Ahri", Key = "103", KoreanName = "아리", KoreanTitle = "구미호",
                ChineseOfficial = "九尾妖狐", ChinesePersonal = "阿狸", EnglishName = "Ahri"
            }
        }
    };
}
