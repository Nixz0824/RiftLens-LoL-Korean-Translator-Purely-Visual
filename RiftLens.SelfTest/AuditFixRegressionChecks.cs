using System.Net;
using System.Text;
using System.Text.Json;
using RiftLens.Models;
using RiftLens.Services;

internal static class AuditFixRegressionChecks
{
    public static Task ChangeOnlyDisabledAlwaysDetectsChangeAsync()
    {
        var detector = new ImageChangeDetector();
        using var bitmap = new Bitmap(120, 40);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(20, 24, 30));
        }

        Require(detector.HasMeaningfulChange(bitmap, 0.035), "Initial frame should always trigger baseline.");
        Require(!detector.HasMeaningfulChange(bitmap, 0.035), "Identical frame should not trigger when ChangeOnly is enabled.");
        Require(detector.HasMeaningfulChange(bitmap, 0), "Identical frame must still trigger when ChangeThreshold <= 0 (ChangeOnly disabled).");
        return Task.CompletedTask;
    }

    public static Task VisionPeekDoesNotPoisonDeduplicatorAsync()
    {
        var deduplicator = new MessageDeduplicator();
        // Establish baseline with an existing message.
        Require(deduplicator.FindNewLineResults("레오나: 탑 조심").Count == 0, "First frame should only establish baseline.");

        // Local OCR previews a new frame without committing so Vision remains authoritative.
        var localPreview = deduplicator.PeekNewLineResults("레오나: 탑 조심\n아리: 미드 밀고 갈게");
        Require(localPreview.Count == 1 && localPreview[0].NormalizedText.Contains("미드 밀고 갈게"),
            "Peek did not detect the new line.");

        // Vision returns the same or slightly corrected line and commits cleanly.
        var visionCommitted = deduplicator.FindNewLineResults("레오나: 탑 조심\n아리: 미드 밀고 갈게요");
        Require(visionCommitted.Count == 1 && visionCommitted[0].NormalizedText.Contains("미드 밀고 갈게요"),
            "Vision commit was swallowed by prior local peek.");

        // Subsequent frame with no new lines stays deduplicated.
        Require(deduplicator.FindNewLineResults("레오나: 탑 조심\n아리: 미드 밀고 갈게요").Count == 0,
            "Committed Vision line was emitted twice.");
        return Task.CompletedTask;
    }

    public static async Task TranslationCompatibilityDowngradeIsCachedAsync()
    {
        var requestCount = 0;
        var seenThinkingCount = 0;
        var handler = new CompatibilityStubHandler((body, uri) =>
        {
            requestCount++;
            if (uri.AbsolutePath.EndsWith("/chat/completions/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Duplicate /chat/completions path was requested.");
            }
            if (body.Contains("\"thinking\"", StringComparison.Ordinal) || body.Contains("\"response_format\"", StringComparison.Ordinal))
            {
                seenThinkingCount++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"unsupported parameter\"}", Encoding.UTF8, "application/json")
                };
            }
            var responseJson = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = JsonSerializer.Serialize(new
                            {
                                displayLines = new[] { new { id = "line-1", translation = "小心上路。" } },
                                toxicityFiltered = false
                            })
                        }
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var service = new OpenAiCompatibleTranslationService(
            "https://api.deepseek.com/v1/chat/completions",
            "deepseek-chat",
            "test-key",
            handler);
        var request = new TranslationRequest(new[] { "탑 조심" }, Array.Empty<HeroMention>(), SpeechFilterLevel.Standard, 64);
        var first = await service.TranslateAsync(request, CancellationToken.None);
        var second = await service.TranslateAsync(request, CancellationToken.None);
        Require(first.DisplayLines[0] == "小心上路。" && second.DisplayLines[0] == "小心上路。",
            "Downgraded requests did not return valid translations.");
        Require(seenThinkingCount == 1 && requestCount == 3,
            $"Expected compatibility downgrade to be cached (seenOptional=1, totalRequests=3), got seenOptional={seenThinkingCount}, totalRequests={requestCount}.");
    }

    public static async Task SettingsStoreSurvivesNullPropertiesAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RiftLens-null-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 11,
                  "SpeechVolume": 42,
                  "CustomThemeColor": null,
                  "InterfacePalette": null,
                  "ApiBaseUrl": null,
                  "ApiModel": null,
                  "OutputDeviceIndex": -99
                }
                """);
            var store = new SettingsStore(path);
            var loaded = await store.LoadAsync();
            Require(store.LoadWarning is null, "Null optional properties triggered a full settings reset.");
            Require(loaded.SpeechVolume == 42, "Valid settings were lost when null properties were present.");
            Require(loaded.CustomThemeColor == "#6f7f91" && loaded.InterfacePalette == "porcelain",
                "Null palette properties did not fall back cleanly.");
            Require(loaded.ApiBaseUrl == "https://api.deepseek.com" && loaded.ApiModel == "deepseek-v4-flash",
                "Null API endpoint properties did not fall back cleanly.");
            Require(loaded.OutputDeviceIndex == -1,
                "Out-of-range output device index was not normalized properly.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CompatibilityStubHandler(Func<string, Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(token);
            return respond(body, request.RequestUri!);
        }
    }
}
