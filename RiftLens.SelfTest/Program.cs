using RiftLens.Models;
using RiftLens.Services;
using System.Net;
using System.Text;
using System.Text.Json;

var failures = new List<string>();

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
    expand.Invoke(control, new object[] { true });
    control.CreateControl();
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
    Require(MessageDeduplicator.StripGameTimestamp("레오나: 탑 가세요") == "레오나: 탑 가세요", "无时间戳消息被误修改");

    // 时间戳不影响去重：相同内容不同时间戳应视为重复
    deduplicator.Reset();
    Require(deduplicator.FindNewLines("[12:34] 레오나: 탑 조심\n[12:35] 아리: 미드 미아").Count == 0, "首次识别不应播报");
    Require(deduplicator.FindNewLines("[12:34] 레오나: 탑 조심\n[12:35] 아리: 미드 미아\n[12:36] 레오나: 정글 갱").SequenceEqual(new[] { "레오나: 정글 갱" }), "时间戳消息去重失败");

    return Task.CompletedTask;
});

await CheckAsync("玩家与系统消息分类", () =>
{
    var classifier = new ChatMessageClassifier();
    // ── 韩文 ──
    Require(classifier.IsPlayerMessage("[팀] 레오나: 탑 합류 좀", SystemMessageFilterMode.Balanced), "玩家聊天被过滤");
    Require(!classifier.IsPlayerMessage("적이 포탑을 파괴했습니다", SystemMessageFilterMode.Balanced), "防御塔消息未过滤");
    Require(!classifier.IsPlayerMessage("[시스템] 플레이어가 게임에서 나갔습니다", SystemMessageFilterMode.Balanced), "离开消息未过滤");
    Require(!classifier.IsPlayerMessage("레오나님이 위험 신호를 보냄", SystemMessageFilterMode.Balanced), "关闭战术播报后仍放行了信号");
    Require(classifier.IsPlayerMessage("레오나님이 위험 신호를 보냄", SystemMessageFilterMode.Balanced, true), "开启战术播报后信号被过滤");
    Require(!classifier.IsPlayerMessage("형 탑 조심", SystemMessageFilterMode.Strict), "严格模式接受了无玩家格式文本");
    Require(classifier.IsPlayerMessage("형 탑 조심", SystemMessageFilterMode.Off), "关闭过滤后仍丢弃文本");

    // ── 中文（汉化客户端） ──
    Require(classifier.IsPlayerMessage("[队伍] 蕾欧娜：上路支援一下", SystemMessageFilterMode.Balanced), "中文玩家聊天被过滤");
    Require(!classifier.IsPlayerMessage("敌方摧毁了一座防御塔", SystemMessageFilterMode.Balanced), "中文防御塔消息未过滤");
    Require(!classifier.IsPlayerMessage("玩家离开了游戏", SystemMessageFilterMode.Balanced), "中文离开消息未过滤");
    Require(classifier.IsPlayerMessage("蕾欧娜发出撤退信号", SystemMessageFilterMode.Balanced, true), "中文战术信号被过滤");
    Require(classifier.IsPlayerMessage("[所有人] 阿狸：中路不见了", SystemMessageFilterMode.Balanced), "中文全频道聊天被过滤");

    // ── 中韩混合 ──
    Require(classifier.IsPlayerMessage("[队伍] 레오나：上路 조심", SystemMessageFilterMode.Balanced), "中韩混合玩家聊天被过滤");
    Require(classifier.Classify("아리가 가렌을 처치했습니디", SystemMessageFilterMode.Balanced).Kind == GameMessageKind.KillEvent,
        "OCR 轻微错字后的击杀消息未被识别");
    Require(classifier.Classify("와드가 만료되었습니다", SystemMessageFilterMode.Balanced).Kind == GameMessageKind.Unknown,
        "未知系统文字不应伪装成玩家聊天");
    Require(classifier.Classify("알림: 서버 연결 상태가 불안정합니다", SystemMessageFilterMode.Strict).Kind == GameMessageKind.SystemEvent,
        "带冒号的系统通知被误判为玩家聊天");
    Require(classifier.Classify("아리: help top", SystemMessageFilterMode.Balanced).Kind == GameMessageKind.PlayerChat,
        "包含战术关键词的玩家发言被降级成了系统 Ping");
    return Task.CompletedTask;
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
    return Task.CompletedTask;
});

