using RiftLens.Models;
using RiftLens.Services;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

var failures = new List<string>();

if (args.Length >= 2 && string.Equals(args[0], "--vision-upload-guard-eval", StringComparison.OrdinalIgnoreCase))
{
    var sessionDirectory = Path.GetFullPath(args[1]);
    var frameDirectory = Path.Combine(sessionDirectory, "frames");
    var files = Directory.GetFiles(frameDirectory, "*.png").OrderBy(path => path, StringComparer.Ordinal).ToArray();
    var decisions = new List<(string File, VisionUploadDecision Decision)>();
    foreach (var file in files)
    {
        using var bitmap = new Bitmap(file);
        decisions.Add((Path.GetFileName(file), VisionUploadGuard.Evaluate(bitmap)));
    }
    var blocked = decisions.Where(item => item.Decision.ShouldBlock).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        frames = decisions.Count,
        blocked = blocked.Length,
        maximumDominantColourRatio = decisions.Count == 0 ? 0 : decisions.Max(item => item.Decision.DominantColourRatio),
        maximumCoveredTileRatio = decisions.Count == 0 ? 0 : decisions.Max(item => item.Decision.CoveredTileRatio),
        blockedFrames = blocked.Select(item => new
        {
            item.File,
            item.Decision.DominantColourRatio,
            item.Decision.CoveredTileRatio
        }).ToArray()
    }));
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--classify-history", StringComparison.OrdinalIgnoreCase))
{
    var path = Path.GetFullPath(args[1]);
    var classifier = new ChatMessageClassifier();
    var counts = Enum.GetValues<GameMessageKind>().ToDictionary(kind => kind, _ => 0);
    var unknown = new List<string>();
    foreach (var json in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
    {
        using var document = JsonDocument.Parse(json);
        var original = document.RootElement.TryGetProperty("original", out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
        foreach (var line in original.Split(
                     new[] { "\r\n", "\n", "\r" },
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var message = classifier.Classify(line);
            counts[message.Kind]++;
            if (message.Kind == GameMessageKind.Unknown) unknown.Add(message.RawText);
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        categories = counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
        unknown = unknown.Distinct(StringComparer.Ordinal).Take(80).ToArray()
    }));
    return 0;
}

if (args.Length >= 3 && string.Equals(args[0], "--vision-eval", StringComparison.OrdinalIgnoreCase))
{
    var sessionDirectory = Path.GetFullPath(args[1]);
    var outputPath = Path.GetFullPath(args[2]);
    var maximum = args.Length >= 4 && int.TryParse(args[3], out var parsedMaximum)
        ? Math.Max(1, parsedMaximum)
        : int.MaxValue;
    var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");
    if (!File.Exists(eventsPath))
    {
        throw new FileNotFoundException("Evaluation events file was not found.", eventsPath);
    }

    var frames = new List<VisionEvalFrame>();
    foreach (var line in File.ReadLines(eventsPath))
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("eventType", out var eventType)
            || !string.Equals(eventType.GetString(), "frame", StringComparison.Ordinal)
            || !root.TryGetProperty("ocrAttempted", out var attempted)
            || !attempted.GetBoolean())
        {
            continue;
        }
        frames.Add(new VisionEvalFrame(
            root.GetProperty("frameId").GetString() ?? string.Empty,
            root.GetProperty("screenshotFile").GetString() ?? string.Empty,
            root.TryGetProperty("rawOcr", out var rawOcr) ? rawOcr.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("ocrElapsedMs", out var elapsed) ? elapsed.GetDouble() : 0));
    }
    frames = frames.Take(maximum).ToList();
    var key = new CredentialStore().ReadApiKey();
    if (string.IsNullOrWhiteSpace(key))
    {
        throw new InvalidOperationException("No DeepSeek API key was found in Windows Credential Manager.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    using var vision = new DeepSeekVisionOcrService("https://api.deepseek.com", key);
    key = null;
    Console.WriteLine($"Vision evaluation: {frames.Count} selected-region frames. Output stays local: {outputPath}");
    for (var index = 0; index < frames.Count; index++)
    {
        var frame = frames[index];
        var imagePath = Path.GetFullPath(Path.Combine(sessionDirectory, frame.ScreenshotFile));
        var timer = Stopwatch.StartNew();
        string visionText = string.Empty;
        string error = string.Empty;
        try
        {
            using var bitmap = new Bitmap(imagePath);
            var result = await vision.RecognizeAsync(bitmap, CancellationToken.None);
            visionText = result.RawText;
        }
        catch (Exception exception)
        {
            error = exception.Message.Length <= 240 ? exception.Message : exception.Message[..240] + "…";
        }
        timer.Stop();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            frameId = frame.FrameId,
            screenshotFile = frame.ScreenshotFile,
            localOcr = frame.LocalOcr,
            visionOcr = visionText,
            localOcrElapsedMs = frame.LocalOcrElapsedMs,
            visionElapsedMs = Math.Round(timer.Elapsed.TotalMilliseconds, 2),
            error
        }));
        if ((index + 1) % 10 == 0 || index + 1 == frames.Count)
        {
            Console.WriteLine($"Vision evaluation progress: {index + 1}/{frames.Count}");
        }
    }
    Console.WriteLine("Vision evaluation completed. The output contains chat text and must not be uploaded unredacted.");
    return 0;
}

if (args.Length >= 4 && string.Equals(args[0], "--vision-hybrid-eval", StringComparison.OrdinalIgnoreCase))
{
    var inputPath = Path.GetFullPath(args[1]);
    var sessionDirectory = Path.GetFullPath(args[2]);
    var outputPath = Path.GetFullPath(args[3]);
    var maximum = args.Length >= 5 && int.TryParse(args[4], out var parsedMaximum)
        ? Math.Max(1, parsedMaximum)
        : int.MaxValue;
    var rows = File.ReadLines(inputPath)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Take(maximum)
        .Select(line => JsonDocument.Parse(line))
        .ToArray();
    var heroes = await HeroDictionary.LoadAsync();
    var processor = new VisionOcrPostProcessor(heroes);
    var rawRoute = new RouteReplayAccumulator(heroes);
    var structuredRoute = new RouteReplayAccumulator(heroes);
    var hybridTextRoute = new RouteReplayAccumulator(heroes);
    var hybridRuntimeRoute = new RouteReplayAccumulator(heroes);
    using var localOcr = new BundledOcrService();
    localOcr.WarmUp();
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    Console.WriteLine($"Hybrid Vision evaluation: {rows.Length} frames. Output stays local: {outputPath}");
    try
    {
        for (var index = 0; index < rows.Length; index++)
        {
            var root = rows[index].RootElement;
            var frameId = root.GetProperty("frameId").GetString() ?? string.Empty;
            var screenshotFile = root.GetProperty("screenshotFile").GetString() ?? string.Empty;
            var localText = root.TryGetProperty("localOcr", out var localValue)
                ? localValue.GetString() ?? string.Empty
                : string.Empty;
            var visionText = root.TryGetProperty("visionOcr", out var visionValue)
                ? visionValue.GetString() ?? string.Empty
                : string.Empty;
            var deepSeekElapsedMs = root.TryGetProperty("visionElapsedMs", out var visionElapsed)
                ? visionElapsed.GetDouble()
                : 0;
            using var bitmap = new Bitmap(Path.GetFullPath(Path.Combine(sessionDirectory, screenshotFile)));
            var visionDocument = OcrDocument.FromText(visionText);

            var structuredTimer = Stopwatch.StartNew();
            var structured = processor.Process(null, visionDocument);
            structuredTimer.Stop();

            var hybridTextTimer = Stopwatch.StartNew();
            var hybridText = processor.Process(null, visionDocument, OcrDocument.FromText(localText));
            hybridTextTimer.Stop();

            var localTimer = Stopwatch.StartNew();
            var localDetailed = await localOcr.RecognizeDetailedAsync(bitmap, CancellationToken.None, 3);
            localTimer.Stop();

            var hybridRuntimeTimer = Stopwatch.StartNew();
            var hybridRuntime = processor.Process(bitmap, visionDocument, localDetailed);
            hybridRuntimeTimer.Stop();
            rawRoute.Observe(visionDocument);
            structuredRoute.Observe(structured.Document);
            hybridTextRoute.Observe(hybridText.Document);
            hybridRuntimeRoute.Observe(hybridRuntime.Document);
            var estimatedParallelElapsedMs = Math.Max(deepSeekElapsedMs, localTimer.Elapsed.TotalMilliseconds)
                + hybridRuntimeTimer.Elapsed.TotalMilliseconds;

            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                frameId,
                screenshotFile,
                localOcr = localText,
                localRerunOcr = localDetailed.RawText,
                visionOcr = visionText,
                visionStructuredOcr = structured.Document.RawText,
                visionHybridTextOcr = hybridText.Document.RawText,
                visionHybridRuntimeOcr = hybridRuntime.Document.RawText,
                deepSeekElapsedMs = Math.Round(deepSeekElapsedMs, 2),
                localRerunElapsedMs = Math.Round(localTimer.Elapsed.TotalMilliseconds, 2),
                structuredElapsedMs = Math.Round(structuredTimer.Elapsed.TotalMilliseconds, 3),
                hybridTextElapsedMs = Math.Round(hybridTextTimer.Elapsed.TotalMilliseconds, 3),
                hybridRuntimeElapsedMs = Math.Round(hybridRuntimeTimer.Elapsed.TotalMilliseconds, 3),
                estimatedParallelElapsedMs = Math.Round(estimatedParallelElapsedMs, 2),
                structuredStats = structured.Stats,
                hybridTextStats = hybridText.Stats,
                hybridRuntimeStats = hybridRuntime.Stats
            }));
            if ((index + 1) % 10 == 0 || index + 1 == rows.Length)
            {
                Console.WriteLine($"Hybrid Vision evaluation progress: {index + 1}/{rows.Length}");
            }
        }
    }
    finally
    {
        foreach (var row in rows) row.Dispose();
    }
    var routeSummaryPath = Path.ChangeExtension(outputPath, ".route-summary.json");
    var routeSummary = new
    {
        rawVision = rawRoute.Snapshot(),
        structured = structuredRoute.Snapshot(),
        hybridStoredLocal = hybridTextRoute.Snapshot(),
        hybridRuntimeWithColour = hybridRuntimeRoute.Snapshot()
    };
    await File.WriteAllTextAsync(
        routeSummaryPath,
        JsonSerializer.Serialize(routeSummary, new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(routeSummary));
    Console.WriteLine($"Aggregate route summary: {routeSummaryPath}");
    Console.WriteLine("Hybrid Vision evaluation completed. Output contains chat text and must not be uploaded unredacted.");
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--route-replay", StringComparison.OrdinalIgnoreCase))
{
    var rows = File.ReadLines(Path.GetFullPath(args[1]))
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line => JsonDocument.Parse(line))
        .ToArray();
    try
    {
        var properties = args.Length > 2
            ? args.Skip(2).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : new[] { "localOcr", "visionOcr" };
        foreach (var property in properties)
        {
            var deduplicator = new MessageDeduplicator();
            var classifier = new ChatMessageClassifier();
            var counts = Enum.GetValues<GameMessageKind>().ToDictionary(kind => kind, _ => 0);
            var acceptedLines = 0;
            foreach (var row in rows)
            {
                var raw = row.RootElement.TryGetProperty(property, out var value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
                foreach (var line in deduplicator.FindNewLineResults(raw))
                {
                    if (!IsPlausibleForReplay(line.NormalizedText))
                    {
                        continue;
                    }
                    acceptedLines++;
                    counts[classifier.Classify(line.NormalizedText).Kind]++;
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                source = property,
                acceptedLines,
                categories = counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
            }));
        }
    }
    finally
    {
        foreach (var row in rows) row.Dispose();
    }
    return 0;
}

