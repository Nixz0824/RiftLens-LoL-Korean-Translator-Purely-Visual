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
    private readonly ITranslationService _translator;
    private readonly Channel<ClassifiedGameMessage> _messages = Channel.CreateBounded<ClassifiedGameMessage>(new BoundedChannelOptions(24)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });

    private CancellationTokenSource? _cancellation;
    private Task? _captureTask;
    private Task? _translationTask;
    private int _paused;
    private int _stateGeneration;
    private int _evaluationFrameSequence;
    private int _evaluationCaptureEnabled;
    private DateTimeOffset _lastEvaluationPeriodicSampleAt = DateTimeOffset.MinValue;

    public event Action<string>? StatusChanged;
    public event Action<OcrBatch>? OcrRecognized;
    public event Action<TranslationResult>? TranslationReady;
    public event Action<MessageDiagnosticObservation>? DiagnosticObserved;
    public event Action<EvaluationFrameObservation>? EvaluationFrameObserved;

    public bool IsRunning => _cancellation is { IsCancellationRequested: false };
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

    public CaptureCoordinator(AppSettings settings, HeroDictionary heroes, ITranslationService translator)
    {
        _settings = settings;
        _heroes = heroes;
        _playerHeroes = new PlayerHeroSessionTracker(heroes);
        _translator = translator;
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
        _lastEvaluationPeriodicSampleAt = DateTimeOffset.MinValue;
        ClearPendingMessages();
        Interlocked.Increment(ref _stateGeneration);
        Interlocked.Exchange(ref _paused, 0);
        _cancellation = new CancellationTokenSource();
        _captureTask = CaptureLoopAsync(_cancellation.Token);
        _translationTask = TranslationLoopAsync(_cancellation.Token);
        StatusChanged?.Invoke(L(
            "正在监听；首次识别用于建立基线，不会播报旧消息。",
            "Listening. The first scan builds a baseline and will not read old messages."));
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

        Interlocked.Increment(ref _stateGeneration);
        ClearPendingMessages();
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

        Interlocked.Increment(ref _stateGeneration);
        ClearPendingMessages();
        _cancellation.Cancel();
        try
        {
            await Task.WhenAll(_captureTask ?? Task.CompletedTask, _translationTask ?? Task.CompletedTask);
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
            StatusChanged?.Invoke(L("监听已停止。", "Listening stopped."));
        }
    }

    public async Task<string> CaptureAndRecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        using var bitmap = _capture.Capture(_settings.CaptureRegion);
        return await _ocr.RecognizeAsync(bitmap, cancellationToken, _settings.OcrScale);
    }

    public string OcrEngineName => BundledOcrService.DisplayName;

    /// <summary>
    /// 过滤 OCR 噪声：太短/无意义字符/纯符号的行不发送给 AI。
    /// </summary>
    private static bool IsPlausibleGameChat(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;

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
        _ocr.Dispose();
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
                    var capturedAt = DateTimeOffset.UtcNow;
                    var localPipelineTimer = Stopwatch.StartNew();
                    var ocrTimer = Stopwatch.StartNew();
                    var document = await _ocr.RecognizeDetailedAsync(
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
                    var observed = _deduplicator
                        .FindNewLineResults(document.RawText)
                        .Where(line => IsPlausibleGameChat(line.NormalizedText))
                        .Select((line, index) => new
                        {
                            Line = line,
                            Message = _playerHeroes.Observe(
                                _classifier.Classify(
                                    line.NormalizedText,
                                    FindColorEvidence(document, line.NormalizedText)) with
                                {
                                    CapturedAt = capturedAt,
                                    EvaluationLineId = $"{frameId}/line-{index + 1:00}"
                                })
                        })
                        .ToArray();
                    localPipelineTimer.Stop();
                    EmitEvaluationFrame(
                        stableBitmap,
                        document,
                        capturedAt,
                        captureKind: "ocr_stable_frame",
                        meaningfulChange: true,
                        ocrElapsedMs: ocrTimer.Elapsed.TotalMilliseconds,
                        localPipelineElapsedMs: localPipelineTimer.Elapsed.TotalMilliseconds,
                        frameId);
                    foreach (var item in observed)
                    {
                        DiagnosticObserved?.Invoke(new MessageDiagnosticObservation(
                            capturedAt,
                            item.Line,
                            item.Message,
                            item.Message.Kind is GameMessageKind.PlayerChat or GameMessageKind.Unknown
                                || _settings.IsSystemMessageEnabled(item.Message.Kind),
                            frameId,
                            item.Message.EvaluationLineId));
                    }
                    var routed = observed
                        .Select(item => item.Message)
                        .Where(message => message.Kind is GameMessageKind.PlayerChat or GameMessageKind.Unknown
                            || _settings.IsSystemMessageEnabled(message.Kind))
                        .ToArray();
                    if (routed.Length > 0)
                    {
                        var batch = new OcrBatch(
                            document.RawText,
                            routed.Select(message => message.RawText).ToArray(),
                            capturedAt);
                        OcrRecognized?.Invoke(batch);
                        foreach (var message in routed)
                        {
                            await _messages.Writer.WriteAsync(message, cancellationToken);
                        }
                    }
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
        var turns = new PlayerTurnAggregator();
        ClassifiedGameMessage? pendingSystemMessage = null;
        var generation = Volatile.Read(ref _stateGeneration);
        var lastPlayerObservedAt = DateTimeOffset.MinValue;
        var lastSystemMessageSpokenAt = DateTimeOffset.MinValue;
        var partialWaitUsed = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (generation != Volatile.Read(ref _stateGeneration))
            {
                _ = turns.Flush();
                pendingSystemMessage = null;
                generation = Volatile.Read(ref _stateGeneration);
            }

            var timeoutMs = turns.HasPending
                ? Math.Clamp(_settings.SummaryWindowMs, 500, 2500)
                : pendingSystemMessage is not null ? 450 : Timeout.Infinite;
            var message = await ReadNextMessageAsync(timeoutMs, cancellationToken);

            if (message is null)
            {
                if (turns.HasPending)
                {
                    if (!partialWaitUsed && PartialLineWait.LooksIncomplete(turns.LastContent))
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
                    var messageAge = now - pendingSystemMessage.CapturedAt;
                    if (now - lastPlayerObservedAt >= TimeSpan.FromMilliseconds(2200)
                        && now - lastSystemMessageSpokenAt >= TimeSpan.FromSeconds(4)
                        && messageAge <= TimeSpan.FromSeconds(4)
                        && _settings.IsSystemMessageEnabled(pendingSystemMessage.Kind)
                        && !IsPaused
                        && generation == Volatile.Read(ref _stateGeneration))
                    {
                        TranslationReady?.Invoke(CreateSystemResult(pendingSystemMessage));
                        lastSystemMessageSpokenAt = now;
                    }
                    pendingSystemMessage = null;
                }
                continue;
            }

            if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
            {
                continue;
            }

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
                || observedAt - lastSystemMessageSpokenAt < TimeSpan.FromSeconds(4))
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
        CancellationToken cancellationToken)
    {
        if (timeoutMs == Timeout.Infinite)
        {
            return await _messages.Reader.ReadAsync(cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task TranslatePlayerTurnAsync(
        PlayerSpeechTurn turn,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var glossedLines = turn.Contents
                .Select(line => GameTermGlossary.ReplaceTerms(line, _settings.Language))
                .ToArray();
            var mentionSource = glossedLines.Append(turn.SpeakerRaw);
            var mentions = _heroes.FindMentions(
                mentionSource,
                _settings.PreferChampionPersonalName,
                _settings.Language).ToList();
            var speakerMention = _heroes.FindById(
                    turn.SpeakerHeroId,
                    _settings.PreferChampionPersonalName,
                    _settings.Language)
                ?? _heroes.FindSpeakerMention(
                    turn.SpeakerRaw,
                    _settings.PreferChampionPersonalName,
                    _settings.Language);
            if (speakerMention is not null
                && mentions.All(mention =>
                    !string.Equals(mention.Id, speakerMention.Id, StringComparison.OrdinalIgnoreCase)))
            {
                mentions.Add(speakerMention);
            }
            var speakerLabel = speakerMention?.SpokenName
                ?? (_settings.Language == AppLanguage.English ? "Teammate" : "队友");
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
            var result = await _translator.TranslateAsync(request, cancellationToken);
            if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
            {
                return;
            }

            result.SourceKind = TranslationSourceKind.PlayerChat;
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
