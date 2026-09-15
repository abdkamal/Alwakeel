namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §3 — correspondence, the document vault index, OCR pages, referrals,
// follow-ups, corrections, duplicate review, templates and the inter-org exchange log.

/// <summary>An incoming or outgoing correspondence item.</summary>
public sealed class Correspondence : SyncedEntity
{
    public InOutDirection Direction { get; set; }

    /// <summary>Official number, issued only on approval; <c>null</c> for a draft.</summary>
    public string? OfficialNumber { get; set; }

    public DateTime? NumberIssuedAt { get; set; }

    public string? ExternalNumber { get; set; }

    public DateTime? ExternalDate { get; set; }

    public string Subject { get; set; } = string.Empty;

    /// <summary>Free-text correspondence type (no fixed value list in DATA-MODEL.md).</summary>
    public string? Type { get; set; }

    public Confidentiality Confidentiality { get; set; }

    /// <summary>"For the recipient only": excluded from sync packages except to the recipient device.</summary>
    public bool RecipientOnly { get; set; }

    public CounterpartyKind CounterpartyKind { get; set; }

    /// <summary>External party id, when <see cref="CounterpartyKind"/> is External.</summary>
    public Guid? PartyId { get; set; }

    /// <summary>Internal org unit id, when <see cref="CounterpartyKind"/> is Internal.</summary>
    public Guid? UnitId { get; set; }

    public string? PartyNameSnapshot { get; set; }

    /// <summary>JSON array of CC recipients.</summary>
    public string? Cc { get; set; }

    public CorrespondenceStatus Status { get; set; }

    public string? NextStepAr { get; set; }

    public DateTime? DueAt { get; set; }

    public Guid? LinkedCorrespondenceId { get; set; }

    public Guid? CaseId { get; set; }

    public Guid? MeetingId { get; set; }

    public Guid? TemplateId { get; set; }

    public string? BodyText { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? CancelReason { get; set; }

    public string? CloseNote { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>Links a document to a correspondence item (original, derived print, or attachment).</summary>
public sealed class CorrespondenceDocument : SyncedEntity
{
    public Guid CorrespondenceId { get; set; }

    public Guid DocumentId { get; set; }

    public CorrespondenceDocumentKind Kind { get; set; }

    public int Sort { get; set; }
}

/// <summary>Vault document index; the original bytes live in <c>vault\&lt;sha256&gt;.bin</c>.</summary>
public sealed class Document : SyncedEntity
{
    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Mime { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public DocumentSource Source { get; set; }

    public int PageCount { get; set; }

    public OcrStatus OcrStatus { get; set; }

    public string? OcrLang { get; set; }

    public bool PinnedOnPhone { get; set; }

    public Guid? DerivedFromId { get; set; }
}

/// <summary>
/// OCR result for one page of a document. An official synced table (DATA-MODEL.md §3, decision
/// of 2026-09-16): OCR text is produced on the phone or on a single PC and must reach the other
/// devices without re-running OCR, so the row carries the full §0 column set and is written to
/// <c>change_log</c> by its own triggers. Unique on (<see cref="DocumentId"/>, <see cref="PageNo"/>).
/// </summary>
public sealed class DocumentPage : SyncedEntity
{
    public Guid DocumentId { get; set; }

    public int PageNo { get; set; }

    public string? Text { get; set; }

    /// <summary>JSON array of word bounding boxes.</summary>
    public string? Words { get; set; }

    public double? Confidence { get; set; }
}

/// <summary>
/// Links a document to any entity that references it. An official synced table (DATA-MODEL.md
/// §3, decision of 2026-09-16) so a document's links travel with it to the other devices.
/// Unique on (<see cref="DocumentId"/>, <see cref="EntityType"/>, <see cref="EntityId"/>).
/// </summary>
public sealed class DocumentLink : SyncedEntity
{
    public Guid DocumentId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }
}

/// <summary>A referral of a correspondence item to another unit (AGREEMENT item 31).</summary>
public sealed class Referral : SyncedEntity
{
    public Guid CorrespondenceId { get; set; }

    public Guid? ToUnitId { get; set; }

    public string? ToName { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? DueAt { get; set; }

    public ReferralStatus Status { get; set; }

    public Guid? DerivedDocumentId { get; set; }

    public bool ExtraPageAdded { get; set; }
}

/// <summary>One entry on a correspondence item's follow-up timeline.</summary>
public sealed class Followup : SyncedEntity
{
    public Guid CorrespondenceId { get; set; }

    public FollowupKind Kind { get; set; }

    public string? Note { get; set; }

    public DateTime? NextAt { get; set; }

    public DateTime? ReminderAt { get; set; }

    public CorrespondenceStatus? StatusFrom { get; set; }

    public CorrespondenceStatus? StatusTo { get; set; }
}

/// <summary>A post-numbering correction to a correspondence item (AGREEMENT item 19).</summary>
public sealed class Correction : SyncedEntity
{
    public Guid CorrespondenceId { get; set; }

    /// <summary>JSON array of {field, old, new}.</summary>
    public string Changes { get; set; } = "[]";

    public string Reason { get; set; } = string.Empty;

    public DateTime At { get; set; }
}

/// <summary>A possible-duplicate review between two correspondence items (AGREEMENT item 14).</summary>
public sealed class DuplicateReview : SyncedEntity
{
    public Guid CorrespondenceId { get; set; }

    public Guid SimilarId { get; set; }

    public double Score { get; set; }

    public DuplicateVerdict Verdict { get; set; }
}

/// <summary>A letter or report document template.</summary>
public sealed class Template : SyncedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Document id of the underlying .docx.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>JSON object describing fillable fields.</summary>
    public string? Fields { get; set; }

    public bool IsDefault { get; set; }

    public TemplateKind Kind { get; set; }
}

/// <summary>A record of one inter-office/inter-org exchange package sent or received (AGREEMENT items 22, 49).</summary>
public sealed class ExchangeLogEntry : SyncedEntity
{
    public InOutDirection Direction { get; set; }

    public ExchangeKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? OtherOrg { get; set; }

    public string? OtherOffice { get; set; }

    public Guid? EntityId { get; set; }

    public bool SignatureOk { get; set; }

    public DateTime At { get; set; }
}