if (args.Contains("--voice", StringComparer.OrdinalIgnoreCase))
{
    using var speech = new NaturalSpeechService();
    speech.StatusChanged += message => Console.WriteLine("[语音] " + message);
    speech.Enqueue("晓晓语音已经连接。蕾欧娜提醒上单及时支援，并小心敌方打野。", 65, 0, 10000, true);
    await Task.Delay(1200);
    speech.SetVolume(35);
    Console.WriteLine("[语音] 播放中音量已调整为 35%");
    await Task.Delay(7800);
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--render-floating", StringComparison.OrdinalIgnoreCase))
{
    ApplicationConfiguration.Initialize();
    using var control = new RiftLens.UI.OverlayControlForm();
    control.SetPaused(false);
    control.SetVolume(65);
    var expand = typeof(RiftLens.UI.OverlayControlForm).GetMethod(
        "SetExpanded",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("找不到悬浮控制展开方法");
    var animate = typeof(RiftLens.UI.OverlayControlForm).GetMethod(
        "AnimateWidth",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("找不到悬浮控制动画方法");
    control.Location = new Point(-30000, -30000);
    control.Show();
    Application.DoEvents();
    expand.Invoke(control, new object[] { true });
    Thread.Sleep(210);
    animate.Invoke(control, null);
    using var bitmap = new Bitmap(control.Width, control.Height);
    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
    var outputPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"悬浮控制预览：{control.Width}x{control.Height} · {outputPath}");
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--render-translation", StringComparison.OrdinalIgnoreCase))
{
    ApplicationConfiguration.Initialize();
    using var overlay = new RiftLens.UI.TranslationOverlayForm();
    var settings = new AppSettings
    {
        Language = AppLanguage.Chinese,
        CaptureRegion = new ScreenRegion { X = 180, Y = 520, Width = 520, Height = 148 },
        DisplayRegion = new ScreenRegion { X = 180, Y = 350, Width = 520, Height = 148 },
        OverlayPlacement = OverlayPlacement.Custom,
        OverlayFontSize = 16,
        ShowOriginalText = true
    };
    var overlayType = typeof(RiftLens.UI.TranslationOverlayForm);
    var result = new TranslationResult
    {
        Speaker = "嘉文四世",
        DisplayTranslation = "嘉文四世：敌方打野正在靠近下路，先后退并保留闪现。",
        DisplayLines = new List<string>
        {
            "敌方打野正在靠近下路。",
            "先后退，闪现留着。"
        },
        OriginalLines = new List<string>
        {
            "적 정글 바텀 가는 중",
            "일단 빼고 점멸 아껴"
        },
        RawText = "[12:34] PlayerOne#KR1 (자르반 4세): 적 정글 바텀 가는 중\n[12:35] PlayerOne#KR1 (자르반 4세): 일단 빼고 점멸 아껴"
    };
    overlayType.GetMethod("PrepareContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(overlay, new object[] { result, settings });
    overlayType.GetMethod("EnsureFonts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(overlay, new object[] { settings });
    overlayType.GetMethod("LayoutContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(overlay, new object[] { settings });
    overlayType.GetField("_surfaceOpacity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(overlay, (byte)255);
    overlay.Location = new Point(-30000, -30000);
    overlay.Show();
    Application.DoEvents();
    overlayType.GetMethod("RenderLayeredSurface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(overlay, null);

    using var bitmap = new Bitmap(overlay.Width, overlay.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        overlayType.GetMethod("DrawSurface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(overlay, new object[] { graphics });
    }
    var outputPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"译文框预览：{overlay.Width}x{overlay.Height} · {outputPath}");
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--render-main", StringComparison.OrdinalIgnoreCase))
{
    ApplicationConfiguration.Initialize();
    using var form = new RiftLens.UI.MainForm
    {
        Size = new Size(1240, 810),
        Opacity = 0,
        ShowInTaskbar = false
    };
    form.Show();
    Application.DoEvents();
    if (args.Length >= 3)
    {
        var showPage = typeof(RiftLens.UI.MainForm).GetMethod(
            "ShowPage",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到页面切换方法");
        showPage.Invoke(form, new object[] { args[2].ToLowerInvariant() });
        Application.DoEvents();
    }
    Thread.Sleep(80);
    Application.DoEvents();
    using var bitmap = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
    var outputPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"主界面预览：{form.Width}x{form.Height} · {outputPath}");
    return 0;
}

await CheckAsync("英雄中韩映射", async () =>
{
    var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var path = Path.Combine(sourceRoot, "RiftLens", "Assets", "heroes.json");
    var dictionary = await HeroDictionary.LoadAsync(path);
    var mentions = dictionary.FindMentions(new[] { "레오나 탑 조심" }, true);
    Require(mentions.Count == 1, "没有识别到 레오나");
    Require(mentions[0].ChineseOfficial == "曙光女神", "中文官方名称不正确");
    Require(mentions[0].ChinesePersonal == "蕾欧娜", "中文角色名不正确");
    Require(dictionary.FindSpeakerMention("아리", true)?.SpokenName == "阿狸", "短英雄名前缀未映射到中文名");
    Require(dictionary.FindSpeakerMention("아리", true, AppLanguage.English)?.SpokenName == "Ahri", "英雄英文名映射失败");
    Require(dictionary.FindOfficialHeroId("자르반4세") == "JarvanIV", "嘉文韩文紧凑写法未归一化");
    Require(dictionary.FindOfficialHeroId("자르반 4 세") == "JarvanIV", "嘉文韩文空格波动未归一化");
    Require(dictionary.FindOfficialHeroId("자르반 IV") == "JarvanIV", "嘉文罗马数字写法未归一化");
    Require(dictionary.FindOfficialHeroId("Jarvan IV") == "JarvanIV", "嘉文英文官方名未对齐");
    Require(dictionary.FindOfficialHeroId("嘉文四世") == "JarvanIV", "嘉文中文角色名未对齐");
    Require(dictionary.FindOfficialHeroId("贾尔班四世") is null, "非官方误译英雄名不应进入词典");
    Require(dictionary.FindOfficialHeroId("犽宿") == "Yasuo", "台服官方英雄名犽宿未映射到亚索");
    Require(dictionary.FindOfficialHeroId("凱莎") == "Kaisa", "台服官方英雄名凱莎未映射到卡莎");
    Require(dictionary.FindOfficialHeroId("烏迪爾") == "Udyr", "台服官方英雄名烏迪爾未映射到乌迪尔");
    Require(dictionary.TryCorrectChineseHeroPrefix(
            "夢宿已使用R - 奪命氣息",
            out var twHeroId,
            out var twCorrected,
            out _)
        && twHeroId == "Yasuo"
        && twCorrected == "亚索",
        "繁体近形英雄名没有唯一修复到官方亚索");
    Require(dictionary.CorrectStructuredHeroPrefix(
            "弗宿 R",
            preferPersonalName: true,
            AppLanguage.Chinese) == "亚索 R",
        "结构化语义中的错误犽宿名称没有规范化");
    var traditionalMentions = dictionary.FindMentions(
        new[] { "凱莎已使用閃現，犽宿已使用R" },
        preferPersonalName: true);
    Require(traditionalMentions.Any(hero => hero.Id == "Kaisa")
        && traditionalMentions.Any(hero => hero.Id == "Yasuo"),
        "繁体正文中的英雄实体未识别");
    var embedded = await HeroDictionary.LoadAsync();
    Require(embedded.Version == dictionary.Version, "发布版内嵌英雄词典缺失");
});

await CheckAsync("聊天上滚去重", () =>
{
    var deduplicator = new MessageDeduplicator();
    Require(deduplicator.FindNewLines(string.Empty).Count == 0, "空白基线不应产生消息");
    Require(deduplicator.FindNewLines("First line").SequenceEqual(new[] { "First line" }), "空白后首条消息被错误吞掉");
    deduplicator.Reset();
    Require(deduplicator.FindNewLines("A one\nB two").Count == 0, "首次识别不应播报旧消息");
    Require(deduplicator.FindNewLines("A one\nB two\nC three").SequenceEqual(new[] { "C three" }), "追加消息识别失败");
    Require(deduplicator.FindNewLines("B two\nC three\nD four").SequenceEqual(new[] { "D four" }), "上滚消息识别失败");
    Require(deduplicator.FindNewLines("B two\nC three\nD four").Count == 0, "重复消息未过滤");

    // 游戏时间戳去除
    Require(MessageDeduplicator.StripGameTimestamp("[12:34]").Length == 0, "纯时间戳行未清空");
    Require(MessageDeduplicator.StripGameTimestamp("[12:34] 레오나: 탑 가세요") == "레오나: 탑 가세요", "时间戳+玩家消息未正确去除");
    Require(MessageDeduplicator.StripGameTimestamp("[0:05][팀] 아리: 미드 미아") == "[팀] 아리: 미드 미아", "时间戳+频道标签未正确去除");
    Require(MessageDeduplicator.StripGameTimestamp("(3:15) 적이 사라짐") == "적이 사라짐", "圆括号时间戳未去除");
    Require(MessageDeduplicator.StripGameTimestamp("[12：34] 레오나: 탑 가세요") == "레오나: 탑 가세요", "全角冒号时间戳未去除");
    Require(MessageDeduplicator.StripGameTimestamp("12:34] [팀] PlayerOne#KR1 (아리): 미드 조심") == "[팀] PlayerOne#KR1 (아리): 미드 조심", "缺左括号时间戳未恢复");
    Require(MessageDeduplicator.StripGameTimestamp("I12:34] [팀] PlayerOne#KR1 (아리): 미드 조심") == "[팀] PlayerOne#KR1 (아리): 미드 조심", "左括号 OCR 为 I 的时间戳未恢复");
    Require(MessageDeduplicator.StripGameTimestamp("[12:341 [팀] PlayerOne#KR1 (아리): 미드 조심") == "[팀] PlayerOne#KR1 (아리): 미드 조심", "右括号 OCR 为 1 的时间戳未恢复");
    Require(MessageDeduplicator.StripGameTimestamp("12 34 [팀] PlayerOne#KR1 (아리): 미드 조심") == "[팀] PlayerOne#KR1 (아리): 미드 조심", "时间冒号漏识别时未恢复");
    Require(MessageDeduplicator.StripGameTimestamp("25:99 Player: text") == "25:99 Player: text", "非法时钟值被误删");
    Require(GameTimestampPrefixParser.StripForSpeech("12:34] 没有意义的时间前缀") == "没有意义的时间前缀",
        "最终 TTS 前没有清除缺左括号的时间戳");
    Require(GameTimestampPrefixParser.StripForSpeech("[12：34] 时间前缀") == "时间前缀",
        "最终 TTS 前没有清除全角冒号时间戳");
    Require(MessageDeduplicator.StripGameTimestamp("레오나: 탑 가세요") == "레오나: 탑 가세요", "无时间戳消息被误修改");
    var longGameTimestamp = GameTimestampPrefixParser.Parse(
        "24:03OnPi(사일러스)님이 가고 있음");
    Require(longGameTimestamp.Matched
        && longGameTimestamp.Timestamp == TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(3)
        && GameTimestampPrefixParser.Format(longGameTimestamp.Timestamp) == "24:03"
        && longGameTimestamp.TextWithoutTimestamp == "OnPi(사일러스)님이 가고 있음",
        "24 分钟后的无空格游戏时间戳没有按分钟:秒移除");
    Require(MessageDeduplicator.StripGameTimestamp(
            "59.07PlayerOne (아리): mid careful") == "PlayerOne (아리): mid careful",
        "点号损坏且无空格的长局时间戳未恢复");
    Require(MessageDeduplicator.StripGameTimestamp(
            "12:34InGame (아리): mid careful") == "InGame (아리): mid careful",
        "正文以 I 开头时被误当成损坏的右括号吞掉");

    // 时间戳不影响去重：相同内容不同时间戳应视为重复
    deduplicator.Reset();
    Require(deduplicator.FindNewLines("[12:34] 레오나: 탑 조심\n[12:35] 아리: 미드 미아").Count == 0, "首次识别不应播报");
    Require(deduplicator.FindNewLines("[12:34] 레오나: 탑 조심\n[12:35] 아리: 미드 미아\n[12:36] 레오나: 정글 갱").SequenceEqual(new[] { "레오나: 정글 갱" }), "时间戳消息去重失败");

    // 空格、标点和单字符 OCR 波动不应让同一可见消息再次入队。
    deduplicator.Reset();
    Require(deduplicator.FindNewLines("루시안(루시안): 마나 24%\n세나(세나): 제어와드-1회충전됨").Count == 0,
        "稳定事件键基线不应播报旧消息");
    Require(deduplicator.FindNewLines("루시안 (루시안):마나24%\n세나(세나): 제어와드-1회충전됨").Count == 0,
        "空格与标点变化造成同一消息重复入队");
    Require(deduplicator.FindNewLines("루시안 (루시안):마나24%\n세나(세나): 제어와드-1회충전됨\n아칼리(아칼리): R")
            .SequenceEqual(new[] { "아칼리(아칼리): R" }),
        "稳定事件去重吞掉了真正新增的行");

    // 屏幕中确实追加一条相同 Ping 时仍应视为新的可见行。
    deduplicator.Reset();
    Require(deduplicator.FindNewLines("세나(세나): 제어와드-1회충전됨").Count == 0,
        "重复 Ping 测试基线不应播报");
    Require(deduplicator.FindNewLines(
            "세나(세나): 제어와드-1회충전됨\n세나(세나): 제어와드-1회충전됨").Count == 1,
        "玩家真正再次 Ping 被跨帧去重错误吞掉");

    return Task.CompletedTask;
});

await CheckAsync("玩家与系统消息分类", () =>
{
    var classifier = new ChatMessageClassifier();
    var playerPalette = new OcrColorEvidence(
        120, 0.26f, 0.52f, 0.01f, 0.42f, 0.69f);
    var systemPalette = new OcrColorEvidence(
        120, 0.18f, 0.31f, 0.16f, 0.20f, 0.24f);

    // In-game structure: optional timestamp/channel + player ID + (champion) + colon.
    var timestamped = classifier.Classify(
        "[12:34] [팀] PlayerOne#KR1 (아리): 미드 밀고 갈게",
        playerPalette);
    Require(timestamped.Kind == GameMessageKind.PlayerChat, "带时间戳的韩文玩家聊天被过滤");
    Require(timestamped.SpeakerKey == "PLAYERONE#KR1", "玩家 ID 未从英雄名中独立提取");
    Require(timestamped.SpeakerRaw.Contains("아리", StringComparison.Ordinal), "英雄名未保留给播报映射");
    Require(timestamped.Content == "미드 밀고 갈게", "玩家聊天正文提取失败");

    Require(ChineseTextNormalizer.ToSimplifiedCharacters("伊瑞莉雅已使用閃現") == "伊瑞莉雅已使用闪现",
        "Windows 繁简转换没有生效：" + ChineseTextNormalizer.ToSimplifiedCharacters("伊瑞莉雅已使用閃現"));
    Require(GameSemanticEventParser.TryParsePlayerFeed("伊瑞莉雅已使用閃現", out _),
        "繁体召唤师技能正文没有被语义解析器接收");
    var traditionalFlash = classifier.Classify(
        "MiloBramble (烏迪爾): 伊瑞莉雅已使用閃現");
    Require(traditionalFlash.Kind == GameMessageKind.TacticalSignal,
        "繁体召唤师技能 HUD 被误分类为玩家聊天");
    var traditionalAbility = classifier.Classify(
        "아니 (潘森): 凱莎已使用R - 殺手本能");
    Require(traditionalAbility.Kind == GameMessageKind.TacticalSignal,
        "繁体英雄技能 HUD 被误分类为玩家聊天");
    var traditionalItem = classifier.Classify(
        "Luon (亞歷斯塔): 凱莎 海妖殺手");
    Require(traditionalItem.Kind == GameMessageKind.PlayerChat
        && traditionalItem.SemanticEvent is { Status: "item_marker" },
        "繁体装备栏点选没有进入玩家装备播报");
    var traditionalSignal = classifier.Classify(
        "Luon (亞歷斯塔) 發出信號告知所有人敵人消失");
    Require(traditionalSignal.Kind == GameMessageKind.TacticalSignal,
        "台服固定信号句未进入战术分类");
    Require(classifier.Classify(
            "Luon (亞歷斯塔) 已把 治癒 換成 點燃").Kind == GameMessageKind.TacticalSignal,
        "台服召唤师技能交换未进入 HUD 战术分类");
    Require(classifier.Classify(
            "Luon (亞歷斯塔) 已購買 控域守衛!").Kind == GameMessageKind.TacticalSignal,
        "台服控制守卫购买提示未进入 HUD 战术分类");
    Require(classifier.Classify(
            "천하아빠 (費德提克) 已擊殺生化科技飛龍!").Kind == GameMessageKind.ObjectiveEvent,
        "台服炼金科技亚龙击杀未进入地图资源分类");
    Require(classifier.Classify(
            "천수아비 (費德提克) 擊殺了虛空幼蟲!").Kind == GameMessageKind.ObjectiveEvent,
        "台服虚空巢虫击杀未进入地图资源分类");
    Require(classifier.Classify(
            "처수아비 (費德提克) 打野任務完成!").Kind == GameMessageKind.OtherSystemEvent,
        "台服打野任务完成提示未进入系统分类");
    Require(classifier.Classify(
            "아니 (潘森) 中路任務完成!").Kind == GameMessageKind.OtherSystemEvent,
        "台服中路任务完成提示未进入系统分类");
    Require(classifier.Classify(
            "MiloBramble (烏迪爾) 已經無人能擋!").Kind == GameMessageKind.KillEvent,
        "台服无人能挡击杀播报未进入击杀分类");
    Require(classifier.Classify(
            "MiloBramble (烏迪爾) 已達到已擊殺的史詩野怪的新里程碑: 4,3").Kind
            == GameMessageKind.OtherSystemEvent,
        "台服史诗野怪里程碑未进入系统分类");
    Require(classifier.Classify(
            "who am i (尤娜拉) 標記了 敵方莉珊 - (妮可)").Kind
            == GameMessageKind.TacticalSignal,
        "台服标记目标句未进入战术分类");
    Require(classifier.Classify("08-23 22:54 負 1 / 9 / 16").Kind == GameMessageKind.Unknown,
        "战绩日期和 KDA 被时间冒号误判为玩家聊天");
    Require(classifier.Classify("Luori (亞歷斯塔) 凱莎 海妖殺手").Kind == GameMessageKind.TacticalSignal,
        "缺少冒号的台服装备标记仍被当成未知或玩家聊天");
    Require(classifier.Classify("천수아비 (費德提克) 要求隊友雷速支援").Kind == GameMessageKind.TacticalSignal,
        "尽速支援的 OCR 近形没有进入战术分类");
    Require(classifier.Classify("처수아비 (費德提克)野野任務完成!").Kind == GameMessageKind.OtherSystemEvent,
        "打野任务 OCR 近形没有进入系统分类");
    Require(classifier.Classify(
            "MiloBramble (烏迪爾) 已達到已擊殺的史詩野怪的新里程").Kind
            == GameMessageKind.OtherSystemEvent,
        "截断的史诗野怪里程碑没有进入系统分类");
    Require(classifier.Classify(
            "MiloBramble (烏迪爾) 已達到已擊殺的史詩野徑的新里雅").Kind
            == GameMessageKind.OtherSystemEvent,
        "史诗野怪里程碑近形字没有进入系统分类");
    Require(classifier.Classify("한수아비 (費德提克) 發出").Kind == GameMessageKind.Unknown,
        "截断的发出信号残句不应被当成玩家聊天");
    Require(NonPlayerContentGate.IsBlockedPlayerPayload("08-23 22:54 負 1 / 9 / 16"),
        "战绩 KDA 没有在翻译前闸门被拦截");
    Require(NonPlayerContentGate.IsBlockedPlayerPayload("伊瑞莉雅已使用閃現"),
        "召唤师技能 HUD 没有在翻译前闸门被拦截");
    Require(!NonPlayerContentGate.IsBlockedPlayerPayload("미드 밀고 갈게"),
        "真正的队友聊天被翻译前闸门误伤");
    Require(VisionFramePreparer.ChooseScale(498, 214) == 1,
        "Vision 不应再把小聊天框放大后上传");
    Require(VisionFramePreparer.ChooseScale(1280, 420) == 1,
        "已经足够大的框选被错误放大");
    Require(!VisionDispatchPolicy.NeedsVision(new[]
        {
            classifier.Classify("Ally (疾风剑豪): 离群之刺使用了闪现")
        }),
        "本地已能确定的闪现 HUD 仍要求上传 Vision");
    Require(VisionDispatchPolicy.NeedsVision(new[]
        {
            classifier.Classify("Ally (아리): 미드 밀고 갈게")
        }),
        "真正的队友聊天没有送 Vision");
    Require(VisionDispatchPolicy.PublishImmediately(
            classifier.Classify("Ally (疾风剑豪)购买了控制守卫")),
        "买眼 HUD 没有在本地立即发布");
    Require(!VisionDispatchPolicy.PublishImmediately(
            classifier.Classify("Ally (아리): 미드 밀고 갈게")),
        "队友聊天被本地抢先发布");

    var limiter = new TacticalSpeechLimiter();
    var assist = classifier.Classify("Luon (亞歷斯塔) 要求隊友盡速支援");
    var now = DateTimeOffset.UtcNow;
    Require(limiter.CanSpeak(assist, now), "首次支援信号被节流");
    limiter.MarkSpoken(assist, now);
    Require(!limiter.CanSpeak(assist, now.AddSeconds(2)), "同一人的重复支援信号没有冷却");
    var retreat = classifier.Classify("Luon (亞歷斯塔) 發出信號請求撤退");
    Require(limiter.CanSpeak(retreat, now.AddSeconds(2)), "不同信号类型被错误地一起静音");

    Require(classifier.Classify("Ally (疾风剑豪): 离群之刺使用了闪现").Kind == GameMessageKind.TacticalSignal,
        "简体使用了闪现 HUD 被当成玩家聊天");
    Require(classifier.Classify("Ally (疾风剑豪): 祖安花火使用了虚弱").Kind == GameMessageKind.TacticalSignal,
        "简体使用了虚弱 HUD 被当成玩家聊天");
    Require(classifier.Classify("Ally (疾风剑豪): 祖安花火使用了R - 超限爆闪").Kind == GameMessageKind.TacticalSignal,
        "简体使用了R HUD 被当成玩家聊天");
    Require(classifier.Classify("Ally (疾风剑豪): 723金").Kind == GameMessageKind.TacticalSignal,
        "金币短句没有进入 HUD 分类");
    Require(classifier.Classify("Ally (狂暴之心): 符文罗盘 - 0层充能").Kind == GameMessageKind.TacticalSignal,
        "装备层数充能没有进入 HUD 分类");
    Require(classifier.Classify("Ally (疾风剑豪):发出信号,请谨慎行事!").Kind == GameMessageKind.TacticalSignal,
        "英文逗号的谨慎信号没有进入战术分类");
    Require(classifier.Classify("Ally (疾风剑豪)购买了控制守卫").Kind == GameMessageKind.TacticalSignal,
        "简体购买了控制守卫没有进入 HUD 分类");
    Require(classifier.Classify("Ally (疾风剑豪)正在防守外防御塔 (镀层 4/5)").Kind == GameMessageKind.TacticalSignal,
        "简体防守镀层塔没有进入战术分类");
    Require(classifier.Classify("Ally (疾风剑豪)已将目标定为外防御塔 (镀层 2/5)").Kind == GameMessageKind.TacticalSignal,
        "简体目标定为防御塔没有进入战术分类");
    Require(classifier.Classify("Ally (疾风剑豪)示意元素女皇 - (元素女皇)往后撤。").Kind == GameMessageKind.TacticalSignal,
        "简体示意后撤没有进入战术分类");
    Require(classifier.Classify("Ally (럼블): 이렐리아 순간이동").Kind == GameMessageKind.TacticalSignal,
        "韩文传送标记没有进入 HUD 分类");
    Require(classifier.Classify("Ally (铁血狼母)刚刚拿到了第一滴血!").Kind == GameMessageKind.KillEvent,
        "简体刚刚拿到了第一滴血没有进入击杀分类");
    Require(classifier.Classify("Ally (疾风剑豪)击杀了Kars (离群之刺)完成了一次双杀!").Kind == GameMessageKind.KillEvent,
        "简体完成了一次双杀没有进入击杀分类");
    Require(classifier.Classify("[队伍] 玩家甲 (阿狸)：上路支援一下").Kind == GameMessageKind.PlayerChat,
        "简体 HUD 扩展后误伤了真正的玩家聊天");

    Require(classifier.Classify("Player One (Ahri): help 上路 조심", playerPalette).Kind == GameMessageKind.PlayerChat,
        "韩中英混排玩家聊天被过滤");
    Require(classifier.Classify("[队伍] 玩家甲 (阿狸)：上路支援一下").Kind == GameMessageKind.PlayerChat,
        "中文玩家聊天结构未识别");
    Require(classifier.Classify("[All] PlayerTwo (Leona): careful bot").Kind == GameMessageKind.PlayerChat,
        "英文玩家聊天结构未识别");
    Require(classifier.Classify("[팀] PlayerThree: 챔피언 선택 중").Kind == GameMessageKind.PlayerChat,
        "选人阶段明确频道聊天未识别");

    // The same cyan prefix is not enough to turn a system line into player chat.
    Require(classifier.Classify("PlayerOne#KR1: help top", playerPalette).Kind == GameMessageKind.PlayerChat,
        "颜色辅助未恢复 OCR 丢失括号的玩家聊天");
    var damagedTimestamp = classifier.Classify(
        "12:34] [팀] PlayerOne#KR1 (아리): 미드 밀고 갈게",
        playerPalette);
    Require(damagedTimestamp.Kind == GameMessageKind.PlayerChat
        && damagedTimestamp.SpeakerKey == "PLAYERONE#KR1"
        && damagedTimestamp.Content == "미드 밀고 갈게",
        "损坏时间戳中的冒号仍被当成玩家正文分隔符");
    Require(classifier.Classify("12:34] unreadable fragment", playerPalette).Kind == GameMessageKind.Unknown,
        "无法安全恢复的时间型冒号被颜色规则误当作玩家分隔符");
    Require(classifier.Classify("알림: 서버 연결 상태가 불안정합니다", playerPalette).Kind == GameMessageKind.OtherSystemEvent,
        "带相同青色前缀的系统通知冒充了玩家聊天");
    Require(classifier.Classify("unknown prefix: no colour evidence").Kind == GameMessageKind.Unknown,
        "无频道、英雄名或颜色证据的冒号文本不应冒充玩家聊天");
    Require(!systemPalette.SupportsPlayerChat && systemPalette.SupportsSystemEvent,
        "系统强调色证据被错误解释为玩家聊天配色");

    Require(classifier.Classify("레오나님이 위험 신호를 보냄").Kind == GameMessageKind.TacticalSignal,
        "韩文战术信号分类失败");
    Require(classifier.Classify("아리가 가렌을 처치했습니다").Kind == GameMessageKind.KillEvent,
        "韩文击杀播报分类失败");
    Require(classifier.Classify("내셔 남작이 30초 후 생성됩니다").Kind == GameMessageKind.ObjectiveEvent,
        "中立资源刷新提醒分类失败");
    Require(classifier.Classify("已连续游戏 2 小时，请注意休息").Kind == GameMessageKind.ClientReminder,
        "防沉迷提醒分类失败");
    Require(classifier.Classify("敌方摧毁了一座防御塔").Kind == GameMessageKind.ObjectiveEvent,
        "建筑事件没有归入地图资源与建筑");
    Require(classifier.Classify("와드가 만료되었습니다").Kind == GameMessageKind.Unknown,
        "未知文字不应伪装成玩家聊天或已知系统类别");
    return Task.CompletedTask;
});

await CheckAsync("设置保存到最终 TTS 闸门集成", async () =>
{
    var testRoot = Path.Combine(Path.GetTempPath(), "RiftLens-PolicyGate-" + Guid.NewGuid().ToString("N"));
    var settingsPath = Path.Combine(testRoot, "settings.json");
    try
    {
        var store = new SettingsStore(settingsPath);
        var enabled = new AppSettings { KillEventEnabled = true };
        await store.SaveAsync(enabled);
        var settingsAtOcr = await store.LoadAsync();

        var ocr = OcrDocument.FromText("TRIPLE KILL!");
        var classifier = new ChatMessageClassifier();
        var queue = Channel.CreateBounded<ClassifiedGameMessage>(1);
        var classified = classifier.Classify(ocr.Lines.Single().Text);
        await queue.Writer.WriteAsync(classified);
        var queued = await queue.Reader.ReadAsync();
        var result = SystemMessageResultFactory.Create(queued, settingsAtOcr);
        Require(FinalSpeechPolicy.ShouldSpeak(result, settingsAtOcr),
            "开启击杀播报后，合法三杀消息没有通过最终闸门");

        settingsAtOcr.KillEventEnabled = false;
        await store.SaveAsync(settingsAtOcr);
        var latestSettings = await store.LoadAsync();
        Require(!FinalSpeechPolicy.ShouldSpeak(result, latestSettings),
            "分类后关闭击杀播报，队列中的旧消息仍能绕过最终 TTS 闸门");

        var unknown = classifier.Classify("男爵刷新了吗");
        await queue.Writer.WriteAsync(unknown);
        var unknownResult = SystemMessageResultFactory.Create(await queue.Reader.ReadAsync(), latestSettings);
        Require(unknownResult.SourceKind == TranslationSourceKind.Unknown
            && unknownResult.DisplayTranslation == "男爵刷新了吗"
            && !FinalSpeechPolicy.ShouldSpeak(unknownResult, latestSettings),
            "Unknown 没有保持只显示、不播报");
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
});

await CheckAsync("用户主动导出的脱敏诊断记录", async () =>
{
    var recorder = new DiagnosticSessionRecorder();
    const string raw = "[12:34] [팀] SecretPlayer#KR1 (아리): 미드 조심";
    var timestamp = GameTimestampPrefixParser.Parse(raw);
    var line = new DeduplicatedMessageLine(raw, timestamp.TextWithoutTimestamp, timestamp);
    var message = new ChatMessageClassifier().Classify(line.NormalizedText) with
    {
        CapturedAt = DateTimeOffset.UtcNow
    };
    recorder.RecordClassification(new MessageDiagnosticObservation(
        DateTimeOffset.UtcNow,
        line,
        message,
        CategoryEnabled: true));
    recorder.RecordOutput(new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        RoutedMessageKind = GameMessageKind.PlayerChat,
        Intent = "warning",
        SourceType = "player_chat",
        SpeechAct = "warning",
        ClassificationRuleId = "player.structure",
        DisplayLines = new List<string> { "中路小心" },
        OriginalLines = new List<string> { "미드 조심" },
        SpokenSummary = "中路小心",
        ShouldSpeak = true
    }, finalSpeechAllowed: true);

    var output = Path.Combine(Path.GetTempPath(), "RiftLens-Diagnostic-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        await recorder.ExportAsync(output);
        var json = await File.ReadAllTextAsync(output);
        Require(json.Contains("Player-01", StringComparison.Ordinal), "诊断导出没有使用会话玩家别名");
        Require(!json.Contains("SecretPlayer", StringComparison.Ordinal), "诊断导出泄露原始玩家 ID");
        Require(!json.Contains("\"apiKey\"", StringComparison.OrdinalIgnoreCase), "诊断导出包含 API Key 字段");
        Require(json.Contains("timestamp.bracketed", StringComparison.Ordinal)
            && json.Contains("player_authored_chat", StringComparison.Ordinal),
            "诊断导出缺少时间戳证据或来源归属");
    }
    finally
    {
        if (File.Exists(output)) File.Delete(output);
    }
});

await CheckAsync("用户主动开启的真实评估会话", async () =>
{
    var testRoot = Path.Combine(
        Path.GetTempPath(),
        "RiftLens-RealEvaluation-" + Guid.NewGuid().ToString("N"));
    try
    {
        var settings = new AppSettings
        {
            CaptureRegion = new ScreenRegion { X = 40, Y = 50, Width = 320, Height = 96 },
            PollIntervalMs = 500,
            OcrScale = 3,
            ApiModel = "test-model"
        };
        var recorder = await RealEvaluationSessionRecorder.StartAsync(
            testRoot,
            settings,
            "self-test");
        using var source = new Bitmap(320, 96);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.FromArgb(24, 31, 39));
            graphics.DrawString(
                "[12:34] Player (아리): 미드 조심",
                SystemFonts.DefaultFont,
                Brushes.White,
                6,
                20);
        }
        recorder.RecordFrame(new EvaluationFrameObservation(
            "frame-000001",
            DateTimeOffset.UtcNow,
            "ocr_stable_frame",
            true,
            (Bitmap)source.Clone(),
            OcrDocument.FromText("[12:34] Player (아리): 미드 조심"),
            123.4,
            129.8,
            500,
            3));

        const string raw = "[12:34] Player (아리): 미드 조심";
        var timestamp = GameTimestampPrefixParser.Parse(raw);
        var line = new DeduplicatedMessageLine(raw, timestamp.TextWithoutTimestamp, timestamp);
        var message = new ChatMessageClassifier().Classify(line.NormalizedText) with
        {
            CapturedAt = DateTimeOffset.UtcNow,
            EvaluationLineId = "frame-000001/line-01"
        };
        recorder.RecordClassification(new MessageDiagnosticObservation(
            DateTimeOffset.UtcNow,
            line,
            message,
            CategoryEnabled: true,
            FrameId: "frame-000001",
            LineId: "frame-000001/line-01"));
        recorder.RecordOutput(new TranslationResult
        {
            SourceKind = TranslationSourceKind.PlayerChat,
            RoutedMessageKind = GameMessageKind.PlayerChat,
            EvaluationLineIds = new List<string> { "frame-000001/line-01" },
            OriginalLines = new List<string> { "미드 조심" },
            DisplayLines = new List<string> { "中路小心" },
            SpokenSummary = "中路小心",
            ShouldSpeak = true
        }, finalSpeechAllowed: true);
        var sessionDirectory = recorder.SessionDirectory;
        await recorder.CompleteAsync();

        var manifest = await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "manifest.json"));
        var events = await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "events.jsonl"));
        Require(File.Exists(Path.Combine(sessionDirectory, "frames", "frame-000001.png")),
            "真实评估会话没有保存框选区域帧");
        Require(manifest.Contains("\"status\": \"completed\"", StringComparison.Ordinal)
            && manifest.Contains("\"containsOriginalCroppedScreenshots\": true", StringComparison.Ordinal)
            && manifest.Contains("\"containsApiKeyOrCredentials\": false", StringComparison.Ordinal),
            "真实评估清单缺少完成状态或隐私声明");
        Require(events.Contains("\"eventType\":\"frame\"", StringComparison.Ordinal)
            && events.Contains("\"eventType\":\"classification\"", StringComparison.Ordinal)
            && events.Contains("\"eventType\":\"output\"", StringComparison.Ordinal)
            && events.Contains("frame-000001/line-01", StringComparison.Ordinal),
            "真实评估事件没有完整关联帧、分类和实际输出");
        Require(!events.Contains("apiKey", StringComparison.OrdinalIgnoreCase),
            "真实评估事件意外包含 API Key 字段");
    }
    finally
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
    }
});

await CheckAsync("结构化 Ping 与 HUD 语义", () =>
{
    var classifier = new ChatMessageClassifier();

    var cooldown = classifier.Classify("[팀] PlayerOne (아리): 점멸 - 20s");
    Require(cooldown.Kind == GameMessageKind.TacticalSignal,
        "带玩家前缀的 HUD 冷却分享仍被当作普通聊天");
    Require(cooldown.SemanticEvent is
        {
            SpeechAct: SemanticSpeechAct.Countdown,
            DurationSeconds: 20,
            Status: "cooldown"
        }, "技能倒计时没有形成可核验的结构化事件");
    var cooldownSpeech = GameSemanticEventParser.Format(
        cooldown.SemanticEvent!,
        "阿狸",
        AppLanguage.Chinese);
    Require(cooldownSpeech == "闪现还有20秒"
        && !cooldownSpeech.Contains("负20", StringComparison.Ordinal),
        "HUD 分隔横杠仍被解释为负数");

    var alive = classifier.Classify("[팀] PlayerOne (아리): Yasuo - Alive");
    Require(alive.Kind == GameMessageKind.TacticalSignal
        && alive.SemanticEvent?.SpeechAct == SemanticSpeechAct.Status
        && alive.SemanticEvent.Status == "alive",
        "敌方存活状态分享没有进入状态事件");
    var aliveSpeech = GameSemanticEventParser.Format(
        alive.SemanticEvent!,
        "阿狸",
        AppLanguage.Chinese);
    Require(aliveSpeech.EndsWith("仍然存活", StringComparison.Ordinal)
        && !aliveSpeech.Contains("提醒", StringComparison.Ordinal)
        && !aliveSpeech.Contains("正在", StringComparison.Ordinal),
        "存活状态被误说成正在执行的行动");

    var unattributedRetreat = classifier.Classify("信号：撤退");
    Require(unattributedRetreat.Kind == GameMessageKind.TacticalSignal
        && unattributedRetreat.SemanticEvent is not null,
        "无发起人的撤退信号未进入结构化信号分类");
    Require(GameSemanticEventParser.Format(
            unattributedRetreat.SemanticEvent!,
            string.Empty,
            AppLanguage.Chinese) == "收到撤退信号",
        "无发起人的系统信号仍冒充队友发言");

    var marker = classifier.Classify("[팀] PlayerOne (아리): 신호: 드래곤");
    Require(marker.Kind == GameMessageKind.TacticalSignal
        && marker.SemanticEvent?.SpeechAct == SemanticSpeechAct.Marker,
        "带玩家前缀的目标标记未从自由聊天中提前分流");
    Require(GameSemanticEventParser.Format(marker.SemanticEvent!, "阿狸", AppLanguage.Chinese)
        == "阿狸标记了小龙", "目标标记被错误扩写为正在争夺目标");

    var generatedOnMyWay = classifier.Classify("24:03OnPi(사일러스)님이 가고 있음");
    Require(generatedOnMyWay.Kind == GameMessageKind.TacticalSignal
        && generatedOnMyWay.SpeakerKey == "ONPI"
        && generatedOnMyWay.SpeakerHeroRaw == "사일러스"
        && generatedOnMyWay.SemanticEvent?.Status == "on_my_way",
        "真实韩服正在路上固定句仍被当作玩家自由聊天");
    Require(GameSemanticEventParser.Format(
            generatedOnMyWay.SemanticEvent!,
            "塞拉斯",
            AppLanguage.Chinese) == "塞拉斯发出正在路上的信号",
        "正在路上信号仍被扩写成玩家正在执行动作");

    var spellMarker = classifier.Classify("[팀] PlayerOne (사일러스): 오리아나점멸");
    var spellMarkerSpeech = spellMarker.SemanticEvent is null
        ? string.Empty
        : GameSemanticEventParser.Format(spellMarker.SemanticEvent, "塞拉斯", AppLanguage.Chinese);
    Require(spellMarker.Kind == GameMessageKind.TacticalSignal
        && spellMarker.SemanticEvent is { SpeechAct: SemanticSpeechAct.Marker, Status: "summoner_spell" }
        && spellMarkerSpeech.StartsWith("塞拉斯标记了", StringComparison.Ordinal)
        && spellMarkerSpeech.EndsWith("的闪现", StringComparison.Ordinal),
        $"召唤师技能 HUD 分享没有以保守标记语气处理：{spellMarker.Kind}/{spellMarker.SemanticEvent}/{spellMarkerSpeech}");

    var abilityMarker = classifier.Classify("[팀] PlayerOne (요네): 아칼리 R");
    var abilitySpeech = GameSemanticEventParser.Format(
        abilityMarker.SemanticEvent!,
        "永恩",
        AppLanguage.Chinese);
    Require(abilityMarker.Kind == GameMessageKind.TacticalSignal
        && abilityMarker.SemanticEvent is { SpeechAct: SemanticSpeechAct.Marker, Status: "ability" }
        && !abilitySpeech.Contains("冷却", StringComparison.Ordinal)
        && !abilitySpeech.Contains("已交", StringComparison.Ordinal),
        "只有英雄名加 R 时仍推断了冷却或已使用状态");

    var manaShare = classifier.Classify("[팀] PlayerOne (루시안): 마나24%");
    Require(manaShare.Kind == GameMessageKind.TacticalSignal
        && GameSemanticEventParser.Format(manaShare.SemanticEvent!, "卢锡安", AppLanguage.Chinese)
            == "卢锡安蓝量24%",
        "蓝量百分比 HUD 分享未进入本地确定性语义");

    var itemNeed = classifier.Classify("[팀] PlayerOne (요네): 몰락한왕의검-91골드필요");
    Require(itemNeed.Kind == GameMessageKind.TacticalSignal
        && GameSemanticEventParser.Format(itemNeed.SemanticEvent!, "永恩", AppLanguage.Chinese)
            .Contains("91金币", StringComparison.Ordinal),
        "装备金币需求仍被发送给自由聊天 AI");

    var objectiveTimer = classifier.Classify("[팀] PlayerOne (바드): 내셔남작-5:22후재생성");
    Require(objectiveTimer.Kind == GameMessageKind.TacticalSignal
        && objectiveTimer.SemanticEvent is { Status: "objective_respawn", DurationSeconds: 322 }
        && GameSemanticEventParser.Format(objectiveTimer.SemanticEvent, "巴德", AppLanguage.Chinese)
            .Contains("5分22秒刷新", StringComparison.Ordinal),
        "目标刷新倒计时没有保留分钟和秒");

    var failClosed = new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        RoutedMessageKind = GameMessageKind.PlayerChat,
        ShouldSpeak = true,
        SpokenSummary = "永恩说，阿卡丽大招冷却中",
        OriginalLines = new List<string> { "아칼리 R" }
    };
    Require(!FinalSpeechPolicy.ShouldSpeak(
            failClosed,
            new AppSettings { TacticalSignalEnabled = true }),
        "误入玩家路径的固定 HUD 内容仍能绕过最终语音闸门");

    Require(classifier.Classify("[팀] PlayerOne (아리): 小龙等我").Kind == GameMessageKind.PlayerChat,
        "普通玩家战术聊天被宽泛规则误判为系统 Ping");
    Require(classifier.Classify("[팀] PlayerOne (아리): 후퇴 후 드래곤 가자").Kind == GameMessageKind.PlayerChat,
        "包含撤退词的完整玩家计划被宽泛规则误判为系统 Ping");

    var flashOcr = classifier.Classify("[팀] PlayerOne (제이스): 바루스정멸");
    Require(flashOcr.Kind == GameMessageKind.TacticalSignal
        && flashOcr.SemanticEvent is { Status: "summoner_spell" },
        "闪现 OCR 近形字仍被当成玩家聊天");
    Require(GameSemanticEventParser.Format(flashOcr.SemanticEvent!, "杰斯", AppLanguage.Chinese)
            .Contains("闪现", StringComparison.Ordinal),
        "闪现 OCR 近形字没有规范化为闪现");

    var ireliaFlashOcr = classifier.Classify("[팀] lingyo (나피리): 아델리아 정렬");
    Require(ireliaFlashOcr.Kind == GameMessageKind.TacticalSignal
        && ireliaFlashOcr.SemanticEvent is { Status: "summoner_spell" },
        "英雄名加闪现 OCR 乱码仍进入玩家翻译");

    var windDrake = classifier.Classify("[팀] 스카너 (스카너): 바람의 드레이크 - 1:08 후 재생성");
    Require(windDrake.Kind == GameMessageKind.TacticalSignal
        && windDrake.SemanticEvent is { Status: "objective_respawn", DurationSeconds: 68 },
        "드레이크 刷新 HUD 没有进入目标刷新分类");

    var damagedWard = classifier.Classify("lingyo (나피리): 님이 져야 와드 아이템을 구입했습니다.");
    Require(damagedWard.Kind == GameMessageKind.TacticalSignal
        && damagedWard.SemanticEvent is { Status: "ward_purchased" },
        "控制守卫购买 OCR 近形字仍被当成玩家聊天");
    return Task.CompletedTask;
});

await CheckAsync("玩家闪现口语与装备栏点选", async () =>
{
    var dictionary = await HeroDictionary.LoadAsync();
    var classifier = new ChatMessageClassifier();

    Require(
        PlayerCalloutParser.TryFormat("1213mid", AppLanguage.Chinese, dictionary, true, out var midFlash)
            == PlayerCalloutKind.Formatted
        && midFlash == "中路12分13秒交了闪现",
        "1213mid 没有本地解读为中路闪现时间：" + midFlash);
    Require(
        PlayerCalloutParser.TryFormat("933 sup", AppLanguage.Chinese, dictionary, true, out var supportFlash)
            == PlayerCalloutKind.Formatted
        && supportFlash == "辅助9分33秒交了闪现",
        "933 sup 没有本地解读为辅助闪现时间：" + supportFlash);
    Require(
        PlayerCalloutParser.TryFormat("mid f on", AppLanguage.Chinese, dictionary, true, out var midFlashOn)
            == PlayerCalloutKind.Formatted
        && midFlashOn == "中路还有闪现",
        "mid f on 没有本地解读为中路还有闪现：" + midFlashOn);
    Require(
        PlayerCalloutParser.TryFormat("mid f off", AppLanguage.Chinese, dictionary, true, out var midFlashOff)
            == PlayerCalloutKind.Formatted
        && midFlashOff == "中路没有闪现",
        "mid f off 没有本地解读为中路没有闪现：" + midFlashOff);
    Require(
        PlayerCalloutParser.TryFormat("1630SUP 17TOP 1710MID", AppLanguage.Chinese, dictionary, true, out var packed)
            == PlayerCalloutKind.Formatted
        && packed.Contains("辅助16分30秒交了闪现", StringComparison.Ordinal)
        && packed.Contains("上单17分交了闪现", StringComparison.Ordinal)
        && packed.Contains("中路17分10秒交了闪现", StringComparison.Ordinal),
        "连写闪现时间没有拆开播报：" + packed);
    Require(
        PlayerCalloutParser.TryFormat("1630SUP17T0P1710MID", AppLanguage.Chinese, dictionary, true, out var smashedTimes)
            == PlayerCalloutKind.Formatted
        && smashedTimes.Contains("上单17分交了闪现", StringComparison.Ordinal),
        "无空格且 T0P OCR 的闪现时间没有解读：" + smashedTimes);

    var itemClick = classifier.Classify("[팀] lingyo (나피리): 이렐리아-주문포식자");
    Require(itemClick.Kind == GameMessageKind.PlayerChat
        && itemClick.SemanticEvent is { Status: "item_marker" },
        "点选对面装备栏没有留在玩家播报路径");
    Require(
        PlayerCalloutParser.TryFormat("이렐리아-주문포식자", AppLanguage.Chinese, dictionary, true, out var itemSpeech)
            == PlayerCalloutKind.Formatted
        && itemSpeech.Contains("买了", StringComparison.Ordinal)
        && itemSpeech.Contains("海克斯饮魔刀", StringComparison.Ordinal)
        && itemSpeech.Contains("艾瑞莉娅", StringComparison.Ordinal),
        "装备栏点选没有播报成买了官方装备名：" + itemSpeech);
    Require(
        PlayerCalloutParser.TryFormat("이텔리아주문포식자", AppLanguage.Chinese, dictionary, true, out var smashedItem)
            == PlayerCalloutKind.Formatted
        && smashedItem.Contains("买了海克斯饮魔刀", StringComparison.Ordinal),
        "无分隔符的英雄+装备 OCR 没有解读：" + smashedItem);
    Require(
        PlayerCalloutParser.TryFormat("이텔리아주문포심자", AppLanguage.Chinese, dictionary, true, out var damagedItem)
            == PlayerCalloutKind.Formatted
        && damagedItem.Contains("海克斯饮魔刀", StringComparison.Ordinal),
        "装备名近形字没有纠正：" + damagedItem);

    Require(
        PlayerCalloutParser.TryFormat("미드 밀고 갈게", AppLanguage.Chinese, dictionary, true, out _)
            == PlayerCalloutKind.PassThrough,
        "普通韩文计划被闪现口语规则误伤");
    Require(
        PlayerCalloutParser.TryFormat("미템리아주문포심자", AppLanguage.Chinese, dictionary, true, out _)
            == PlayerCalloutKind.Silence,
        "无法安全还原的英雄+装备乱码仍送给翻译模型");

    var itemSpeechResult = new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        RoutedMessageKind = GameMessageKind.PlayerChat,
        ShouldSpeak = true,
        SpokenSummary = "纳亚菲利说，艾瑞莉娅买了海克斯饮魔刀",
        OriginalLines = new List<string> { "艾瑞莉娅买了海克斯饮魔刀" },
        Intent = "assist",
        Priority = 2
    };
    Require(FinalSpeechPolicy.ShouldSpeak(
            itemSpeechResult,
            new AppSettings { SpeakImportantPlayerChat = true, TacticalSignalEnabled = false }),
        "装备栏点选在关闭战术信号后仍不能播报");
});

