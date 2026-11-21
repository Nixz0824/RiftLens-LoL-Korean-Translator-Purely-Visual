using System.Runtime.InteropServices;
using RiftLens.Services;

internal static class WebViewStartupRecoveryChecks
{
    public static async Task VerifyAsync()
    {
        const string profile = "original-profile";
        const string recovery = "independent-writable-profile";
        var attempts = new List<string>();
        await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
        {
            attempts.Add(folder);
            return Task.CompletedTask;
        });
        Require(attempts.SequenceEqual(new[] { profile }), "A healthy profile was replaced or retried.");

        attempts.Clear();
        await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
        {
            attempts.Add(folder);
            return attempts.Count == 1
                ? Task.FromException(new InvalidOperationException("wrapped", Busy()))
                : Task.CompletedTask;
        });
        Require(attempts.SequenceEqual(new[] { profile, profile }), "A transient busy profile was not retried in place.");

        attempts.Clear();
        await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
        {
            attempts.Add(folder);
            return folder == profile ? Task.FromException(Busy()) : Task.CompletedTask;
        });
        Require(attempts.SequenceEqual(new[] { profile, profile, recovery }),
            "A persistently busy profile did not use exactly one separate recovery profile.");

        attempts.Clear();
        var rejected = false;
        try
        {
            await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
            {
                attempts.Add(folder);
                return Task.FromException(new COMException("access denied", unchecked((int)0x80070005)));
            });
        }
        catch (COMException) { rejected = true; }
        Require(rejected && attempts.Count == 1, "An unrelated failure was masked as profile contention.");

        attempts.Clear();
        rejected = false;
        try
        {
            await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
            {
                attempts.Add(folder);
                return Task.FromException(Busy());
            });
        }
        catch (COMException) { rejected = true; }
        Require(rejected && attempts.Count == 3, "Failure in the recovery profile caused unbounded retries.");

        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var cancelled = false;
        try
        {
            await WebViewStartupRecovery.InitializeAsync(profile, recovery, folder =>
            {
                calls++;
                cancellation.Cancel();
                return Task.FromException(Busy());
            }, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && calls == 1, "Shutdown continued creating browser profiles after cancellation.");
    }

    private static Exception Busy() => new COMException("profile busy", WebViewStartupRecovery.ResourceBusyHResult);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
