using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.Wave;
using RiftLens.Models;

namespace RiftLens.Services;

public sealed class NaturalSpeechService : IDisposable
{
    public const string NeuralVoiceName = "zh-CN-XiaoxiaoNeural（晓晓）";
    public const string EnglishNeuralVoiceName = "en-US-JennyNeural (Jenny)";

    private readonly BlockingCollection<SpeechItem> _queue = new(3);
    private readonly object _queueMutationGate = new();
    private readonly Thread _worker;
    private readonly object _activeGate = new();
    private readonly EdgeNeuralSpeechClient _neuralClient = new();
    private WaveOutEvent? _currentPlayer;
    private CancellationTokenSource? _currentSynthesis;
    private int _purgeRequested;
    private int _configuredVolume = 65;
    private bool _disposed;
    private DateTimeOffset _onlineDisabledUntil;
    private bool _fallbackReported;
    private bool _neuralReported;
    private bool _currentIsTactical;
    private int _currentPriority;

    public event Action<string>? StatusChanged;

    public NaturalSpeechService()
    {
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "RiftLens.NaturalSpeech",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public void Enqueue(
        string text,
        int volume,
        int rate,
        int expiresAfterMs,
        bool preferNeural = true,
        bool isTactical = false,
        AppLanguage language = AppLanguage.Chinese,
        int priority = 1,
        Func<bool>? canSpeak = null)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        SetVolume(volume);
        priority = Math.Clamp(priority, 0, 3);
        if (!isTactical && priority >= 3)
        {
            MakeRoomForUrgentPlayerSpeech(priority);
        }
        else if (!isTactical)
        {
            MakeRoomForPlayerSpeech();
        }

        var item = new SpeechItem(
            text.Trim(),
            Math.Clamp(rate, -5, 5),
            preferNeural,
            isTactical,
            language,
            priority,
            DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(expiresAfterMs, 1500, 10000)),
            canSpeak);
        if (_queue.TryAdd(item))
        {
            return;
        }

        // Tactical information is optional and must never evict queued player speech.
        if (isTactical)
        {
            return;
        }

