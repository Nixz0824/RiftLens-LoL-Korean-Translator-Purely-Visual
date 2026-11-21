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
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public string? LoadWarning { get; private set; }
    public string? RecoveryBackupPath { get; private set; }

    public SettingsStore(string? settingsPath = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftLens");
        _settingsPath = settingsPath ?? Path.Combine(root, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        LoadWarning = null;
        RecoveryBackupPath = null;
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
            if (settings.SchemaVersion < 11)
            {
                // v11 adds an opt-in experimental DeepSeek Vision OCR path.
                // Existing users stay on local OCR and no screenshot is sent
                // unless the user explicitly selects Vision and consents.
                settings.RecognitionMode = RecognitionPipelineMode.LocalOcr;
                settings.VisionPrivacyConsent = false;
                settings.SchemaVersion = 11;
            }
            settings.CaptureRegion ??= new ScreenRegion();
            settings.DisplayRegion ??= new ScreenRegion();
            settings.Presets ??= new List<CapturePreset>();
            if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
            {
                settings.ApiBaseUrl = "https://api.deepseek.com";
            }
            if (string.IsNullOrWhiteSpace(settings.ApiModel))
            {
                settings.ApiModel = "deepseek-v4-flash";
            }
            if (!Enum.IsDefined(settings.RecognitionMode))
            {
                settings.RecognitionMode = RecognitionPipelineMode.LocalOcr;
            }
            var supportedPalettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "porcelain", "mono", "pearl", "coral", "blue", "teal", "sage", "sand", "custom"
            };
            if (string.IsNullOrWhiteSpace(settings.InterfacePalette)
                || !supportedPalettes.Contains(settings.InterfacePalette))
            {
                settings.InterfacePalette = "porcelain";
            }
            var color = settings.CustomThemeColor?.Trim() ?? string.Empty;
            if (color.Length != 7
                || color[0] != '#'
                || !color.Skip(1).All(Uri.IsHexDigit))
            {
                settings.CustomThemeColor = "#6f7f91";
            }
            else
            {
                settings.CustomThemeColor = color.ToLowerInvariant();
            }
            if (settings.OutputDeviceIndex < -1)
            {
                settings.OutputDeviceIndex = -1;
            }
            // 旧版本默认 2 倍放大，对游戏内 12-16px 韩文不足；读取时自动迁移到聊天优化值。
            settings.OcrScale = Math.Clamp(settings.OcrScale, 3, 4);
            return settings;
        }
        catch (Exception exception)
        {
            LoadWarning = "设置无法读取，已使用默认设置。 / Settings could not be read; defaults are in use.";
            if (exception is JsonException or InvalidOperationException or NullReferenceException
                or NotSupportedException or ArgumentException)
            {
                try
                {
                    var backup = _settingsPath + $".corrupt-{Guid.NewGuid():N}.bak";
                    File.Copy(_settingsPath, backup);
                    RecoveryBackupPath = backup;
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    LoadWarning += " 原文件备份失败。 / The original file could not be backed up.";
                }
            }
            return new AppSettings();
        }
    }

    public async Task FlushAsync()
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        _saveGate.Release();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        // Snapshot before yielding: later UI edits must not change an earlier save.
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await _saveGate.WaitAsync().ConfigureAwait(false);
        var temporaryPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!);
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            finally
            {
                _saveGate.Release();
            }
        }
    }
}
