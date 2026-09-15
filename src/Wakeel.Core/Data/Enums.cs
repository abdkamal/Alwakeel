namespace Wakeel.Core.Data;

/// <summary>Role of the operating account on this installation.</summary>
public enum InstallationRole
{
    Director,
    Secretary,
    Custodian,
}

/// <summary>Sync scope granted to a device or account.</summary>
public enum SyncScope
{
    Full,
    Custody,
}

/// <summary>Level within the four fixed organizational layers.</summary>
public enum OrgUnitLevel
{
    Org,
    Department,
    Section,
    Unit,
}

/// <summary>Kind of a device known to the installation.</summary>
public enum DeviceKind
{
    Pc,
    Phone,
}

/// <summary>Kind of an external party in the directory.</summary>
public enum PartyKind
{
    Ministry,
    Municipality,
    Authority,
    Company,
    Person,
    Other,
}

/// <summary>Whether a correspondence counterparty is external or an internal org unit.</summary>
public enum CounterpartyKind
{
    External,
    Internal,
}

/// <summary>Generic incoming/outgoing direction used by numbering, correspondence, exchange and transfers.</summary>
public enum InOutDirection
{
    In,
    Out,
}

/// <summary>Confidentiality level of a correspondence item.</summary>
public enum Confidentiality
{
    Public,
    Private,
    Secret,
    TopSecret,
}

/// <summary>Lifecycle status of a correspondence item.</summary>
public enum CorrespondenceStatus
{
    Draft,
    New,
    InProgress,
    AwaitingReply,
    Done,
    Closed,
    Cancelled,
    Archived,
}

/// <summary>Role a linked document plays for a correspondence item.</summary>
public enum CorrespondenceDocumentKind
{
    Original,
    DerivedPrint,
    Attachment,
}

/// <summary>Where a stored document came from.</summary>
public enum DocumentSource
{
    Scan,
    Import,
    Phone,
    Generated,
    Backup,
}

/// <summary>OCR processing status of a stored document.</summary>
public enum OcrStatus
{
    Pending,
    Running,
    Done,
    Unsupported,
    Failed,
}

/// <summary>Status of a referral.</summary>
public enum ReferralStatus
{
    Open,
    Answered,
    Overdue,
    Closed,
}

/// <summary>Kind of a follow-up timeline entry.</summary>
public enum FollowupKind
{
    Call,
    Visit,
    Reply,
    Note,
    Status,
}

/// <summary>Verdict of a duplicate-correspondence review.</summary>
public enum DuplicateVerdict
{
    Pending,
    NotDuplicate,
    Duplicate,
}

/// <summary>Kind of a document template.</summary>
public enum TemplateKind
{
    Letter,
    Report,
}

/// <summary>Kind of an inter-office/inter-org exchange package.</summary>
public enum ExchangeKind
{
    Msg,
    Transfer,
    Inventory,
    Payroll,
}

/// <summary>Priority of a task.</summary>
public enum TaskPriority
{
    Low,
    Normal,
    High,
}

/// <summary>Lifecycle status of a task.</summary>
public enum WorkTaskStatus
{
    Open,
    InProgress,
    Done,
    Postponed,
    Transferred,
}

/// <summary>Where a decision originated from.</summary>
public enum DecisionSourceType
{
    Meeting,
    Correspondence,
    Other,
}

/// <summary>Execution status of a decision.</summary>
public enum DecisionStatus
{
    NotStarted,
    InProgress,
    Done,
}

/// <summary>Payment status of a commitment.</summary>
public enum CommitmentStatus
{
    Open,
    Partial,
    Paid,
    Overdue,
}

/// <summary>Status of an obstacle.</summary>
public enum ObstacleStatus
{
    Open,
    Resolved,
}

/// <summary>Status of a scheduled meeting.</summary>
public enum MeetingStatus
{
    Planned,
    Held,
    Cancelled,
}

/// <summary>Kind of a meeting attendee reference.</summary>
public enum AttendeeKind
{
    Employee,
    Party,
    Unit,
}

/// <summary>Role of a meeting attendee.</summary>
public enum AttendeeRole
{
    Organizer,
    Attendee,
}

