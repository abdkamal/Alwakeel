using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>The computed boundaries and Arabic name of a financial cycle, before it necessarily exists as a row.</summary>
public sealed record FinancialCycleBounds(DateTime StartDate, DateTime EndDate, string NameAr);

/// <summary>
/// Financial cycle math (AGREEMENT item 52): a cycle runs from the configured start day (1-28)
/// through the day before the next start day, and is named "دورة &lt;شهر&gt; &lt;سنة&gt;" by its
/// END month. Example: start day 20 → 20/09/2026-19/10/2026, "دورة أكتوبر 2026".
/// </summary>
public interface IFinancialCycleService
{
    /// <summary>Pure boundary/name computation for the cycle that contains <paramref name="referenceDate"/>; does not touch the database.</summary>
    FinancialCycleBounds ComputeCycle(int startDay, DateTime referenceDate);

    /// <summary>
    /// Ensures the cycle containing <paramref name="now"/> exists (creating it as Open if missing),
    /// and moves any still-Open cycle whose end date has passed to AwaitingIssue.
    /// </summary>
    Task<FinancialCycle> EnsureCurrentCycleAsync(DateTime now, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFinancialCycleService"/>
public sealed class FinancialCycleService(WakeelDb db) : IFinancialCycleService
{
    private static readonly string[] ArabicMonthNames =
    [
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر",
    ];

    public FinancialCycleBounds ComputeCycle(int startDay, DateTime referenceDate)
    {
        if (startDay is < 1 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(startDay), startDay, "Cycle start day must be between 1 and 28.");
        }

        // Normalize the Kind exactly as ClockCheckService.CheckAsync does before deriving the
        // date: a caller passing DateTime.Now shortly after local midnight (or shortly before it
        // in a negative-offset zone) would otherwise compute the cycle of the wrong day, and the
        // resulting boundaries would be compared, as ISO text, against UTC-written values.
        var date = NormalizeToUtc(referenceDate).Date;
        var start = new DateTime(date.Year, date.Month, startDay, 0, 0, 0, DateTimeKind.Utc);
        if (start > date)
        {
            start = start.AddMonths(-1);
        }

        var end = start.AddMonths(1).AddDays(-1);
        var name = $"دورة {ArabicMonthNames[end.Month - 1]} {end.Year}";
        return new FinancialCycleBounds(start, end, name);
    }

    public async Task<FinancialCycle> EnsureCurrentCycleAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("financial cycle unavailable: installation is not configured");

        // Same Kind normalization as ComputeCycle and ClockCheckService.CheckAsync, so `today`
        // and the computed boundaries are derived from one and the same UTC instant.
        var utcNow = NormalizeToUtc(now);
        var today = utcNow.Date;
        var bounds = ComputeCycle(installation.CycleStartDay, utcNow);

        var pastOpenCycles = await db.FinancialCycles
            .Where(c => c.Status == FinancialCycleStatus.Open && c.EndDate < today)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pastCycle in pastOpenCycles)
        {
            pastCycle.Status = FinancialCycleStatus.AwaitingIssue;
        }

        // IgnoreDeleted(): ux_financial_cycles_start_date is an UNFILTERED unique index (the
        // single policy stated at the top of 0001_initial.sql), so a soft-deleted cycle still
        // occupies its start date and inserting a second row for that date would fail with an
        // unmapped SQLite error. The default query filter would hide such a row from this
        // lookup, so the lookup opts out of it — and, since a hidden row must never be handed
        // back as "the current cycle" while every list and report filters it away, a row found
        // this way is restored (visible again, and Open) before it is returned.
        var current = await db.FinancialCycles.IgnoreDeleted()
            .FirstOrDefaultAsync(c => c.StartDate == bounds.StartDate, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            current = new FinancialCycle
            {
                NameAr = bounds.NameAr,
                StartDate = bounds.StartDate,
                EndDate = bounds.EndDate,
                Status = FinancialCycleStatus.Open,
            };
            db.FinancialCycles.Add(current);
        }
        else if (current.DeletedAt is not null)
        {
            current.DeletedAt = null;
            current.Status = FinancialCycleStatus.Open;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return current;
    }

    /// <summary>Same Kind normalization as <see cref="ClockCheckService.CheckAsync"/>: Local converts, Unspecified is taken as UTC.</summary>
    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
