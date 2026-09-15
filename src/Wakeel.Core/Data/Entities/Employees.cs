namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §7 — employees and payroll. Money columns are long agorot (1 ₪ = 100).

/// <summary>An employee of this office's scope. Termination is blocked until all custody is settled.</summary>
public sealed class Employee : SyncedEntity
{
    public string Name { get; set; } = string.Empty;

    public string EmployeeNumber { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }

    public string? JobTitle { get; set; }

    public EmployeeStatus Status { get; set; }

    public DateTime? HiredAt { get; set; }

    public DateTime? TerminatedAt { get; set; }

    public string? Phone { get; set; }

    public Guid? PhotoDocumentId { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A salary component (basic/allowance/deduction/bonus rule), editable at any time.</summary>
public sealed class SalaryComponent : SyncedEntity
{
    public Guid EmployeeId { get; set; }

    public SalaryComponentKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }
}

/// <summary>A monthly payroll run for a unit.</summary>
public sealed class PayrollRun : SyncedEntity
{
    /// <summary>Period in "YYYY-MM" form.</summary>
    public string Period { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }

    public PayrollRunStatus Status { get; set; }

    public DateTime? CommittedAt { get; set; }

    /// <summary>JSON-encoded totals.</summary>
    public string? Totals { get; set; }
}

/// <summary>One employee's line within a payroll run.</summary>
public sealed class PayrollLine : SyncedEntity
{
    public Guid RunId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Basic { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Allowances { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Deductions { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Bonuses { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Net { get; set; }

    /// <summary>JSON-encoded per-line manual overrides.</summary>
    public string? Overrides { get; set; }
}

/// <summary>A one-off bonus granted to an employee.</summary>
public sealed class Bonus : SyncedEntity
{
    public Guid EmployeeId { get; set; }

    public string Type { get; set; } = string.Empty;

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public string? Reason { get; set; }

    public DateTime GrantedAt { get; set; }

    public Guid? RunId { get; set; }
}

/// <summary>An imported, signed payroll file from a subordinate unit (AGREEMENT item 48).</summary>
public sealed class PayrollImport : SyncedEntity
{
    public string FileName { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }

    /// <summary>Period in "YYYY-MM" form.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>JSON-encoded totals.</summary>
    public string? Totals { get; set; }

    public bool SignatureOk { get; set; }

    public string? SignerDevice { get; set; }

    public DateTime ImportedAt { get; set; }

    public Guid? RunId { get; set; }
}
