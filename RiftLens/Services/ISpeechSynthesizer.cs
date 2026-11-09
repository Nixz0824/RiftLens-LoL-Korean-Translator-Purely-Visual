using RiftLens.Models;

namespace RiftLens.Services;

public interface ISpeechSynthesizer
{
    Task<byte[]> SynthesizeAsync(string text, int ratePercent, AppLanguage language,
        CancellationToken cancellationToken);
}
