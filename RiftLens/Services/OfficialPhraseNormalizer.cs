using System.Text.RegularExpressions;

namespace RiftLens.Services;

/// <summary>
/// Constrained OCR near-miss recovery for official LoL HUD/Ping phrases only.
/// Free-form player chat is never rewritten here.
/// </summary>
public static partial class OfficialPhraseNormalizer
{
    private static readonly (string Damaged, string Official)[] Phrases =
    {
        ("雷速支援", "尽速支援"),
        ("野野任务", "打野任务"),
        ("史诗野径", "史诗野怪"),
        ("史诗野徑", "史诗野怪"),
        ("新里雅", "新里程碑")
    };

    public static string Normalize(string? value)
    {
        var normalized = ChineseTextNormalizer.ToSimplifiedCharacters(value)
            .Replace(',', '，');
        foreach (var (damaged, official) in Phrases)
        {
            normalized = normalized.Replace(damaged, official, StringComparison.Ordinal);
        }

        return MilestoneStemRegex().Replace(normalized, "新里程碑");
    }

    public static bool IsTruncatedGeneratedPayload(string? value)
    {
        var normalized = Normalize(value).Trim();
        return normalized is "发出" or "发出信号" or "发出信号告知" or "要求队友"
            or "标记了" or "已使用" or "已达到" or "已达到已击杀";
    }

    [GeneratedRegex(@"新里程(?!碑)", RegexOptions.CultureInvariant)]
    private static partial Regex MilestoneStemRegex();
}
