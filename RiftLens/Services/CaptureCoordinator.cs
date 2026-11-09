using System.Threading.Channels;
using System.Diagnostics;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class CaptureCoordinator : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly ScreenCaptureService _capture = new();
    private readonly ImageChangeDetector _changeDetector = new();
    private readonly BundledOcrService _ocr = new();
    private readonly MessageDeduplicator _deduplicator = new();
    private readonly ChatMessageClassifier _classifier = new();
    private readonly HeroDictionary _heroes;
    private readonly PlayerHeroSessionTracker _playerHeroes;
    private readonly TacticalSpeechLimiter _speechLimiter = new();
    private readonly VisionOcrPostProcessor _visionPostProcessor;
    private readonly ITranslationService _translator;
    private readonly IVisionOcrService? _visionOcr;
    private readonly TimeProvider _clock;
    private readonly Channel<ClassifiedGameMessage> _messages = Channel.CreateBounded<ClassifiedGameMessage>(new BoundedChannelOptions(24)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly object _pipelineLock = new();
    private readonly object _requestGate = new();
    private CancellationTokenSource _requestCancellation = new();
    private readonly object _visionScheduleLock = new();
    private Bitmap? _pendingVisionBitmap;
    private OcrDocument? _pendingVisionLocalDocument;
    private DateTimeOffset _pendingVisionCapturedAt;
    private MessageLifetime? _pendingVisionLifetime;
    private ClassifiedGameMessage[] _pendingHeldPlayerChat = Array.Empty<ClassifiedGameMessage>();
    private int _pendingVisionGeneration;
    private int _visionWorkerRunning;
    private Task? _visionWorkerTask;

    private CancellationTokenSource? _cancellation;
    private Task? _captureTask;
    private Task? _translationTask;
    private int _paused;
    private int _stateGeneration;
    private int _evaluationFrameSequence;
    private int _evaluationCaptureEnabled;
    private int _visionFallbackAnnounced;
    private int _visionUploadBlockedAnnounced;
    private DateTimeOffset _lastEvaluationPeriodicSampleAt = DateTimeOffset.MinValue;

    public event Action<string>? StatusChanged;
    public event Action<OcrBatch>? OcrRecognized;
    public event Action<TranslationResult>? TranslationReady;
    public event Action<MessageDiagnosticObservation>? DiagnosticObserved;
    public event Action<EvaluationFrameObservation>? EvaluationFrameObserved;

    public bool IsRunning => _cancellation is { IsCancellationRequested: false }
        && _captureTask is not { IsCompleted: true }
        && _translationTask is not { IsCompleted: true };
    public bool IsPaused => Volatile.Read(ref _paused) == 1;
    public bool EvaluationCaptureEnabled
    {
        get => Volatile.Read(ref _evaluationCaptureEnabled) == 1;
        set
        {
            Interlocked.Exchange(ref _evaluationCaptureEnabled, value ? 1 : 0);
            if (value)
            {
                _lastEvaluationPeriodicSampleAt = DateTimeOffset.MinValue;
            }
        }
    }

    public CaptureCoordinator(
        AppSettings settings,
        HeroDictionary heroes,
        ITranslationService translator,
        IVisionOcrService? visionOcr = null,
        TimeProvider? clock = null)
    {
        _settings = settings;
        _heroes = heroes;
        _playerHeroes = new PlayerHeroSessionTracker(heroes);
        _visionPostProcessor = new VisionOcrPostProcessor(heroes);
        _translator = translator;
        _visionOcr = visionOcr;
        _clock = clock ?? TimeProvider.System;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!_settings.CaptureRegion.IsValid)
        {
            throw new InvalidOperationException(L(
                "请先框选聊天或屏幕文字区域。",
                "Select the chat or on-screen text region first."));
        }

        _changeDetector.Reset();
        _deduplicator.Reset();
        _playerHeroes.Reset();
        _evaluationFrameSequence = 0;
        Interlocked.Exchange(ref _visionFallbackAnnounced, 0);
        Interlocked.Exchange(ref _visionUploadBlockedAnnounced, 0);
        DropPendingVision();
        _lastEvaluationPeriodicSampleAt = DateTimeOffset.MinValue;
        ClearPendingMessages();
        InvalidateRequests();
        Interlocked.Exchange(ref _paused, 0);
        _cancellation = new CancellationTokenSource();
        _captureTask = RunWorkerAsync(CaptureLoopAsync, _cancellation.Token);
        _translationTask = RunWorkerAsync(TranslationLoopAsync, _cancellation.Token);
        StatusChanged?.Invoke(UsesVisionOcr
            ? L(
                "正在使用 DeepSeek V4 Vision 实验识别；首次结果只建立基线，不播报旧消息。",
                "Using experimental DeepSeek V4 Vision recognition. The first result only builds a baseline.")
            : L(
                "正在使用本地 OCR；首次识别用于建立基线，不会播报旧消息。",
                "Using local OCR. The first scan builds a baseline and will not read old messages."));
    }

    public void SetPaused(bool paused)
    {
        if (!IsRunning)
        {
            return;
        }

        var newValue = paused ? 1 : 0;
        if (Interlocked.Exchange(ref _paused, newValue) == newValue)
        {
            return;
        }

        InvalidateRequests();
        ClearPendingMessages();
        DropPendingVision();
        if (paused)
        {
            StatusChanged?.Invoke(L(
                "翻译已暂停；截图、识别和新的联网请求均已停止。",
                "Translation paused. Capture, OCR, and new network requests are stopped."));
        }
        else
        {
            _changeDetector.Reset();
            _deduplicator.Reset();
            StatusChanged?.Invoke(L(
                "翻译已继续；首次识别仅重新建立聊天基线。",
                "Translation resumed. The first scan only rebuilds the chat baseline."));
        }
    }

    public async Task StopAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        InvalidateRequests();
        ClearPendingMessages();
        DropPendingVision();
        _cancellation.Cancel();
        try
        {
            await Task.WhenAll(
                _captureTask ?? Task.CompletedTask,
                _translationTask ?? Task.CompletedTask,
                _visionWorkerTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _captureTask = null;
            _translationTask = null;
            _visionWorkerTask = null;
            Interlocked.Exchange(ref _visionWorkerRunning, 0);
            StatusChanged?.Invoke(L("监听已停止。", "Listening stopped."));
        }
    }

    public async Task<string> CaptureAndRecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        using var bitmap = _capture.Capture(_settings.CaptureRegion);
        if (UsesVisionOcr)
        {
            return (await RecognizeVisionHybridAsync(bitmap, cancellationToken)).Document.RawText;
        }
        return await _ocr.RecognizeAsync(bitmap, cancellationToken, _settings.OcrScale);
    }

    private void InvalidateRequests()
    {
        CancellationTokenSource previous;
        lock (_requestGate)
        {
            Interlocked.Increment(ref _stateGeneration);
            previous = _requestCancellation;
            _requestCancellation = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
    }

    private CancellationTokenSource CreateRequestCancellation(int generation, CancellationToken lifetime)
    {
        lock (_requestGate)
        {
            var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _requestCancellation.Token);
            if (IsPaused || generation != Volatile.Read(ref _stateGeneration)) request.Cancel();
            return request;
        }
    }

    private async Task RunWorkerAsync(Func<CancellationToken, Task> worker, CancellationToken token)
    {
        try
        {
            await worker(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _cancellation?.Cancel();
            StatusChanged?.Invoke(L(
                $"监听意外停止，请重新开始：{exception.Message}",
                $"Listening stopped unexpectedly. Start again: {exception.Message}"));
        }
    }

    public string OcrEngineName => UsesVisionOcr
        ? "DeepSeek V4 Flash Vision Exp"
        : BundledOcrService.DisplayName;

    private bool UsesVisionOcr =>
        _settings.RecognitionMode == RecognitionPipelineMode.DeepSeekVisionExperimental
        && _settings.VisionPrivacyConsent
        && _visionOcr is not null;

    /// <summary>
    /// 过滤 OCR 噪声：太短/无意义字符/纯符号的行不发送给 AI。
    /// </summary>
    private static bool IsPlausibleGameChat(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;
        if (!line.Any(char.IsLetter)) return false;
        var simplifiedLine = OfficialPhraseNormalizer.Normalize(line).Trim();
        if (simplifiedLine is "单/双排积分" or "法师之靴" or "魔法穿透") return false;
        if (simplifiedLine.Contains("直播设置", StringComparison.Ordinal)
            || simplifiedLine.Contains("中控台", StringComparison.Ordinal)
            || simplifiedLine.Contains("素材库", StringComparison.Ordinal)
            || simplifiedLine.Contains("绿幕直播", StringComparison.Ordinal)
            || simplifiedLine.Contains("互动工具", StringComparison.Ordinal)
            || simplifiedLine.Contains("直播流量", StringComparison.Ordinal)
            || simplifiedLine.Contains("精品母带", StringComparison.Ordinal))
        {
            return false;
        }
        if (OfficialPhraseNormalizer.IsTruncatedGeneratedPayload(simplifiedLine)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(
                simplifiedLine,
                @"^\s*\d{1,2}\s*[-/.]\s*\d{1,2}\s+\d{1,2}\s*[:：]\s*\d{2}\s*(?:胜|负)\s*\d+\s*/\s*\d+\s*/\s*\d+",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return false;
        }

        // 统计可读字符（韩文/中文/英文字母/数字）
        var readable = 0;
        var total = 0;
        foreach (var ch in line)
        {
            if (char.IsWhiteSpace(ch)) continue;
            total++;
            if (IsReadableGameChar(ch)) readable++;
        }

        if (total == 0) return false;

        // 可读字符占比低于 40% → 噪声
        var ratio = readable / (double)total;
        if (ratio < 0.40) return false;

        // 全是同一字符的重复 → 噪声
        var distinctCount = line.Where(c => !char.IsWhiteSpace(c)).Distinct().Count();
        if (distinctCount <= 2 && line.Length > 4) return false;

        return true;
    }

    private static bool IsReadableGameChar(char ch) =>
        char.IsLetterOrDigit(ch)               // 英文/数字/各语言字母
        || ch >= 0xAC00                         // 韩文音节起（0xAC00='가'）
        || (ch >= 0x3130 && ch <= 0x318F)      // 韩文兼容字母
        || (ch >= 0x4E00 && ch <= 0x9FFF)      // CJK 汉字
        || (ch >= 0x3000 && ch <= 0x303F)      // CJK 标点
        || (ch >= 0xFF00 && ch <= 0xFFEF)      // 全角字符/标点
        || ch == ':' || ch == '：'              // 冒号（玩家名:消息格式）
        || ch == '[' || ch == ']'               // 频道标签
        || ch == '!' || ch == '？' || ch == '?'; // 标点

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_translator is IDisposable disposable)
        {
            disposable.Dispose();
        }
        if (_visionOcr is IDisposable visionDisposable)
        {
            visionDisposable.Dispose();
        }
        _ocr.Dispose();
        _requestCancellation.Dispose();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(_settings.PollIntervalMs, 250, 3000);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsPaused)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            try
            {
                var generation = Volatile.Read(ref _stateGeneration);
                using var bitmap = _capture.Capture(_settings.CaptureRegion);
                var meaningfulChange = _changeDetector.HasMeaningfulChange(
                    bitmap,
                    _settings.ChangeThreshold);
                if (!meaningfulChange)
                {
                    var periodicAt = DateTimeOffset.UtcNow;
                    if (EvaluationCaptureEnabled
                        && EvaluationFrameObserved is not null
                        && periodicAt - _lastEvaluationPeriodicSampleAt >= TimeSpan.FromSeconds(2))
                    {
                        _lastEvaluationPeriodicSampleAt = periodicAt;
                        EmitEvaluationFrame(
                            bitmap,
                            document: null,
                            periodicAt,
                            captureKind: "periodic_sample",
                            meaningfulChange: false,
                            ocrElapsedMs: 0,
                            localPipelineElapsedMs: 0);
                    }
                }
                if (meaningfulChange)
                {
                    // Avoid OCR during LOL's short chat-scroll animation. Existing settings
                    // below 180 ms are raised at runtime without adding another screen capture.
                    await Task.Delay(Math.Clamp(_settings.StabilizationDelayMs, 180, 600), cancellationToken);
                    if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }
                    using var stableBitmap = _capture.Capture(_settings.CaptureRegion);
                    var capturedAt = _clock.GetUtcNow();
                    var lifetime = MessageLifetime.FromCapture(capturedAt, TimeSpan.FromSeconds(6), _clock);
                    var localPipelineTimer = Stopwatch.StartNew();
                    var ocrTimer = Stopwatch.StartNew();
                    var localDocument = await _ocr.RecognizeDetailedAsync(
                        stableBitmap,
                        cancellationToken,
                        _settings.OcrScale);
                    ocrTimer.Stop();
                    if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }

                    var frameSequence = Interlocked.Increment(ref _evaluationFrameSequence);
                    var frameId = $"frame-{frameSequence:000000}";
                    var observed = ObserveDocument(
                        localDocument,
                        capturedAt,
                        frameId,
                        "local-ppocrv5", lifetime);
                    localPipelineTimer.Stop();

                    var captureKind = "ocr_stable_frame";
                    var publishNow = observed;
                    if (UsesVisionOcr)
                    {
                        if (VisionUploadGuard.Evaluate(stableBitmap).ShouldBlock)
                        {
                            captureKind = "vision_upload_blocked_obstruction";
                            AnnounceVisionUploadBlocked();
                        }
                        else if (VisionDispatchPolicy.NeedsVision(observed.Select(item => item.Message)))
                        {
                            captureKind = "vision_scheduled_latest_frame";
                            publishNow = observed
                                .Where(item => VisionDispatchPolicy.PublishImmediately(item.Message))
                                .ToArray();
                            ScheduleVision(
                                stableBitmap,
                                localDocument,
                                capturedAt,
                                observed
                                    .Select(item => item.Message)
                                    .Where(message => !VisionDispatchPolicy.PublishImmediately(message))
                                    .ToArray(),
                                generation, lifetime);
                        }
                        else
                        {
                            captureKind = "vision_skipped_local_hud";
                        }
                    }

                    EmitEvaluationFrame(
                        stableBitmap,
                        localDocument,
                        capturedAt,
                        captureKind,
                        meaningfulChange: true,
                        ocrElapsedMs: ocrTimer.Elapsed.TotalMilliseconds,
                        localPipelineElapsedMs: localPipelineTimer.Elapsed.TotalMilliseconds,
                        frameId);
                    await PublishObservedAsync(
                        localDocument,
                        publishNow,
                        capturedAt,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(L(
                    $"识别暂时失败：{exception.Message}",
                    $"Recognition temporarily failed: {exception.Message}"));
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private void EmitEvaluationFrame(
        Bitmap bitmap,
        OcrDocument? document,
        DateTimeOffset capturedAt,
        string captureKind,
        bool meaningfulChange,
        double ocrElapsedMs,
        double localPipelineElapsedMs,
        string? frameId = null)
    {
        var handler = EvaluationFrameObserved;
        if (!EvaluationCaptureEnabled || handler is null)
        {
            return;
        }

        var resolvedFrameId = frameId
            ?? $"frame-{Interlocked.Increment(ref _evaluationFrameSequence):000000}";
        var snapshot = (Bitmap)bitmap.Clone();
        try
        {
            handler(new EvaluationFrameObservation(
                resolvedFrameId,
                capturedAt,
                captureKind,
                meaningfulChange,
                snapshot,
                document,
                Math.Round(ocrElapsedMs, 2),
                Math.Round(localPipelineElapsedMs, 2),
                _settings.PollIntervalMs,
                _settings.OcrScale));
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private async Task TranslationLoopAsync(CancellationToken cancellationToken)
    {
        var turns = new PlayerTurnAggregator(_clock);
        ClassifiedGameMessage? pendingSystemMessage = null;
        var generation = Volatile.Read(ref _stateGeneration);
        var lastPlayerObservedAt = DateTimeOffset.MinValue;
        var partialWaitUsed = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (generation != Volatile.Read(ref _stateGeneration))
            {
                _ = turns.Flush();
                pendingSystemMessage = null;
                _speechLimiter.Reset();
                generation = Volatile.Read(ref _stateGeneration);
                partialWaitUsed = false;
            }

            if (IsPaused)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            // Check independently of reads: a continuously nonempty channel must not
            // postpone a turn just because ReadAsync never times out.
            if (turns.HasReachedMaximumWait)
            {
                var completed = turns.Flush();
                partialWaitUsed = false;
                if (completed is not null)
                    await TranslatePlayerTurnAsync(completed, generation, cancellationToken);
                continue;
            }

            var idleTimeoutMs = Math.Clamp(_settings.SummaryWindowMs, 500, 2500);
            var timeoutMs = turns.HasPending
                ? turns.GetWaitMilliseconds(idleTimeoutMs * (partialWaitUsed ? 2 : 1))
                : pendingSystemMessage is not null ? 450 : Timeout.Infinite;
            var message = await ReadNextMessageAsync(timeoutMs, generation, cancellationToken);

            if (IsPaused || generation != Volatile.Read(ref _stateGeneration)) continue;

            if (message is null)
            {
                if (turns.HasPending)
                {
                    if (!partialWaitUsed && !turns.HasReachedMaximumWait
                        && PartialLineWait.LooksIncomplete(turns.LastContent))
                    {
                        partialWaitUsed = true;
                        continue;
                    }

                    partialWaitUsed = false;
                    var completed = turns.Flush();
                    if (completed is not null)
                    {
                        await TranslatePlayerTurnAsync(completed, generation, cancellationToken);
                    }
                    continue;
                }

                if (pendingSystemMessage is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastPlayerObservedAt >= TimeSpan.FromMilliseconds(2200)
                        && _speechLimiter.CanSpeak(pendingSystemMessage, now)
                        && pendingSystemMessage.Lifetime is { IsExpired: false }
                        && _settings.IsSystemMessageEnabled(pendingSystemMessage.Kind)
                        && !IsPaused
                        && generation == Volatile.Read(ref _stateGeneration))
                    {
                        TranslationReady?.Invoke(CreateSystemResult(pendingSystemMessage));
                        _speechLimiter.MarkSpoken(pendingSystemMessage, now);
                    }
                    pendingSystemMessage = null;
                }
                continue;
            }

            if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
            {
                continue;
            }

            message = EnsureLifetime(message);
            if (message.Lifetime!.IsExpired) continue;

            if (message.Kind == GameMessageKind.PlayerChat)
            {
                lastPlayerObservedAt = DateTimeOffset.UtcNow;
                pendingSystemMessage = null;
                partialWaitUsed = false;
                var completed = turns.Add(message);
                if (completed is not null)
                {
                    await TranslatePlayerTurnAsync(completed, generation, cancellationToken);
                }
                if (PlayerSpeechPolicy.IsLikelyUrgent(message.Content) && turns.HasPending)
                {
                    var urgent = turns.Flush();
                    if (urgent is not null)
                    {
                        await TranslatePlayerTurnAsync(urgent, generation, cancellationToken);
                    }
                }
                continue;
            }

            if (message.Kind == GameMessageKind.Unknown)
            {
                TranslationReady?.Invoke(SystemMessageResultFactory.Create(message, _settings));
                continue;
            }

            if (!_settings.IsSystemMessageEnabled(message.Kind))
            {
                continue;
            }

            var observedAt = DateTimeOffset.UtcNow;
            if (turns.HasPending
                || observedAt - lastPlayerObservedAt < TimeSpan.FromMilliseconds(2200)
                || !_speechLimiter.CanSpeak(message, observedAt))
            {
                continue;
            }

            if (pendingSystemMessage is null || message.Priority >= pendingSystemMessage.Priority)
            {
                pendingSystemMessage = message;
            }
        }
    }

    private async Task<ClassifiedGameMessage?> ReadNextMessageAsync(
        int timeoutMs,
        int generation,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateRequestCancellation(generation, cancellationToken);
        if (timeoutMs != Timeout.Infinite) timeout.CancelAfter(timeoutMs);
        try
        {
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private ClassifiedGameMessage EnsureLifetime(ClassifiedGameMessage message)
    {
        var budget = message.Kind == GameMessageKind.PlayerChat
            ? TimeSpan.FromMilliseconds(PlayerSpeechPolicy.IsLikelyUrgent(message.Content) ? 4500 : 6000)
            : TimeSpan.FromMilliseconds(4000);
        var lifetime = message.Lifetime?.LimitTo(budget)
            ?? MessageLifetime.FromCapture(message.CapturedAt, budget, _clock);
        return message with { CapturedAt = lifetime.CapturedAt, Lifetime = lifetime };
    }

    private static MessageLifetime GetTurnLifetime(PlayerSpeechTurn turn) =>
        turn.GetMessages().Select(message => message.Lifetime!)
            .MinBy(lifetime => lifetime.Remaining)!;

    private async Task TranslatePlayerTurnAsync(
        PlayerSpeechTurn turn,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            turn = turn.SelectMessages(turn.GetMessages().Select(EnsureLifetime)
                .Where(message => !message.Lifetime!.IsExpired).ToArray());
            if (turn.Contents.Count == 0) return;
            var speakerMention = _heroes.FindById(
                    turn.SpeakerHeroId,
                    _settings.PreferChampionPersonalName,
                    _settings.Language)
                ?? _heroes.FindSpeakerMention(
                    turn.SpeakerRaw,
                    _settings.PreferChampionPersonalName,
                    _settings.Language);
            var speakerLabel = speakerMention?.SpokenName
                ?? (_settings.Language == AppLanguage.English ? "Teammate" : "队友");
            if (IsPaused || generation != Volatile.Read(ref _stateGeneration)) return;
            var remainingMessages = new List<ClassifiedGameMessage>();
            if (TryEmitPlayerCallouts(turn, speakerLabel, generation, remainingMessages)
                && remainingMessages.Count == 0)
            {
                return;
            }

            var remainingTurn = turn.SelectMessages(remainingMessages);
            turn = turn.SelectMessages(remainingMessages
                .Where(message => !NonPlayerContentGate.IsBlockedPlayerPayload(message.Content)).ToArray());
            var playerLines = turn.Contents
                .Select(line => _heroes.StabilizeHeroTokens(
                    line,
                    turn.SpeakerHeroId,
                    _settings.Language))
                .ToArray();
            if (playerLines.Length == 0
                || NonPlayerContentGate.ShouldSkipPlayerTranslation(remainingMessages.Count > 0
                    ? remainingMessages.Select(message => message.Content)
                    : turn.RawLines.Concat(turn.Contents)))
            {
                foreach (var original in remainingTurn.GetMessages())
                {
                    var rerouted = EnsureLifetime(_playerHeroes.Observe(
                        _classifier.Classify(original.RawText) with
                        {
                            CapturedAt = original.CapturedAt,
                            Lifetime = original.Lifetime,
                            EvaluationLineId = original.EvaluationLineId,
                            RecognitionEngine = original.RecognitionEngine
                        }));
                    if (rerouted.Kind is GameMessageKind.PlayerChat or GameMessageKind.Unknown
                        || !_settings.IsSystemMessageEnabled(rerouted.Kind))
                    {
                        continue;
                    }

                    var now = DateTimeOffset.UtcNow;
                    if (rerouted.Lifetime!.IsExpired || !_speechLimiter.CanSpeak(rerouted, now))
                    {
                        continue;
                    }

                    TranslationReady?.Invoke(CreateSystemResult(rerouted));
                    _speechLimiter.MarkSpoken(rerouted, now);
                }
                return;
            }

            var glossedLines = playerLines
                .Select(line => GameTermGlossary.ReplaceTerms(line, _settings.Language))
                .ToArray();
            var mentionSource = glossedLines.Append(turn.SpeakerRaw);
            var mentions = _heroes.FindMentions(
                mentionSource,
                _settings.PreferChampionPersonalName,
                _settings.Language).ToList();
            if (speakerMention is not null
                && mentions.All(mention =>
                    !string.Equals(mention.Id, speakerMention.Id, StringComparison.OrdinalIgnoreCase)))
            {
                mentions.Add(speakerMention);
            }
            var canonicalLines = _heroes.CanonicalizeLines(glossedLines, mentions);
            var maxSummaryCharacters = Math.Clamp(_settings.MaxSummaryChars, 24, 96);
            if (_settings.Language == AppLanguage.English)
            {
                // Equivalent English wording is usually longer than Chinese; preserve
                // tactical objects and actions instead of truncating them prematurely.
                maxSummaryCharacters = Math.Max(maxSummaryCharacters, 88);
            }
            var request = new TranslationRequest(
                canonicalLines,
                mentions,
                PlayerSpeechPolicy.GetModelFilter(_settings),
                maxSummaryCharacters,
                speakerLabel,
                _settings.KeepEmotionSummary,
                _settings.Language);
            using var requestCancellation = CreateRequestCancellation(generation, cancellationToken);
            var lifetime = GetTurnLifetime(turn);
            if (lifetime.IsExpired) return;
            requestCancellation.CancelAfter(lifetime.Remaining);
            requestCancellation.Token.ThrowIfCancellationRequested();
            var result = await _translator.TranslateAsync(request, requestCancellation.Token);
            if (lifetime.IsExpired || requestCancellation.IsCancellationRequested
                || IsPaused || generation != Volatile.Read(ref _stateGeneration))
            {
                return;
            }

            result.SourceKind = TranslationSourceKind.PlayerChat;
            result.Lifetime = lifetime;
            result.RecognitionEngine = turn.RecognitionEngine;
            result.RoutedMessageKind = GameMessageKind.PlayerChat;
            result.ClassificationConfidence = 1;
            result.ClassificationRuleId = "player.structure";
            result.Speaker = speakerLabel;
            result.RawText = string.Join("\n", turn.RawLines);
            result.OriginalLines = turn.Contents
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .Take(8)
                .ToList();
            result.EvaluationLineIds = (turn.EvaluationLineIds ?? Array.Empty<string>())
                .Where(lineId => !string.IsNullOrWhiteSpace(lineId))
                .Take(8)
                .ToList();
            result.SpokenSummary = PlayerSpeechTextSanitizer.Clean(result.SpokenSummary, turn);
            if (!PlayerSpeechPolicy.ShouldSpeak(result, _settings)
                || string.IsNullOrWhiteSpace(result.SpokenSummary))
            {
                result.ShouldSpeak = false;
                if (string.IsNullOrWhiteSpace(result.SpeechSilenceReason))
                {
                    result.SpeechSilenceReason = result.ToxicityFiltered
                        ? SpeechSilenceReasons.Toxicity
                        : SpeechSilenceReasons.UserFilter;
                }
            }
            TranslationReady?.Invoke(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeouts and pause cancellation must not kill the consumer.
            if (!IsPaused && generation == Volatile.Read(ref _stateGeneration))
            {
                StatusChanged?.Invoke(L(
                    "本条玩家发言翻译超时，已跳过；继续处理后续消息。",
                    "This translation timed out and was skipped. Listening continues."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(L(
                $"玩家发言翻译暂时失败：{exception.Message}",
                $"Player chat translation temporarily failed: {exception.Message}"));
        }
    }

    private bool TryEmitPlayerCallouts(
        PlayerSpeechTurn turn,
        string speakerLabel,
        int generation,
        List<ClassifiedGameMessage> remainingMessages)
    {
        var formatted = new List<string>();
        var formattedMessages = new List<ClassifiedGameMessage>();
        foreach (var message in turn.GetMessages())
        {
            var kind = PlayerCalloutParser.TryFormat(
                message.Content,
                _settings.Language,
                _heroes,
                _settings.PreferChampionPersonalName,
                out var displayLine);
            switch (kind)
            {
                case PlayerCalloutKind.Formatted when !string.IsNullOrWhiteSpace(displayLine):
                    formatted.Add(displayLine);
                    formattedMessages.Add(message);
                    break;
                case PlayerCalloutKind.Silence:
                    break;
                default:
                    remainingMessages.Add(message);
                    break;
            }
        }

        if (formatted.Count == 0)
        {
            return remainingMessages.Count == 0;
        }

        if (IsPaused || generation != Volatile.Read(ref _stateGeneration)
            || GetTurnLifetime(turn.SelectMessages(formattedMessages)).IsExpired)
        {
            return true;
        }

        TranslationReady?.Invoke(CreatePlayerCalloutResult(turn.SelectMessages(formattedMessages), speakerLabel, formatted));
        return true;
    }

    private TranslationResult CreatePlayerCalloutResult(
        PlayerSpeechTurn turn,
        string speakerLabel,
        IReadOnlyList<string> displayLines)
    {
        var body = string.Join(
            _settings.Language == AppLanguage.English ? ". " : "。",
            displayLines);
        var spoken = _settings.Language == AppLanguage.English
            ? $"{speakerLabel} said {body}"
            : $"{speakerLabel}说，{body}";
        var result = new TranslationResult
        {
            Lifetime = GetTurnLifetime(turn),
            RecognitionEngine = turn.RecognitionEngine,
            SourceKind = TranslationSourceKind.PlayerChat,
            RoutedMessageKind = GameMessageKind.PlayerChat,
            ClassificationConfidence = 1,
            ClassificationRuleId = "player.callout",
            Speaker = speakerLabel,
            Intent = "assist",
            Priority = 2,
            SourceType = "player_chat",
            SpeechAct = "information",
            Modality = "asserted",
            SemanticConfidence = 1,
            SemanticValidationPassed = true,
            Evidence = displayLines[0],
            DisplayTranslation = _settings.Language == AppLanguage.English
                ? $"{speakerLabel}: {body}"
                : $"{speakerLabel}：{body}",
            DisplayLines = displayLines.ToList(),
            SpokenSummary = spoken,
            RawText = string.Join("\n", turn.RawLines),
            OriginalLines = turn.Contents.ToList(),
            EvaluationLineIds = (turn.EvaluationLineIds ?? Array.Empty<string>())
                .Where(lineId => !string.IsNullOrWhiteSpace(lineId))
                .Take(8)
                .ToList(),
            ShouldSpeak = true,
            ExpiresAfterMs = 4500
        };
        if (!PlayerSpeechPolicy.ShouldSpeak(result, _settings)
            || string.IsNullOrWhiteSpace(result.SpokenSummary))
        {
            result.ShouldSpeak = false;
            result.SpeechSilenceReason = SpeechSilenceReasons.UserFilter;
        }

        return result;
    }

    private TranslationResult CreateSystemResult(ClassifiedGameMessage message)
    {
        if (message.Kind != GameMessageKind.TacticalSignal)
        {
            return SystemMessageResultFactory.Create(message, _settings);
        }

        var speakerMention = _heroes.FindById(
                message.SpeakerHeroId,
                _settings.PreferChampionPersonalName,
                _settings.Language)
            ?? _heroes.FindSpeakerMention(
                message.SpeakerRaw,
                _settings.PreferChampionPersonalName,
                _settings.Language);
        var hasKnownInitiator = !string.IsNullOrWhiteSpace(message.SpeakerKey);
        var speaker = speakerMention?.SpokenName
            ?? (hasKnownInitiator
                ? _settings.Language == AppLanguage.English ? "Teammate" : "队友"
                : string.Empty);
        var semanticEvent = message.SemanticEvent is null
            ? null
            : message.SemanticEvent with
            {
                Subject = ResolveSemanticSubject(message.SemanticEvent.Subject)
            };
        var spoken = semanticEvent is not null
            ? GameSemanticEventParser.Format(semanticEvent, speaker, _settings.Language)
            : SignalMessageTemplates.FormatSpokenSignal(
                message.TacticalSummary,
                speaker,
                _settings.Language,
                hasKnownInitiator);
        return new TranslationResult
        {
            Lifetime = EnsureLifetime(message).Lifetime,
            RecognitionEngine = message.RecognitionEngine,
            SourceKind = TranslationSourceKind.TacticalSignal,
            RoutedMessageKind = GameMessageKind.TacticalSignal,
            ClassificationConfidence = message.ClassificationConfidence,
            ClassificationRuleId = message.SystemEvent?.RuleId
                ?? (message.SemanticEvent is null ? "signal.template" : "signal.semantic"),
            Speaker = hasKnownInitiator
                ? speaker
                : _settings.Language == AppLanguage.English ? "Signal" : "信号",
            Intent = semanticEvent is null
                ? "tactical_signal"
                : GameSemanticEventParser.Intent(semanticEvent),
            SourceType = semanticEvent?.SourceType.ToString().ToLowerInvariant() ?? "tactical_signal",
            SpeechAct = semanticEvent?.SpeechAct.ToString().ToLowerInvariant() ?? "marker",
            Subject = semanticEvent?.Subject ?? string.Empty,
            Target = semanticEvent?.Target ?? string.Empty,
            Status = semanticEvent?.Status ?? string.Empty,
            DurationSeconds = semanticEvent?.DurationSeconds,
            Modality = semanticEvent?.Modality.ToString().ToLowerInvariant() ?? "marked",
            SemanticConfidence = semanticEvent?.Confidence ?? 0.8,
            Evidence = semanticEvent?.Evidence ?? message.Content,
            SemanticValidationPassed = semanticEvent is not null,
            Priority = message.Priority,
            DisplayTranslation = spoken,
            DisplayLines = new List<string> { spoken },
            SpokenSummary = spoken,
            RawText = message.RawText,
            OriginalLines = new List<string> { message.Content },
            EvaluationLineIds = string.IsNullOrWhiteSpace(message.EvaluationLineId)
                ? new List<string>()
                : new List<string> { message.EvaluationLineId },
            ShouldSpeak = message.ClassificationConfidence
                >= SystemMessageCatalog.SpeechConfidenceThreshold,
            ExpiresAfterMs = 3500
        };
    }

    private string ResolveSemanticSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        subject = _heroes.CorrectStructuredHeroPrefix(
            subject,
            _settings.PreferChampionPersonalName,
            _settings.Language);
        var localized = GameTermGlossary.ReplaceTerms(subject, _settings.Language);
        var mentions = _heroes.FindMentions(
            new[] { subject },
            _settings.PreferChampionPersonalName,
            _settings.Language);
        foreach (var mention in mentions)
        {
            foreach (var alias in new[]
                     {
                         mention.KoreanName,
                         mention.ChineseOfficial,
                         mention.ChinesePersonal,
                         mention.Id
                     })
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }
                localized = System.Text.RegularExpressions.Regex.Replace(
                    localized,
                    System.Text.RegularExpressions.Regex.Escape(alias),
                    mention.SpokenName,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}
        return localized.Trim();
    }

    private static OcrColorEvidence? FindColorEvidence(
        OcrDocument document,
        string deduplicatedLine)
    {
        var normalizedTarget = NormalizeForEvidenceMatch(deduplicatedLine);
        return document.Lines
            .LastOrDefault(line =>
                NormalizeForEvidenceMatch(line.Text)
                    .Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            ?.ColorEvidence;
    }

    private sealed record ObservedLine(DeduplicatedMessageLine Line, ClassifiedGameMessage Message);

    private ObservedLine[] ObserveDocument(
        OcrDocument document,
        DateTimeOffset capturedAt,
        string frameId,
        string recognitionEngine,
        MessageLifetime? lifetime = null)
    {
        lock (_pipelineLock)
        {
            return _deduplicator
                .FindNewLineResults(document.RawText)
                .Where(line => IsPlausibleGameChat(line.NormalizedText))
                .Select((line, index) => new ObservedLine(
                    line,
                    EnsureLifetime(_playerHeroes.Observe(
                        _classifier.Classify(
                            line.NormalizedText,
                            FindColorEvidence(document, line.NormalizedText)) with
                        {
                            CapturedAt = capturedAt,
                            Lifetime = lifetime,
                            EvaluationLineId = $"{frameId}/line-{index + 1:00}",
                            RecognitionEngine = recognitionEngine
                        }))))
                .ToArray();
        }
    }

    private async Task PublishObservedAsync(
        OcrDocument document,
        IReadOnlyList<ObservedLine> observed,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        foreach (var item in observed)
        {
            DiagnosticObserved?.Invoke(new MessageDiagnosticObservation(
                capturedAt,
                item.Line,
                item.Message,
                item.Message.Kind is GameMessageKind.PlayerChat or GameMessageKind.Unknown
                    || _settings.IsSystemMessageEnabled(item.Message.Kind),
                item.Message.EvaluationLineId.Split('/')[0],
                item.Message.EvaluationLineId));
        }

        var routed = observed
            .Select(item => item.Message)
            .Where(message => message.Kind is GameMessageKind.PlayerChat or GameMessageKind.Unknown
                || _settings.IsSystemMessageEnabled(message.Kind))
            .ToArray();
        if (routed.Length == 0)
        {
            return;
        }

        OcrRecognized?.Invoke(new OcrBatch(
            document.RawText,
            routed.Select(message => message.RawText).ToArray(),
            capturedAt));
        foreach (var message in routed)
        {
            var timed = EnsureLifetime(message);
            if (!timed.Lifetime!.IsExpired)
                await _messages.Writer.WriteAsync(timed, cancellationToken);
        }
    }

    private async Task PublishMessagesAsync(
        IReadOnlyList<ClassifiedGameMessage> messages,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        OcrRecognized?.Invoke(new OcrBatch(
            string.Join(Environment.NewLine, messages.Select(message => message.RawText)),
            messages.Select(message => message.RawText).ToArray(),
            capturedAt));
        foreach (var message in messages)
        {
            var timed = EnsureLifetime(message);
            if (!timed.Lifetime!.IsExpired)
                await _messages.Writer.WriteAsync(timed, cancellationToken);
        }
    }

    private void ScheduleVision(
        Bitmap source,
        OcrDocument localDocument,
        DateTimeOffset capturedAt,
        ClassifiedGameMessage[] heldPlayerChat,
        int generation,
        MessageLifetime? lifetime = null)
    {
        var clone = source.Clone(new Rectangle(Point.Empty, source.Size), source.PixelFormat);
        lock (_visionScheduleLock)
        {
            _pendingVisionBitmap?.Dispose();
            _pendingVisionBitmap = clone;
            _pendingVisionLocalDocument = localDocument;
            _pendingVisionCapturedAt = capturedAt;
            _pendingVisionLifetime = lifetime ?? MessageLifetime.FromCapture(capturedAt, TimeSpan.FromSeconds(6), _clock);
            _pendingHeldPlayerChat = heldPlayerChat;
            _pendingVisionGeneration = generation;
        }

        if (Interlocked.CompareExchange(ref _visionWorkerRunning, 1, 0) != 0)
        {
            return;
        }

        var token = _cancellation?.Token ?? CancellationToken.None;
        _visionWorkerTask = DrainVisionAsync(token);
    }

    private void DropPendingVision()
    {
        lock (_visionScheduleLock)
        {
            _pendingVisionBitmap?.Dispose();
            _pendingVisionBitmap = null;
            _pendingVisionLocalDocument = null;
            _pendingVisionLifetime = null;
            _pendingHeldPlayerChat = Array.Empty<ClassifiedGameMessage>();
        }
    }

    private void AnnounceVisionUploadBlocked()
    {
        if (Interlocked.Exchange(ref _visionUploadBlockedAnnounced, 1) == 0)
        {
            StatusChanged?.Invoke(L(
                "检测到框选区域大面积被遮挡，已在本地跳过本帧 Vision 上传。",
                "A large obstruction covers the selected region. This Vision upload was blocked locally."));
        }
    }

    private async Task DrainVisionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Bitmap? frame;
            OcrDocument? localDocument;
            DateTimeOffset capturedAt;
            MessageLifetime? lifetime;
            ClassifiedGameMessage[] held;
            var workGeneration = 0;
            lock (_visionScheduleLock)
            {
                frame = _pendingVisionBitmap;
                localDocument = _pendingVisionLocalDocument;
                capturedAt = _pendingVisionCapturedAt;
                lifetime = _pendingVisionLifetime;
                held = _pendingHeldPlayerChat;
                workGeneration = _pendingVisionGeneration;
                _pendingVisionBitmap = null;
                _pendingVisionLocalDocument = null;
                _pendingVisionLifetime = null;
                _pendingHeldPlayerChat = Array.Empty<ClassifiedGameMessage>();
            }

            if (frame is null)
            {
                Interlocked.Exchange(ref _visionWorkerRunning, 0);
                lock (_visionScheduleLock)
                {
                    if (_pendingVisionBitmap is not null
                        && Interlocked.CompareExchange(ref _visionWorkerRunning, 1, 0) == 0)
                    {
                        continue;
                    }
                }

                return;
            }

            try
            {
                using (frame)
                {
                    if (lifetime is null || lifetime.IsExpired
                        || IsPaused || workGeneration != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }

                    using var requestCancellation = CreateRequestCancellation(workGeneration, cancellationToken);
                    requestCancellation.CancelAfter(lifetime.Remaining);
                    requestCancellation.Token.ThrowIfCancellationRequested();
                    var hybrid = await RecognizeVisionGivenLocalAsync(
                        frame,
                        localDocument,
                        requestCancellation.Token);
                    if (lifetime.IsExpired || requestCancellation.IsCancellationRequested
                        || IsPaused || workGeneration != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }

                    if (hybrid.UploadBlocked)
                    {
                        AnnounceVisionUploadBlocked();
                        continue;
                    }

                    if (hybrid.UsedVision)
                    {
                        Interlocked.Exchange(ref _visionFallbackAnnounced, 0);
                        var frameId = $"frame-{Interlocked.Increment(ref _evaluationFrameSequence):000000}";
                        var observed = ObserveDocument(
                            hybrid.Document,
                            capturedAt,
                            frameId,
                            DeepSeekVisionOcrService.ModelName + "+local-evidence", lifetime);
                        var playerLines = observed
                            .Where(item => !VisionDispatchPolicy.PublishImmediately(item.Message))
                            .ToArray();
                        if (playerLines.Length > 0)
                        {
                            await PublishObservedAsync(
                                hybrid.Document,
                                playerLines,
                                capturedAt,
                                cancellationToken);
                        }
                        else
                        {
                            await PublishMessagesAsync(held, capturedAt, cancellationToken);
                        }
                    }
                    else
                    {
                        if (Interlocked.Exchange(ref _visionFallbackAnnounced, 1) == 0)
                        {
                            StatusChanged?.Invoke(L(
                                $"DeepSeek Vision 暂时不可用，已回退本地 OCR：{hybrid.VisionError}",
                                $"DeepSeek Vision is temporarily unavailable; using local OCR: {hybrid.VisionError}"));
                        }

                        await PublishMessagesAsync(held, capturedAt, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Pause invalidates this frame; the worker remains available after resume.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(L(
                    $"Vision 识别暂时失败：{exception.Message}",
                    $"Vision recognition temporarily failed: {exception.Message}"));
                try
                {
                    await PublishMessagesAsync(held, capturedAt, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }
        }
    }

    private async Task<VisionHybridRecognition> RecognizeVisionGivenLocalAsync(
        Bitmap bitmap,
        OcrDocument? localDocument,
        CancellationToken cancellationToken)
    {
        var uploadDecision = VisionUploadGuard.Evaluate(bitmap);
        if (uploadDecision.ShouldBlock)
        {
            return new VisionHybridRecognition(
                localDocument ?? OcrDocument.FromText(string.Empty),
                UsedVision: false,
                UsedLocalEvidence: localDocument is not null,
                VisionError: string.Empty,
                UploadBlocked: true,
                UploadBlockReason: uploadDecision.Reason);
        }

        using var visionBitmap = VisionFramePreparer.Prepare(bitmap);
        try
        {
            var visionDocument = await _visionOcr!.RecognizeAsync(visionBitmap, cancellationToken);
            var processed = _visionPostProcessor.Process(bitmap, visionDocument, localDocument);
            return new VisionHybridRecognition(
                processed.Document,
                UsedVision: true,
                UsedLocalEvidence: localDocument is not null,
                VisionError: string.Empty,
                UploadBlocked: false,
                UploadBlockReason: string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (localDocument is not null)
            {
                return new VisionHybridRecognition(
                    localDocument,
                    UsedVision: false,
                    UsedLocalEvidence: true,
                    VisionError: exception.Message,
                    UploadBlocked: false,
                    UploadBlockReason: string.Empty);
            }

            throw;
        }
    }

    private async Task<VisionHybridRecognition> RecognizeVisionHybridAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        var uploadDecision = VisionUploadGuard.Evaluate(bitmap);
        if (uploadDecision.ShouldBlock)
        {
            return new VisionHybridRecognition(
                OcrDocument.FromText(string.Empty),
                UsedVision: false,
                UsedLocalEvidence: false,
                VisionError: string.Empty,
                UploadBlocked: true,
                UploadBlockReason: uploadDecision.Reason);
        }

        using var localBitmap = bitmap.Clone(
            new Rectangle(Point.Empty, bitmap.Size),
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var visionBitmap = VisionFramePreparer.Prepare(bitmap);
        var localTask = _ocr.RecognizeDetailedAsync(
            localBitmap,
            cancellationToken,
            _settings.OcrScale);
        var visionTask = _visionOcr!.RecognizeAsync(visionBitmap, cancellationToken);

        OcrDocument? visionDocument = null;
        OcrDocument? localDocument = null;
        Exception? visionError = null;
        try
        {
            visionDocument = await visionTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await localTask;
            }
            catch
            {
                // Both operations share the same cancellation token; wait only
                // so the cloned bitmap is not disposed while local OCR still reads it.
            }
            throw;
        }
        catch (Exception exception)
        {
            visionError = exception;
        }

        try
        {
            localDocument = await localTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (visionDocument is not null)
        {
            // DeepSeek remains usable if local evidence collection fails.
        }

        if (visionDocument is not null)
        {
            var processed = _visionPostProcessor.Process(bitmap, visionDocument, localDocument);
            return new VisionHybridRecognition(
                processed.Document,
                UsedVision: true,
                UsedLocalEvidence: localDocument is not null,
                VisionError: string.Empty,
                UploadBlocked: false,
                UploadBlockReason: string.Empty);
        }

        if (localDocument is not null)
        {
            return new VisionHybridRecognition(
                localDocument,
                UsedVision: false,
                UsedLocalEvidence: true,
                VisionError: visionError?.Message ?? "unknown Vision error",
                UploadBlocked: false,
                UploadBlockReason: string.Empty);
        }

        throw new InvalidOperationException(
            "DeepSeek Vision and bundled local OCR both failed.",
            visionError);
    }

    private static string NormalizeForEvidenceMatch(string line) =>
        MessageDeduplicator.StripGameTimestamp(line)
            .Normalize(System.Text.NormalizationForm.FormKC)
            .Trim();

    private void ClearPendingMessages()
    {
        while (_messages.Reader.TryRead(out _))
        {
        }
    }

    private string L(string chinese, string english) =>
        _settings.Language == AppLanguage.English ? english : chinese;

    private sealed record VisionHybridRecognition(
        OcrDocument Document,
        bool UsedVision,
        bool UsedLocalEvidence,
        string VisionError,
        bool UploadBlocked,
        string UploadBlockReason);
}

public sealed record EvaluationFrameObservation(
    string FrameId,
    DateTimeOffset CapturedAt,
    string CaptureKind,
    bool MeaningfulChange,
    Bitmap Screenshot,
    OcrDocument? Document,
    double OcrElapsedMs,
    double LocalPipelineElapsedMs,
    int PollIntervalMs,
    int OcrScale);
