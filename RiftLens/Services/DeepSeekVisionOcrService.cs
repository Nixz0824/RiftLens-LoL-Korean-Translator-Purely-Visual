using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

public interface IVisionOcrService
{
    Task<OcrDocument> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken);
}

/// <summary>
/// Experimental OCR-only adapter for DeepSeek V4 Flash Vision. It sends only
/// the user-selected crop and asks for literal line transcription. All LoL
/// classification, terminology, translation, aggregation, and speech policy
/// remain in the existing local/text pipeline so this experiment isolates OCR.
/// </summary>
public sealed class DeepSeekVisionOcrService : IVisionOcrService, IDisposable
{
    public const string ModelName = "deepseek-v4-flash-vision-exp";

    private const string SystemPrompt = """
        Transcribe every visible line of text in the supplied image exactly as written.
        Preserve line order, Korean, Simplified Chinese, English, digits, punctuation,
        timestamps, brackets, player names, spacing when meaningful, and mixed-language text.
        Do not translate, correct spelling, normalize game terminology, summarize, classify,
        explain, or infer missing characters. Omit a line if it is not readable instead of
        guessing. Return JSON only: {"lines":["line 1","line 2"]}.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public DeepSeekVisionOcrService(string baseUrl, string apiKey, HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(35);
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<OcrDocument> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        SaveJpeg(bitmap, stream);
        var imageUrl = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
        var messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = "Transcribe the image now." },
                    new
                    {
                        type = "image_url",
                        image_url = new { url = imageUrl, detail = "original" }
                    }
                }
            }
        };
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ModelName,
            ["messages"] = messages,
            ["temperature"] = 0,
            ["max_tokens"] = 1600,
            ["response_format"] = new { type = "json_object" },
            ["thinking"] = new { type = "disabled" },
            ["stream"] = false
        };

        using var response = await SendAsync(payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DeepSeek Vision returned {(int)response.StatusCode}: {TrimForError(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var modelContent = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return ParseModelContent(modelContent ?? string.Empty);
    }

    public static OcrDocument ParseModelContent(string content)
    {
        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            throw new InvalidOperationException("DeepSeek Vision did not return valid OCR JSON.");
        }

        var payload = JsonSerializer.Deserialize<VisionOcrPayload>(content[first..(last + 1)], JsonOptions)
            ?? throw new InvalidOperationException("DeepSeek Vision returned an empty OCR result.");
        var lines = (payload.Lines ?? new List<string>())
            .Select(NormalizeLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(50)
            .Select(line => new OcrRecognizedLine(line, Rectangle.Empty, 0.75f))
            .ToArray();
        return new OcrDocument(
            string.Join(Environment.NewLine, lines.Select(line => line.Text)),
            lines);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var response = await PostAsync(payload, cancellationToken);
        if (response.IsSuccessStatusCode || (int)response.StatusCode is not (400 or 422))
        {
            return response;
        }

        response.Dispose();
        payload.Remove("thinking");
        payload.Remove("response_format");
        return await PostAsync(payload, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync("chat/completions", content, cancellationToken);
    }

    private static void SaveJpeg(Bitmap bitmap, Stream stream)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .First(info => info.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        bitmap.Save(stream, codec, parameters);
    }

    private static string NormalizeLine(string? value) => Regex.Replace(
            (value ?? string.Empty).Normalize(NormalizationForm.FormKC),
            @"\s+",
            " ",
            RegexOptions.CultureInvariant)
        .Trim();

    private static string TrimForError(string value) =>
        value.Length <= 240 ? value : value[..240] + "…";

    private sealed class VisionOcrPayload
    {
        public List<string> Lines { get; set; } = new();
    }
}
