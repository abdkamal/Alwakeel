using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>The five things the quick-capture dialog (W94) can create.</summary>
public enum QuickCaptureKind
{
    /// <summary>«مهمة».</summary>
    Task,

    /// <summary>«ملاحظة».</summary>
    Note,

    /// <summary>«ملاحظة للتقرير» — a note flagged for the monthly report (AGREEMENT item 53(a)).</summary>
    ReportNote,

    /// <summary>«مصروف» (AGREEMENT item 50).</summary>
    Expense,

    /// <summary>«موعد».</summary>
    Appointment,
}

/// <summary>What one quick capture produced.</summary>
/// <param name="Kind">Which section of the dialog was used.</param>
/// <param name="Id">Id of the created record.</param>
/// <param name="UndoToken">Token the toast's «تراجع» passes back to <see cref="IQuickCaptureService.UndoAsync"/>.</param>
/// <param name="MessageAr">The toast's Arabic text.</param>
/// <param name="UndoableUntil">After this instant the token is refused, UTC.</param>
public sealed record QuickCaptureResult(
    QuickCaptureKind Kind,
    Guid Id,
    string UndoToken,
    string MessageAr,
    DateTime UndoableUntil);

/// <summary>
/// The quick-capture dialog's back end (W94): a task, a note, a note for the monthly report, an
/// expense or an appointment from a handful of short fields, saved immediately, with an undo
/// token the toast can use (AGREEMENT item 32).
/// </summary>
public interface IQuickCaptureService
{
    /// <summary>Creates a task. <paramref name="titleAr"/> is required; everything else is optional.</summary>
    Task<QuickCaptureResult> CaptureTaskAsync(string titleAr, DateTime? dueAt = null, TaskPriority priority = TaskPriority.Normal, string? assigneeAr = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a free-text note.</summary>
    Task<QuickCaptureResult> CaptureNoteAsync(string textAr, CancellationToken cancellationToken = default);

    /// <summary>Creates a note flagged for the monthly report.</summary>
    Task<QuickCaptureResult> CaptureReportNoteAsync(string textAr, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an expense captured on this PC. It is booked directly (unlike a phone expense,
    /// which waits for confirmation — AGREEMENT item 50), into the open financial cycle.
    /// </summary>
    Task<QuickCaptureResult> CaptureExpenseAsync(long amountAgorot, string purposeAr, string? categoryNameAr = null, string? noteAr = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a calendar appointment.</summary>
    Task<QuickCaptureResult> CaptureAppointmentAsync(string titleAr, DateTime startsAt, int? reminderMinutes = null, string? notesAr = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a capture. Returns false when the token is unknown, already used, or expired — the
    /// caller then leaves the record in place rather than guessing what to remove.
    /// </summary>
    Task<bool> UndoAsync(string undoToken, CancellationToken cancellationToken = default);

    /// <summary>How long an undo token stays valid.</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(2);
}

/// <inheritdoc cref="IQuickCaptureService"/>
/// <remarks>
/// <para>
/// <b>Undo soft-deletes.</b> Official records are never physically deleted (DATA-MODEL.md §0,
/// ARCHITECTURE.md §12), so undo sets <c>deleted_at</c> — which is exactly what makes the record
/// vanish from every list, badge and report, because every query goes through the soft-delete
/// query filter. An expense's undo also removes its ledger entries the same way, so the book
/// balance goes back to what it was.
/// </para>
/// <para>
/// <b>Tokens live in memory, scoped to the session.</b> They are opaque, single-use, and expire
/// after <see cref="IQuickCaptureService.UndoWindow"/> — long enough for the toast, short enough
/// that a token cannot quietly delete a record the user has since edited. Nothing about a token
/// is persisted: a closed application forgets them, which is the correct behaviour for an undo
/// that belongs to a toast on screen.
/// </para>
/// </remarks>
public sealed class QuickCaptureService(
    WakeelDb db,
    IClock clock,
    IIdGenerator ids,
    IFinancialCycleService cycles,
    IBadgeService badges) : IQuickCaptureService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, UndoEntry> _undo = new(StringComparer.Ordinal);

    public async Task<QuickCaptureResult> CaptureTaskAsync(
        string titleAr,
        DateTime? dueAt = null,
        TaskPriority priority = TaskPriority.Normal,
        string? assigneeAr = null,
        CancellationToken cancellationToken = default)
    {
        var title = Required(titleAr, nameof(titleAr));
        var row = new TaskItem
        {
            Id = ids.NewId(),
            Title = title,
            DueAt = dueAt is null ? null : ArabicRelativeTime.ToUtc(dueAt.Value),
            Priority = priority,
            Status = WorkTaskStatus.Open,
            AssigneeName = string.IsNullOrWhiteSpace(assigneeAr) ? null : assigneeAr.Trim(),
            SourceDeviceKind = DeviceKind.Pc,
        };
        db.Tasks.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Finish(QuickCaptureKind.Task, row.Id, CoreAr.QuickCaptureTaskSaved, new UndoEntry(UndoTarget.Task, row.Id, null));
    }

    public Task<QuickCaptureResult> CaptureNoteAsync(string textAr, CancellationToken cancellationToken = default) =>
        CaptureNoteCoreAsync(textAr, forReport: false, cancellationToken);

    public Task<QuickCaptureResult> CaptureReportNoteAsync(string textAr, CancellationToken cancellationToken = default) =>
        CaptureNoteCoreAsync(textAr, forReport: true, cancellationToken);

    private async Task<QuickCaptureResult> CaptureNoteCoreAsync(string textAr, bool forReport, CancellationToken cancellationToken)
    {
        var text = Required(textAr, nameof(textAr));
        var row = new Note
        {
            Id = ids.NewId(),
            Text = text,
            ForReport = forReport,
            ReportInclude = forReport,
            SourceDeviceKind = DeviceKind.Pc,
        };
        db.Notes.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var kind = forReport ? QuickCaptureKind.ReportNote : QuickCaptureKind.Note;
        var message = forReport ? CoreAr.QuickCaptureReportNoteSaved : CoreAr.QuickCaptureNoteSaved;
        return Finish(kind, row.Id, message, new UndoEntry(UndoTarget.Note, row.Id, null));
    }

    public async Task<QuickCaptureResult> CaptureExpenseAsync(
        long amountAgorot,
        string purposeAr,
        string? categoryNameAr = null,
        string? noteAr = null,
        CancellationToken cancellationToken = default)
    {
        if (amountAgorot <= 0)
        {
            // A caller bug (the dialog validates the field itself); the user never sees this text.
            throw new ArgumentOutOfRangeException(nameof(amountAgorot), amountAgorot, "A quick-capture expense must have a positive amount.");
        }

        var purpose = Required(purposeAr, nameof(purposeAr));
        var now = clock.UtcNow;

        Guid? categoryId = null;
        if (!string.IsNullOrWhiteSpace(categoryNameAr))
        {
            var name = categoryNameAr.Trim();
            categoryId = await db.Categories.AsNoTracking()
                .Where(c => c.Kind == TransactionKind.Expense && c.Name == name)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        // The expense belongs to whichever cycle is open now (AGREEMENT items 51, 52). An
        // installation that is not configured yet has no cycle; the expense is still recorded,
        // and the cycle is attached when one exists.
        Guid? cycleId = null;
        try
        {
            var cycle = await cycles.EnsureCurrentCycleAsync(now, cancellationToken).ConfigureAwait(false);
            cycleId = cycle.Id;
        }
        catch (InvalidOperationException)
        {
            // No installation row yet; leave the transaction uncycled rather than refuse the save.
        }

        var transaction = new Transaction
        {
            Id = ids.NewId(),
            Kind = TransactionKind.Expense,
            Amount = amountAgorot,
            Purpose = purpose,
            CategoryId = categoryId,
            At = now,
            Source = DeviceKind.Pc,
            Note = string.IsNullOrWhiteSpace(noteAr) ? null : noteAr.Trim(),
            CycleId = cycleId,
        };

        // Double-entry: cash out, expense in (DATA-MODEL.md §9).
        var account = categoryNameAr is { Length: > 0 } ? $"expense:{categoryNameAr.Trim()}" : "expense:";
        var debit = new LedgerEntry { Id = ids.NewId(), TransactionId = transaction.Id, Account = account, Debit = amountAgorot, Credit = 0, At = now };
        var credit = new LedgerEntry { Id = ids.NewId(), TransactionId = transaction.Id, Account = "cash", Debit = 0, Credit = amountAgorot, At = now };

        // The transaction row must exist before its ledger lines: ledger_entries.transaction_id is
        // a foreign key, and WakeelDb deliberately configures no navigations (the referential
        // integrity lives in the SQL schema), so EF has no graph to order the two inserts by and
        // would otherwise write the lines first. Two saves, inside one database transaction, so a
        // failure between them cannot leave a transaction with no ledger lines behind. A caller
        // that already opened a transaction keeps ownership of it.
        var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            db.LedgerEntries.Add(debit);
            db.LedgerEntries.Add(credit);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        return Finish(
            QuickCaptureKind.Expense,
            transaction.Id,
            CoreAr.QuickCaptureExpenseSaved,
            new UndoEntry(UndoTarget.Transaction, transaction.Id, [debit.Id, credit.Id]));
    }

    public async Task<QuickCaptureResult> CaptureAppointmentAsync(
        string titleAr,
        DateTime startsAt,
        int? reminderMinutes = null,
        string? notesAr = null,
        CancellationToken cancellationToken = default)
    {
        var title = Required(titleAr, nameof(titleAr));
        var row = new Appointment
        {
            Id = ids.NewId(),
            Title = title,
            StartsAt = ArabicRelativeTime.ToUtc(startsAt),
            ReminderMinutes = reminderMinutes,
            Notes = string.IsNullOrWhiteSpace(notesAr) ? null : notesAr.Trim(),
            Status = AppointmentStatus.Planned,
        };
        db.Appointments.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Finish(QuickCaptureKind.Appointment, row.Id, CoreAr.QuickCaptureAppointmentSaved, new UndoEntry(UndoTarget.Appointment, row.Id, null));
    }

    public async Task<bool> UndoAsync(string undoToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(undoToken))
        {
            return false;
        }

        UndoEntry entry;
        lock (_gate)
        {
            if (!_undo.TryGetValue(undoToken, out var found))
            {
                return false;
            }

            // Single use: remove it whether or not it turns out to be expired, so a token can
            // never delete twice and an expired one cannot linger.
            _undo.Remove(undoToken);
            entry = found;
        }

        if (clock.UtcNow > entry.ExpiresAt)
        {
            return false;
        }

        var now = clock.UtcNow;
        switch (entry.Target)
        {
            case UndoTarget.Task:
                if (!await SoftDeleteAsync(db.Tasks, entry.Id, now, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                break;

            case UndoTarget.Note:
                if (!await SoftDeleteAsync(db.Notes, entry.Id, now, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                break;

            case UndoTarget.Appointment:
                if (!await SoftDeleteAsync(db.Appointments, entry.Id, now, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                break;

            default:
                if (!await SoftDeleteAsync(db.Transactions, entry.Id, now, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                foreach (var ledgerId in entry.LedgerIds ?? [])
                {
                    await SoftDeleteAsync(db.LedgerEntries, ledgerId, now, cancellationToken).ConfigureAwait(false);
                }

                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        badges.Invalidate();
        return true;
    }

    private static async Task<bool> SoftDeleteAsync<T>(DbSet<T> set, Guid id, DateTime now, CancellationToken cancellationToken)
        where T : SyncedEntity
    {
        var row = await set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        row.DeletedAt = now;
        return true;
    }

    private QuickCaptureResult Finish(QuickCaptureKind kind, Guid id, string messageAr, UndoEntry entry)
    {
        var token = Guid.NewGuid().ToString("N");
        var expires = clock.UtcNow + IQuickCaptureService.UndoWindow;
        lock (_gate)
        {
            PruneExpired();
            _undo[token] = entry with { ExpiresAt = expires };
        }

        badges.Invalidate();
        return new QuickCaptureResult(kind, id, token, messageAr, expires);
    }

    /// <summary>Drops tokens whose window has closed, so a long session does not accumulate them.</summary>
    private void PruneExpired()
    {
        var now = clock.UtcNow;
        foreach (var key in _undo.Where(p => p.Value.ExpiresAt < now).Select(p => p.Key).ToList())
        {
            _undo.Remove(key);
        }
    }

    private static string Required(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            // The dialog validates its own fields (AGREEMENT item 15 shows the Arabic message);
            // reaching Core with an empty required field is a caller bug.
            throw new ArgumentException("A quick-capture record needs its main text.", parameterName);
        }

        return trimmed;
    }

    private enum UndoTarget
    {
        Task,
        Note,
        Appointment,
        Transaction,
    }

    private readonly record struct UndoEntry(UndoTarget Target, Guid Id, IReadOnlyList<Guid>? LedgerIds, DateTime ExpiresAt = default);
}