await CheckAsync("按发言人聚合播报轮次", () =>
{
    var classifier = new ChatMessageClassifier();
    var turns = new PlayerTurnAggregator();
    var first = classifier.Classify("[팀] 아리: 미드 밀고 갈게", SystemMessageFilterMode.Balanced) with { CapturedAt = DateTimeOffset.UtcNow };
    var second = classifier.Classify("[팀] 아리: 바텀 조심해", SystemMessageFilterMode.Balanced) with { CapturedAt = DateTimeOffset.UtcNow };
    var changed = classifier.Classify("[팀] 레오나: 탑 합류해 줘", SystemMessageFilterMode.Balanced) with { CapturedAt = DateTimeOffset.UtcNow };
    Require(turns.Add(first) is null, "首句不应立刻结算");
    Require(turns.Add(second) is null, "同一玩家连续发言被提前拆分");
    var completed = turns.Add(changed);
    Require(completed is not null, "换人发言时没有结算上一位玩家");
    Require(completed!.Contents.SequenceEqual(new[] { "미드 밀고 갈게", "바텀 조심해" }), "上一位玩家信息没有完整聚合");
    var last = turns.Flush();
    Require(last is not null && last.SpeakerRaw == "레오나", "新发言人的轮次没有保留");

    var noisyTurns = new PlayerTurnAggregator();
    var cleanName = classifier.Classify("[팀] PlayerOne: first", SystemMessageFilterMode.Balanced) with { CapturedAt = DateTimeOffset.UtcNow };
    var noisyName = classifier.Classify("[팀] Player0ne: second", SystemMessageFilterMode.Balanced) with { CapturedAt = DateTimeOffset.UtcNow };
    Require(noisyTurns.Add(cleanName) is null && noisyTurns.Add(noisyName) is null,
        "昵称一个字符的 OCR 波动制造了虚假换人");
    return Task.CompletedTask;
});

