using System.Text.Json;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftLens");
        _settingsPath = settingsPath ?? Path.Combine(root, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            using var document = JsonDocument.Parse(json);
            var hasSchemaVersion = document.RootElement.EnumerateObject()
                .Any(property => property.NameEquals(nameof(AppSettings.SchemaVersion)));
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (!hasSchemaVersion || settings.SchemaVersion < 3)
            {
                // v3 introduces speaker-turn summaries and an idle-only tactical channel.
                // Apply the new safe defaults once; all controls remain user-adjustable.
                settings.SummaryWindowMs = 900;
                settings.MaxSummaryChars = 64;
                settings.SignalVoiceEnabled = true;
                settings.KeepEmotionSummary = true;
                settings.SchemaVersion = 3;
            }
            if (settings.SchemaVersion < 4)
            {
                // v4 adds one lightweight language setting shared by UI, translation and speech.
                settings.Language = AppLanguage.Chinese;
                settings.SchemaVersion = 4;
            }
            // 旧版本默认 2 倍放大，对游戏内 12-16px 韩文不足；读取时自动迁移到聊天优化值。
            settings.OcrScale = Math.Clamp(settings.OcrScale, 3, 4);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }
}
