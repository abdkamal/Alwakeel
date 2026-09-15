namespace Wakeel.Crypto.Tests;

/// <summary>A clock the test decides, so nothing in the suite depends on the real time.</summary>
internal sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;

    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    public static FixedClock At(int year, int month, int day, int hour = 12, int minute = 0, int second = 0) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;

    public void Set(DateTimeOffset now) => _now = now;
}

/// <summary>
/// A monotonic clock whose every reading jumps forward by a fixed step, so a measured
/// operation can be made to look arbitrarily slow or fast.
/// </summary>
internal sealed class SteppingClock : TimeProvider
{
    private readonly long _step;
    private long _current;

    public SteppingClock(TimeSpan step)
    {
        _step = step.Ticks;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        var value = _current;
        _current += _step;
        return value;
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
}