await CheckAsync("本局玩家英雄多数锁定", async () =>
{
    var dictionary = await HeroDictionary.LoadAsync();
    var classifier = new ChatMessageClassifier();
    var tracker = new PlayerHeroSessionTracker(dictionary);

    var invalid = tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (贾尔班四世): first"));
    Require(string.IsNullOrEmpty(invalid.SpeakerHeroId), "非官方 OCR 英雄名获得了投票");

    var firstValid = tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (자르반4세): second"));
    Require(firstValid.SpeakerHeroId == "JarvanIV", "当前有效英雄名没有立即使用");

    var secondValid = tracker.Observe(classifier.Classify(
        "[팀] Player0ne#KR1 (자르반 4 세): third"));
    Require(secondValid.SpeakerKey == "PLAYERONE#KR1", "玩家 ID 单字符 OCR 波动未归并");

    var thirdValid = tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (자르반 IV): fourth"));
    Require(thirdValid.SpeakerHeroId == "JarvanIV", "三次有效多数后没有锁定嘉文四世");

    var conflictingOfficial = tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (아리): fifth"));
    Require(conflictingOfficial.SpeakerHeroId == "JarvanIV", "锁定后被偶发的其他官方英雄名覆盖");
    Require(conflictingOfficial.SpeakerRaw.Contains("자르반 4세", StringComparison.Ordinal),
        "锁定后没有恢复官方韩文英雄名");

    var turns = new PlayerTurnAggregator();
    tracker.Reset();
    Require(turns.Add(tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (贾尔班四世): one"))) is null, "首句不应结算");
    Require(turns.Add(tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (자르반 4세): two"))) is null, "同玩家第二句不应结算");
    var updatedTurn = turns.Flush();
    Require(updatedTurn?.SpeakerHeroId == "JarvanIV", "同一轮次后续正确英雄名未更新发言人");

    tracker.Reset();
    var afterReset = tracker.Observe(classifier.Classify(
        "[팀] PlayerOne#KR1 (贾尔班四世): new game"));
    Require(string.IsNullOrEmpty(afterReset.SpeakerHeroId), "重新监听后沿用了上一局英雄锁定");
});

await CheckAsync("信号与系统消息边界", () =>
{
    var classifier = new ChatMessageClassifier();
    const string retreatSignal = "\uC2E0\uD638: \uD6C4\uD1F4";
    const string surrender = "\u53D1\u8D77\u4E86\u6295\u964D";
    const string restReminder = "\u5DF2\u8FDE\u7EED\u6E38\u620F 2 \u5C0F\u65F6\uFF0C\u8BF7\u6CE8\u610F\u4F11\u606F";

    Require(SignalMessageTemplates.IsSignalMessage(retreatSignal), "固定信号识别失败");
    Require(!classifier.IsPlayerMessage(retreatSignal, SystemMessageFilterMode.Strict), "关闭信号播报时，固定信号不应绕过严格过滤");
    Require(classifier.IsPlayerMessage(retreatSignal, SystemMessageFilterMode.Strict, true), "开启信号播报后，固定信号未被保留");
    Require(!classifier.IsPlayerMessage(surrender, SystemMessageFilterMode.Balanced, true), "投降系统消息被信号规则误放行");
    Require(!classifier.IsPlayerMessage(restReminder, SystemMessageFilterMode.Balanced, true), "健康提醒系统消息被信号规则误放行");
    Require(!SignalMessageTemplates.IsSignalMessage(surrender), "投降消息不应被认定为固定信号");
    Require(!SignalMessageTemplates.IsSignalMessage("gg jg diff"), "普通玩家缩写不应被认定为固定信号");
    Require(SignalMessageTemplates.TryTranslateSignal(retreatSignal) == "\u64A4\u9000\u4FE1\u53F7", "固定信号本地翻译失败");
    Require(classifier.Classify(restReminder).Kind == GameMessageKind.ClientReminder, "健康提醒未进入独立类别");
    Require(classifier.Classify(surrender).Kind == GameMessageKind.OtherSystemEvent, "投降提醒未进入其他系统类别");
    Require(SystemMessageTemplates.Format(
        classifier.Classify("내셔 남작이 30초 후 생성됩니다"),
        AppLanguage.Chinese).Contains("男爵", StringComparison.Ordinal), "中立资源本地播报模板失败");
    return Task.CompletedTask;
});

await CheckAsync("术语表长词优先", () =>
{
    Require(GameTermGlossary.ReplaceTerms("\uC6A9 \uD55C\uD0C0") == "\u9F99\u56E2", "长词应先于短词替换");
    var englishTerms = GameTermGlossary.ReplaceTerms("탑 합류, 바론 시야", AppLanguage.English);
    Require(englishTerms.Contains("top", StringComparison.OrdinalIgnoreCase)
        && englishTerms.Contains("rotate", StringComparison.OrdinalIgnoreCase)
        && englishTerms.Contains("Baron", StringComparison.OrdinalIgnoreCase)
        && englishTerms.Contains("vision", StringComparison.OrdinalIgnoreCase), "英文术语固定映射失败");
    Require(GameTermGlossary.ReplaceTerms("탑합류좀", AppLanguage.English)
        .StartsWith("top rotate ", StringComparison.OrdinalIgnoreCase), "OCR 丢失韩文空格后英文术语粘连");
    Require(GameTermGlossary.Version == "16.14.1", "内置术语表版本错误");
    Require(GameTermGlossary.OfficialTermCount >= 1400, "官方术语表条目不足");
    Require(GameTermGlossary.ReplaceTerms("閃現、點燃、殺手本能、奪命氣息")
            .Contains("闪现、引燃、猎手本能、狂风绝息斩", StringComparison.Ordinal),
        "繁体召唤师技能和英雄技能未映射到简体官方名称");
    Require(!GameTermGlossary.ReplaceTerms("템 뭐가세용").Contains("小龙", StringComparison.Ordinal),
        "韩文礼貌词尾 세용 被单字 용 错误替换成小龙");
    Require(GameTermGlossary.ReplaceTerms("용앞 시야").StartsWith("小龙앞", StringComparison.Ordinal),
        "句首韩文单字 용 的游戏术语映射被边界保护误伤");
    Require(GameTermGlossary.ReplaceTerms("자르반 대격변 조심") == "자르반 天崩地裂 조심",
        "嘉文终极技能韩中官方映射失败");
    Require(GameTermGlossary.ReplaceTerms("Cataclysm 조심") == "天崩地裂 조심",
        "英雄技能英中官方映射失败");
    Require(GameTermGlossary.ReplaceTerms("天崩地裂 조심", AppLanguage.English) == "Cataclysm 조심",
        "英雄技能中英官方映射失败");
    Require(GameTermGlossary.ReplaceTerms("점화, 무한의 대검, 정복자") == "引燃, 无尽之刃, 征服者",
        "召唤师技能、装备或符文韩中映射失败");
    Require(GameTermGlossary.ReplaceTerms("점화, 무한의 대검, 정복자", AppLanguage.English)
        == "Ignite, Infinity Edge, Conqueror", "召唤师技能、装备或符文韩英映射失败");
    return Task.CompletedTask;
});

await CheckAsync("像素变化检测", () =>
{
    var detector = new ImageChangeDetector();
    using var first = new Bitmap(320, 100);
    using (var graphics = Graphics.FromImage(first)) graphics.Clear(Color.Black);
    Require(detector.HasMeaningfulChange(first, 0.03), "首帧应触发");
    Require(!detector.HasMeaningfulChange(first, 0.03), "相同帧不应触发");
    using var second = new Bitmap(320, 100);
    using (var graphics = Graphics.FromImage(second))
    {
        graphics.Clear(Color.Black);
        graphics.FillRectangle(Brushes.White, 0, 0, 180, 60);
    }
    Require(detector.HasMeaningfulChange(second, 0.03), "明显变化未触发");
    return Task.CompletedTask;
});

await CheckAsync("Vision 上传前本地遮挡门控", () =>
{
    using var normal = new Bitmap(320, 140, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
    for (var y = 0; y < normal.Height; y++)
    {
        for (var x = 0; x < normal.Width; x++)
        {
            normal.SetPixel(x, y, Color.FromArgb(
                (x * 3 + y * 5) % 180 + 30,
                (x * 5 + y * 2) % 170 + 25,
                (x * 2 + y * 7) % 160 + 35));
        }
    }
    using (var graphics = Graphics.FromImage(normal))
    {
        graphics.DrawString("Player (Ahri): mid careful", SystemFonts.DefaultFont, Brushes.White, 8, 104);
    }
    Require(!VisionUploadGuard.Evaluate(normal).ShouldBlock,
        "正常有纹理的聊天区域被误判为大面积遮挡");

    using var obscured = (Bitmap)normal.Clone();
    using (var graphics = Graphics.FromImage(obscured))
    {
        graphics.FillRectangle(new SolidBrush(Color.FromArgb(22, 24, 29)), 12, 8, 296, 118);
        graphics.DrawString("OVERLAY", SystemFonts.DefaultFont, Brushes.White, 130, 60);
    }
    var blocked = VisionUploadGuard.Evaluate(obscured);
    Require(blocked.ShouldBlock && blocked.Reason == "large_opaque_obstruction",
        "覆盖大部分框选区域的不透明面板没有在本地拦截");

    using var partial = (Bitmap)normal.Clone();
    using (var graphics = Graphics.FromImage(partial))
    {
        graphics.FillRectangle(new SolidBrush(Color.FromArgb(22, 24, 29)), 0, 0, 80, 140);
    }
    Require(!VisionUploadGuard.Evaluate(partial).ShouldBlock,
        "局部小遮挡不应阻止整帧 Vision 识别");
    return Task.CompletedTask;
});

await CheckAsync("设置持久化", async () =>
{
    var path = Path.Combine(Path.GetTempPath(), "RiftLens-SelfTest-" + Guid.NewGuid().ToString("N"), "settings.json");
    var store = new SettingsStore(path);
    var settings = new AppSettings
    {
        SchemaVersion = 2,
        CaptureRegion = new ScreenRegion { X = 10, Y = 20, Width = 300, Height = 120 },
        OutputMode = OutputMode.VoiceOnly,
        OverlayOpacity = 0.61,
        OverlayDurationMs = 5200,
        SpeechVolume = 42,
        OcrScale = 2
    };
    await store.SaveAsync(settings);
    var legacyJson = await File.ReadAllTextAsync(path);
    legacyJson = System.Text.RegularExpressions.Regex.Replace(
        legacyJson,
        "\\s*\"SchemaVersion\"\\s*:\\s*\\d+\\s*,?",
        string.Empty,
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    await File.WriteAllTextAsync(path, legacyJson);
    var loaded = await store.LoadAsync();
    Require(loaded.CaptureRegion.Width == 300, "区域设置未保存");
    Require(loaded.OutputMode == OutputMode.VoiceOnly, "输出模式未保存");
    Require(loaded.SpeechVolume == 42, "语音音量未保存");
    Require(loaded.OcrScale == 3, "旧版 OCR 放大倍数未迁移到聊天优化值");
    Require(loaded.SchemaVersion == 11, "旧版设置未迁移到视觉识别实验设置版本");
    Require(!loaded.TacticalSignalEnabled
        && !loaded.KillEventEnabled
        && !loaded.ObjectiveEventEnabled
        && !loaded.ClientReminderEnabled
        && !loaded.OtherSystemEventEnabled,
        "旧版设置迁移后可选系统消息未采用安全关闭默认值");
    Require(loaded.SystemMessageFilter == SystemMessageFilterMode.Strict,
        "旧版宽松过滤设置未迁移为结构优先分类");
    Require(loaded.Language == AppLanguage.Chinese, "旧版设置未迁移到默认中文语言");
    Require(loaded.MaxSummaryChars == 64 && loaded.SummaryWindowMs == 900, "玩家轮次摘要默认参数迁移失败");
    Require(loaded.OverlayDurationMs == 7000, "旧版译文展示时长未迁移到 7 秒");
    Require(loaded.SpeakUrgentPlayerChat
        && loaded.SpeakImportantPlayerChat
        && !loaded.SpeakNeutralPlayerChat,
        "旧版设置未迁移为紧急与重点默认播报");
    loaded.Language = AppLanguage.English;
    loaded.ObjectiveEventEnabled = true;
    loaded.SpeakImportantPlayerChat = false;
    loaded.SpeakNeutralPlayerChat = true;
    loaded.OnboardingCompleted = true;
    loaded.InterfacePalette = "mono";
    loaded.CustomThemeColor = "#345678";
    loaded.SaveTranslationHistory = true;
    loaded.RecognitionMode = RecognitionPipelineMode.DeepSeekVisionExperimental;
    loaded.VisionPrivacyConsent = true;
    await store.SaveAsync(loaded);
    var reloaded = await store.LoadAsync();
    Require(reloaded.Language == AppLanguage.English, "英文语言设置未持久化");
    Require(reloaded.ObjectiveEventEnabled
        && reloaded.IsSystemMessageEnabled(GameMessageKind.ObjectiveEvent)
        && !reloaded.IsSystemMessageEnabled(GameMessageKind.KillEvent),
        "独立系统类别开关未持久化或路由错误");
    Require(reloaded.SpeakUrgentPlayerChat
        && !reloaded.SpeakImportantPlayerChat
        && reloaded.SpeakNeutralPlayerChat,
        "玩家三档播报开关未独立持久化");
    Require(reloaded.OnboardingCompleted
        && reloaded.InterfacePalette == "mono"
        && reloaded.CustomThemeColor == "#345678"
        && reloaded.SaveTranslationHistory
        && reloaded.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental
        && reloaded.VisionPrivacyConsent,
        "首次引导、界面配色、本地历史或视觉实验设置未持久化");
});

await CheckAsync("DeepSeek Vision OCR 结构化解析", () =>
{
    var document = DeepSeekVisionOcrService.ParseModelContent(
        "```json\n{\"lines\":[\"[12:34] 레오나: 탑 조심\",\"阿狸: river miss\",\"  \"]}\n```");
    Require(document.Lines.Count == 2, "Vision OCR 空行未过滤");
    Require(document.Lines[0].Text == "[12:34] 레오나: 탑 조심", "韩文与时间戳未原样保留");
    Require(document.Lines[1].Text == "阿狸: river miss", "中英混排未原样保留");
    Require(document.RawText.Contains("레오나", StringComparison.Ordinal)
        && document.RawText.Contains("river miss", StringComparison.Ordinal),
        "Vision OCR 文档未保留三语文本");
    return Task.CompletedTask;
});

await CheckAsync("Vision 混合证据结构修复", async () =>
{
    var dictionary = await HeroDictionary.LoadAsync();
    var processor = new VisionOcrPostProcessor(dictionary);
    var vision = OcrDocument.FromText(
        "[12:34] PlayerOne#KR1 (뽀뽀) 탑 조심\n"
        + "[12:35] PlayerOne#KR1 (뽀뽀) 님이 제어 와드 아이템을\n"
        + "구입했습니다.");
    var local = OcrDocument.FromText(
        "[12:34] PlayerOne#KR1 (뽀삐): 탑 위험\n"
        + "[12:35] PlayerOne#KR1 (뽀삐) 님이 제어 와드 아이템을\n"
        + "구입했습니다.");
    var processed = processor.Process(null, vision, local);
    Require(processed.Document.Lines.Count == 2, "带时间戳的视觉换行未合并");
    Require(processed.Stats.JoinedContinuations == 1, "视觉换行修复统计错误");
    Require(processed.Stats.RepairedHeroTokens >= 1, "本地官方英雄证据未修复 Vision 近形字");
    Require(processed.Stats.RepairedPlayerSeparators == 1, "本地明确分隔符未修复玩家聊天冒号");
    Require(processed.Document.Lines[0].Text.Contains("(뽀삐): 탑 조심", StringComparison.Ordinal),
        "Vision 玩家正文被本地 OCR 覆盖或头部未修复：" + processed.Document.Lines[0].Text);
    Require(processed.Document.Lines[1].Text.Contains("아이템을 구입했습니다", StringComparison.Ordinal),
        "固定消息视觉换行内容丢失");

    var hudShare = processor.Process(
        null,
        OcrDocument.FromText(
            "15:24 키아나 (키아나) 님이 적이 사라졌다고 알림\n"
            + "15:30 키아나 (키아나) 님이 요새파괴자 - 구매 가능"),
        OcrDocument.FromText(
            "15:24 키아나 (키아나) 님이 적이 사라졌다고 알림\n"
            + "15:30 키아나 (키아나): 요새파괴자 - 구매 가능"));
    Require(hudShare.Document.Lines[1].Text.Contains("(키아나): 요새파괴자", StringComparison.Ordinal)
        && !hudShare.Document.Lines[1].Text.Contains(": 님이", StringComparison.Ordinal),
        "HUD 分享修复冒号时没有移除 Vision 伪造的系统主语：" + hudShare.Document.Lines[1].Text);

    var targetWrap = processor.Process(
        null,
        OcrDocument.FromText(
            "12:31 PlayerOne (아칼리) 님이 위험 신호를 보냄\n"
            + "12:36 PlayerOne (아칼리) 님이\n"
            + "EnemyOne (갈리오) 지목"));
    Require(targetWrap.Document.Lines.Count == 2
        && targetWrap.Document.Lines[1].Text.Contains("님이 EnemyOne (갈리오) 지목", StringComparison.Ordinal),
        "看似玩家头部的目标行没有与前一条固定消息合并");

    Require(dictionary.TryCorrectKoreanHeroPrefix("백토르 점멸", out var correctedHero, out _)
        && correctedHero == "빅토르", "唯一近形英雄前缀没有修复");
    Require(GameTermGlossary.RepairKoreanOcrTokens("접멸") == "접멸",
        "有多个近形官方候选时不应强行改单词");
    Require(GameSemanticEventParser.TryParsePlayerFeed("빅토르 점멸", out _),
        "修复后的召唤师技能分享未被固定语义解析器接收");
    var spellShare = processor.Process(
        null,
        OcrDocument.FromText("13:13 SeTab (진): 빈트로 접멸"),
        OcrDocument.FromText("13:13 SeTab (진): 백토르 점멸"));
    Require(spellShare.Document.RawText.Contains("빅토르 점멸", StringComparison.Ordinal),
        "受限 HUD 字段没有使用本地证据和官方词典修复英雄/召唤师技能：" + spellShare.Document.RawText);
    Require(spellShare.Stats.RepairedOfficialTermFields == 1,
        "受限官方术语字段修复没有记录统计");
    var unsupportedTarget = processor.Process(
        null,
        OcrDocument.FromText("13:13 SeTab (진): 빅토르 정밀"),
        OcrDocument.FromText("13:13 SeTab (진): 백토 점멸"));
    Require(unsupportedTarget.Document.RawText.Contains("빅토르 정밀", StringComparison.Ordinal)
        && !unsupportedTarget.Document.RawText.Contains("백토", StringComparison.Ordinal),
        "本地目标英雄没有官方唯一证据时不应覆盖 Vision 正文");

    var traditionalHud = processor.Process(
        null,
        OcrDocument.FromText("아니 (潘森): 夢宿已使用R - 奪命氣息"),
        OcrDocument.FromText("아니 (潘森): 犽宿已使用R - 奪命氣息"));
    Require(traditionalHud.Document.RawText.Contains("亚索", StringComparison.Ordinal)
        && traditionalHud.Document.RawText.Contains("狂风绝息斩", StringComparison.Ordinal)
        && new ChatMessageClassifier().Classify(traditionalHud.Document.RawText).Kind
            == GameMessageKind.TacticalSignal,
        "繁体近形英雄和台服技能没有在受限 HUD 字段中规范化");
});

await CheckAsync("战术信号也参与玩家英雄会话锁定", async () =>
{
    var dictionary = await HeroDictionary.LoadAsync();
    var classifier = new ChatMessageClassifier();
    var tracker = new PlayerHeroSessionTracker(dictionary);
    for (var count = 0; count < 3; count++)
    {
        var signal = classifier.Classify("PlayerOne#KR1 (아리) 님이 위험 신호를 보냄");
        Require(signal.Kind == GameMessageKind.TacticalSignal, "测试信号未进入战术分类");
        tracker.Observe(signal);
    }
    var noisyChat = classifier.Classify("PlayerOne#KR1 (아칼리): 미드 조심");
    var stabilized = tracker.Observe(noisyChat);
    Require(stabilized.SpeakerHeroId == "Ahri", "战术信号证据没有锁定本局玩家英雄");

    var bodyTracker = new PlayerHeroSessionTracker(dictionary);
    for (var count = 0; count < 3; count++)
    {
        bodyTracker.Observe(classifier.Classify("MiloBramble (烏迪爾): 伊瑞莉雅已使用閃現"));
    }
    var damagedSpeaker = bodyTracker.Observe(
        classifier.Classify("MiloBramble (馬迪爾): 伊瑞莉雅已使用點燃"));
    Require(damagedSpeaker.SpeakerHeroId == "Udyr",
        "发言人近形英雄名没有被本局锁定纠正：" + damagedSpeaker.SpeakerHeroId);
    var damagedBody = bodyTracker.Observe(
        classifier.Classify("아니 (潘森): 夢宿已使用R - 奪命氣息"));
    Require(damagedBody.Content.Contains("亚索", StringComparison.Ordinal),
        "HUD 正文里的近形英雄名没有纠正：" + damagedBody.Content);
});

await CheckAsync("DeepSeek Vision OCR 图片请求协议", async () =>
{
    var response = JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new { message = new { content = "{\"lines\":[\"레오나: 탑 조심\"]}" } }
        }
    });
    var handler = new FakeHttpHandler(response);
    using var service = new DeepSeekVisionOcrService("https://api.deepseek.com", "test-key", handler);
    using var bitmap = new Bitmap(96, 32);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.White);
        graphics.DrawString("레오나", SystemFonts.DefaultFont, Brushes.Black, 4, 5);
    }
    var document = await service.RecognizeAsync(bitmap, CancellationToken.None);
    Require(document.RawText == "레오나: 탑 조심", "Vision 图片请求响应未解析");
    Require(handler.RequestBody.Contains(DeepSeekVisionOcrService.ModelName, StringComparison.Ordinal),
        "Vision 请求未使用固定实验模型");
    Require(handler.RequestBody.Contains("data:image/jpeg;base64,", StringComparison.Ordinal),
        "Vision 请求未内联框选区域 JPEG");
    Require(handler.RequestBody.Contains("\"detail\":\"original\"", StringComparison.Ordinal),
        "Vision 请求未保留原始图片细节");
    Require(!handler.RequestBody.Contains("League of Legends", StringComparison.OrdinalIgnoreCase),
        "Vision 原生识别实验不应注入 LOL 语境提示");
});

await CheckAsync("玩家分级播报与隐私化语音", () =>
{
    Require(!NaturalSpeechService.ShouldFallbackToInstalledVoice(true, AppLanguage.Chinese),
        "中文晓晓模式不应回退到其他本机音色");
    Require(NaturalSpeechService.ShouldFallbackToInstalledVoice(false, AppLanguage.Chinese),
        "用户明确关闭神经语音时应允许本机语音");
    Require(NaturalSpeechService.ShouldFallbackToInstalledVoice(true, AppLanguage.English),
        "英文 Jenny 模式应保留原有本机回退");
    var settings = new AppSettings
    {
        SpeakUrgentPlayerChat = true,
        SpeakImportantPlayerChat = true,
        SpeakNeutralPlayerChat = false
    };
    var urgent = new TranslationResult { ShouldSpeak = true, Intent = "warning", Priority = 3 };
    var important = new TranslationResult { ShouldSpeak = true, Intent = "assist", Priority = 2 };
    var neutral = new TranslationResult { ShouldSpeak = true, Intent = "message", Priority = 1 };
    var hostile = new TranslationResult { ShouldSpeak = true, Intent = "hostility", Priority = 3 };
    Require(PlayerSpeechPolicy.Classify(urgent) == PlayerSpeechCategory.Urgent
        && PlayerSpeechPolicy.ShouldSpeak(urgent, settings), "紧急玩家消息未进入紧急播报");
    Require(PlayerSpeechPolicy.Classify(important) == PlayerSpeechCategory.Important
        && PlayerSpeechPolicy.ShouldSpeak(important, settings), "重点玩家消息未进入重点播报");
    Require(PlayerSpeechPolicy.Classify(neutral) == PlayerSpeechCategory.Neutral
        && !PlayerSpeechPolicy.ShouldSpeak(neutral, settings), "关闭中性闲聊后仍被播报");
    Require(!PlayerSpeechPolicy.ShouldSpeak(hostile, settings), "纯辱骂不应进入语音");
    Require(PlayerSpeechPolicy.IsLikelyUrgent("bot careful, 정글 온다"),
        "韩英混排危险提醒未跳过普通汇总等待");

    var turn = new PlayerSpeechTurn(
        "PLAYERONE#KR1",
        "PlayerOne#KR1 (자르반 4세)",
        Array.Empty<string>(),
        Array.Empty<string>(),
        DateTimeOffset.UtcNow,
        "JarvanIV");
    var cleaned = PlayerSpeechTextSanitizer.Clean(
        "[12:34] PlayerOne#KR1：嘉文四世提醒下路先后退",
        turn);
    Require(cleaned == "嘉文四世提醒下路先后退", "语音未删除时间戳或玩家 ID");
    return Task.CompletedTask;
});

await CheckAsync("AI 语义证据本地校验", () =>
{
    var unsupportedAction = new TranslationResult
    {
        ShouldSpeak = true,
        SourceType = "player_chat",
        SpeechAct = "action",
        Modality = "asserted",
        SemanticConfidence = 0.96,
        Evidence = "小龙",
        SpokenSummary = "队友正在打小龙",
        DisplayLines = new List<string> { "小龙" }
    };
    TranslationSemanticGuard.Apply(
        unsupportedAction,
        new TranslationRequest(
            new[] { "小龙" },
            Array.Empty<HeroMention>(),
            SpeechFilterLevel.Standard,
            64));
    Require(!unsupportedAction.ShouldSpeak && !unsupportedAction.SemanticValidationPassed,
        "没有动作证据时，AI 仍能把目标标记说成正在执行的行动");

    var validWarning = new TranslationResult
    {
        ShouldSpeak = true,
        SourceType = "player_chat",
        SpeechAct = "warning",
        Subject = "敌方打野",
        Status = "danger",
        Modality = "asserted",
        SemanticConfidence = 0.94,
        Evidence = "小心敌方打野",
        SpokenSummary = "小心敌方打野"
    };
    TranslationSemanticGuard.Apply(
        validWarning,
        new TranslationRequest(
            new[] { "小心敌方打野" },
            Array.Empty<HeroMention>(),
            SpeechFilterLevel.Standard,
            64));
    Require(validWarning.ShouldSpeak && validWarning.SemanticValidationPassed,
        "证据完整的玩家警告被过度静音");

    var negativeCountdown = new TranslationResult
    {
        ShouldSpeak = true,
        SourceType = "player_chat",
        SpeechAct = "countdown",
        Subject = "闪现",
        Status = "cooldown",
        DurationSeconds = 20,
        Modality = "asserted",
        SemanticConfidence = 0.99,
        Evidence = "闪现 - 20s",
        SpokenSummary = "闪现负20秒"
    };
    TranslationSemanticGuard.Apply(
        negativeCountdown,
        new TranslationRequest(
            new[] { "闪现 - 20s" },
            Array.Empty<HeroMention>(),
            SpeechFilterLevel.Standard,
            64));
    Require(!negativeCountdown.ShouldSpeak,
        "AI 把 HUD 分隔符解释成负数后仍通过本地语音校验");
    return Task.CompletedTask;
});

await CheckAsync("自定义译文框固定尺寸", () =>
{
    var settings = new AppSettings
    {
        OverlayPlacement = OverlayPlacement.Custom,
        CaptureRegion = new ScreenRegion { X = 100, Y = 300, Width = 640, Height = 120 },
        DisplayRegion = new ScreenRegion { X = 140, Y = 160, Width = 520, Height = 108 }
    };
    var bounds = RiftLens.UI.OverlayPlacementService.Calculate(settings, new Size(380, 72));
    Require(bounds == new Rectangle(140, 160, 520, 108),
        "自定义译文框没有严格使用用户框选的位置与尺寸");
    return Task.CompletedTask;
});

await CheckAsync("AI 结构化翻译协议", async () =>
{
    var modelResult = JsonSerializer.Serialize(new
    {
        displayLines = new[] { "请上路支援。", "小心敌方打野。" },
        toxicityFiltered = false
    });
    var apiResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = modelResult } } }
    });
    var handler = new FakeHttpHandler(apiResponse);
    using var translator = new OpenAiCompatibleTranslationService(
        "https://api.deepseek.com",
        "deepseek-v4-flash",
        "test-key",
        handler);
    var hero = new HeroMention("Leona", "89", "레오나", "曙光女神", "蕾欧娜", "蕾欧娜");
    var result = await translator.TranslateAsync(new TranslationRequest(
        new[] { "<英雄 id=\"Leona\">蕾欧娜</英雄>: 탑 합류 좀", "상대 정글 조심해" },
        new[] { hero },
        SpeechFilterLevel.Standard,
        42,
        "蕾欧娜",
        true), CancellationToken.None);
    Require(result.ShouldSpeak, "播报标志解析失败");
    Require(result.Speaker == "蕾欧娜", "发言英雄校验失败");
    Require(result.DisplayLines.SequenceEqual(new[] { "请上路支援。", "小心敌方打野。" }),
        "多条玩家发言没有按原顺序保留独立译文");
    Require(result.DisplayTranslation == $"蕾欧娜：请上路支援。{Environment.NewLine}小心敌方打野。",
        "屏显仍在使用理解总结，而不是逐条直译内容");
    Require(handler.RequestBody.Contains("thinking", StringComparison.Ordinal), "DeepSeek 非思考模式参数缺失");
    using var sent = JsonDocument.Parse(handler.RequestBody);
    var systemPrompt = sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    Require(systemPrompt.Contains("不可信", StringComparison.Ordinal), "提示注入防护规则缺失");
    Require(systemPrompt.Contains("不添加原文没有的英雄", StringComparison.Ordinal), "忠实直译规则缺失");
    Require(systemPrompt.Contains("本地官方三语词典", StringComparison.Ordinal), "AI 提示未保护本地官方术语");
    Require(systemPrompt.Contains("displayLines", StringComparison.Ordinal), "AI 提示未要求逐条换行翻译");
    Require(systemPrompt.Contains("逐条直译", StringComparison.Ordinal)
        && systemPrompt.Contains("语音摘要、意图、优先级和是否播报由本地代码决定", StringComparison.Ordinal),
        "屏显直译与本地语音闸门没有严格分离");
    Require(systemPrompt.Contains("HUD 分隔符", StringComparison.Ordinal),
        "AI 提示没有禁止把冷却横杠译成负数");

    var genericResult = JsonSerializer.Serialize(new
    {
        displayLines = new[] { "请上路支援，并小心对面打野。" },
        toxicityFiltered = false
    });
    using var faithfulTranslator = new OpenAiCompatibleTranslationService(
        "https://api.deepseek.com",
        "deepseek-v4-flash",
        "test-key",
        new FakeHttpHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = genericResult } } }
        })));
    var faithful = await faithfulTranslator.TranslateAsync(new TranslationRequest(
        new[] { "탑 합류 좀, 상대 정글 조심해" },
        new[] { hero },
        SpeechFilterLevel.Standard,
        64,
        "蕾欧娜",
        true), CancellationToken.None);
    Require(faithful.SpokenSummary.Contains("上路", StringComparison.Ordinal)
        && faithful.SpokenSummary.Contains("打野", StringComparison.Ordinal), "正常沟通仍被空泛摘要取代");

    var hostileResult = JsonSerializer.Serialize(new
    {
        displayLines = new[] { "队友持续进行人身攻击。" },
        toxicityFiltered = true
    });
    using var protectiveTranslator = new OpenAiCompatibleTranslationService(
        "https://api.deepseek.com",
        "deepseek-v4-flash",
        "test-key",
        new FakeHttpHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = hostileResult } } }
        })));
    var protective = await protectiveTranslator.TranslateAsync(new TranslationRequest(
        new[] { "심한 욕설" },
        Array.Empty<HeroMention>(),
        SpeechFilterLevel.Standard,
        64,
        "队友",
        true), CancellationToken.None);
    Require(!protective.ShouldSpeak
        && protective.DisplayTranslation.Contains("持续进行人身攻击", StringComparison.Ordinal),
        "纯不友善沟通应保留文明化字幕但关闭语音");

    var englishResult = JsonSerializer.Serialize(new
    {
        displayLines = new[] { "Rotate top now and watch for the enemy jungler." },
        toxicityFiltered = false
    });
    var englishHandler = new FakeHttpHandler(JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = englishResult } } }
    }));
    using var englishTranslator = new OpenAiCompatibleTranslationService(
        "https://api.deepseek.com", "deepseek-v4-flash", "test-key", englishHandler);
    var english = await englishTranslator.TranslateAsync(new TranslationRequest(
        new[] { "탑 합류 좀, 상대 정글 조심해" },
        new[] { hero with { Id = "Vayne", SpokenName = "Vayne" } },
        SpeechFilterLevel.Standard,
        80,
        "Vayne",
        true,
        AppLanguage.English), CancellationToken.None);
    Require(english.SpokenSummary.Contains("top", StringComparison.OrdinalIgnoreCase)
        && english.SpokenSummary.Contains("jungler", StringComparison.OrdinalIgnoreCase), "英文正常沟通仍被空泛摘要取代");
    Require(english.SpokenSummary.StartsWith("Vayne:", StringComparison.Ordinal), "英文播报未保留发言人");
    using var englishRequest = JsonDocument.Parse(englishHandler.RequestBody);
    var englishUserPayload = englishRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() ?? string.Empty;
    Require(englishUserPayload.Contains("English (en-US)", StringComparison.Ordinal), "AI 请求缺少英文目标语言");
});

