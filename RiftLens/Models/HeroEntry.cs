namespace RiftLens.Models;

public sealed class HeroCatalog
{
    public string Version { get; set; } = "unknown";
    public List<HeroEntry> Heroes { get; set; } = new();
}

public sealed class HeroEntry
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string KoreanName { get; set; } = string.Empty;
    public string KoreanTitle { get; set; } = string.Empty;
    public string ChineseOfficial { get; set; } = string.Empty;
    public string ChinesePersonal { get; set; } = string.Empty;
    public List<string> ChineseAliases { get; set; } = new();
}
