using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Official number, plus whether the sequence is close to running out (ARCHITECTURE.md §5, AGREEMENT item 5).</summary>
public sealed record OfficialNumberResult(string Number, int Sequence, bool NearLimitWarning);

/// <summary>
/// Issues official numbers per ARCHITECTURE.md §5: format <c>YYYYMMDD/DESSS</c> (D = device
/// number, E = employee number, SSS = 001-999, reset yearly), separate incoming/outgoing
/// sequences, no number for a draft, refusal of a date earlier than the last issued number's
/// date (enforced across a year boundary, not just within the current year), and a near-limit
/// warning at 900.
/// </summary>
/// <remarks>
/// Transactional contract (ARCHITECTURE.md §5, decision of 2026-09-16): the number and the
/// approval it belongs to must be committed together or not at all, so
/// <see cref="IssueAsync(InOutDirection,DateTime,CancellationToken)"/> and its explicit-verdict
/// overload REFUSE to run unless the caller has already opened a transaction on the same context
/// (<c>db.Database.BeginTransaction()</c>); calling without one throws
/// <see cref="InvalidOperationException"/> — a programming error, not something the user can act
/// on. Both overloads call <see cref="WakeelDb.SaveChangesAsync(CancellationToken)"/> on the
/// injected <see cref="WakeelDb"/> themselves, which flushes every other tracked change on that
/// context into the caller's transaction. If that transaction is later rolled back,
/// the tracked <c>OfficialNumber</c> row keeps its incremented <c>LastSeq</c> in memory while the
/// database reverts to the old value; the caller must <c>db.Entry(row).Reload()</c> (or discard
/// the <see cref="WakeelDb"/> instance) after a rollback, or the next issue will silently skip a
/// number, which the AGREEMENT item 37 sequence-integrity check would later flag.
/// <c>official_numbers.last_seq</c> is an optimistic-concurrency token, so a write from a context
/// holding a value another context has already moved on affects no row; the service reloads that
/// row and retries. The very first number of a (kind, year) races differently: two contexts can
/// both find no row yet and both try to insert one, so the loser's INSERT fails on the row's
/// UNIQUE/PRIMARY KEY constraint instead of losing a concurrency check, there being no existing
/// row to lose one against; the service detects that failure too — but ONLY when the failing
/// SQLite extended error code names a primary-key or unique violation AND the exception's own
/// <c>Entries</c> name this method's own <c>OfficialNumber</c> row as one of the offenders, since
/// the SaveChanges this method calls also flushes every other change the caller has tracked on
/// the same context, and an unrelated NOT NULL, FOREIGN KEY or CHECK violation on one of those
/// (or even a PRIMARY KEY/UNIQUE violation on a different entity entirely) must propagate as-is
/// rather than being misread as this race — and reloads and retries exactly as it would for the
/// ordinary case, detaching the row first if the reload finds it still absent (a re-query, not a
/// reload, since the row was never actually inserted) so the next attempt cannot collide with its
/// own stale Added instance. The row the loser just tried (and failed) to insert then reloads into
/// the winner's now-existing row, so two contexts over the same database never issue the same
/// number, whether the row already existed or not. Either failure that survives every retry is
/// thrown as <see cref="OfficialNumberConcurrencyException"/> — wrapping the original
/// <see cref="DbUpdateConcurrencyException"/> or <see cref="DbUpdateException"/> unchanged as its
/// <see cref="Exception.InnerException"/> — rather than escaping as a raw
/// <see cref="DbUpdateException"/>, or being pre-mapped through an <see cref="IErrorMapper"/> here
/// (which would mint a reference nothing logs and mint a second, different one when the caller
/// maps the escaping exception, and would also be indistinguishable from the
/// <see cref="InvalidOperationException"/>s this method throws for user-actionable business
/// refusals). Callers should catch <see cref="OfficialNumberConcurrencyException"/> and map its
/// <see cref="Exception.InnerException"/> through their own <see cref="IErrorMapper"/> exactly
/// once.
/// </remarks>
public interface IOfficialNumberService
{
    /// <summary>
    /// Issues the next number of <paramref name="kind"/> for <paramref name="now"/>, running the
    /// ARCHITECTURE.md §9 clock check itself first and refusing on anything but
    /// <see cref="ClockVerdict.Ok"/> — this is the entry point services should call. Also refuses
    /// (throws <see cref="InvalidOperationException"/>) when <paramref name="now"/> is earlier
    /// than the last issued date for this kind (checked globally, across year boundaries), or
    /// when no more numbers remain this year.
    /// </summary>
    Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues the next number of <paramref name="kind"/> for <paramref name="now"/>, skipping
    /// Core's own clock check in favor of a caller-supplied <paramref name="clockVerdict"/>.
    /// Intended for callers that already ran (and want to reuse) a clock check of their own, and
    /// for tests; production code should normally prefer the two-argument overload above so the
    /// ARCHITECTURE.md §9 guarantee cannot be silently skipped. Refuses (throws
    /// <see cref="InvalidOperationException"/>) when <paramref name="now"/> is earlier than the
    /// last issued date for this kind, when no more numbers remain this year, or when
    /// <paramref name="clockVerdict"/> is not <see cref="ClockVerdict.Ok"/>.
    /// </summary>
    Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now, ClockVerdict clockVerdict, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOfficialNumberService"/>
public sealed class OfficialNumberService(WakeelDb db, IClockCheckService clockCheckService) : IOfficialNumberService
{
    public const string DefaultFormat = "YYYYMMDD/DESSS";
    private const int MaxSequence = 999;
    public const int WarningThreshold = 900;

