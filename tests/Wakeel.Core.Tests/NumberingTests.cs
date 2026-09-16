using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    public async Task Issue_WhenAnotherContextInsertedTheVeryFirstRowFirst_IsRetried_AndTheNumbersDiffer()
    {
        // Regression test for the INSERT race (verify-core.json B0-closeout, low finding 1):
        // when the (kind, year) row does not exist yet, two contexts can both take the
        // "row is null" branch and both try to insert it; the loser does not lose an
        // optimistic-concurrency check (there is no existing row to lose one against) but hits the
        // row's UNIQUE/PRIMARY KEY constraint instead — a plain DbUpdateException wrapping a
        // SqliteException, not a DbUpdateConcurrencyException, so it needs its own catch.
        //
        // A live two-connection race cannot reach that constraint on this database: SQLite (WAL)
        // resolves it earlier, as a "database is locked" busy error the moment the losing
        // transaction — its read snapshot now stale relative to the winner's commit — tries to
        // write at all (proven separately: even a plain read-only transaction on one context,
        // still open, makes a second context's own write attempt fail this way, regardless of
        // isolation level, and retrying inside that same transaction never recovers, since no
        // amount of retrying refreshes a transaction's snapshot). So the exact failure this test
        // needs is engineered without any live overlap: it tracks a new row on otherSession
        // exactly as this context's own first attempt would (Added, unsaved), then lets a second,
        // ordinary context fully insert and commit the real row first. Once otherSession opens its
        // own transaction afterwards, its snapshot is current, so its query returns that real row —
        // but identity resolution hands back the already-tracked (Added) instance instead of a
        // fresh one, so the service still tries to insert it, and this time the row really is
        // there: a genuine UNIQUE-constraint DbUpdateException, exactly as the review describes.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var paths = WakeelPaths.ForRoot(_root);
        using var otherSession = DbSession.Open(paths, _key);
        otherSession.Db.OfficialNumbers.Add(new OfficialNumber { Kind = InOutDirection.Out, Year = 2026, LastSeq = 0, LastDate = date });

        var winner = await IssueAsync(InOutDirection.Out, date);

        var losingService = new OfficialNumberService(otherSession.Db, new ClockCheckService(otherSession.Db));
        await using var transaction = await otherSession.Db.Database.BeginTransactionAsync();
        var loser = await losingService.IssueAsync(InOutDirection.Out, date, ClockVerdict.Ok);
        await transaction.CommitAsync();

        Assert.Equal(1, winner.Sequence);
        Assert.Equal(2, loser.Sequence);
        Assert.NotEqual(winner.Number, loser.Number);
    }

    [Fact]
    public async Task Issue_WhenAnotherTrackedEntityViolatesAnUnrelatedConstraint_PropagatesTheOriginalException_NotMisreadAsARace()
    {
        // Regression test for the review finding on IsSequenceRaceException (B0.5-CORE-POLISH):
        // it must be scoped to THIS official-numbers row and to the constraint's extended error
        // code, not just SQLite's primary "constraint failed" code 19 — that primary code also
        // covers NOT NULL, FOREIGN KEY and CHECK violations on ANY entity the caller has tracked
        // on this context, since IssueAsync's own SaveChangesAsync call flushes every other
        // tracked change too (see the class remarks). Here a second, unrelated Setting row with
        // its required Value column left null (via the null-forgiving operator, purely to
        // engineer the constraint violation) is tracked alongside the very first IssueAsync for a
        // (kind, year) row that does not exist yet. That NOT NULL violation must propagate
        // as-is — not be swallowed as a supposed insert race, and not corrupt the Added row so a
        // retry crashes with an untranslated EF identity-conflict message.
        //
        // Uses the explicit-verdict overload (via the three-argument IssueAsync helper) rather
        // than the two-argument one: the two-argument overload's clock check
        // (ClockCheckService.CheckAsync) calls SaveChangesAsync on this same context BEFORE
        // IssueCoreAsync's own numbering SaveChanges ever runs, so the invalid Setting would be
        // flushed and throw there instead — before any OfficialNumber row is even tracked, and
        // before IsSequenceRaceException is reached at all, which would make this test pass
        // regardless of how narrow (or broad) that method is.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        _session.Db.Settings.Add(new Setting
        {
            Key = "b0.5-core-polish-unrelated-constraint-test",
            Value = null!,
            UpdatedAt = date,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => IssueAsync(InOutDirection.Out, date, ClockVerdict.Ok));

        // Non-vacuous proof that the failure was actually seen by IssueCoreAsync's numbering
        // catch and correctly rejected as NOT this row's race: the failed Setting is among the
        // entries EF reports, the OfficialNumber row is not, and — since the transaction the
        // IssueAsync helper opened was never committed (the exception propagated instead) — no
        // number was left committed either.
        Assert.Contains(ex.Entries, e => e.Entity is Setting);
        Assert.DoesNotContain(ex.Entries, e => e.Entity is OfficialNumber);
        Assert.True(ContainsSqliteException(ex), "expected the SqliteException to still be reachable via InnerException");
        Assert.Empty(_session.Db.OfficialNumbers.AsNoTracking());
    }

    [Fact]
    public async Task Issue_WhenRetriesAreExhausted_ThrowsOfficialNumberConcurrencyException_PreservingTheSqliteException()
    {
        // Regression test for the review finding that the exhausted-retry path was untested and
        // mapped its exception through IErrorMapper only to splice away the reference, using the
        // same type (InvalidOperationException) as this service's own business refusals
        // (B0.5-CORE-POLISH). Drives the bounded retry to exhaustion via the internal
        // maxConcurrencyAttempts test seam (1 attempt), reusing the same proven insert-race setup
        // as Issue_WhenAnotherContextInsertedTheVeryFirstRowFirst_IsRetried_AndTheNumbersDiffer so
        // the single attempt hits a genuine PRIMARY KEY race and is exhausted immediately. Asserts
        // that what escapes is the dedicated OfficialNumberConcurrencyException — not a raw
        // DbUpdateException, and not the InvalidOperationException this service throws for
        // business refusals — and that its InnerException chain still reaches the SqliteException
        // so a caller's IErrorMapper can map it.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var paths = WakeelPaths.ForRoot(_root);
        using var otherSession = DbSession.Open(paths, _key);
        otherSession.Db.OfficialNumbers.Add(new OfficialNumber { Kind = InOutDirection.Out, Year = 2026, LastSeq = 0, LastDate = date });

        var winner = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(1, winner.Sequence);

        var losingService = new OfficialNumberService(otherSession.Db, new ClockCheckService(otherSession.Db), maxConcurrencyAttemptsForTests: 1);
        await using var transaction = await otherSession.Db.Database.BeginTransactionAsync();

        var ex = await Assert.ThrowsAsync<OfficialNumberConcurrencyException>(
            () => losingService.IssueAsync(InOutDirection.Out, date, ClockVerdict.Ok));

        Assert.True(ContainsSqliteException(ex), "expected the SqliteException to still be reachable via InnerException");
    }

    [Fact]
    public async Task Issue_WhenTheConflictingRowVanishesBeforeTheReload_DetachesTheStaleAddedEntry_AndTheRetrySucceeds()
    {
        // Regression test for the review finding that the Detached-recovery branch in
        // OfficialNumberService.cs (immediately after ReloadAsync in the catch) was executed by
        // no test (B0.5-CORE-POLISH). Neither existing INSERT-race test reaches it:
        // Issue_WhenAnotherContextInsertedTheVeryFirstRowFirst_... reloads straight into the
        // winner's still-committed row, so the entry flips to Unchanged, not Detached; and
        // Issue_WhenRetriesAreExhausted_... uses maxConcurrencyAttemptsForTests: 1, so it throws
        // before any reload runs at all. This test needs attempt 1's own insert to genuinely fail
        // against a real committed row (a real PRIMARY KEY violation — nothing about
        // IsSequenceRaceException's classification is faked), and then that row to be gone by the
        // time the service's own ReloadAsync runs one line later, in the same method call. A live
        // two-connection race cannot deliver that ordering deterministically (see the INSERT-race
        // test above for why SQLite/WAL turns real overlap into a "database is locked" busy error
        // instead), so it is engineered with a SaveChangesInterceptor that deletes the just-raced
        // row — via the SAME connection and transaction the failing SaveChanges is using, so nothing
        // needs its own competing write lock — the instant that SaveChanges fails, before the
        // service's catch block ever calls ReloadAsync.
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var paths = WakeelPaths.ForRoot(_root);

        var winner = await IssueAsync(InOutDirection.Out, date);
        Assert.Equal(1, winner.Sequence);

        var interceptor = new DeleteRowOnFirstSaveFailureInterceptor(InOutDirection.Out, 2026);
        using var connection = DbConnectionFactory.Open(paths.DbPath, _key);
        var options = new DbContextOptionsBuilder<WakeelDb>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        using var otherDb = new WakeelDb(options, new SystemClock(TimeProvider.System));

        // The exact same setup the proven INSERT-race test above uses: a tracked, unsaved Added
        // row for a (kind, year) that has no committed row of its own yet on THIS context's
        // snapshot — except here the winner's row genuinely exists, so this insert really does
        // collide with it.
        var trackedRow = new OfficialNumber { Kind = InOutDirection.Out, Year = 2026, LastSeq = 0, LastDate = date };
        otherDb.OfficialNumbers.Add(trackedRow);

        var losingService = new OfficialNumberService(otherDb, new ClockCheckService(otherDb), maxConcurrencyAttemptsForTests: 2);
        await using (var transaction = await otherDb.Database.BeginTransactionAsync())
        {
            var loser = await losingService.IssueAsync(InOutDirection.Out, date, ClockVerdict.Ok);
            await transaction.CommitAsync();

            // Attempt 1 raced the winner's row and lost (a genuine PRIMARY KEY violation); the
            // interceptor then deleted that row before ReloadAsync ran, so attempt 1's reload
            // found nothing and had to detach rather than adopt an existing row. Attempt 2 found
            // the slot empty again and inserted its own row fresh, from sequence 1 — proving the
            // retry recovered rather than throwing EF's untranslated identity-conflict
            // InvalidOperationException for re-adding an already-tracked instance.
            Assert.Equal(1, loser.Sequence);
        }

        Assert.Equal(EntityState.Detached, otherDb.Entry(trackedRow).State);
    }

    /// <summary>
    /// Test-only interceptor for
    /// <see cref="Issue_WhenTheConflictingRowVanishesBeforeTheReload_DetachesTheStaleAddedEntry_AndTheRetrySucceeds"/>:
    /// the moment a SaveChanges on <paramref name="kind"/>/<paramref name="year"/>'s context fails
    /// (for any reason — this only ever fires once, on the engineered PRIMARY KEY race), deletes
    /// that (kind, year) row through the SAME <see cref="WakeelDb"/>/connection/transaction that
    /// just failed, before control returns to the service's own catch block. EF Core rolls back
    /// the failed SaveChanges to its internal savepoint before dispatching this interceptor, so
    /// the delete lands in the still-open outer (caller-owned) transaction rather than being
    /// undone along with the failed insert.
    /// </summary>
    private sealed class DeleteRowOnFirstSaveFailureInterceptor(InOutDirection kind, int year) : SaveChangesInterceptor
    {
        private bool _deleted;

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (_deleted || eventData.Context is not WakeelDb context)
            {
                return;
            }

            _deleted = true;
            await context.OfficialNumbers
                .Where(n => n.Kind == kind && n.Year == year)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool ContainsSqliteException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqliteException)
            {
                return true;
            }
        }

        return false;
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
