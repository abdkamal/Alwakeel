namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §10 — the comprehensive monthly report, its correction addenda, and ad-hoc
// "other reports" (AGREEMENT item 53).

/// <summary>The single comprehensive report for one financial cycle; issuing it closes the cycle.</summary>
public sealed class MonthlyReport : SyncedEntity
{
    public Guid CycleId { get; set; }

    public ReportStatus Status { get; set; }

    /// <summary>JSON: section order, hidden sections, generated text, edited text.</summary>
    public string? Sections { get; set; }

    /// <summary>JSON: readiness percentage and the list of gaps.</summary>
    public string? Readiness { get; set; }

    public string? DirectorWord { get; set; }

    public DateTime? IssuedAt { get; set; }

    public Guid? DocxDocumentId { get; set; }

    public Guid? PdfDocumentId { get; set; }

    public string? Sha256 { get; set; }

    public Guid? OutgoingCorrespondenceId { get; set; }
}

/// <summary>A numbered correction addendum to an already-issued monthly report.</summary>
public sealed class ReportAddendum : SyncedEntity
{
    public Guid ReportId { get; set; }

    public int Number { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; }

    public Guid? DocumentId { get; set; }
}

/// <summary>An ad-hoc generated report (register, payroll, custody, late tasks, meetings — W74).</summary>
public sealed class OtherReport : SyncedEntity
{
    public OtherReportKind Kind { get; set; }

    /// <summary>JSON-encoded filter criteria used to generate this report.</summary>
    public string? Filters { get; set; }

    public DateTime GeneratedAt { get; set; }

    public Guid? DocumentId { get; set; }
}
