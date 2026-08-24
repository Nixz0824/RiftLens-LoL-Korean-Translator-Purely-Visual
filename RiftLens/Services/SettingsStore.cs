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
            if (settings.SchemaVersion < 5)
            {
                // v5 replaces overlapping filter levels with explicit, safe-by-default
                // system-message categories. Player chat remains always enabled.
                settings.TacticalSignalEnabled = false;
                settings.KillEventEnabled = false;
                settings.ObjectiveEventEnabled = false;
                settings.ClientReminderEnabled = false;
                settings.OtherSystemEventEnabled = false;
                settings.SignalVoiceEnabled = false;
                settings.SystemMessageFilter = SystemMessageFilterMode.Strict;
                settings.SpeechFilter = SpeechFilterLevel.Standard;
                settings.SchemaVersion = 5;
            }
            if (settings.SchemaVersion < 6)
            {
                // v6 exposes the three player-speech tiers independently. Existing
                // users retain standard tactical speech; only the old full mode opts
                // into neutral chat.
                settings.SpeakUrgentPlayerChat = true;
                settings.SpeakImportantPlayerChat = true;
                settings.SpeakNeutralPlayerChat = settings.SpeechFilter == SpeechFilterLevel.Full;
                settings.SchemaVersion = 6;
            }
            if (settings.SchemaVersion < 7)
            {
                // v7 keeps each translated message visible long enough to read
                // during play, including for users carrying older saved settings.
                settings.OverlayDurationMs = 7000;
                settings.SchemaVersion = 7;
            }
            if (settings.SchemaVersion < 8)
            {
                // v8 keeps the same five safe-by-default switches while clarifying
                // that objectives include structures and other notices mean match flow.
                settings.SchemaVersion = 8;
            }
            if (settings.SchemaVersion < 9)
            {
                // v9 stores window-relative capture boxes and ships real default
                // ratios instead of name-only empty presets.
                if (settings.Presets.Count == 0
                    || settings.Presets.All(preset => !preset.CaptureRegion.IsValid))
                {
                    settings.Presets = CapturePreset.CreateDefaults();
                }

                settings.SchemaVersion = 9;
            }
            if (settings.SchemaVersion < 10)
            {
                // v10 adds the one-time onboarding state and a lightweight,
                // user-selectable light-interface palette.
                settings.OnboardingCompleted = false;
                settings.InterfacePalette = "porcelain";
                settings.CustomThemeColor = "#6f7f91";
                settings.SaveTranslationHistory = false;
                settings.SchemaVersion = 10;
            }
            var supportedPalettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "porcelain", "mono", "pearl", "coral", "blue", "teal", "sage", "sand", "custom"
            };
            if (!supportedPalettes.Contains(settings.InterfacePalette))
            {
                settings.InterfacePalette = "porcelain";
            }
            if (settings.CustomThemeColor.Length != 7
                || settings.CustomThemeColor[0] != '#'
                || !settings.CustomThemeColor.Skip(1).All(Uri.IsHexDigit))
            {
                settings.CustomThemeColor = "#6f7f91";
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
