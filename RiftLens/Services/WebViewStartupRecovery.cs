namespace RiftLens.Services;

/// <summary>Recover a busy browser profile without deleting it or stopping other browsers.</summary>
public static class WebViewStartupRecovery
{
    public const int ResourceBusyHResult = unchecked((int)0x800700AA);

    public static bool IsResourceBusy(Exception exception) =>
        exception.HResult == ResourceBusyHResult
        || (exception.InnerException is not null && IsResourceBusy(exception.InnerException));

    public static async Task InitializeAsync(string preferredFolder, string recoveryFolder, Func<string, Task> initialize,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await initialize(preferredFolder);
                return;
            }
            catch (Exception exception) when (IsResourceBusy(exception))
            {
                if (attempt == 0) await Task.Delay(250, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // A single stable recovery directory avoids unbounded per-launch profiles.
        // App settings and API keys live outside these browser cache directories.
        await initialize(recoveryFolder);
    }
}
