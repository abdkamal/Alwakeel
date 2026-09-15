namespace Wakeel.Core.Services;

/// <summary>Current UTC time, abstracted over <see cref="TimeProvider"/> so tests can control it.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/>, backed by a <see cref="TimeProvider"/> (<see cref="TimeProvider.System"/> in production).</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