/// <summary>Status of a calendar appointment.</summary>
public enum AppointmentStatus
{
    Planned,
    Done,
    Cancelled,
}

/// <summary>Status of a legal case.</summary>
public enum CaseStatus
{
    Open,
    Pending,
    Closed,
}

/// <summary>Kind of a case event.</summary>
public enum CaseEventKind
{
    Hearing,
    Filing,
    Decision,
    Note,
}

/// <summary>Employment status of an employee.</summary>
public enum EmployeeStatus
{
    Active,
    Suspended,
    Terminated,
}

/// <summary>Kind of a salary component.</summary>
public enum SalaryComponentKind
{
    Basic,
    Allowance,
    Deduction,
    BonusRule,
}

/// <summary>Status of a payroll run.</summary>
public enum PayrollRunStatus
{
    Draft,
    Committed,
}

/// <summary>Status of an asset.</summary>
public enum AssetStatus
{
    InService,
    InTransfer,
    Lost,
    Damaged,
    Retired,
}

/// <summary>Kind of a custody movement entry.</summary>
public enum CustodyMovementKind
{
    Handover,
    Receive,
    Confirm,
    Return,
    TransferOut,
    TransferIn,
}

/// <summary>Status of an inter-office asset transfer.</summary>
public enum AssetTransferStatus
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>Result of checking one asset during an inventory session.</summary>
public enum InventoryResult
{
    Present,
    Missing,
    Damaged,
}

/// <summary>How an inventory item was checked.</summary>
public enum InventoryVia
{
    Pc,
    PhoneQr,
}

/// <summary>Status of a financial cycle.</summary>
public enum FinancialCycleStatus
{
    Open,
    AwaitingIssue,
    Issued,
}

/// <summary>Kind of a financial transaction.</summary>
public enum TransactionKind
{
    Expense,
    Income,
}

/// <summary>Status of a phone-captured expense awaiting confirmation.</summary>
public enum PhoneExpenseStatus
{
    Pending,
    Confirmed,
    Rejected,
}

/// <summary>Status of a monthly report.</summary>
public enum ReportStatus
{
    Draft,
    Issued,
}

/// <summary>Kind of an ad-hoc "other report".</summary>
public enum OtherReportKind
{
    Register,
    Payroll,
    Custody,
    LateTasks,
    Meetings,
}

/// <summary>Direction of a sync package.</summary>
public enum ExportImportDirection
{
    Export,
    Import,
}

/// <summary>Kind of a sync package.</summary>
public enum SyncPackageKind
{
    Sync,
    Phone,
    Msg,
    Transfer,
    Inventory,
}

/// <summary>Status of a sync package through the verify/preview/apply pipeline.</summary>
public enum SyncPackageStatus
{
    Created,
    Verified,
    Previewed,
    Applied,
    Rejected,
}

/// <summary>Chosen resolution of a sync conflict.</summary>
public enum ConflictResolution
{
    Ours,
    Theirs,
    Merged,
}

/// <summary>Direction of a phone USB queue entry.</summary>
public enum PhoneDirection
{
    ToPhone,
    FromPhone,
}

/// <summary>Status of a phone USB queue entry.</summary>
public enum PhoneQueueStatus
{
    Pending,
    Written,
    Applied,
    Failed,
}

/// <summary>Status of a phone pairing session.</summary>
public enum PairingStatus
{
    Waiting,
    Paired,
    Expired,
    Used,
}

/// <summary>Sequence-integrity check outcome after a restore.</summary>
public enum SequenceCheck
{
    Ok,
    NeedsReview,
}

/// <summary>Kind of a model dropped into the models folder.</summary>
public enum ModelKind
{
    Embedding,
    Ocr,
}

/// <summary>Suitability status of a discovered model.</summary>
public enum ModelStatus
{
    Ok,
    Unsuitable,
    Preparing,
    Active,
}

/// <summary>Verdict of a clock-sanity check.</summary>
public enum ClockVerdict
{
    Ok,
    Suspect,
    Bad,
}

/// <summary>Status of a health-center component snapshot.</summary>
public enum HealthStatus
{
    Ok,
    Warning,
    Error,
}

/// <summary>Row operation recorded by a change_log trigger.</summary>
public enum ChangeOperation
{
    Insert,
    Update,
}
