using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RiftLens.Models;
using RiftLens.Services;

internal static class MessageDeliveryRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static Task MonotonicLifetimeAsync()
    {
        var clock = new ManualClock();
        var lifetime = MessageLifetime.FromCapture(clock.GetUtcNow(), TimeSpan.FromSeconds(6), clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.ShiftUtc(TimeSpan.FromDays(-1));
        Require(lifetime.Remaining == TimeSpan.FromSeconds(4), "A wall-clock adjustment renewed the message.");
        Require(lifetime.LimitTo(TimeSpan.FromSeconds(20)).Remaining == lifetime.Remaining,
            "Reclassification extended the budget.");
        clock.Advance(TimeSpan.FromSeconds(4));
        Require(lifetime.IsExpired && lifetime.Remaining == TimeSpan.Zero, "The exact deadline was not enforced.");
        var expired = new TranslationResult { ShouldSpeak = true, SpokenSummary = "小心打野", Lifetime = lifetime };
        Require(!FinalSpeechPolicy.ShouldSpeak(expired, new AppSettings())
            && FinalSpeechPolicy.DescribeSilence(expired, new AppSettings()) == SpeechSilenceReasons.Expired,
            "The final speech gate ignored the original deadline.");
        return Task.CompletedTask;
    }

    public static async Task ExpiredAndMixedTurnsAsync()
    {
        var clock = new ManualClock();
        var requests = new List<TranslationRequest>();
        await using var coordinator = await CreateAsync(new Translator((request, token) =>
        {
            requests.Add(request);
            return Task.FromResult(Result(request));
        }), clock);
        var outputs = new List<TranslationResult>();
        coordinator.TranslationReady += outputs.Add;
        var old = Message(clock, "미드 밀고 갈게", "old") with { CapturedAt = clock.GetUtcNow().AddSeconds(-30) };
        await Translate(coordinator, Turn(old));
        await Translate(coordinator, Turn(old with { Content = "1213mid" }));
        Require(requests.Count == 0 && outputs.Count == 0, "Expired chat or a local callout was emitted.");

        var fresh = Message(clock, "탑 합류해 줘", "fresh");
        await Translate(coordinator, Turn(old, fresh));
        Require(requests.Count == 1 && requests[0].Lines.Count == 1 && outputs.Count == 1,
            "Stale lines were sent to translation or fresh lines were lost.");
        Require(outputs[0].EvaluationLineIds.SequenceEqual(new[] { "fresh" })
            && outputs[0].OriginalLines.SequenceEqual(new[] { fresh.Content })
            && outputs[0].Lifetime?.CapturedAt == fresh.CapturedAt,
            "Filtering stale lines changed source/ID/deadline alignment.");
    }

    public static async Task ExpiryDuringTranslationAsync()
    {
        var clock = new ManualClock();
        var calls = 0;
        await using var coordinator = await CreateAsync(new Translator((request, token) =>
        {
            calls++;
            clock.Advance(TimeSpan.FromSeconds(7)); // A provider may return despite cancellation.
            return Task.FromResult(Result(request));
        }), clock);
        var outputs = 0;
        coordinator.TranslationReady += _ => outputs++;
        await Translate(coordinator, Turn(Message(clock, "미드 밀고 갈게", "line")));
        Require(calls == 1 && outputs == 0, "A translation that expired in flight was emitted.");
    }

    public static async Task DeadlineCancellationAndRecoveryAsync()
    {
        var calls = 0;
        var cancelled = false;
        await using var coordinator = await CreateAsync(new Translator(async (request, token) =>
        {
            if (++calls == 1)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            }
            return Result(request);
        }));
        var message = Message(TimeProvider.System, "미드 밀고 갈게", "deadline") with
        {
            Lifetime = MessageLifetime.FromCapture(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(150))
        };
        await Translate(coordinator, Turn(message)).WaitAsync(TimeSpan.FromSeconds(2));
        Require(cancelled, "The translator was not cancelled at the remaining deadline.");
        var outputs = 0;
        coordinator.TranslationReady += _ => outputs++;
        await Translate(coordinator, Turn(Message(TimeProvider.System, "탑 합류해 줘", "after")));
        Require(calls == 2 && outputs == 1, "Deadline cancellation prevented later translations.");
    }

    public static Task AggregationLimitsAsync()
    {
        var clock = new ManualClock();
        var aggregator = new PlayerTurnAggregator(clock);
        var completed = new List<PlayerSpeechTurn>();
        for (var i = 0; i < 20; i++)
        {
            var turn = aggregator.Add(Message(clock, $"message {i}", $"id-{i}"));
            if (turn is not null) completed.Add(turn);
        }
        Require(completed.Count == 2 && completed.All(turn => turn.Contents.Count == 8),
            "The line limit did not immediately emit a complete turn.");
        completed.Add(aggregator.Flush()!);
        Require(completed.SelectMany(turn => turn.EvaluationLineIds!).SequenceEqual(
            Enumerable.Range(0, 20).Select(i => $"id-{i}")), "Flooded chat lost or reordered source lines.");

        aggregator.Add(Message(clock, "first", "first"));
        clock.Advance(TimeSpan.FromMilliseconds(2400));
        aggregator.Add(Message(clock, "second", "second"));
        Require(aggregator.GetWaitMilliseconds(2500) == 100, "A later line renewed the maximum wait.");
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Require(aggregator.HasReachedMaximumWait && aggregator.GetWaitMilliseconds(2500) == 0,
            "The maximum wait depends on an idle gap.");
        var limited = aggregator.Add(Message(clock, "third", "third"));
        Require(limited?.Contents.SequenceEqual(new[] { "first", "second" }) == true
            && aggregator.Flush()?.Contents.SequenceEqual(new[] { "third" }) == true,
            "The time limit dropped the boundary line.");
        return Task.CompletedTask;
    }

    public static async Task ContinuousConsumerAsync()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new CaptureCoordinator(new AppSettings { SummaryWindowMs = 2500 },
            await HeroDictionary.LoadAsync(), new Translator((request, token) =>
            {
                received.TrySetResult();
                return Task.FromResult(Result(request));
            }));
        var lifetime = new CancellationTokenSource();
        typeof(CaptureCoordinator).GetField("_cancellation", Private)!.SetValue(coordinator, lifetime);
        var loop = (Task)typeof(CaptureCoordinator).GetMethod("TranslationLoopAsync", Private)!
            .Invoke(coordinator, new object[] { lifetime.Token })!;
        typeof(CaptureCoordinator).GetField("_translationTask", Private)!.SetValue(coordinator, loop);
        var channel = (Channel<ClassifiedGameMessage>)typeof(CaptureCoordinator).GetField("_messages", Private)!
            .GetValue(coordinator)!;
        // Duplicates keep arriving faster than the idle timeout without reaching eight lines.
        for (var i = 0; i < 7 && !received.Task.IsCompleted; i++)
        {
            await channel.Writer.WriteAsync(Message(TimeProvider.System, "미드 밀고 갈게", $"line-{i}"));
            await Task.Delay(450);
        }
        Require(received.Task.IsCompleted, "Continuous chat postponed the turn beyond the maximum wait.");
    }

    public static async Task TranslationProtocolAsync()
    {
        var request = new TranslationRequest(new[] { "탑 합류해 줘", "상대 정글 조심해" },
            Array.Empty<HeroMention>(), SpeechFilterLevel.Standard, 96);
        object Line(string id, string? text) => new { id, translation = text };
        var first = Line("line-1", "请上路支援。");
        var second = Line("line-2", "小心敌方打野。");
        var invalid = new (object Lines, object? Toxicity, string? Finish)[]
        {
            (new[] { first }, false, "stop"),
            (new[] { first, first }, false, "stop"),
            (new[] { first, Line("unknown", "小心打野") }, false, "stop"),
            (new[] { first, Line("line-2", " ") }, false, "stop"),
            (new[] { first, Line("line-2", null) }, false, "stop"),
            (new[] { first, second }, false, "length"),
            (new[] { first, second }, false, "content_filter"),
            (new[] { first, second }, false, null),
            (new[] { first, second }, "false", "stop"),
            (new[] { "请上路支援", "小心打野" }, false, "stop"),
            (new[] { first, new { id = "line-2", translation = 123 } }, false, "stop"),
            (new[] { first, second, Line("line-3", "多余行") }, false, "stop")
        };
        foreach (var sample in invalid)
        {
            using var translator = TranslatorFor(sample.Lines, sample.Toxicity, sample.Finish);
            var rejected = false;
            try { await translator.TranslateAsync(request, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Incomplete, truncated or unaligned translation was accepted.");
        }
        using var valid = TranslatorFor(new[] { second, first }, false, "stop");
        var result = await valid.TranslateAsync(request, CancellationToken.None);
        Require(result.ShouldSpeak && result.DisplayLines.SequenceEqual(new[] { "请上路支援。", "小心敌方打野。" }),
            "Valid line IDs did not restore source order.");

        var eight = request with { Lines = Enumerable.Repeat("미드 밀고 갈게", 8).ToArray() };
        var handler = new ResponseHandler(JsonSerializer.Serialize(new { choices = Array.Empty<object>() }));
        using var budget = new OpenAiCompatibleTranslationService("https://example.invalid", "test", "test", handler);
        try { await budget.TranslateAsync(eight, CancellationToken.None); } catch (InvalidOperationException) { }
        using var sent = JsonDocument.Parse(handler.RequestBody);
        Require(sent.RootElement.GetProperty("max_tokens").GetInt32() > 384,
            "An eight-line turn retained the single-line output budget.");
        using var payload = JsonDocument.Parse(sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Require(payload.RootElement.GetProperty("chatLines")[7].GetProperty("id").GetString() == "line-8",
            "The request omitted stable line IDs.");
    }

    public static Task LiteralSpeechSafetyAsync()
    {
        var request = new TranslationRequest(new[] { "help top", "care jungle" }, Array.Empty<HeroMention>(),
            SpeechFilterLevel.Standard, 96);
        var missing = new TranslationResult { DisplayLines = new() { "请上路支援。" } };
        LocalSpeechPlanner.Apply(missing, request);
        Require(!missing.ShouldSpeak && missing.SpeechSilenceReason == SpeechSilenceReasons.TranslationQuality,
            "A partial translation passed the local speech planner.");
        var literal = "请来上路支援，等我们清理兵线后再走河道，并且不要单独进入敌方野区。";
        var longResult = new TranslationResult { DisplayLines = new() { literal } };
        LocalSpeechPlanner.Apply(longResult, request with { Lines = new[] { "help top, do not invade alone" }, MaxSummaryChars = 8 });
        Require(!longResult.ShouldSpeak && longResult.DisplayLines[0] == literal
            && longResult.SpeechSilenceReason == SpeechSilenceReasons.SpeechTooLong
            && longResult.SpokenSummary.Contains("不要单独进入敌方野区", StringComparison.Ordinal),
            "The planner truncated the sentence after semantic validation.");
        return Task.CompletedTask;
    }

    public static async Task SpeechExpiryAsync()
    {
        var clock = new ManualClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var statuses = new List<string>();
        using var speech = new NaturalSpeechService(new Synthesizer(async token =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task;
            return Array.Empty<byte>(); // Must be discarded before MP3 decoding or playback.
        }));
        speech.StatusChanged += statuses.Add;
        var first = MessageLifetime.FromCapture(clock.GetUtcNow(), TimeSpan.FromSeconds(6), clock);
        speech.Enqueue("过期音频", 0, 0, 6000, lifetime: first);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            speech.Enqueue("排队过期", 0, 0, 6000,
                lifetime: MessageLifetime.FromCapture(clock.GetUtcNow(), TimeSpan.FromSeconds(6), clock));
            clock.Advance(TimeSpan.FromSeconds(7));
            // A sentinel confirms that both earlier items have completed processing.
            speech.Enqueue("检查完成", 0, 0, 6000, canSpeak: () => { drained.TrySetResult(); return false; },
                lifetime: MessageLifetime.FromCapture(clock.GetUtcNow(), TimeSpan.FromSeconds(6), clock));
            release.TrySetResult();
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Require(calls == 1 && statuses.Count == 0,
                "Expired speech was synthesized, decoded, played or treated as a provider failure.");
        }
        finally { release.TrySetResult(); }

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledStatuses = new List<string>();
        using var cancellableSpeech = new NaturalSpeechService(new Synthesizer(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return Array.Empty<byte>();
        }));
        cancellableSpeech.StatusChanged += cancelledStatuses.Add;
        cancellableSpeech.Enqueue("截止后取消", 0, 0, 6000,
            lifetime: MessageLifetime.FromCapture(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(150)));
        cancellableSpeech.Enqueue("确认取消完成", 0, 0, 6000,
            canSpeak: () => { afterCancellation.TrySetResult(); return false; });
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await afterCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(cancelledStatuses.Count == 0, "Deadline cancellation disabled the neural provider or triggered a fallback.");
    }

    private static async Task<CaptureCoordinator> CreateAsync(ITranslationService translator, TimeProvider? clock = null) =>
        new(new AppSettings(), await HeroDictionary.LoadAsync(), translator, clock: clock);

    private static Task Translate(CaptureCoordinator coordinator, PlayerSpeechTurn turn) =>
        (Task)typeof(CaptureCoordinator).GetMethod("TranslatePlayerTurnAsync", Private)!
            .Invoke(coordinator, new object[] { turn, 0, CancellationToken.None })!;

    private static PlayerSpeechTurn Turn(params ClassifiedGameMessage[] messages) => new("same", "아리",
        messages.Select(message => message.RawText).ToArray(), messages.Select(message => message.Content).ToArray(),
        messages[^1].CapturedAt, Messages: messages);

    private static ClassifiedGameMessage Message(TimeProvider clock, string text, string id) => new(
        GameMessageKind.PlayerChat, $"아리: {text}", text, "same", "아리", CapturedAt: clock.GetUtcNow(), EvaluationLineId: id);

    private static TranslationResult Result(TranslationRequest request) => new()
    {
        DisplayLines = request.Lines.Select(_ => "请来支援").ToList(), DisplayTranslation = "请来支援"
    };

    private static OpenAiCompatibleTranslationService TranslatorFor(object lines, object? toxicity, string? finish) =>
        new("https://example.invalid", "test", "test", new ResponseHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = finish, message = new
            {
                content = JsonSerializer.Serialize(new { displayLines = lines, toxicityFiltered = toxicity })
            } } }
        })));

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void ShiftUtc(TimeSpan delta) => _utc += delta;
        public void Advance(TimeSpan delta) { _utc += delta; Interlocked.Add(ref _timestamp, delta.Ticks); }
    }

    private sealed class Translator(Func<TranslationRequest, CancellationToken, Task<TranslationResult>> translate) : ITranslationService
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) => translate(request, token);
    }

    private sealed class Synthesizer(Func<CancellationToken, Task<byte[]>> synthesize) : ISpeechSynthesizer
    {
        public Task<byte[]> SynthesizeAsync(string text, int ratePercent, AppLanguage language, CancellationToken token) => synthesize(token);
    }

    private sealed class ResponseHandler(string response) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
