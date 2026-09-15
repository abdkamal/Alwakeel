namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §9 — financial cycles, categories, transactions, the internal double-entry
// ledger, phone-captured expenses awaiting confirmation, and cash counts. Money columns are
// long agorot (1 ₪ = 100). Free before a cycle's monthly report is issued (AGREEMENT item 51).

/// <summary>A financial cycle (AGREEMENT item 52): from the configured start day through the day before the next start day.</summary>
public sealed class FinancialCycle : SyncedEntity
{
    /// <summary>Arabic name, "دورة &lt;شهر&gt; &lt;سنة&gt;", named by the END month.</summary>
    public string NameAr { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public FinancialCycleStatus Status { get; set; }

    public DateTime? IssuedAt { get; set; }

    public Guid? ReportId { get; set; }
}

/// <summary>An expense or income category.</summary>
public sealed class Category : SyncedEntity
{
    public TransactionKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Sort { get; set; }
}

/// <summary>A financial transaction (income or expense).</summary>
public sealed class Transaction : SyncedEntity
{
    public TransactionKind Kind { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public Guid? CategoryId { get; set; }

    public DateTime At { get; set; }

    public DeviceKind Source { get; set; }

    public Guid? ReceiptDocumentId { get; set; }

    public string? Note { get; set; }

    public Guid? CycleId { get; set; }

    public Guid? PhoneExpenseId { get; set; }

    /// <summary>When this is a correction entry, the original transaction it corrects (AGREEMENT item 51).</summary>
    public Guid? CorrectionOfId { get; set; }

    /// <summary>When this is a correction entry, the original transaction's date.</summary>
    public DateTime? OriginalAt { get; set; }
}

/// <summary>One line of the internal double-entry ledger for a transaction.</summary>
public sealed class LedgerEntry : SyncedEntity
{
    public Guid TransactionId { get; set; }

    /// <summary>"cash", "expense:&lt;category&gt;", "income:&lt;category&gt;", or "adjustment".</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Debit { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Credit { get; set; }

    public DateTime At { get; set; }
}

/// <summary>An expense captured on the phone, awaiting confirmation on الوكيل (AGREEMENT item 50).</summary>
public sealed class PhoneExpense : SyncedEntity
{
    public Guid PhoneDeviceId { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public DateTime At { get; set; }

    public Guid? ReceiptDocumentId { get; set; }

    public string? Note { get; set; }

    public PhoneExpenseStatus Status { get; set; }

    public string? RejectReason { get; set; }

    public Guid? TransactionId { get; set; }

    public DateTime? DecidedAt { get; set; }
}

/// <summary>A physical cash count and its reconciliation against the book balance.</summary>
public sealed class CashCount : SyncedEntity
{
    public DateTime At { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long BookBalance { get; set; }

    /// <summary>JSON-encoded denomination counts.</summary>
    public string Counted { get; set; } = "{}";

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long CountedTotal { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Difference { get; set; }

    public string? Note { get; set; }

    public Guid? CycleId { get; set; }
}
