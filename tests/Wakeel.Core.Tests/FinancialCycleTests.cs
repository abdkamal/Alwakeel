using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class FinancialCycleTests : IDisposable
{
    private readonly string _root;
    private readonly DbSession _session;
    private readonly IFinancialCycleService _service;

    public FinancialCycleTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _);
        _service = new FinancialCycleService(_session.Db);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public void ComputeCycle_StartDay20_MatchesAgreementExample()
    {
        var bounds = _service.ComputeCycle(20, new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
        Assert.Equal("دورة أكتوبر 2026", bounds.NameAr);
    }

    [Fact]
    public void ComputeCycle_StartDay20_BeforeStartDay_BelongsToPreviousMonthsCycle()
    {
        var bounds = _service.ComputeCycle(20, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
        Assert.Equal("دورة سبتمبر 2026", bounds.NameAr);
    }

    [Fact]
    public void ComputeCycle_StartDay1_IsACalendarMonth()
    {
        var bounds = _service.ComputeCycle(1, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
        Assert.Equal("دورة مارس 2026", bounds.NameAr);
    }

    [Fact]
    public void ComputeCycle_StartDay1_HandlesNonLeapFebruary()
    {
        var bounds = _service.ComputeCycle(1, new DateTime(2027, 2, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2027, 2, 28, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
    }

    [Fact]
    public void ComputeCycle_StartDay1_HandlesLeapFebruary()
    {
        var bounds = _service.ComputeCycle(1, new DateTime(2028, 2, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
    }

    [Fact]
    public void ComputeCycle_CrossesYearEnd_NamesByEndYear()
    {
        var bounds = _service.ComputeCycle(20, new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc), bounds.StartDate);
        Assert.Equal(new DateTime(2027, 1, 19, 0, 0, 0, DateTimeKind.Utc), bounds.EndDate);
        Assert.Equal("دورة يناير 2027", bounds.NameAr);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(-1)]
    public void ComputeCycle_InvalidStartDay_Throws(int startDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ComputeCycle(startDay, DateTime.UtcNow));
    }

    [Fact]
    public async Task EnsureCurrentCycle_CreatesOpenCycle_ForToday()
    {
        TestHelpers.SeedInstallation(_session.Db, cycleStartDay: 1, activatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var cycle = await _service.EnsureCurrentCycleAsync(now);

        Assert.Equal(FinancialCycleStatus.Open, cycle.Status);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), cycle.StartDate);
    }

    [Fact]
    public async Task EnsureCurrentCycle_CalledTwiceForSamePeriod_DoesNotDuplicate()
    {
        TestHelpers.SeedInstallation(_session.Db, cycleStartDay: 1, activatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var first = await _service.EnsureCurrentCycleAsync(now);
        var second = await _service.EnsureCurrentCycleAsync(now);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_session.Db.FinancialCycles);
    }

    [Fact]
    public async Task EnsureCurrentCycle_MovesPastOpenCycleToAwaitingIssue()
    {
        TestHelpers.SeedInstallation(_session.Db, cycleStartDay: 1, activatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var january = await _service.EnsureCurrentCycleAsync(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(FinancialCycleStatus.Open, january.Status);

        await _service.EnsureCurrentCycleAsync(new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc));

        var refreshedJanuary = _session.Db.FinancialCycles.Single(c => c.Id == january.Id);
        Assert.Equal(FinancialCycleStatus.AwaitingIssue, refreshedJanuary.Status);
    }

    [Fact]
    public async Task EnsureCurrentCycle_AfterSoftDeleteOfCurrentCycle_RestoresTheHiddenRow_RatherThanReturningItStillDeleted()
    {
        // ux_financial_cycles_start_date is an UNFILTERED unique index (the single policy stated
        // at the top of 0001_initial.sql), so a soft-deleted cycle still owns its start date and
        // a second row for that date cannot be inserted. The service therefore looks the row up
        // with IgnoreDeleted() — and, because a soft-deleted cycle is hidden from every list and
        // report, it must not be handed back as "the current cycle" while still deleted: it is
        // restored (visible again, and Open) before being returned.
        TestHelpers.SeedInstallation(_session.Db, cycleStartDay: 1, activatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var first = await _service.EnsureCurrentCycleAsync(now);
        var tracked = _session.Db.FinancialCycles.Single(c => c.Id == first.Id);
        tracked.Status = FinancialCycleStatus.Issued;
        tracked.DeletedAt = DateTime.UtcNow;
        _session.Db.SaveChanges();
        Assert.False(_session.Db.FinancialCycles.Any(c => c.Id == first.Id));

        var second = await _service.EnsureCurrentCycleAsync(now);

        Assert.Equal(first.Id, second.Id);
        Assert.Null(second.DeletedAt);
        Assert.Equal(FinancialCycleStatus.Open, second.Status);

        // Visible again to the ordinary (filtered) queries every list and report uses, and still
        // the only row for that start date.
        Assert.Single(_session.Db.FinancialCycles.Where(c => c.Id == first.Id));
    }

    [Fact]
    public async Task EnsureCurrentCycle_WithLocalKindReferenceDate_NormalizesTheKind_LikeTheClockCheck()
    {
        // A caller passing DateTime.Now just after local midnight (or just before it, in a
        // negative-offset zone) must land on the same cycle the UTC instant belongs to: the Kind
        // is normalized exactly as ClockCheckService.CheckAsync does before the date is derived.
        TestHelpers.SeedInstallation(_session.Db, cycleStartDay: 1, activatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // The instant right after the cycle boundary, expressed as a Local-kind value.
        var utcJustAfterBoundary = new DateTime(2026, 3, 1, 0, 30, 0, DateTimeKind.Utc);
        var localJustAfterBoundary = utcJustAfterBoundary.ToLocalTime();
        Assert.Equal(DateTimeKind.Local, localJustAfterBoundary.Kind);

        var cycle = await _service.EnsureCurrentCycleAsync(localJustAfterBoundary);

        var expected = _service.ComputeCycle(1, utcJustAfterBoundary);
        Assert.Equal(expected.StartDate, cycle.StartDate);
        Assert.Equal(expected.EndDate, cycle.EndDate);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), cycle.StartDate);
    }

    [Fact]
    public void ComputeCycle_WithLocalKindReferenceDate_NearABoundary_MatchesTheSameUtcInstant()
    {
        // Same instant, three Kinds: the computed cycle must not depend on how the caller
        // labelled it, only on the UTC instant it represents.
        var utc = new DateTime(2026, 9, 19, 23, 45, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        var fromUtc = _service.ComputeCycle(20, utc);
        var fromLocal = _service.ComputeCycle(20, local);
        var fromUnspecified = _service.ComputeCycle(20, unspecified);

        // 19/09 is still inside the cycle that started on 20/08 — the boundary is the next day.
        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), fromUtc.StartDate);
        Assert.Equal(fromUtc, fromLocal);
        Assert.Equal(fromUtc, fromUnspecified);
        Assert.Equal(DateTimeKind.Utc, fromLocal.StartDate.Kind);
    }
}
