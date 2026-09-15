using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class NumberingTests : IDisposable
{
    private readonly string _root;
    private readonly byte[] _key;
    private readonly DbSession _session;
    private readonly IOfficialNumberService _service;

    public NumberingTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _key);
        TestHelpers.SeedInstallation(_session.Db, deviceNo: 1, employeeNo: 2);
        _service = new OfficialNumberService(_session.Db, new ClockCheckService(_session.Db));
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    // ARCHITECTURE.md §5 (decision of 2026-09-16): a number may only be issued inside a
    // transaction opened by the caller, so that the number and the approval it belongs to commit
    // together. These helpers play the part of that caller — B3's correspondence approval will
    // open the same transaction around its own writes.
    private async Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now)
    {
        await using var transaction = await _session.Db.Database.BeginTransactionAsync();
        var result = await _service.IssueAsync(kind, now);
        await transaction.CommitAsync();
        return result;
    }

    private async Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now, ClockVerdict clockVerdict)
    {
        await using var transaction = await _session.Db.Database.BeginTransactionAsync();
        var result = await _service.IssueAsync(kind, now, clockVerdict);
        await transaction.CommitAsync();
        return result;
    }

    // The following tests use the primary two-argument overload, which runs Core's own
    // ARCHITECTURE.md §9 clock check itself; installation is seeded with build/activation dates
    // of 2026-01-01, so every non-decreasing date used below passes that check on its own.

    [Fact]
    public async Task Issue_FirstNumber_MatchesFormatFromArchitectureExample()
    {
        var result = await IssueAsync(InOutDirection.Out, new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("20260911/12001", result.Number);
        Assert.Equal(1, result.Sequence);
        Assert.False(result.NearLimitWarning);
    }

    [Fact]
    public async Task Issue_Sequence_IncrementsWithinSameYear()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var first = await IssueAsync(InOutDirection.Out, date);
        var second = await IssueAsync(InOutDirection.Out, date);
        var third = await IssueAsync(InOutDirection.Out, date.AddDays(1));

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(3, third.Sequence);
        Assert.Equal("20260912/12003", third.Number);
    }

    [Fact]
    public async Task Issue_InAndOut_HaveIndependentSequences()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var out1 = await IssueAsync(InOutDirection.Out, date);
        var in1 = await IssueAsync(InOutDirection.In, date);
        var out2 = await IssueAsync(InOutDirection.Out, date);

        Assert.Equal(1, out1.Sequence);
        Assert.Equal(1, in1.Sequence);
        Assert.Equal(2, out2.Sequence);
    }

    [Fact]
    public async Task Issue_NewYear_ResetsSequenceToOne()
    {
        var lastDayOfYear = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var firstDayOfNextYear = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var last2026 = await IssueAsync(InOutDirection.Out, lastDayOfYear);
        var first2027 = await IssueAsync(InOutDirection.Out, firstDayOfNextYear);

        Assert.Equal(1, last2026.Sequence);
        Assert.Equal(1, first2027.Sequence);
        Assert.Equal("20261231/12001", last2026.Number);
        Assert.Equal("20270101/12001", first2027.Number);
    }

    [Fact]
    public async Task Issue_EarlierDateThanLastIssued_IsRefused()
    {
        var later = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var earlier = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

        await IssueAsync(InOutDirection.Out, later);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, earlier));
    }

    [Fact]
    public async Task Issue_SameDateAsLastIssued_IsAllowed()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        await IssueAsync(InOutDirection.Out, date);
        var second = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task Issue_At900_RaisesNearLimitWarning()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i < 900; i++)
        {
            var r = await IssueAsync(InOutDirection.Out, date);
            Assert.False(r.NearLimitWarning);
        }

        var at900 = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(900, at900.Sequence);
        Assert.True(at900.NearLimitWarning);
    }

    [Fact]
    public async Task Issue_At999_Succeeds_ThenSequenceIsExhausted()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i < 999; i++)
        {
            await IssueAsync(InOutDirection.Out, date);
        }

        var last = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(999, last.Sequence);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, date));
    }

    [Fact]
    public async Task Issue_TwoArgOverload_RunsClockCheckItself_AndRefusesWhenClockIsBad()
    {
        // No ClockVerdict is passed here at all: this demonstrates that the primary entry point
        // enforces ARCHITECTURE.md §9 on its own rather than trusting a caller-supplied verdict.
        // Installation's build date is 2026-01-01 (see TestHelpers.SeedInstallation); a date
        // before that must fail the clock check and so refuse numbering.
        var beforeBuildDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, beforeBuildDate));
    }

    [Fact]
    public async Task Issue_WithoutAnOpenTransaction_IsRefused_OnBothOverloads()
    {
        // ARCHITECTURE.md §5 (decision of 2026-09-16): issuing outside a transaction would let
        // the number be committed while the approval it belongs to is rolled back. Both entry
        // points refuse before touching the database; the message is an English
        // programming-error message because no user can act on it.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        Assert.Null(_session.Db.Database.CurrentTransaction);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.IssueAsync(InOutDirection.Out, date));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.IssueAsync(InOutDirection.Out, date, ClockVerdict.Ok));

        // Nothing was written: no number was consumed and no clock check was recorded.
        Assert.Empty(_session.Db.OfficialNumbers);
        Assert.Empty(_session.Db.ClockChecks);
    }

    [Fact]
    public async Task Issue_WhenAnotherContextIssuedInBetween_IsRetried_AndTheNumbersDiffer()
    {
        // Regression test for the reissue race: official_numbers.last_seq is an
        // optimistic-concurrency token, so a context writing a value another context has already
        // moved past updates no row; the service reloads and retries instead of handing out a
        // number that is already in use. Two DbSession instances are two connections to the same
        // database file — exactly the app-plus-background-job case.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var paths = WakeelPaths.ForRoot(_root);
        using var otherSession = DbSession.Open(paths, _key);
        var otherService = new OfficialNumberService(otherSession.Db, new ClockCheckService(otherSession.Db));

        var first = await IssueAsync(InOutDirection.Out, date);

        // The other context reads the row now, so its tracked copy is pinned at last_seq = 1 ...
        var stale = await otherSession.Db.OfficialNumbers
            .FirstAsync(n => n.Kind == InOutDirection.Out && n.Year == 2026);
        Assert.Equal(1, stale.LastSeq);

        // ... while this context moves the database on to last_seq = 2.
        var second = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(2, second.Sequence);

        // The other context now issues against its stale value.
        await using var transaction = await otherSession.Db.Database.BeginTransactionAsync();
        var third = await otherService.IssueAsync(InOutDirection.Out, date);
        await transaction.CommitAsync();

        Assert.Equal(1, first.Sequence);
        Assert.Equal(3, third.Sequence);
        Assert.Equal(3, new[] { first.Number, second.Number, third.Number }.Distinct(StringComparer.Ordinal).Count());
    }

    // The following two tests use the explicit-verdict overload directly: they isolate
    // OfficialNumberService's own logic (verdict pass-through, and the cross-year backdating
    // guard) from ClockCheckService's independent behavior, which is covered by ClockCheckTests.

    [Fact]
    public async Task Issue_WhenClockVerdictIsNotOk_IsRefused()
    {
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, date, ClockVerdict.Bad));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, date, ClockVerdict.Suspect));
    }

    [Fact]
    public async Task Issue_EarlierDateThanLastIssued_IsRefused_AcrossAYearBoundary_EvenWhenTheEarlierYearHasNoRowYet()
    {
        // Regression test: issuing first into a later year, then attempting an earlier date in a
        // year that has no official_numbers row of its own yet, must still be refused — the
        // per-(kind, year) row lookup alone cannot see this, so the service must also check a
        // watermark across every year for this kind.
        await IssueAsync(InOutDirection.Out, new DateTime(2027, 3, 5, 0, 0, 0, DateTimeKind.Utc), ClockVerdict.Ok);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IssueAsync(InOutDirection.Out, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), ClockVerdict.Ok));
    }
}