    /// <summary>
    /// SQLite's primary result code for ANY constraint violation — UNIQUE, PRIMARY KEY, NOT NULL,
    /// FOREIGN KEY and CHECK all report this same primary code, distinguished only by the more
    /// specific <see cref="SqliteException.SqliteExtendedErrorCode"/> (see
    /// <see cref="SqlitePrimaryKeyConstraint"/> and <see cref="SqliteUniqueConstraint"/> below).
    /// The primary code alone is therefore not sufficient to identify the sequence-insert race.
    /// </summary>
    private const int SqliteConstraintErrorCode = 19;

    /// <summary>SQLite's extended result code for a PRIMARY KEY constraint violation.</summary>
    private const int SqlitePrimaryKeyConstraint = 1555;

    /// <summary>SQLite's extended result code for a UNIQUE constraint violation.</summary>
    private const int SqliteUniqueConstraint = 2067;

    /// <summary>
    /// How many times the read-modify-write of <c>official_numbers.last_seq</c> is retried after
    /// another context wins the race — either by updating the row first (a lost optimistic-
    /// concurrency check) or by inserting it first (a lost UNIQUE/PRIMARY KEY race on the row's
    /// very first insert). Each attempt re-reads the row, so a handful is plenty: the only
    /// contenders are the few contexts open over one office database.
    /// </summary>
    private const int MaxConcurrencyAttempts = 5;

    /// <summary>
    /// Test-only override of <see cref="MaxConcurrencyAttempts"/> (negative means "use the
    /// production default"), set only by the internal test constructor below. Lets a test drive
    /// the bounded retry to exhaustion deterministically — with this at 1, a single genuine,
    /// engineered race is enough — instead of having to force <see cref="MaxConcurrencyAttempts"/>
    /// consecutive real races through actual thread interleaving.
    /// </summary>
    private readonly int maxConcurrencyAttemptsOverride = -1;

    /// <summary>Test-only constructor: see <see cref="maxConcurrencyAttemptsOverride"/>.</summary>
    internal OfficialNumberService(WakeelDb db, IClockCheckService clockCheckService, int maxConcurrencyAttemptsForTests)
        : this(db, clockCheckService)
    {
        maxConcurrencyAttemptsOverride = maxConcurrencyAttemptsForTests;
    }

    private int EffectiveMaxConcurrencyAttempts => maxConcurrencyAttemptsOverride < 0 ? MaxConcurrencyAttempts : maxConcurrencyAttemptsOverride;

