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
/// row and retries, and two contexts over the same database never issue the same number.
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
    /// How many times the read-modify-write of <c>official_numbers.last_seq</c> is retried after
    /// another context wins the race. Each attempt reloads the row, so a handful is plenty: the
    /// only contenders are the few contexts open over one office database.
    /// </summary>
    private const int MaxConcurrencyAttempts = 5;

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
        // and EF throws; the row is then reloaded and the whole step repeated against the fresh
        // value, so the same number is never handed out twice.
        for (var attempt = 1; ; attempt++)
        {
            var row = await db.OfficialNumbers.FirstOrDefaultAsync(n => n.Kind == kind && n.Year == year, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new OfficialNumber { Kind = kind, Year = year, LastSeq = 0, LastDate = today };
                db.OfficialNumbers.Add(row);
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
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                await db.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            return new OfficialNumberResult(number, sequence, sequence >= WarningThreshold);
        }
    }

    private static string Format(string format, int deviceNo, int employeeNo, DateTime date, int sequence)
        => format
            .Replace("YYYYMMDD", date.ToString("yyyyMMdd"))
            .Replace("SSS", sequence.ToString("000"))
            .Replace("D", deviceNo.ToString())
            .Replace("E", employeeNo.ToString());
}
