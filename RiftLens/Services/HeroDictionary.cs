using System.Text.Json;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class HeroDictionary
{
    private readonly IReadOnlyList<HeroEntry> _entries;

    public string Version { get; }

    private HeroDictionary(HeroCatalog catalog)
    {
        Version = catalog.Version;
        _entries = catalog.Heroes
            .Where(entry => !string.IsNullOrWhiteSpace(entry.KoreanName))
            .OrderByDescending(entry => entry.KoreanName.Length)
            .ToArray();
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
                    && text.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new HeroMention(
                entry.Id,
                entry.Key,
                entry.KoreanName,
                entry.ChineseOfficial,
                entry.ChinesePersonal,
                GetSpokenName(entry, preferPersonalName, language)))
            .DistinctBy(mention => mention.Id)
            .ToArray();
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
        var entry = _entries.FirstOrDefault(candidate =>
            IsSpeakerToken(normalized, candidate.KoreanName)
            || IsSpeakerToken(normalized, candidate.ChineseOfficial)
            || IsSpeakerToken(normalized, candidate.ChinesePersonal)
            || candidate.ChineseAliases.Any(alias => IsSpeakerToken(normalized, alias)));
        return entry is null
            ? null
            : new HeroMention(
                entry.Id,
                entry.Key,
                entry.KoreanName,
                entry.ChineseOfficial,
                entry.ChinesePersonal,
                GetSpokenName(entry, preferPersonalName, language));
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

    private static string GetSpokenName(
        HeroEntry entry,
        bool preferPersonalName,
        AppLanguage language)
    {
        if (language == AppLanguage.English)
        {
            return entry.Id switch
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
                ChineseOfficial = "曙光女神", ChinesePersonal = "蕾欧娜", ChineseAliases = { "日女" }
            },
            new HeroEntry
            {
                Id = "Ahri", Key = "103", KoreanName = "아리", KoreanTitle = "구미호",
                ChineseOfficial = "九尾妖狐", ChinesePersonal = "阿狸"
            }
        }
    };
}
