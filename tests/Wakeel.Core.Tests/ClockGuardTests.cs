using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

/// <summary>
/// The clock guard (W11, AGREEMENT item 20): a bad clock raises the banner and blocks official
/// numbering until it is corrected, and «تجاهل مؤقتًا» hides the banner without unblocking
/// anything.
/// </summary>
public sealed class ClockGuardTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly DailyShellWorld _world = new(Now);

    public ClockGuardTests() => _world.SeedInstallation();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task AHealthyClock_ShowsNoBannerAndBlocksNothing()
    {
        var state = await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now);

        Assert.Equal(ClockVerdict.Ok, state.Verdict);
        Assert.False(state.Visible);
        Assert.False(state.NumberingBlocked);
        Assert.False(_world.ClockGuard.NumberingBlocked);
        Assert.Equal(string.Empty, state.TitleAr);
    }

    [Fact]
    public async Task AClockBeforeTheBuildDate_RaisesTheBannerAndBlocksNumbering()
    {
        var state = await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        Assert.Equal(ClockVerdict.Bad, state.Verdict);
        Assert.True(state.Visible);
        Assert.True(state.NumberingBlocked);
        Assert.Equal("ساعة الجهاز غير صحيحة", state.TitleAr);
        Assert.Equal("لن تُصدر أرقام رسمية حتى التصحيح", state.NumberingNoticeAr);
    }

    [Fact]
    public async Task WhileTheClockIsBad_IssuingAnOfficialNumberIsRefused()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        Assert.True(_world.ClockGuard.NumberingBlocked);

        // The guard refuses before a transaction is even opened...
        Assert.Throws<InvalidOperationException>(_world.ClockGuard.EnsureNumberingAllowed);

        // ...and the numbering service refuses independently, so bypassing the guard changes nothing.
        var numbering = new OfficialNumberService(_world.Db, _world.ClockCheck);
        await using var transaction = await _world.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => numbering.IssueAsync(InOutDirection.In, Now.AddYears(-5)));
    }

    [Fact]
    public async Task OnceTheClockIsCorrected_TheBannerClearsAndNumberingWorksAgain()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        Assert.True(_world.ClockGuard.NumberingBlocked);

        // The device clock is put right; the wrong reading is in the past, so the check passes.
        var corrected = await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Manual, Now);

        Assert.Equal(ClockVerdict.Ok, corrected.Verdict);
        Assert.False(corrected.Visible);
        Assert.False(_world.ClockGuard.NumberingBlocked);
        _world.ClockGuard.EnsureNumberingAllowed();

        var numbering = new OfficialNumberService(_world.Db, _world.ClockCheck);
        await using var transaction = await _world.Db.Database.BeginTransactionAsync();
        var issued = await numbering.IssueAsync(InOutDirection.In, Now);
        await transaction.CommitAsync();

        Assert.Equal(1, issued.Sequence);
    }

    [Fact]
    public async Task DismissForSession_HidesTheBannerButKeepsNumberingBlocked()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));

        _world.ClockGuard.DismissForSession();

        Assert.False(_world.ClockGuard.State.Visible);
        Assert.True(_world.ClockGuard.State.NumberingBlocked);
        Assert.True(_world.ClockGuard.NumberingBlocked);
        Assert.Throws<InvalidOperationException>(_world.ClockGuard.EnsureNumberingAllowed);
    }

    [Fact]
    public async Task ADismissedBanner_StaysHiddenThroughTheSessionsLaterIntervalChecks()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        _world.ClockGuard.DismissForSession();

        var later = await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Interval, Now.AddYears(-5).AddMinutes(10));

        Assert.False(later.Visible);
        Assert.True(later.NumberingBlocked);
    }

    [Fact]
    public async Task AnImportThatFindsAProblem_ShowsTheBannerAgainEvenAfterADismissal()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        _world.ClockGuard.DismissForSession();
        Assert.False(_world.ClockGuard.State.Visible);

        var imported = await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Import, Now.AddYears(-5).AddMinutes(1));

        Assert.True(imported.Visible);
    }

    [Fact]
    public async Task AClockBehindAnotherDevice_IsSuspect_AndStillBlocksNumbering()
    {
        // An official number carries a date; a clock we already suspect must not stamp one.
        var state = await _world.ClockGuard.CheckAsync(
            ClockCheckTrigger.Import,
            Now,
            otherDeviceTimestamps: [Now.AddHours(3)]);

        Assert.Equal(ClockVerdict.Suspect, state.Verdict);
        Assert.True(state.Visible);
        Assert.True(state.NumberingBlocked);
        Assert.Equal("ساعة الجهاز تختلف عن ساعة جهاز آخر في المكتب", state.BodyAr);
    }

    [Fact]
    public async Task StateChanged_FiresOnceForAChangeAndNotForARepeatOfTheSameVerdict()
    {
        var states = new List<ClockBannerState>();
        _world.ClockGuard.StateChanged += (_, s) => states.Add(s);

        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now.AddYears(-5));
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Interval, Now.AddYears(-5).AddMinutes(10));

        Assert.Single(states);
        Assert.True(states[0].NumberingBlocked);
    }

    [Fact]
    public async Task EveryCheckIsRecorded_SoTheHealthCenterAndTheAuditTrailCanSeeIt()
    {
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Startup, Now);
        await _world.ClockGuard.CheckAsync(ClockCheckTrigger.Interval, Now.AddMinutes(10));

        Assert.Equal(2, _world.Db.ClockChecks.Count());
    }

    [Fact]
    public async Task StartAsync_RunsTheStartupCheckImmediately()
    {
        await _world.ClockGuard.StartAsync(Now.AddYears(-5), BackgroundPass.Inline);
        try
        {
            Assert.Equal(ClockVerdict.Bad, _world.ClockGuard.State.Verdict);
            Assert.True(_world.ClockGuard.NumberingBlocked);
        }
        finally
        {
            _world.ClockGuard.Stop();
        }
    }

    [Fact]
    public async Task StartAsync_RunsEveryCheckThroughTheDispatcherTheShellSupplied()
    {
        // The guard writes a clock_checks row through the session the screens are using, so the
        // shell hands it a dispatcher and the guard must not go around it.
        var passes = 0;
        Task Dispatcher(Func<Task> pass)
        {
            passes++;
            return pass();
        }

        await _world.ClockGuard.StartAsync(Now, Dispatcher);
        try
        {
            Assert.Equal(1, passes);
            Assert.Equal(1, _world.Db.ClockChecks.Count());
        }
        finally
        {
            _world.ClockGuard.Stop();
        }
    }

    [Fact]
    public async Task StartAsync_RefusesAMissingDispatcher_RatherThanQuietlyUsingTheTimerThread()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _world.ClockGuard.StartAsync(Now, null!));
    }

    [Fact]
    public async Task Stop_CancelsAnIntervalCheckThatIsAlreadyRunning()
    {
        // Disposing the timer only stops the NEXT check. A pass already inside the dispatcher
        // would otherwise keep reading and writing the very session a sign-out is tearing down,
        // so the pass is handed a token that Stop() cancels.
        var time = new ManualTimeProvider();
        var checks = new RecordingClockCheckService();
        using var guard = new ClockGuard(checks, time);

        await guard.StartAsync(Now, BackgroundPass.Inline);
        time.FireLiveTimers();

        var token = Assert.Single(checks.Tokens.Skip(1));
        Assert.False(token.IsCancellationRequested);

        guard.Stop();
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task ASecondStart_CancelsTheFirstBindingsToken_SoTheOldSessionIsLetGo()
    {
        var time = new ManualTimeProvider();
        var checks = new RecordingClockCheckService();
        using var guard = new ClockGuard(checks, time);

        await guard.StartAsync(Now, BackgroundPass.Inline);
        time.FireLiveTimers();
        var first = checks.Tokens[^1];

        // A lock/unlock hands the guard a new renderer; the old binding must not keep working.
        await guard.StartAsync(Now, BackgroundPass.Inline);
        Assert.True(first.IsCancellationRequested);

        time.FireLiveTimers();
        Assert.False(checks.Tokens[^1].IsCancellationRequested);
        Assert.Equal(1, time.LiveTimers);

        guard.Stop();
    }

    [Fact]
    public async Task ACancelledIntervalCheck_DoesNotTearDownTheGuard()
    {
        var time = new ManualTimeProvider();
        var checks = new RecordingClockCheckService();
        using var guard = new ClockGuard(checks, time);

        await guard.StartAsync(Now, BackgroundPass.Inline);
        checks.Throws = () => new OperationCanceledException();

        // The tick swallows the cancellation instead of letting it escape the timer callback.
        time.FireLiveTimers();
        Assert.Equal(1, time.LiveTimers);

        guard.Stop();
    }
}

/// <summary>
/// An <see cref="IClockCheckService"/> that records the token each call was given, so a test can
/// assert what <see cref="ClockGuard.Stop"/> does to a check that is already in flight.
/// </summary>
internal sealed class RecordingClockCheckService : IClockCheckService
{
    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>When set, every call throws what this returns.</summary>
    public Func<Exception>? Throws { get; set; }

    public Task<ClockCheck> CheckAsync(
        DateTime now,
        IReadOnlyCollection<DateTime>? otherDeviceTimestamps = null,
        CancellationToken cancellationToken = default)
    {
        Tokens.Add(cancellationToken);
        return Throws is null
            ? Task.FromResult(new ClockCheck { At = now, Verdict = ClockVerdict.Ok })
            : Task.FromException<ClockCheck>(Throws());
    }
}