await CheckAsync("窗口相对框选与残行等待", () =>
{
    var region = ScreenRegion.FromRelative("League of Legends", 0.01, 0.67, 0.22, 0.24, 12, 720, 420, 250);
    Require(region.HasRelative && region.IsValid, "相对区域没有被视为有效框选");
    var client = new Rectangle(100, 50, 1920, 1080);
    region.BindToClient(new Rectangle(0, 0, 1920, 1080), "League of Legends");
    Require(region.AnchorProcess == "League of Legends", "框选没有绑定到游戏窗口");
    Require(PartialLineWait.LooksIncomplete("탑"), "过短残行没有触发等待");
    Require(PartialLineWait.LooksIncomplete("레오나:"), "只有发言人前缀时没有触发等待");
    Require(!PartialLineWait.LooksIncomplete("탑 조심해."), "完整短句被误判为残行");
    return Task.CompletedTask;
});

await CheckAsync("本地语音闸门会写明静音原因", () =>
{
    var silenced = new TranslationResult
    {
        ShouldSpeak = true,
        DisplayLines = new List<string> { "正在打小龙" }
    };
    LocalSpeechPlanner.Apply(
        silenced,
        new TranslationRequest(new[] { "小龙" }, Array.Empty<HeroMention>(), SpeechFilterLevel.Standard, 64));
    Require(!silenced.ShouldSpeak, "没有动作证据时本地闸门仍允许播报");
    Require(silenced.SpeechSilenceReason == SpeechSilenceReasons.SemanticGuard, "静音后没有留下语义原因");
    Require(LocalSpeechPlanner.FormatReason(silenced.SpeechSilenceReason, AppLanguage.Chinese).Contains("仅字幕"),
        "静音原因没有转成玩家能看懂的字幕提示");

    var fullSpeech = new AppSettings
    {
        SpeakUrgentPlayerChat = true,
        SpeakImportantPlayerChat = true,
        SpeakNeutralPlayerChat = true
    };
    var numericNoise = new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        ShouldSpeak = true,
        SpokenSummary = "队友说，730",
        OriginalLines = new List<string> { "730" }
    };
    Require(!FinalSpeechPolicy.ShouldSpeak(numericNoise, fullSpeech)
        && FinalSpeechPolicy.DescribeSilence(numericNoise, fullSpeech)
            == SpeechSilenceReasons.TranslationQuality,
        "纯数字 OCR 噪声仍能进入语音");
    var residualHangul = new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        ShouldSpeak = true,
        SpokenSummary = "队友说，템무가세小龙",
        OriginalLines = new List<string> { "템 뭐가세용" }
    };
    Require(!FinalSpeechPolicy.ShouldSpeak(residualHangul, fullSpeech),
        "中文译文残留大量韩文时仍能进入语音");
    var naturalChat = new TranslationResult
    {
        SourceKind = TranslationSourceKind.PlayerChat,
        ShouldSpeak = true,
        SpokenSummary = "队友说，今天团战慢慢打",
        OriginalLines = new List<string> { "오늘 한타 천천히 하자" }
    };
    Require(FinalSpeechPolicy.ShouldSpeak(naturalChat, fullSpeech),
        "正常完整玩家聊天被翻译质量闸门误伤");
    return Task.CompletedTask;
});

