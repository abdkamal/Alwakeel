using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The minute tick itself. What matters here is not the interval — that is the time provider's
/// job — but that a second <see cref="IMinuteTicker.Start"/> binds the NEW callback. The shell
/// starts the scheduler again after every unlock, and a ticker that quietly kept the first
/// callback would go on calling a scheduler whose database session has closed: every tick would
/// throw into the ticker's own catch, and the office would never see another reminder.
/// </summary>
public sealed class MinuteTickerTests
{
    [Fact]
    public async Task StartingASecondTime_TicksTheNewCallbackAndNotTheOldOne()
    {
        var time = new ManualTimeProvider();
        using var ticker = new TimeProviderMinuteTicker(time);

        var firstCalls = 0;
        var secondCalls = 0;
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ticker.Start((_, _) =>
        {
            Interlocked.Increment(ref firstCalls);
            return Task.CompletedTask;
        });

        ticker.Start((_, _) =>
        {
            Interlocked.Increment(ref secondCalls);
            secondRan.TrySetResult();
            return Task.CompletedTask;
        });

        time.FireLiveTimers();
        await secondRan.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void StartingASecondTime_LeavesNoTimerBehind()
    {
        var time = new ManualTimeProvider();
        using var ticker = new TimeProviderMinuteTicker(time);

        ticker.Start((_, _) => Task.CompletedTask);
        ticker.Start((_, _) => Task.CompletedTask);

        Assert.Equal(2, time.CreatedTimers);
        Assert.Equal(1, time.LiveTimers);

        ticker.Stop();
        Assert.Equal(0, time.LiveTimers);
    }

    [Fact]
    public async Task ATickThatThrows_DoesNotStopTheFollowingTicks()
    {
        var time = new ManualTimeProvider();
        using var ticker = new TimeProviderMinuteTicker(time);

        var calls = 0;
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ticker.Start((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("the pass failed");
            }

            secondRan.TrySetResult();
            return Task.CompletedTask;
        });

        time.FireLiveTimers();
        time.FireLiveTimers();

        await secondRan.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, calls);
    }
}

/// <summary>A <see cref="TimeProvider"/> whose timers only fire when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly Lock _gate = new();

    /// <summary>The instant <see cref="GetUtcNow"/> returns.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>How many timers have been created since the provider was made.</summary>
    public int CreatedTimers { get; private set; }

    /// <summary>How many of those have not been disposed.</summary>
    public int LiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(t => !t.Disposed);
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        lock (_gate)
        {
            CreatedTimers++;
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Fires every timer that has not been disposed.</summary>
    public void FireLiveTimers()
    {
        ManualTimer[] live;
        lock (_gate)
        {
            live = _timers.Where(t => !t.Disposed).ToArray();
        }

        foreach (var timer in live)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

        public void Fire() => callback(state);

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
