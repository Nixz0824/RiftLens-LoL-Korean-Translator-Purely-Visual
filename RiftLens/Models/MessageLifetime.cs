namespace RiftLens.Models;

/// <summary>A capture-based budget measured with a monotonic clock after creation.</summary>
public sealed class MessageLifetime
{
    private readonly TimeProvider _clock;
    private readonly long _startedAt;
    private readonly TimeSpan _initialAge;
    private readonly TimeSpan _budget;

    private MessageLifetime(DateTimeOffset capturedAt, TimeSpan budget, TimeProvider clock,
        long startedAt, TimeSpan initialAge)
    {
        CapturedAt = capturedAt;
        _budget = budget;
        _clock = clock;
        _startedAt = startedAt;
        _initialAge = initialAge;
    }

    public DateTimeOffset CapturedAt { get; }
    public DateTimeOffset Deadline => CapturedAt + _budget;
    public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0,
        (_budget - _initialAge - _clock.GetElapsedTime(_startedAt)).Ticks));
    public bool IsExpired => Remaining <= TimeSpan.Zero;

    public static MessageLifetime FromCapture(DateTimeOffset capturedAt, TimeSpan budget,
        TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var now = clock.GetUtcNow();
        if (capturedAt == default) capturedAt = now;
        var age = now > capturedAt ? now - capturedAt : TimeSpan.Zero;
        return new MessageLifetime(capturedAt, budget, clock, clock.GetTimestamp(), age);
    }

    // Reclassification may shorten a budget, but can never renew it.
    public MessageLifetime LimitTo(TimeSpan budget) => new(CapturedAt,
        budget < _budget ? budget : _budget, _clock, _startedAt, _initialAge);
}
