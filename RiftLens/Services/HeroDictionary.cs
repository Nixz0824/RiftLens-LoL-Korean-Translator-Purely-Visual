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
            foreach (var hero in mentions.OrderByDescending(hero => hero.KoreanName.Length))
            {
                result = result.Replace(
                    hero.KoreanName,
                    $"<英雄 id=\"{hero.Id}\">{hero.SpokenName}</英雄>",
                    StringComparison.OrdinalIgnoreCase);
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
            GetSpokenName(entry, preferPersonalName, language));

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