await CheckAsync("韩中英脚本感知融合", () =>
{
    var korean = TrilingualOcrFusion.Select(
        new OcrTextCandidate("탑 조심", 0.95f, OcrRecognitionModel.KoreanEnglish),
        new OcrTextCandidate("皆召", 0.76f, OcrRecognitionModel.ChineseEnglish));
    Require(korean.Text == "탑 조심", "韩文片段被中文模型的高相似字形替换");

    var chinese = TrilingualOcrFusion.Select(
        new OcrTextCandidate("ㅁ", 0.63f, OcrRecognitionModel.KoreanEnglish),
        new OcrTextCandidate("先看视野", 0.87f, OcrRecognitionModel.ChineseEnglish));
    Require(chinese.Text == "先看视野", "中文片段被韩文模型的兼容字母幻觉替换");

    var english = TrilingualOcrFusion.Select(
        new OcrTextCandidate("dragon", 0.94f, OcrRecognitionModel.KoreanEnglish),
        new OcrTextCandidate("dragon", 0.92f, OcrRecognitionModel.ChineseEnglish));
    Require(english.Text == "dragon", "两个模型一致的英文没有保留");

    var mixed = TrilingualOcrFusion.JoinSegments(new[] { korean, english, chinese });
    Require(mixed.Contains("탑", StringComparison.Ordinal)
        && mixed.Contains("dragon", StringComparison.Ordinal)
        && mixed.Contains("视野", StringComparison.Ordinal), "三语片段拼接丢失脚本");
    return Task.CompletedTask;
});