await CheckAsync("术语表长词优先", () =>
{
    Require(GameTermGlossary.ReplaceTerms("\uC6A9 \uD55C\uD0C0") == "\u9F99\u56E2", "长词应先于短词替换");
    Require(GameTermGlossary.ReplaceTerms("탑 합류, 바론 시야", AppLanguage.English) == "top 합류, Baron vision", "英文术语固定映射失败");
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
    Require(loaded.SchemaVersion == 4 && loaded.SignalVoiceEnabled, "旧版设置未迁移到玩家优先播报逻辑");
    Require(loaded.Language == AppLanguage.Chinese, "旧版设置未迁移到默认中文语言");
    Require(loaded.MaxSummaryChars == 64 && loaded.SummaryWindowMs == 900, "玩家轮次摘要默认参数迁移失败");
    loaded.Language = AppLanguage.English;
    await store.SaveAsync(loaded);
    Require((await store.LoadAsync()).Language == AppLanguage.English, "英文语言设置未持久化");
});

await CheckAsync("AI 结构化翻译协议", async () =>
{
    var modelResult = JsonSerializer.Serialize(new
    {
        shouldSpeak = true,
        speaker = "蕾欧娜",
        intent = "warning",
        emotion = "urgent",
        emotionIntensity = 2,
        priority = 3,
        displayTranslation = "蕾欧娜焦急地提醒上单支援，并小心敌方打野。",
        spokenSummary = "蕾欧娜提醒上单及时支援，小心敌方打野。",
        toxicityFiltered = true,
        expiresAfterMs = 6000
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
        new[] { "<英雄 id=\"Leona\">蕾欧娜</英雄>: 탑 합류 좀" },
        new[] { hero },
        SpeechFilterLevel.Standard,
        42,
        "蕾欧娜",
        true), CancellationToken.None);
    Require(result.ShouldSpeak, "播报标志解析失败");
    Require(result.Speaker == "蕾欧娜", "发言英雄校验失败");
    Require(handler.RequestBody.Contains("thinking", StringComparison.Ordinal), "DeepSeek 非思考模式参数缺失");
    using var sent = JsonDocument.Parse(handler.RequestBody);
    var systemPrompt = sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    Require(systemPrompt.Contains("不可信", StringComparison.Ordinal), "提示注入防护规则缺失");
    Require(systemPrompt.Contains("禁止把正常内容降级", StringComparison.Ordinal), "忠实摘要规则缺失");

    var genericResult = JsonSerializer.Serialize(new
    {
        shouldSpeak = true,
        speaker = "蕾欧娜",
        intent = "assist",
        emotion = "neutral",
        emotionIntensity = 0,
        priority = 2,
        displayTranslation = "请上路支援，并小心对面打野。",
        spokenSummary = "蕾欧娜在请求支援",
        toxicityFiltered = false,
        expiresAfterMs = 5000
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
        shouldSpeak = true,
        speaker = "队友",
        intent = "hostility",
        emotion = "angry",
        emotionIntensity = 3,
        priority = 1,
        displayTranslation = "队友持续进行人身攻击。",
        spokenSummary = "队友有些不满",
        toxicityFiltered = true,
        expiresAfterMs = 4000
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
    Require(protective.ShouldSpeak && protective.SpokenSummary == "队友有些不满", "纯不友善沟通未采用保护性概括");

    var englishResult = JsonSerializer.Serialize(new
    {
        shouldSpeak = true,
        speaker = "Vayne",
        intent = "assist",
        emotion = "urgent",
        emotionIntensity = 2,
        priority = 3,
        displayTranslation = "Rotate top now and watch for the enemy jungler.",
        spokenSummary = "Vayne is asking for help",
        toxicityFiltered = false,
        expiresAfterMs = 5000
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

await CheckAsync("内置韩英 OCR", async () =>
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
    using var prepared = new BitmapPreprocessor().Prepare(source, 3);
    var recognized = await ocr.RecognizeAsync(prepared, CancellationToken.None);
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

    using var prepared = new BitmapPreprocessor().Prepare(source, 3);
    var recognized = await ocr.RecognizeAsync(prepared, CancellationToken.None);
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
        == "嘉文四世说，敌人不见了", "中文战术播报仍在生硬拼接发言人与内容");
    Require(SignalMessageTemplates.FormatSpokenSignal("敌人不见了", "Jarvan IV", AppLanguage.English)
        == "Jarvan IV says the enemy is missing.", "英文战术播报句式不自然");
    Require(SignalMessageTemplates.FormatSpokenSignal("撤退信号", "嘉文四世", AppLanguage.Chinese)
        == "嘉文四世发出撤退信号", "撤退信号动作句式被破坏");

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
