namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §4 — tasks, decisions, commitments and their payments, obstacles, needs and notes.
// Every table here participates in the monthly report per AGREEMENT item 53(a): each row carries
// ReportInclude / ReportHighlight / ReportComment.

/// <summary>A follow-up task. Named TaskItem (not Task) to avoid clashing with System.Threading.Tasks.Task.</summary>
public sealed class TaskItem : SyncedEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? DueAt { get; set; }

    public TaskPriority Priority { get; set; }

    public WorkTaskStatus Status { get; set; }

    public int Progress { get; set; }

    public string? AssigneeName { get; set; }

    public string? SourceType { get; set; }

    public Guid? SourceId { get; set; }

    public DateTime? ReminderAt { get; set; }

    public string? PostponeReason { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }

    /// <summary>Which kind of device the task was captured on (pc/phone quick capture).</summary>
    public DeviceKind? SourceDeviceKind { get; set; }
}

/// <summary>A decision, from a meeting, a correspondence item, or entered directly.</summary>
public sealed class Decision : SyncedEntity
{
    public string Text { get; set; } = string.Empty;

    public DecisionSourceType SourceType { get; set; }

    public Guid? SourceId { get; set; }

    public DateTime DecidedAt { get; set; }

    public string? OwnerName { get; set; }

    public DecisionStatus Status { get; set; }

    public int ExecutionPct { get; set; }

    public DateTime? DueAt { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>A financial commitment owed to a party; the commitment and its payments are kept separate.</summary>
public sealed class Commitment : SyncedEntity
{
    public string Title { get; set; } = string.Empty;

    public Guid? PartyId { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public string? RequiredText { get; set; }

    public DateTime? DueAt { get; set; }

    public CommitmentStatus Status { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>One payment made against a commitment.</summary>
public sealed class CommitmentPayment : SyncedEntity
{
    public Guid CommitmentId { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long Amount { get; set; }

    public DateTime PaidAt { get; set; }

    public string? Note { get; set; }

    public Guid? TransactionId { get; set; }
}

/// <summary>An obstacle blocking progress, optionally escalated to a parent unit.</summary>
public sealed class Obstacle : SyncedEntity
{
    public string Description { get; set; } = string.Empty;

    public string? Impact { get; set; }

    public string? RequiredFromParent { get; set; }

    public ObstacleStatus Status { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>A stated need or resource request, feeding "needs and recommendations for next cycle" in the monthly report.</summary>
public sealed class Need : SyncedEntity
{
    public string Item { get; set; } = string.Empty;

    public string? Justification { get; set; }

    /// <summary>Free-text priority (no fixed value list in DATA-MODEL.md).</summary>
    public string? Priority { get; set; }

    /// <summary>Free-text status (no fixed value list in DATA-MODEL.md).</summary>
    public string? Status { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>A free-text or voice note, optionally attached to any entity and/or flagged for the monthly report.</summary>
public sealed class Note : SyncedEntity
{
    public string Text { get; set; } = string.Empty;

    public Guid? VoiceDocumentId { get; set; }

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>Marked, via quick capture, as a note intended for the monthly report.</summary>
    public bool ForReport { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }

    public DeviceKind? SourceDeviceKind { get; set; }
}
