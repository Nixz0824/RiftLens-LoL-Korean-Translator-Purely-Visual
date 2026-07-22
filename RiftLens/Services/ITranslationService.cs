using RiftLens.Models;
using System.Text.RegularExpressions;

namespace RiftLens.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

public sealed partial class OcrOnlyTranslationService : ITranslationService
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = HeroTagRegex().Replace(string.Join(" / ", request.Lines), "$1");
        return Task.FromResult(new TranslationResult
        {
            ShouldSpeak = false,
            DisplayTranslation = request.Language == AppLanguage.English
                ? $"AI is not configured. OCR only: {raw}"
                : $"未配置 AI，仅显示 OCR：{raw}",
            Speaker = request.Language == AppLanguage.English ? "Teammate" : "队友",
            RawText = raw,
            ExpiresAfterMs = 6000
        });
    }

    [System.Text.RegularExpressions.GeneratedRegex("<英雄[^>]*>(.*?)</英雄>")]
    private static partial Regex HeroTagRegex();
}
