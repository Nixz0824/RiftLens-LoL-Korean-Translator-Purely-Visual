using System.Reflection;
using System.Threading.Channels;
using RiftLens.Models;
using RiftLens.Services;

internal static class PipelineRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task TimeoutRecoveryAsync()
    {
        var count = 0;
        var translator = new StubTranslator((request, token) => ++count == 1
            ? Task.FromException<TranslationResult>(new TaskCanceledException("simulated HTTP timeout"))
            : Task.FromResult(Result(request)));
        await using var coordinator = await CreateAsync(translator);
        var timedOut = Signal();
        var received = Signal();
        coordinator.StatusChanged += text => { if (text.Contains("超时")) timedOut.TrySetResult(); };
        coordinator.TranslationReady += _ => received.TrySetResult();
        StartConsumer(coordinator);
        await Queue(coordinator, "first", "미드 밀고 갈게");
        await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Queue(coordinator, "second", "바텀 조심해");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(count == 2 && coordinator.IsRunning, "HTTP timeout stopped the translation consumer.");
    }

    public static async Task PauseRecoveryAsync()
    {
        var started = Signal();
        var cancelled = Signal();
        var resumed = Signal();
        var count = 0;
        var timeoutStatuses = 0;
        var translator = new StubTranslator(async (request, token) =>
        {
            if (++count == 1)
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            }
            return Result(request);
        });
        await using var coordinator = await CreateAsync(translator);
        coordinator.StatusChanged += text => { if (text.Contains("超时")) timeoutStatuses++; };
        coordinator.TranslationReady += _ => resumed.TrySetResult();
        StartConsumer(coordinator);
        await Queue(coordinator, "first", "미드 밀고 갈게");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        coordinator.SetPaused(true);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Require(!resumed.Task.IsCompleted, "A paused request produced output.");
        coordinator.SetPaused(false);
        await Queue(coordinator, "second", "바텀 조심해");
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(timeoutStatuses == 0 && count == 2, "Pause was treated as failure or first resumed message was lost.");
        // Also exercise resume while the consumer is waiting on an empty channel.
        coordinator.SetPaused(true);
        coordinator.SetPaused(false);
        var final = Signal();
        coordinator.TranslationReady += _ => final.TrySetResult();
        await Queue(coordinator, "third", "탑 합류해 줘");
        await final.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public static async Task SourceAlignmentAsync()
    {
        TranslationRequest? sent = null;
        await using var coordinator = await CreateAsync(new StubTranslator((request, token) =>
        {
            sent = request;
            return Task.FromResult(Result(request));
        }));
        var outputs = new List<TranslationResult>();
        coordinator.TranslationReady += outputs.Add;
        var aggregator = new PlayerTurnAggregator();
        var contents = new[] { "1213mid", "미드 밀고 갈게", "933 sup", "바텀 조심해" };
        for (var i = 0; i < contents.Length; i++)
        {
            aggregator.Add(Message("same", contents[i], $"line-{i}"));
        }
        // Repeated content must not shift raw text / evaluation IDs relative to contents.
        aggregator.Add(Message("same", contents[1], "duplicate"));
        var turn = aggregator.Flush()!;
        Require(turn.GetMessages().Count == 4, "Duplicate source shifted the turn.");
        await InvokeTask(coordinator, "TranslatePlayerTurnAsync", turn, 0, CancellationToken.None);
        Require(outputs.Count == 2 && sent?.Lines.Count == 2, "Mixed turn did not split into local and AI outputs.");
        var callout = outputs.Single(result => result.ClassificationRuleId == "player.callout");
        var chat = outputs.Single(result => result.ClassificationRuleId == "player.structure");
        Require(callout.OriginalLines.SequenceEqual(new[] { contents[0], contents[2] })
            && callout.EvaluationLineIds.SequenceEqual(new[] { "line-0", "line-2" }), "Local callout lost its original/ID mapping.");
        Require(chat.OriginalLines.SequenceEqual(new[] { contents[1], contents[3] })
            && chat.EvaluationLineIds.SequenceEqual(new[] { "line-1", "line-3" }), "AI translation original/ID mapping is misaligned.");
        Require(!chat.RawText.Contains("1213mid") && !callout.RawText.Contains(contents[1]), "History contains lines belonging to the other route.");
    }

    public static async Task VisionTimeoutAsync()
    {
        var calls = 0;
        var vision = new StubVision((bitmap, token) => ++calls == 1
            ? Task.FromException<OcrDocument>(new TaskCanceledException("simulated Vision timeout"))
            : Task.FromResult(OcrDocument.FromText("아리: 미드 밀고 갈게")));
        await using var coordinator = await CreateAsync(new StubTranslator((r, t) => Task.FromResult(Result(r))), vision);
        using var bitmap = new Bitmap(480, 180);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            for (var x = 0; x < bitmap.Width; x += 12)
            for (var y = 0; y < bitmap.Height; y += 12)
            {
                using var brush = new SolidBrush(Color.FromArgb((x * 7 + y) % 256, (y * 11 + x) % 256, (x + y * 3) % 256));
                graphics.FillRectangle(brush, x, y, 12, 12);
            }
        }
        var local = OcrDocument.FromText("아리: 미드 밀고 갈게");
        var result = await InvokeResult(coordinator, "RecognizeVisionGivenLocalAsync", bitmap, local, CancellationToken.None);
        Require(!(bool)result.GetType().GetProperty("UsedVision")!.GetValue(result)!, "Vision timeout did not fall back to local OCR.");
        result = await InvokeResult(coordinator, "RecognizeVisionGivenLocalAsync", bitmap, local, CancellationToken.None);
        Require((bool)result.GetType().GetProperty("UsedVision")!.GetValue(result)! && calls == 2, "Vision did not recover on the next frame.");
    }

    public static async Task ConcurrentSettingsAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RiftLens-save-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(path);
            var settings = new AppSettings();
            var saves = new List<Task>();
            for (var i = 0; i < 80; i++)
            {
                settings.SpeechVolume = i;
                saves.Add(store.SaveAsync(settings));
            }
            settings.SpeechVolume = 100; // Not submitted: an in-flight save must not capture this mutation.
            await Task.WhenAll(saves);
            Require((await store.LoadAsync()).SpeechVolume == 79, "Concurrent saves lost the final submitted snapshot.");
            Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "Save left temporary files behind.");
            await File.WriteAllTextAsync(path, "{broken settings");
            await store.LoadAsync();
            Require(store.LoadWarning is not null && store.RecoveryBackupPath is not null, "Corrupt settings were silently reset.");
            Require(await File.ReadAllTextAsync(store.RecoveryBackupPath!) == "{broken settings", "Recovery backup did not preserve the original.");
            await store.SaveAsync(new AppSettings());
            Require((await store.LoadAsync()).SchemaVersion == new AppSettings().SchemaVersion, "Save did not recover after corrupt settings.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<CaptureCoordinator> CreateAsync(ITranslationService translator, IVisionOcrService? vision = null) =>
        new(new AppSettings { SummaryWindowMs = 500 }, await HeroDictionary.LoadAsync(), translator, vision);

    private static TranslationResult Result(TranslationRequest request) => new()
    {
        DisplayLines = request.Lines.Select(line => "译文：" + line).ToList(),
        DisplayTranslation = "翻译结果"
    };

    private static ClassifiedGameMessage Message(string speaker, string content, string id = "") => new(
        GameMessageKind.PlayerChat, $"{speaker}: {content}", content, speaker, "아리",
        CapturedAt: DateTimeOffset.UtcNow, EvaluationLineId: id);

    private static ValueTask Queue(CaptureCoordinator coordinator, string speaker, string content) =>
        ((Channel<ClassifiedGameMessage>)typeof(CaptureCoordinator).GetField("_messages", Private)!.GetValue(coordinator)!)
        .Writer.WriteAsync(Message(speaker, content));

    private static void StartConsumer(CaptureCoordinator coordinator)
    {
        var lifetime = new CancellationTokenSource();
        typeof(CaptureCoordinator).GetField("_cancellation", Private)!.SetValue(coordinator, lifetime);
        var loop = InvokeTask(coordinator, "TranslationLoopAsync", lifetime.Token);
        typeof(CaptureCoordinator).GetField("_translationTask", Private)!.SetValue(coordinator, loop);
    }

    private static Task InvokeTask(object target, string method, params object[] args) =>
        (Task)target.GetType().GetMethod(method, Private)!.Invoke(target, args)!;

    private static async Task<object> InvokeResult(object target, string method, params object[] args)
    {
        var task = InvokeTask(target, method, args);
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class StubTranslator(Func<TranslationRequest, CancellationToken, Task<TranslationResult>> translate) : ITranslationService
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) => translate(request, token);
    }

    private sealed class StubVision(Func<Bitmap, CancellationToken, Task<OcrDocument>> recognize) : IVisionOcrService
    {
        public Task<OcrDocument> RecognizeAsync(Bitmap bitmap, CancellationToken token) => recognize(bitmap, token);
    }
}
