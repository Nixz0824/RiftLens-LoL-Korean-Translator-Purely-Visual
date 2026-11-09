using System.Globalization;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using RiftLens.Models;

namespace RiftLens.Services;

internal sealed class EdgeNeuralSpeechClient : ISpeechSynthesizer
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ProtocolVersion = "1-143.0.3650.75";
    private const long WindowsEpochSeconds = 11_644_473_600;
    private const long TicksPerSecond = 10_000_000;
    private const long TokenWindowTicks = 3_000_000_000;

    public async Task<byte[]> SynthesizeAsync(
        string text,
        int ratePercent,
        AppLanguage language,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        var connectionId = NewId();
        var uri = new Uri(
            "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1"
            + $"?TrustedClientToken={TrustedClientToken}"
            + $"&Sec-MS-GEC={GenerateSecMsGec()}"
            + $"&Sec-MS-GEC-Version={ProtocolVersion}"
            + $"&ConnectionId={connectionId}");
        await socket.ConnectAsync(uri, cancellationToken);

        var timestamp = Timestamp();
        var config =
            $"X-Timestamp:{timestamp}\r\n"
            + "Content-Type:application/json; charset=utf-8\r\n"
            + "Path:speech.config\r\n\r\n"
            + "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":"
            + "{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},"
            + "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        await SendTextAsync(socket, config, cancellationToken);

        var requestId = NewId();
        var escapedText = SecurityElement.Escape(text) ?? string.Empty;
        var signedRate = Math.Clamp(ratePercent, -50, 50).ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%";

        // 根据文本中情绪标点自动调整 SSML prosody 音高
        var pitch = DetectEmotionalPitch(text);
        var signedPitch = pitch.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "Hz";
        // 疑问句稍微放慢语速让语调更清晰
        var effectiveRate = signedRate;
        if (text.Contains('？') || text.EndsWith('?'))
        {
            var baseRate = Math.Clamp(ratePercent, -50, 50);
            baseRate = (int)(baseRate * 0.88);
            effectiveRate = baseRate.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%";
        }

        var locale = language == AppLanguage.English ? "en-US" : "zh-CN";
        var voiceName = language == AppLanguage.English ? "en-US, JennyNeural" : "zh-CN, XiaoxiaoNeural";
        var ssml =
            $"X-RequestId:{requestId}\r\n"
            + $"Content-Type:application/ssml+xml\r\nX-Timestamp:{Timestamp()}\r\nPath:ssml\r\n\r\n"
            + $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{locale}'>"
            + $"<voice name='Microsoft Server Speech Text to Speech Voice ({voiceName})'>"
            + $"<prosody pitch='{signedPitch}' rate='{effectiveRate}' volume='+0%'>{escapedText}</prosody>"
            + "</voice></speak>";
        await SendTextAsync(socket, ssml, cancellationToken);

        using var audio = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            var payload = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var response = Encoding.UTF8.GetString(payload);
                if (response.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                continue;
            }

            AppendAudioPayload(payload, audio);
        }

        if (audio.Length == 0)
        {
            throw new InvalidOperationException(language == AppLanguage.English
                ? "Jenny Neural did not return audio."
                : "晓晓服务没有返回音频。");
        }
        return audio.ToArray();
    }

    private static void AppendAudioPayload(byte[] payload, Stream destination)
    {
        if (payload.Length < 3)
        {
            return;
        }
        var headerLength = (payload[0] << 8) | payload[1];
        var audioOffset = 2 + headerLength;
        if (audioOffset <= 2 || audioOffset >= payload.Length)
        {
            return;
        }
        var header = Encoding.UTF8.GetString(payload, 2, headerLength);
        if (!header.Contains("Path:audio", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        destination.Write(payload, audioOffset, payload.Length - audioOffset);
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string GenerateSecMsGec()
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + WindowsEpochSeconds;
        var ticks = seconds * TicksPerSecond;
        ticks -= ticks % TokenWindowTicks;
        var material = Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken);
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string NewId() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private static string Timestamp() => DateTime.UtcNow.ToString(
        "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
        CultureInfo.InvariantCulture);

    /// <summary>
    /// 根据文本中的情绪标点返回推荐 SSML 音高偏移（Hz）。
    /// 语气的自然度来自晓晓本身对标点的处理，这里做适度增强。
    /// </summary>
    private static int DetectEmotionalPitch(string text)
    {
        // 双重标点：强烈疑问/震惊 → 明显升调
        if (text.Contains("？！") || text.Contains("！？") || text.Contains("!?"))
            return 18;
        // 疑问句：升调
        if (text.EndsWith('？') || text.EndsWith('?'))
            return 10;
        // 感叹/紧急：微升调加强语气
        if (text.Contains('！') || text.Contains('!'))
            return 8;
        // 陈述句：持平
        return 0;
    }
}
