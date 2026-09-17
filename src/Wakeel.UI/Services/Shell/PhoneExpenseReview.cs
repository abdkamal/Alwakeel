using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.UI.Services.Shell;

/// <summary>One phone-captured expense waiting for the office to confirm it (AGREEMENT item 50).</summary>
/// <param name="Id">Row id.</param>
/// <param name="EmployeeAr">Name of the employee whose phone sent it; null when the device carries none.</param>
/// <param name="PurposeAr">What the money was spent on.</param>
/// <param name="NoteAr">The employee's own note, when there is one.</param>
/// <param name="At">When the expense was made, UTC.</param>
/// <param name="AmountAr">«₪ 42.50», already formatted; a Latin-digit run, so the screen isolates it.</param>
public sealed record PendingPhoneExpense(
    Guid Id,
    string? EmployeeAr,
    string PurposeAr,
    string? NoteAr,
    DateTime At,
    string AmountAr);

/// <summary>Confirming or rejecting a phone expense from the attention center (AGREEMENT item 50).</summary>
/// <remarks>
/// The rows themselves reach W08 through <see cref="IAttentionService"/>'s
/// «بانتظار تأكيدي» bucket; this is only the decision, which the attention center does not own.
/// It lives in Wakeel.UI rather than Wakeel.Core because no B-package has claimed the finance
/// screens yet (B5 does) — when it does, the booking below is the piece to move, unchanged, into
/// the finance service and delete from here.
/// </remarks>
public interface IPhoneExpenseReview
{
    /// <summary>The pending expenses, oldest first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<PendingPhoneExpense>> GetPendingAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>«تأكيد»: books the expense into the open cycle and marks it confirmed. False when it is already decided.</summary>
    Task<bool> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>«رفض»: marks the expense rejected, with an optional reason. False when it is already decided.</summary>
    Task<bool> RejectAsync(Guid id, string? reasonAr = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPhoneExpenseReview"/>
public sealed class PhoneExpenseReview(
    WakeelDb db,
    IIdGenerator ids,
    IClock clock,
    IFinancialCycleService cycles) : IPhoneExpenseReview
{
    public async Task<IReadOnlyList<PendingPhoneExpense>> GetPendingAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await db.PhoneExpenses.AsNoTracking()
            .Where(e => e.Status == PhoneExpenseStatus.Pending)
            .OrderBy(e => e.At)
            .Take(limit)
            .Select(e => new { e.Id, e.PhoneDeviceId, e.Purpose, e.Note, e.At, e.Amount })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        // One extra query for the employee names, the same shape AttentionService uses: the
        // device row carries the name, and W08's «الموظف» column would otherwise be blank.
        var deviceIds = rows.Select(r => r.PhoneDeviceId).Distinct().ToList();
        var names = await db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.EmployeeName })
            .ToDictionaryAsync(d => d.Id, d => d.EmployeeName, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(r => new PendingPhoneExpense(
            r.Id,
            names.TryGetValue(r.PhoneDeviceId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null,
            r.Purpose,
            string.IsNullOrWhiteSpace(r.Note) ? null : r.Note,
            r.At,
            Money.Shekels(r.Amount)))];
    }

    public async Task<bool> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await db.PhoneExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (expense is null || expense.Status != PhoneExpenseStatus.Pending)
        {
            return false;
        }

        var now = clock.UtcNow;

        // The expense belongs to whichever cycle is open now (AGREEMENT items 51, 52). An
        // installation that has no cycle yet still gets the booking; the cycle is attached later.
        Guid? cycleId = null;
        try
        {
            var cycle = await cycles.EnsureCurrentCycleAsync(now, cancellationToken).ConfigureAwait(false);
            cycleId = cycle.Id;
        }
        catch (InvalidOperationException)
        {
            // No installation row yet; book it uncycled rather than refuse the confirmation.
        }

        var transaction = new Transaction
        {
            Id = ids.NewId(),
            Kind = TransactionKind.Expense,
            Amount = expense.Amount,
            Purpose = expense.Purpose,
            At = expense.At,
            Source = DeviceKind.Phone,
            Note = expense.Note,
            CycleId = cycleId,
        };

        var account = string.IsNullOrWhiteSpace(expense.CategoryName) ? "expense:" : $"expense:{expense.CategoryName.Trim()}";
        var debit = new LedgerEntry { Id = ids.NewId(), TransactionId = transaction.Id, Account = account, Debit = expense.Amount, Credit = 0, At = expense.At };
        var credit = new LedgerEntry { Id = ids.NewId(), TransactionId = transaction.Id, Account = "cash", Debit = 0, Credit = expense.Amount, At = expense.At };

        // Three saves inside one database transaction, in the order the foreign keys demand: the
        // transaction row, then its ledger lines (ledger_entries.transaction_id), then the expense
        // that points at it (phone_expenses.transaction_id). WakeelDb configures no navigations
        // between these tables, so nothing tells the change tracker which write has to go first —
        // stamping the expense before the save puts its UPDATE in the same batch as the INSERT it
        // depends on, and SQLite then refuses the whole batch on the foreign key. A caller that
        // already opened a transaction keeps ownership of it.
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

            expense.Status = PhoneExpenseStatus.Confirmed;
            expense.DecidedAt = now;
            expense.TransactionId = transaction.Id;
            expense.RejectReason = null;
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

        return true;
    }

    public async Task<bool> RejectAsync(Guid id, string? reasonAr = null, CancellationToken cancellationToken = default)
    {
        var expense = await db.PhoneExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (expense is null || expense.Status != PhoneExpenseStatus.Pending)
        {
            return false;
        }

        expense.Status = PhoneExpenseStatus.Rejected;
        expense.DecidedAt = clock.UtcNow;
        expense.RejectReason = string.IsNullOrWhiteSpace(reasonAr) ? null : reasonAr.Trim();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
