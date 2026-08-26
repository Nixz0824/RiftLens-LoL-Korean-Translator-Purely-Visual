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
    public string ChineseTraditionalName { get; set; } = string.Empty;
    public string ChineseTraditionalTitle { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public List<string> ChineseAliases { get; set; } = new();
}

public sealed class GameTermCatalog
{
    public string Version { get; set; } = "unknown";
    public List<GameTermEntry> Terms { get; set; } = new();
}

public sealed class GameTermEntry
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Korean { get; set; } = string.Empty;
    public string Chinese { get; set; } = string.Empty;
    public string ChineseTraditional { get; set; } = string.Empty;
    public string English { get; set; } = string.Empty;
}