    public async Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now, CancellationToken cancellationToken = default)
    {
        EnsureInTransaction();
        var check = await clockCheckService.CheckAsync(now, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await IssueCoreAsync(kind, now, check.Verdict, cancellationToken).ConfigureAwait(false);
    }

    public Task<OfficialNumberResult> IssueAsync(InOutDirection kind, DateTime now, ClockVerdict clockVerdict, CancellationToken cancellationToken = default)
    {
        EnsureInTransaction();
        return IssueCoreAsync(kind, now, clockVerdict, cancellationToken);
    }

    /// <summary>
    /// ARCHITECTURE.md §5: the number must be issued inside the same transaction as the approval
    /// it belongs to. This is a contract between Core and its callers, so the message is English
    /// and no user ever sees it — a caller reaching here without a transaction has a bug.
    /// </summary>
    private void EnsureInTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "OfficialNumberService.IssueAsync requires an open transaction on the same WakeelDb: "
                + "call db.Database.BeginTransaction() (or BeginTransactionAsync) first so the number "
                + "and the approval it belongs to commit together.");
        }
    }

    private async Task<OfficialNumberResult> IssueCoreAsync(InOutDirection kind, DateTime now, ClockVerdict clockVerdict, CancellationToken cancellationToken)
    {
        if (clockVerdict != ClockVerdict.Ok)
        {
            throw new InvalidOperationException("official numbering refused: clock check did not pass");
        }

        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("official numbering refused: installation is not configured");

        var utcNow = now.Kind switch
        {
            DateTimeKind.Utc => now,
            DateTimeKind.Local => now.ToUniversalTime(),
            _ => DateTime.SpecifyKind(now, DateTimeKind.Utc),
        };
        var today = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var year = today.Year;

        // Global watermark across every year for this kind: a date earlier than the last issued
        // number must be refused even when the earlier year has no official_numbers row of its
        // own yet (e.g. issuing for 2027 first, then attempting a backdated 2026 number).
        var lastDate = await db.OfficialNumbers.Where(n => n.Kind == kind)
            .Select(n => (DateTime?)n.LastDate)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        if (lastDate is not null && today < lastDate.Value.Date)
        {
            throw new InvalidOperationException("official numbering refused: date is earlier than the last issued number");
        }

        var format = string.IsNullOrWhiteSpace(installation.NumberingFormat) ? DefaultFormat : installation.NumberingFormat;

        // Read-modify-write of last_seq, guarded by the concurrency token on that column: if
        // another context issued a number since this one read the row, the UPDATE matches no row
        // and EF throws DbUpdateConcurrencyException; if another context inserted the row for the
        // very first time since this one read it, the INSERT hits the row's UNIQUE/PRIMARY KEY
        // constraint and EF throws a plain DbUpdateException wrapping that SqliteException instead
        // (there being no existing row to lose a concurrency check against). Either way, reloading
        // the entry re-queries by (kind, year) and refreshes it (and, for the INSERT case, flips
        // its state from Added to Unchanged once the query proves a row now exists) so the next
        // attempt updates the real row instead of trying to insert a second one — the same number
        // is never handed out twice, whether it was the row's first insert or a later update that
        // raced. IsSequenceRaceException below only recognises a concurrency conflict or a PRIMARY
        // KEY/UNIQUE failure that names this row's own entry, so an unrelated concurrency conflict
        // or constraint violation on some other entity the caller has tracked on this context is
        // never caught here at all — it propagates immediately.
        for (var attempt = 1; ; attempt++)
        {
            var row = await db.OfficialNumbers.FirstOrDefaultAsync(n => n.Kind == kind && n.Year == year, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new OfficialNumber { Kind = kind, Year = year, LastSeq = 0, LastDate = today };
                db.OfficialNumbers.Add(row);
            }

            // Re-check the watermark against THIS row's own LastDate on every iteration, including
            // after a reload: the pre-loop MaxAsync check above only proves the date was not
            // backdated at the instant it ran. A retry only ever happens because another context
            // raced THIS SAME (kind, year) row (see IsSequenceRaceException — a race on a
            // different row never lands here at all, since it would not fail this row's
            // SaveChanges), so the ReloadAsync in the catch below always refreshes row.LastDate to
            // whatever that other context just committed before control returns here. Without this
            // recheck, a losing context could reload a row another context just advanced to a
            // later date and still unconditionally overwrite it with its own earlier `today`,
            // regressing official_numbers.last_date and issuing a number dated before one already
            // issued of the same kind — silently bypassing the very invariant the pre-loop check
            // exists to enforce (review finding, B0.5/verify2-core-polish.json).
            if (today < row.LastDate.Date)
            {
                throw new InvalidOperationException("official numbering refused: date is earlier than the last issued number");
            }

            if (row.LastSeq >= MaxSequence)
            {
                throw new InvalidOperationException("official numbering refused: the yearly sequence is exhausted");
            }

            row.LastSeq += 1;
            row.LastDate = today;

            var sequence = row.LastSeq;
            var number = Format(format, installation.DeviceNo, installation.EmployeeNo, today, sequence);

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsSequenceRaceException(ex, row))
            {
                if (attempt >= EffectiveMaxConcurrencyAttempts)
                {
                    throw new OfficialNumberConcurrencyException(
                        $"official numbering failed after {attempt} attempts racing another context over the same (kind, year) row",
                        ex);
                }

                await db.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);

                // The row was Added (this context's own first-ever insert for this (kind, year))
                // and the reload still found no matching row in the database — the failure was
                // therefore never actually this row's own INSERT colliding with a winner's row
                // (that would have flipped the reload to Unchanged), so leaving it tracked as
                // Added would make the next attempt's Add(...) above throw an untranslated EF
                // identity-conflict InvalidOperationException instead of retrying. Detach it so
                // the next attempt re-adds a fresh, untracked row instead.
                if (db.Entry(row).State == EntityState.Added)
                {
                    db.Entry(row).State = EntityState.Detached;
                }

                continue;
            }

            return new OfficialNumberResult(number, sequence, sequence >= WarningThreshold);
        }
    }

    /// <summary>
    /// True for either way another context can win the race on THIS <paramref name="row"/>'s
    /// (kind, year) slot: an optimistic-concurrency loss on an existing row's <c>last_seq</c>
    /// (<see cref="DbUpdateConcurrencyException"/>), or a PRIMARY KEY/UNIQUE violation on the
    /// INSERT when the row did not exist yet and two contexts both tried to create it. BOTH cases
    /// are deliberately narrowed the same way: <see cref="DbUpdateException.Entries"/> must name
    /// this exact <paramref name="row"/> instance as one of the entities SaveChanges failed on —
    /// otherwise a concurrency conflict or constraint violation on some other entity the caller
    /// has tracked on this same context (SaveChangesAsync flushes those together with this row)
    /// would be misattributed to this (kind, year) row's race. For the INSERT case specifically,
    /// the SQLite failure must also be a PRIMARY KEY or UNIQUE violation (its
    /// <see cref="SqliteException.SqliteExtendedErrorCode"/>), not merely SQLite's shared primary
    /// <see cref="SqliteConstraintErrorCode"/> — that primary code also covers NOT NULL, FOREIGN
    /// KEY and CHECK violations. Without either check, an unrelated failure would be misclassified
    /// as this race, and reloading a row whose own write never actually failed would leave it in a
    /// broken state (see the Detached handling at the call site).
    /// </summary>
    private static bool IsSequenceRaceException(DbUpdateException exception, OfficialNumber row)
        => exception.Entries.Any(e => ReferenceEquals(e.Entity, row))
            && (exception is DbUpdateConcurrencyException
                || (exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintErrorCode } sqlite
                    && sqlite.SqliteExtendedErrorCode is SqlitePrimaryKeyConstraint or SqliteUniqueConstraint));

    private static string Format(string format, int deviceNo, int employeeNo, DateTime date, int sequence)
        => format
            .Replace("YYYYMMDD", date.ToString("yyyyMMdd"))
            .Replace("SSS", sequence.ToString("000"))
            .Replace("D", deviceNo.ToString())
            .Replace("E", employeeNo.ToString());
}

/// <summary>
/// Thrown by <see cref="OfficialNumberService"/> when the bounded retry of the read-modify-write
/// of an <c>official_numbers</c> row is exhausted without winning the race against another
/// context. Distinct from the <see cref="InvalidOperationException"/>s this service throws for
/// user-actionable business refusals (clock check, exhausted yearly sequence, backdated date,
/// missing installation), so a caller can tell an infrastructure failure from those; distinct
/// from a raw <see cref="DbUpdateException"/> so a caller is not left to reverse-engineer this
/// specific meaning out of a generic EF exception; and never pre-mapped through an
/// <see cref="IErrorMapper"/> by this service itself, so the caller who does map it gets exactly
/// one reference for the failure rather than the mapper minting a fresh one here (which would go
/// unlogged) and a second, different one when the caller maps the exception it actually sees. The
/// original <see cref="DbUpdateConcurrencyException"/> or <see cref="DbUpdateException"/> is
/// preserved, unchanged, as <see cref="Exception.InnerException"/> so <see cref="IErrorMapper"/>'s
/// own unwrap logic still reaches the underlying <see cref="SqliteException"/>.
/// </summary>
public sealed class OfficialNumberConcurrencyException(string message, DbUpdateException innerException)
    : Exception(message, innerException);
