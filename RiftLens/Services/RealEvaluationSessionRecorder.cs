using System.Drawing.Imaging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// User-enabled, local-only evaluation capture. Unlike the ordinary redacted
/// diagnostic export, this session deliberately retains selected-region crops
/// so a human can establish OCR ground truth later. It never receives an API key.
/// </summary>
public sealed class RealEvaluationSessionRecorder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Channel<EvaluationWriteItem> _queue = Channel.CreateBounded<EvaluationWriteItem>(
        new BoundedChannelOptions(96)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly string _eventsPath;
    private readonly string _framesDirectory;
    private readonly string _manifestPath;
    private readonly string _softwareVersion;
    private readonly string _executableSha256;
    private readonly EvaluationSettingsSnapshot _settings;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _performanceSamplingCancellation = new();
    private readonly Task _performanceSamplingTask;
    private int _completed;
    private int _frameCount;
    private int _classificationCount;
    private int _outputCount;
    private int _performanceSampleCount;
    private int _droppedFrames;
    private int _droppedEvents;
    private string? _writerError;

    public string SessionDirectory { get; }
    public bool IsRecording => Volatile.Read(ref _completed) == 0;

    private RealEvaluationSessionRecorder(
        string sessionDirectory,
        string softwareVersion,
        string executableSha256,
        AppSettings settings)
    {
        SessionDirectory = sessionDirectory;
        _softwareVersion = softwareVersion;
        _executableSha256 = executableSha256;
        _settings = new EvaluationSettingsSnapshot(
            settings.Language.ToString(),
            settings.CaptureRegion.Width,
            settings.CaptureRegion.Height,
            settings.PollIntervalMs,
            settings.StabilizationDelayMs,
            settings.ChangeThreshold,
            settings.OcrScale,
            settings.ApiModel,
            settings.OutputMode.ToString(),
            settings.TacticalSignalEnabled,
            settings.KillEventEnabled,
            settings.ObjectiveEventEnabled,
            settings.ClientReminderEnabled,
            settings.OtherSystemEventEnabled);
        _framesDirectory = Path.Combine(sessionDirectory, "frames");
        _eventsPath = Path.Combine(sessionDirectory, "events.jsonl");
        _manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        _writerTask = Task.Run(RunWriterAsync);
        _performanceSamplingTask = Task.Run(
            () => RunPerformanceSamplerAsync(_performanceSamplingCancellation.Token));
    }

    public static async Task<RealEvaluationSessionRecorder> StartAsync(
        string parentDirectory,
        AppSettings settings,
        string softwareVersion,
        CancellationToken cancellationToken = default)
    {
        if (!settings.CaptureRegion.IsValid)
        {
            throw new InvalidOperationException("A valid selected capture region is required.");
        }

        var parent = Path.GetFullPath(parentDirectory);
        Directory.CreateDirectory(parent);
        var sessionName = $"RiftLens-real-evaluation-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..51];
        var sessionDirectory = Path.Combine(parent, sessionName);
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "frames"));

        var executableSha256 = await ComputeExecutableSha256Async(cancellationToken);
        var recorder = new RealEvaluationSessionRecorder(
            sessionDirectory,
            softwareVersion,
            executableSha256,
            settings);
        await recorder.WriteManifestAsync("recording", stoppedAt: null, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDirectory, "READ-BEFORE-SHARING.txt"),
            "RiftLens real evaluation capture / 真实评估记录\r\n\r\n"
            + "This folder contains original screenshots of only the user-selected region, OCR text, classifications, actual translations, and speech decisions.\r\n"
            + "本文件夹包含用户框选区域的原始截图、OCR 文字、分类结果、实际译文和播报决策。\r\n\r\n"
            + "Screenshots and text may contain player names or chat content. Review and redact them before sharing.\r\n"
            + "截图和文字可能含玩家昵称或聊天内容，分享前必须人工检查并脱敏。\r\n\r\n"
            + "No API key, credential, game-memory data, packet capture, or full-screen screenshot is recorded. Nothing is uploaded automatically.\r\n"
            + "不记录 API Key、凭据、游戏内存、网络数据包或全屏截图，也不会自动上传。\r\n",
            cancellationToken);
        recorder.RecordEvent(new
        {
            eventType = "session_started",
            occurredAt = recorder._startedAt,
            softwareVersion
        });
        return recorder;
    }

    /// <summary>Transfers ownership of observation.Screenshot to this recorder.</summary>
    public void RecordFrame(EvaluationFrameObservation observation)
    {
        if (!IsRecording || !_queue.Writer.TryWrite(new FrameWriteItem(observation)))
        {
            observation.Screenshot.Dispose();
            Interlocked.Increment(ref _droppedFrames);
        }
    }

    public void RecordClassification(MessageDiagnosticObservation observation)
    {
        var message = observation.Message;
        var timestamp = observation.Line.Timestamp;
        RecordEvent(new
        {
            eventType = "classification",
            occurredAt = observation.CapturedAt,
            frameId = observation.FrameId,
            lineId = observation.LineId,
            rawOcrLine = observation.Line.RawText,
            textAfterTimestamp = observation.Line.NormalizedText,
            timestampRemoved = timestamp.Matched,
            timestamp = GameTimestampPrefixParser.Format(timestamp.Timestamp),
            timestampPrefix = timestamp.OriginalPrefix,
            timestampEvidence = timestamp.Evidence,
            timestampConfidence = timestamp.Confidence,
            category = message.Kind.ToString(),
            subtype = message.SystemEvent?.Type.ToString()
                ?? message.SemanticEvent?.SpeechAct.ToString()
                ?? "Unknown",
            origin = ResolveOrigin(message),
            speechAct = message.SemanticEvent?.SpeechAct.ToString() ?? string.Empty,
            ruleId = message.SystemEvent?.RuleId
                ?? (message.SemanticEvent is not null ? "signal.semantic" : string.Empty),
            classificationConfidence = message.ClassificationConfidence,
            categorySwitchEnabled = observation.CategoryEnabled,
            routeDecision = message.Kind == GameMessageKind.Unknown
                ? "display_only_unknown"
                : observation.CategoryEnabled ? "routed" : "disabled_by_category_switch"
        }, EvaluationEventKind.Classification);
    }

    public void RecordOutput(TranslationResult result, bool finalSpeechAllowed)
    {
        RecordEvent(new
        {
            eventType = "output",
            occurredAt = DateTimeOffset.Now,
            evaluationLineIds = result.EvaluationLineIds.Take(8).ToArray(),
            sourceKind = result.SourceKind.ToString(),
            routedCategory = result.RoutedMessageKind.ToString(),
            speaker = result.Speaker,
            intent = result.Intent,
            sourceType = result.SourceType,
            speechAct = result.SpeechAct,
            subject = result.Subject,
            target = result.Target,
            status = result.Status,
            durationSeconds = result.DurationSeconds,
            modality = result.Modality,
            ruleId = result.ClassificationRuleId,
            classificationConfidence = result.ClassificationConfidence,
            semanticConfidence = result.SemanticConfidence,
            originalContent = result.OriginalLines.Take(8).ToArray(),
            displayLines = result.DisplayLines.Count > 0
                ? result.DisplayLines.Take(8).ToArray()
                : new[] { result.DisplayTranslation },
            spokenText = finalSpeechAllowed ? result.SpokenSummary : string.Empty,
            speechAllowed = finalSpeechAllowed,
            finalDecision = finalSpeechAllowed
                ? "queued_for_speech"
                : result.SourceKind == TranslationSourceKind.Unknown
                    ? "silent_unknown"
                    : "silent_by_policy_switch_or_output_mode"
        }, EvaluationEventKind.Output);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _performanceSamplingCancellation.Cancel();
        try
        {
            await _performanceSamplingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _performanceSamplingCancellation.Dispose();
        _queue.Writer.TryComplete();
        await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        await WriteManifestAsync(
            _writerError is null ? "completed" : "completed_with_writer_error",
            DateTimeOffset.Now,
            cancellationToken).ConfigureAwait(false);
        if (_writerError is not null)
        {
            throw new IOException("Evaluation capture writer failed: " + _writerError);
        }
    }

    public async ValueTask DisposeAsync() => await CompleteAsync();

    private void RecordEvent(object payload, EvaluationEventKind kind = EvaluationEventKind.Other)
    {
        if (!IsRecording || !_queue.Writer.TryWrite(new JsonWriteItem(payload, kind)))
        {
            Interlocked.Increment(ref _droppedEvents);
        }
    }

    private async Task RunWriterAsync()
    {
        await using var stream = new FileStream(
            _eventsPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync())
            {
                switch (item)
                {
                    case FrameWriteItem frameItem:
                        await WriteFrameAsync(frameItem.Observation, writer);
                        Interlocked.Increment(ref _frameCount);
                        break;
                    case JsonWriteItem jsonItem:
                        await writer.WriteLineAsync(JsonSerializer.Serialize(
                            jsonItem.Payload,
                            EventJsonOptions));
                        if (jsonItem.Kind == EvaluationEventKind.Classification)
                            Interlocked.Increment(ref _classificationCount);
                        if (jsonItem.Kind == EvaluationEventKind.Output)
                            Interlocked.Increment(ref _outputCount);
                        if (jsonItem.Kind == EvaluationEventKind.Performance)
                            Interlocked.Increment(ref _performanceSampleCount);
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            _writerError = exception.Message;
        }
        finally
        {
            while (_queue.Reader.TryRead(out var remaining))
            {
                if (remaining is FrameWriteItem frame)
                {
                    frame.Observation.Screenshot.Dispose();
                }
            }
        }
    }

    private async Task WriteFrameAsync(
        EvaluationFrameObservation observation,
        StreamWriter writer)
    {
        var fileName = observation.FrameId + ".png";
        var fullPath = Path.Combine(_framesDirectory, fileName);
        try
        {
            observation.Screenshot.Save(fullPath, ImageFormat.Png);
            var document = observation.Document;
            var payload = new
            {
                eventType = "frame",
                occurredAt = observation.CapturedAt,
                frameId = observation.FrameId,
                captureKind = observation.CaptureKind,
                meaningfulChange = observation.MeaningfulChange,
                screenshotFile = "frames/" + fileName,
                width = observation.Screenshot.Width,
                height = observation.Screenshot.Height,
                ocrAttempted = document is not null,
                ocrElapsedMs = observation.OcrElapsedMs,
                localPipelineElapsedMs = observation.LocalPipelineElapsedMs,
                pollIntervalMs = observation.PollIntervalMs,
                ocrScale = observation.OcrScale,
                rawOcr = document?.RawText ?? string.Empty,
                ocrLines = document?.Lines.Select(line => new
                {
                    line.Text,
                    bounds = new
                    {
                        line.Bounds.X,
                        line.Bounds.Y,
                        line.Bounds.Width,
                        line.Bounds.Height
                    },
                    line.Confidence,
                    colorEvidence = line.ColorEvidence
                }).ToArray() ?? Array.Empty<object>()
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(payload, EventJsonOptions));
        }
        finally
        {
            observation.Screenshot.Dispose();
        }
    }

    private async Task WriteManifestAsync(
        string status,
        DateTimeOffset? stoppedAt,
        CancellationToken cancellationToken)
    {
        var manifest = new
        {
            format = "RiftLens real evaluation capture",
            schemaVersion = "1.0",
            status,
            softwareVersion = _softwareVersion,
            executableSha256 = _executableSha256,
            startedAt = _startedAt,
            stoppedAt,
            privacy = new
            {
                userInitiated = true,
                localOnly = true,
                automaticUpload = false,
                selectedRegionOnly = true,
                containsOriginalCroppedScreenshots = true,
                mayContainPlayerNamesAndChat = true,
                containsApiKeyOrCredentials = false,
                containsGameMemoryOrPackets = false,
                instruction = "Review and redact screenshots and text before sharing."
            },
            settings = _settings,
            files = new
            {
                events = "events.jsonl",
                frames = "frames/*.png",
                sharingNotice = "READ-BEFORE-SHARING.txt"
            },
            counts = new
            {
                frames = Volatile.Read(ref _frameCount),
                classifications = Volatile.Read(ref _classificationCount),
                outputs = Volatile.Read(ref _outputCount),
                performanceSamples = Volatile.Read(ref _performanceSampleCount),
                droppedFrames = Volatile.Read(ref _droppedFrames),
                droppedEvents = Volatile.Read(ref _droppedEvents)
            },
            writerError = _writerError
        };
        await File.WriteAllTextAsync(
            _manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            cancellationToken);
    }

    private static string ResolveOrigin(ClassifiedGameMessage message) => message.Kind switch
    {
        GameMessageKind.PlayerChat => "player_authored_chat",
        GameMessageKind.TacticalSignal when !string.IsNullOrWhiteSpace(message.SpeakerKey) =>
            "player_initiated_game_signal",
        GameMessageKind.TacticalSignal => "game_generated_signal",
        GameMessageKind.Unknown => "unknown",
        _ => "game_system"
    };

    private static async Task<string> ComputeExecutableSha256Async(
        CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return "unavailable";
        }
        await using var stream = new FileStream(
            executable,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 128 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private async Task RunPerformanceSamplerAsync(CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var previousCpu = process.TotalProcessorTime;
        var previousAt = Stopwatch.GetTimestamp();
        RecordPerformanceSample(process, cpuCoreNormalizedPercent: null);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            process.Refresh();
            var currentAt = Stopwatch.GetTimestamp();
            var currentCpu = process.TotalProcessorTime;
            var wallMilliseconds = Stopwatch.GetElapsedTime(previousAt, currentAt).TotalMilliseconds;
            var cpuMilliseconds = (currentCpu - previousCpu).TotalMilliseconds;
            var normalized = wallMilliseconds <= 0
                ? 0
                : cpuMilliseconds / wallMilliseconds / Math.Max(1, Environment.ProcessorCount) * 100;
            RecordPerformanceSample(process, Math.Round(normalized, 3));
            previousCpu = currentCpu;
            previousAt = currentAt;
        }
    }

    private void RecordPerformanceSample(Process process, double? cpuCoreNormalizedPercent)
    {
        RecordEvent(new
        {
            eventType = "performance_sample",
            occurredAt = DateTimeOffset.Now,
            cpuCoreNormalizedPercent,
            logicalProcessorCount = Environment.ProcessorCount,
            workingSetMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
            privateMemoryMb = Math.Round(process.PrivateMemorySize64 / 1024d / 1024d, 2),
            peakWorkingSetMb = Math.Round(process.PeakWorkingSet64 / 1024d / 1024d, 2),
            managedHeapMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 2),
            threadCount = process.Threads.Count
        }, EvaluationEventKind.Performance);
    }

    private abstract record EvaluationWriteItem;
    private sealed record FrameWriteItem(EvaluationFrameObservation Observation) : EvaluationWriteItem;
    private sealed record JsonWriteItem(object Payload, EvaluationEventKind Kind) : EvaluationWriteItem;
    private enum EvaluationEventKind { Other, Classification, Output, Performance }

    private sealed record EvaluationSettingsSnapshot(
        string Language,
        int CaptureWidth,
        int CaptureHeight,
        int PollIntervalMs,
        int StabilizationDelayMs,
        double ChangeThreshold,
        int OcrScale,
        string TranslationModel,
        string OutputMode,
        bool TacticalSignalEnabled,
        bool KillEventEnabled,
        bool ObjectiveEventEnabled,
        bool ClientReminderEnabled,
        bool OtherSystemEventEnabled);
}