await CheckAsync("内置韩中英 OCR", async () =>
{
    var modelPath = Path.Combine(Path.GetTempPath(), "RiftLens-OcrSelfTest-" + Guid.NewGuid().ToString("N"));
    using var ocr = new BundledOcrService(modelPath);
    using var bitmap = new Bitmap(840, 120);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.White);
        using var font = new Font("Malgun Gothic", 42, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString("레오나 탑 조심 jungle", font, Brushes.Black, new PointF(18, 24));
    }
    var recognized = await ocr.RecognizeAsync(bitmap, CancellationToken.None);
    Console.WriteLine("  合成图片 OCR：" + (string.IsNullOrWhiteSpace(recognized) ? "未识别文字" : recognized));
    Require(recognized.Contains("레오나", StringComparison.Ordinal), "没有识别到内置韩语模型文本");
});

await CheckAsync("OCR 行颜色辅助证据", async () =>
{
    var modelPath = Path.Combine(Path.GetTempPath(), "RiftLens-OcrColorTest-" + Guid.NewGuid().ToString("N"));
    using var ocr = new BundledOcrService(modelPath);
    using var source = new Bitmap(900, 96);
    using (var graphics = Graphics.FromImage(source))
    {
        graphics.Clear(Color.FromArgb(28, 36, 45));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Malgun Gothic", 27, FontStyle.Regular, GraphicsUnit.Pixel);
        using var cyanBrush = new SolidBrush(Color.FromArgb(82, 205, 232));
        const string prefix = "[팀] PlayerOne#KR1 (아리): ";
        graphics.DrawString(prefix, font, cyanBrush, 16, 24);
        var prefixWidth = graphics.MeasureString(prefix, font).Width;
        graphics.DrawString("mid 来一下 조심", font, Brushes.WhiteSmoke, 16 + prefixWidth - 5, 24);
    }

    var document = await ocr.RecognizeDetailedAsync(source, CancellationToken.None);
    var evidence = document.Lines
        .Select(line => line.ColorEvidence)
        .FirstOrDefault(item => item?.IsAvailable == true);
    Require(evidence is not null, "详细 OCR 结果没有保留行级颜色证据");
    Require(evidence!.PrefixCyanRatio >= 0.10f && evidence.ContentNeutralRatio >= 0.20f,
        "青色玩家前缀与白色聊天正文未形成辅助证据");
});

