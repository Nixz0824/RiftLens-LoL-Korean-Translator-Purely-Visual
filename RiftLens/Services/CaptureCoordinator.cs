using System.Threading.Channels;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class CaptureCoordinator : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly ScreenCaptureService _capture = new();
    private readonly ImageChangeDetector _changeDetector = new();
    private readonly BitmapPreprocessor _preprocessor = new();
    private readonly BundledOcrService _ocr = new();
    private readonly MessageDeduplicator _deduplicator = new();
    private readonly ChatMessageClassifier _classifier = new();
    private readonly HeroDictionary _heroes;
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

    public event Action<string>? StatusChanged;
    public event Action<OcrBatch>? OcrRecognized;
    public event Action<TranslationResult>? TranslationReady;

    public bool IsRunning => _cancellation is { IsCancellationRequested: false };
    public bool IsPaused => Volatile.Read(ref _paused) == 1;

    public CaptureCoordinator(AppSettings settings, HeroDictionary heroes, ITranslationService translator)
    {
        _settings = settings;
        _heroes = heroes;
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
        using var prepared = _preprocessor.Prepare(bitmap, _settings.OcrScale);
        return await _ocr.RecognizeAsync(prepared, cancellationToken);
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
                if (_changeDetector.HasMeaningfulChange(bitmap, _settings.ChangeThreshold))
                {
                    // Avoid OCR during LOL's short chat-scroll animation. Existing settings
                    // below 180 ms are raised at runtime without adding another screen capture.
                    await Task.Delay(Math.Clamp(_settings.StabilizationDelayMs, 180, 600), cancellationToken);
                    if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }
                    using var stableBitmap = _capture.Capture(_settings.CaptureRegion);
                    using var prepared = _preprocessor.Prepare(stableBitmap, _settings.OcrScale);
                    var rawText = await _ocr.RecognizeAsync(prepared, cancellationToken);
                    if (IsPaused || generation != Volatile.Read(ref _stateGeneration))
                    {
                        continue;
                    }
                    var capturedAt = DateTimeOffset.UtcNow;
                    var routed = _deduplicator
                        .FindNewLines(rawText)
                        .Where(IsPlausibleGameChat)
                        .Select(line => _classifier.Classify(line, _settings.SystemMessageFilter) with { CapturedAt = capturedAt })
                        .Where(message => message.Kind == GameMessageKind.PlayerChat
                            || (message.Kind == GameMessageKind.TacticalSignal && _settings.SignalVoiceEnabled)
                            || (message.Kind == GameMessageKind.Unknown && _settings.SystemMessageFilter == SystemMessageFilterMode.Off))
                        .Select(message => message.Kind == GameMessageKind.Unknown
                            ? message with
                            {
                                Kind = GameMessageKind.PlayerChat,
                                SpeakerKey = "UNCLASSIFIED",
                                SpeakerRaw = string.Empty,
                                Content = message.RawText
                            }
                            : message)
                        .ToArray();
                    if (routed.Length > 0)
                    {
                        var batch = new OcrBatch(rawText, routed.Select(message => message.RawText).ToArray(), capturedAt);
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

    private async Task TranslationLoopAsync(CancellationToken cancellationToken)
    {
        var turns = new PlayerTurnAggregator();
        ClassifiedGameMessage? pendingSignal = null;
        var generation = Volatile.Read(ref _stateGeneration);
        var lastPlayerObservedAt = DateTimeOffset.MinValue;
        var lastSignalSpokenAt = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (generation != Volatile.Read(ref _stateGeneration))
            {
                _ = turns.Flush();
                pendingSignal = null;
                generation = Volatile.Read(ref _stateGeneration);
            }

            var timeoutMs = turns.HasPending
                ? Math.Clamp(_settings.SummaryWindowMs, 500, 2500)
                : pendingSignal is not null ? 450 : Timeout.Infinite;
            var message = await ReadNextMessageAsync(timeoutMs, cancellationToken);

            if (message is null)
            {
                if (turns.HasPending)
                {
                    var completed = turns.Flush();
                    if (completed is not null)
                    {
                        await TranslatePlayerTurnAsync(completed, generation, cancellationToken);
                    }
                    continue;
                }

                if (pendingSignal is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var signalAge = now - pendingSignal.CapturedAt;
                    if (now - lastPlayerObservedAt >= TimeSpan.FromMilliseconds(2200)
                        && now - lastSignalSpokenAt >= TimeSpan.FromSeconds(4)
                        && signalAge <= TimeSpan.FromSeconds(4)
                        && !IsPaused
                        && generation == Volatile.Read(ref _stateGeneration))
                    {
                        TranslationReady?.Invoke(CreateTacticalResult(pendingSignal));
                        lastSignalSpokenAt = now;
                    }
                    pendingSignal = null;
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
                pendingSignal = null;
                var completed = turns.Add(message);
                if (completed is not null)
                {
                    await TranslatePlayerTurnAsync(completed, generation, cancellationToken);
                }
                continue;
            }

            if (message.Kind != GameMessageKind.TacticalSignal || !_settings.SignalVoiceEnabled)
            {
                continue;
            }

            var observedAt = DateTimeOffset.UtcNow;
            if (turns.HasPending
                || observedAt - lastPlayerObservedAt < TimeSpan.FromMilliseconds(2200)
                || observedAt - lastSignalSpokenAt < TimeSpan.FromSeconds(4))
            {
                continue;
            }

            // Standard defaults to only important tactical pings. Full additionally allows
            // low-priority status such as recall, gold or cooldown reminders.
            if (message.Priority < 2 && _settings.SpeechFilter != SpeechFilterLevel.Full)
            {
                continue;
            }

            if (pendingSignal is null || message.Priority >= pendingSignal.Priority)
            {
                pendingSignal = message;
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
                _settings.Language);
            var speakerMention = _heroes.FindSpeakerMention(
                turn.SpeakerRaw,
                _settings.PreferChampionPersonalName,
                _settings.Language);
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
                _settings.SpeechFilter,
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
            result.Speaker = speakerLabel;
            result.Priority = 3;
            result.RawText = string.Join("\n", turn.RawLines);
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

    private TranslationResult CreateTacticalResult(ClassifiedGameMessage signal)
    {
        var mentions = _heroes.FindMentions(
            new[] { signal.RawText },
            _settings.PreferChampionPersonalName,
            _settings.Language);
        var speaker = mentions.Count == 1
            ? mentions[0].SpokenName
            : _settings.Language == AppLanguage.English ? "Teammate" : "队友";
        var spoken = SignalMessageTemplates.FormatSpokenSignal(
            signal.TacticalSummary,
            speaker,
            _settings.Language);
        return new TranslationResult
        {
            SourceKind = TranslationSourceKind.TacticalSignal,
            Speaker = speaker,
            Intent = "tactical_signal",
            Priority = signal.Priority,
            DisplayTranslation = spoken,
            SpokenSummary = spoken,
            RawText = signal.RawText,
            ShouldSpeak = true,
            ExpiresAfterMs = 3500
        };
    }

    private void ClearPendingMessages()
    {
        while (_messages.Reader.TryRead(out _))
        {
        }
    }

    private string L(string chinese, string english) =>
        _settings.Language == AppLanguage.English ? english : chinese;
}