        _queue.TryTake(out _);
        _queue.TryAdd(item);
    }

    public void SetVolume(int volume)
    {
        var next = Math.Clamp(volume, 0, 100);
        Volatile.Write(ref _configuredVolume, next);
        lock (_activeGate)
        {
            if (_currentPlayer is not null)
            {
                _currentPlayer.Volume = next / 100f;
            }
        }
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _purgeRequested, 1);
        while (_queue.TryTake(out _))
        {
        }

        lock (_activeGate)
        {
            try
            {
                _currentSynthesis?.Cancel();
                _currentPlayer?.Stop();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
        _queue.CompleteAdding();
        if (_worker.Join(TimeSpan.FromSeconds(3)))
        {
            _queue.Dispose();
        }
    }

    private void Work()
    {
        object? sapiObject = null;
        dynamic? sapiVoice = null;
        var currentLanguage = AppLanguage.Chinese;
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                currentLanguage = item.Language;
                if (DateTimeOffset.UtcNow > item.ExpiresAt)
                {
                    continue;
                }
                if (!IsStillAllowed(item))
                {
                    continue;
                }

                lock (_activeGate)
                {
                    _currentIsTactical = item.IsTactical;
                    _currentPriority = item.Priority;
                }
                try
                {
                    Interlocked.Exchange(ref _purgeRequested, 0);
                    var neuralRequested = item.PreferNeural;
                    var spoken = neuralRequested
                        && DateTimeOffset.UtcNow >= _onlineDisabledUntil
                        && TrySpeakNeural(item);
                    if (spoken || Interlocked.Exchange(ref _purgeRequested, 0) == 1)
                    {
                        continue;
                    }

                    // Chinese neural speech is voice-locked to Xiaoxiao. A
                    // transient online failure must not silently change the
                    // speaker to a Windows SAPI voice with a different timbre.
                    if (!ShouldFallbackToInstalledVoice(neuralRequested, item.Language))
                    {
                        continue;
                    }

                    if (!IsStillAllowed(item))
                    {
                        continue;
                    }

                    sapiVoice ??= CreateSapiVoice(out sapiObject, item.Language);
                    SpeakWithSapi(sapiVoice, item);
                }
                finally
                {
                    lock (_activeGate)
                    {
                        _currentIsTactical = false;
                        _currentPriority = 0;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(currentLanguage == AppLanguage.English
                ? "Speech is temporarily unavailable: " + exception.Message
                : "语音暂时不可用：" + exception.Message);
        }
        finally
        {
            lock (_activeGate)
            {
                try
                {
                    _currentSynthesis?.Cancel();
                    _currentPlayer?.Stop();
                    _currentPlayer?.Dispose();
                }
                catch
                {
                }
                _currentSynthesis?.Dispose();
                _currentSynthesis = null;
                _currentPlayer = null;
            }

            if (sapiObject is not null && Marshal.IsComObject(sapiObject))
            {
                Marshal.FinalReleaseComObject(sapiObject);
            }
        }
    }

    private bool TrySpeakNeural(SpeechItem item)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            lock (_activeGate)
            {
                _currentSynthesis = timeout;
            }
            var audio = _neuralClient
                .SynthesizeAsync(item.Text, item.Rate * 8, item.Language, timeout.Token)
                .GetAwaiter()
                .GetResult();
            lock (_activeGate)
            {
                _currentSynthesis = null;
            }

            if (Interlocked.Exchange(ref _purgeRequested, 0) == 1)
            {
                return true;
            }
            if (!IsStillAllowed(item))
            {
                return true;
            }
            if (!_neuralReported)
            {
                _neuralReported = true;
                StatusChanged?.Invoke(item.Language == AppLanguage.English
                    ? "Jenny Neural voice connected."
                    : "已连接晓晓神经语音。");
            }

            PlayMp3(audio);
            _fallbackReported = false;
            return true;
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _purgeRequested) == 1)
        {
            return true;
        }
        catch
        {
            _onlineDisabledUntil = item.Language == AppLanguage.Chinese
                ? DateTimeOffset.UtcNow.AddSeconds(20)
                : DateTimeOffset.UtcNow.AddMinutes(3);
            ReportFallbackOnce(item.Language == AppLanguage.English
                ? "Jenny is unavailable. Switched quietly to a local English voice."
                : "晓晓暂时不可用，本条播报已跳过；不会切换其他音色，将在 20 秒后重试。");
            return false;
        }
        finally
        {
            lock (_activeGate)
            {
                _currentSynthesis = null;
            }
        }
    }

    private void PlayMp3(byte[] audio)
    {
        using var stream = new MemoryStream(audio, writable: false);
        using var reader = new Mp3FileReader(stream);
        using var completed = new ManualResetEventSlim(false);
        Exception? playbackError = null;
        using var player = new WaveOutEvent { Volume = Volatile.Read(ref _configuredVolume) / 100f };
        player.PlaybackStopped += (_, eventArgs) =>
        {
            playbackError = eventArgs.Exception;
            completed.Set();
        };
        player.Init(reader);
        lock (_activeGate)
        {
            _currentPlayer = player;
        }
        player.Play();
        while (!completed.Wait(50))
        {
            if (Interlocked.Exchange(ref _purgeRequested, 0) == 1)
            {
                player.Stop();
            }
        }
        lock (_activeGate)
        {
            _currentPlayer = null;
        }
        if (playbackError is not null)
        {
            throw playbackError;
        }
    }

    private void ReportFallbackOnce(string message)
    {
        if (_fallbackReported)
        {
            return;
        }
        _fallbackReported = true;
        StatusChanged?.Invoke(message);
    }

    private void MakeRoomForPlayerSpeech()
    {
        lock (_queueMutationGate)
        {
            var retained = new List<SpeechItem>();
            while (_queue.TryTake(out var queued))
            {
                if (!queued.IsTactical)
                {
                    retained.Add(queued);
                }
            }
            foreach (var queued in retained.TakeLast(3))
            {
                _queue.TryAdd(queued);
            }
        }

        lock (_activeGate)
        {
            if (!_currentIsTactical)
            {
                return;
            }

            Interlocked.Exchange(ref _purgeRequested, 1);
            try
            {
                _currentSynthesis?.Cancel();
                _currentPlayer?.Stop();
            }
            catch
            {
            }
        }
    }

    private void MakeRoomForUrgentPlayerSpeech(int priority)
    {
        lock (_queueMutationGate)
        {
            var retained = new List<SpeechItem>();
            while (_queue.TryTake(out var queued))
            {
                if (!queued.IsTactical && queued.Priority >= priority)
                {
                    retained.Add(queued);
                }
            }
            foreach (var queued in retained.TakeLast(2))
            {
                _queue.TryAdd(queued);
            }
        }

        lock (_activeGate)
        {
            if (!_currentIsTactical && _currentPriority >= priority)
            {
                return;
            }

            Interlocked.Exchange(ref _purgeRequested, 1);
            try
            {
                _currentSynthesis?.Cancel();
                _currentPlayer?.Stop();
            }
            catch
            {
            }
        }
    }

    private static dynamic CreateSapiVoice(out object voiceObject, AppLanguage language)
    {
        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException(language == AppLanguage.English
                ? "Windows does not have an available speech component."
                : "当前 Windows 没有可用的语音组件。");
        voiceObject = Activator.CreateInstance(voiceType)
            ?? throw new InvalidOperationException(language == AppLanguage.English
                ? "Unable to create the Windows local speech component."
                : "无法创建 Windows 本地语音组件。");
        return voiceObject;
    }

    private void SpeakWithSapi(dynamic voice, SpeechItem item)
    {
        if (!IsStillAllowed(item))
        {
            return;
        }
        TrySelectVoice(voice, item.Language);
        voice.Volume = Volatile.Read(ref _configuredVolume);
        voice.Rate = item.Rate;
        voice.Speak(item.Text, 1);
        while (!voice.WaitUntilDone(50))
        {
            if (Interlocked.Exchange(ref _purgeRequested, 0) == 1)
            {
                voice.Speak(string.Empty, 2);
                break;
            }
        }
    }

    private static bool IsStillAllowed(SpeechItem item)
    {
        try
        {
            return item.CanSpeak?.Invoke() ?? true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldFallbackToInstalledVoice(bool preferNeural, AppLanguage language) =>
        !preferNeural || language == AppLanguage.English;

    private static void TrySelectVoice(dynamic voice, AppLanguage requestedLanguage)
    {
        try
        {
            dynamic voices = voice.GetVoices("", "");
            for (var index = 0; index < voices.Count; index++)
            {
                dynamic token = voices.Item(index);
                var language = Convert.ToString(token.GetAttribute("Language")) ?? string.Empty;
                var name = Convert.ToString(token.GetDescription()) ?? string.Empty;
                var isMatch = requestedLanguage == AppLanguage.English
                    ? language.Contains("409", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("English", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Zira", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("David", StringComparison.OrdinalIgnoreCase)
                    : language.Contains("804", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Chinese", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Huihui", StringComparison.OrdinalIgnoreCase);
                if (isMatch)
                {
                    voice.Voice = token;
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private sealed record SpeechItem(
        string Text,
        int Rate,
        bool PreferNeural,
        bool IsTactical,
        AppLanguage Language,
        int Priority,
        DateTimeOffset ExpiresAt,
        Func<bool>? CanSpeak);
}