await CheckAsync("同图韩中英混排 OCR", async () =>
{
    var modelPath = Path.Combine(Path.GetTempPath(), "RiftLens-OcrMixedTest-" + Guid.NewGuid().ToString("N"));
    using var ocr = new BundledOcrService(modelPath);
    using var source = new Bitmap(920, 190);
    using (var graphics = Graphics.FromImage(source))
    {
        graphics.Clear(Color.FromArgb(35, 43, 54));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Malgun Gothic", 25, FontStyle.Regular, GraphicsUnit.Pixel);
        var lines = new[]
        {
            "아리: mid 来一下",
            "레오나: 탑 조심, dragon 先看视野",
            "Jinx: 용 앞 vision 先做"
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var point = new PointF(18, 18 + index * 52);
            graphics.DrawString(lines[index], font, Brushes.Black, point.X + 1, point.Y + 1);
            graphics.DrawString(lines[index], font, Brushes.WhiteSmoke, point);
        }
    }

    _ = await ocr.RecognizeAsync(source, CancellationToken.None);
    var timer = Stopwatch.StartNew();
    var recognized = await ocr.RecognizeAsync(source, CancellationToken.None);
    timer.Stop();
    Console.WriteLine("  三语混排 OCR：" + recognized.Replace("\r\n", " / ").Replace("\n", " / "));
    Console.WriteLine($"  热态 OCR 延迟：{timer.ElapsedMilliseconds} ms");

    Require(TrilingualOcrFusion.ContainsHangul(recognized), "三语截图中的韩文被其他模型覆盖");
    Require(TrilingualOcrFusion.ContainsHan(recognized), "三语截图中的中文被其他模型覆盖");
    Require(new[] { "mid", "dragon", "vision", "Jinx" }
        .Count(token => recognized.Contains(token, StringComparison.OrdinalIgnoreCase)) >= 3,
        "三语截图中的英文识别不足");
    Require(new[] { "아리", "레오나", "탑", "용" }
        .Count(token => recognized.Contains(token, StringComparison.Ordinal)) >= 3,
        "三语截图中的韩文关键词识别不足");
    Require(new[] { "一下", "先看", "视野", "先做" }
        .Count(token => recognized.Contains(token, StringComparison.Ordinal)) >= 2,
        "三语截图中的中文关键词识别不足");
    Require(timer.Elapsed < TimeSpan.FromSeconds(1.2), "热态三语 OCR 延迟超过 1.2 秒");
});

await CheckAsync("小字号韩服聊天 OCR", async () =>
{
    var modelPath = Path.Combine(Path.GetTempPath(), "RiftLens-OcrChatTest-" + Guid.NewGuid().ToString("N"));
    using var ocr = new BundledOcrService(modelPath);
    using var source = new Bitmap(760, 150);
    using (var graphics = Graphics.FromImage(source))
    {
        graphics.Clear(Color.FromArgb(35, 43, 54));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Malgun Gothic", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        var lines = new[]
        {
            "레오나: 탑 합류 좀, 상대 정글 조심해",
            "아리: 미드 밀고 바텀 로밍 갈게요",
            "징크스: 용 앞 시야 먼저 잡아 주세요",
            "쓰레쉬: 점멸 빠졌어요 후퇴하세요"
        };
        var colors = new[]
        {
            Color.FromArgb(219, 232, 244), Color.FromArgb(156, 210, 241),
            Color.FromArgb(238, 215, 148), Color.FromArgb(196, 226, 210)
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var point = new PointF(16, 13 + index * 31);
            graphics.DrawString(lines[index], font, Brushes.Black, point.X + 1, point.Y + 1);
            using var brush = new SolidBrush(colors[index]);
            graphics.DrawString(lines[index], font, brush, point);
        }
    }
    var recognized = await ocr.RecognizeAsync(source, CancellationToken.None);
    Console.WriteLine("  小字号聊天 OCR：" + (string.IsNullOrWhiteSpace(recognized) ? "未识别文字" : recognized.Replace("\r\n", " / ").Replace("\n", " / ")));
    var keyHits = new[] { "레오나", "정글", "아리", "미드", "징크스", "시야", "쓰레쉬", "점멸" }
        .Count(token => recognized.Contains(token, StringComparison.Ordinal));
    var lineCount = recognized.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).Length;
    Require(keyHits >= 7, $"小字号聊天关键词仅识别 {keyHits}/8");
    Require(lineCount is >= 4 and <= 6, $"聊天行被错误拆成 {lineCount} 段");
});

await CheckAsync("聊天滚动顶部残行过滤", async () =>
{
    var modelPath = Path.Combine(Path.GetTempPath(), "RiftLens-OcrClippedLineTest-" + Guid.NewGuid().ToString("N"));
    using var ocr = new BundledOcrService(modelPath);
    using var source = new Bitmap(720, 112);
    using (var graphics = Graphics.FromImage(source))
    {
        graphics.Clear(Color.FromArgb(35, 43, 54));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Malgun Gothic", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        var lines = new[]
        {
            ("잘린첫줄: 이전 메시지", -8f),
            ("아리: 미드 조심해", 25f),
            ("레오나: 정글 조심해", 56f)
        };
        foreach (var (line, y) in lines)
        {
            graphics.DrawString(line, font, Brushes.Black, 17, y + 1);
            graphics.DrawString(line, font, Brushes.WhiteSmoke, 16, y);
        }
    }

    var recognized = await ocr.RecognizeAsync(source, CancellationToken.None);
    Console.WriteLine("  顶部残行过滤：" + recognized.Replace("\r\n", " / ").Replace("\n", " / "));
    Require(!recognized.Contains("잘린첫줄", StringComparison.Ordinal), "被裁断的顶部旧消息仍进入 OCR 结果");
    Require(recognized.Contains("아리", StringComparison.Ordinal), "过滤顶部残行时误删了第二行完整消息");
    Require(recognized.Contains("레오나", StringComparison.Ordinal), "过滤顶部残行时误删了底部完整消息");
});

await CheckAsync("中韩信号模板本地翻译", () =>
{
    // ── 韩文 ──
    Require(SignalMessageTemplates.TryTranslate("신호: 후퇴") == "撤退信号", "韩文撤退");
    Require(SignalMessageTemplates.TryTranslate("1234 골드") == "金币：1234", "韩文金币");
    Require(SignalMessageTemplates.TryTranslate("귀환 시작") == "回城中", "韩文回城");

    // ── 中文（汉化客户端） ──
    Require(SignalMessageTemplates.TryTranslate("信号：撤退") == "撤退信号", "中文撤退");
    Require(SignalMessageTemplates.TryTranslate("2500 金币") == "金币：2500", "中文金币");
    Require(SignalMessageTemplates.TryTranslate("回城中") == "回城中", "中文回城");
    Require(SignalMessageTemplates.TryTranslate("发起了投降") == "有人发起了投降投票", "中文投降");
    Require(SignalMessageTemplates.TryTranslate("大招 就绪") == "大招已就绪", "中文大招就绪");
    Require(SignalMessageTemplates.TryTranslate("已连续游戏 2 小时，请注意休息") == "已连续游戏 2 小时，请注意休息", "中文时长提醒");

    // ── 英文输出 ──
    Require(SignalMessageTemplates.TryTranslate("신호: 후퇴", AppLanguage.English) == "Retreat", "英文撤退");
    Require(SignalMessageTemplates.TryTranslate("1234 골드", AppLanguage.English) == "Gold: 1234", "英文金币");
    Require(SignalMessageTemplates.TryTranslate("귀환 시작", AppLanguage.English) == "Recalling", "英文回城");
    Require(SignalMessageTemplates.FormatSpokenSignal("敌人不见了", "嘉文四世", AppLanguage.Chinese)
        == "嘉文四世发出敌人消失信号", "中文战术播报仍错误使用“某人说”句式");
    Require(SignalMessageTemplates.FormatSpokenSignal("敌人不见了", "Jarvan IV", AppLanguage.English)
        == "Jarvan IV signals that the enemy is missing.", "英文战术播报仍错误使用 says 句式");
    Require(SignalMessageTemplates.FormatSpokenSignal("撤退信号", "嘉文四世", AppLanguage.Chinese)
        == "嘉文四世发出撤退信号", "撤退信号动作句式被破坏");
    Require(SignalMessageTemplates.FormatSpokenSignal(
            "撤退信号",
            string.Empty,
            AppLanguage.Chinese,
            hasKnownInitiator: false) == "收到撤退信号",
        "无发起人的撤退信号仍伪造队友主语");

    // ── 应返回 null 的（系统消息不应翻译） ──
    Require(SignalMessageTemplates.TryTranslate("敌方摧毁了一座防御塔") == null, "中文防御塔消息不应翻译");
    Require(SignalMessageTemplates.TryTranslate("玩家击杀了巨龙") == null, "中文击杀消息不应翻译");

    // ── 信号消息检测 ──
    Require(SignalMessageTemplates.IsSignalMessage("信号：撤退"), "中文信号未被检测");
    Require(SignalMessageTemplates.IsSignalMessage("2500 金币"), "中文金币未被检测");

    return Task.CompletedTask;
});

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("全部自检通过。");
    return 0;
}

Console.Error.WriteLine($"自检失败：{failures.Count} 项");
foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
return 1;

async Task CheckAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine("[通过] " + name);
    }
    catch (Exception exception)
    {
        failures.Add(name + "：" + exception.Message);
        Console.WriteLine("[失败] " + name);
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool IsPlausibleForReplay(string line)
{
    if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;
    var readable = 0;
    var total = 0;
    foreach (var character in line)
    {
        if (char.IsWhiteSpace(character)) continue;
        total++;
        if (char.IsLetterOrDigit(character)
            || character >= 0xAC00
            || character is >= (char)0x3130 and <= (char)0x318F
            || character is >= (char)0x4E00 and <= (char)0x9FFF
            || character is ':' or '：' or '[' or ']' or '!' or '？' or '?')
        {
            readable++;
        }
    }
    return total > 0
        && readable / (double)total >= 0.4
        && (line.Where(character => !char.IsWhiteSpace(character)).Distinct().Count() > 2 || line.Length <= 4);
}

sealed class FakeHttpHandler(string responseBody) : HttpMessageHandler
{
    public string RequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}

sealed record VisionEvalFrame(
    string FrameId,
    string ScreenshotFile,
    string LocalOcr,
    double LocalOcrElapsedMs);

sealed class RouteReplayAccumulator
{
    private readonly MessageDeduplicator _deduplicator = new();
    private readonly ChatMessageClassifier _classifier = new();
    private readonly PlayerHeroSessionTracker _playerHeroes;
    private readonly Dictionary<GameMessageKind, int> _counts = Enum
        .GetValues<GameMessageKind>()
        .ToDictionary(kind => kind, _ => 0);
    private int _acceptedLines;

    public RouteReplayAccumulator(HeroDictionary heroes)
    {
        _playerHeroes = new PlayerHeroSessionTracker(heroes);
    }

    public void Observe(OcrDocument document)
    {
        foreach (var line in _deduplicator.FindNewLineResults(document.RawText))
        {
            if (!IsPlausible(line.NormalizedText))
            {
                continue;
            }

            var colour = FindColour(document, line.NormalizedText);
            var message = _playerHeroes.Observe(_classifier.Classify(line.NormalizedText, colour));
            _acceptedLines++;
            _counts[message.Kind]++;
        }
    }

    public object Snapshot()
    {
        var unknown = _counts[GameMessageKind.Unknown];
        return new
        {
            acceptedLines = _acceptedLines,
            categories = _counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            routableShare = _acceptedLines == 0 ? 0 : (_acceptedLines - unknown) / (double)_acceptedLines
        };
    }

    private static OcrColorEvidence? FindColour(OcrDocument document, string text)
    {
        var target = Normalize(text);
        return document.Lines.LastOrDefault(line =>
            string.Equals(Normalize(line.Text), target, StringComparison.OrdinalIgnoreCase))?.ColorEvidence;
    }

    private static string Normalize(string value) =>
        MessageDeduplicator.StripGameTimestamp(value)
            .Normalize(NormalizationForm.FormKC)
            .Trim();

    private static bool IsPlausible(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;
        var readable = 0;
        var total = 0;
        foreach (var character in line)
        {
            if (char.IsWhiteSpace(character)) continue;
            total++;
            if (char.IsLetterOrDigit(character)
                || character >= 0xAC00
                || character is >= (char)0x3130 and <= (char)0x318F
                || character is >= (char)0x4E00 and <= (char)0x9FFF
                || character is ':' or '：' or '[' or ']' or '!' or '？' or '?')
            {
                readable++;
            }
        }
        return total > 0
            && readable / (double)total >= 0.4
            && (line.Where(character => !char.IsWhiteSpace(character)).Distinct().Count() > 2 || line.Length <= 4);
    }
}
