namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §1 — installation identity, directory, devices, account, settings, numbering,
// audit log, notifications, clock checks and health snapshots. None of these tables are synced
// (they are local, or — for installation/account/org_units/devices — sourced read-only from a
// .wakeel-setup / pairing file), so none inherit SyncedEntity.

/// <summary>Single-row installation identity, filled from the consumed <c>.wakeel-setup</c> file. Read-only in الوكيل.</summary>
public sealed class Installation
{
    /// <summary>Fixed literal key so EF has a primary key for this single-row table; not a DATA-MODEL column.</summary>
    public string Id { get; set; } = SingletonId;

    public const string SingletonId = "singleton";

    public Guid OrgId { get; set; }

    public string OrgName { get; set; } = string.Empty;

    public Guid? LogoDocumentId { get; set; }

    public Guid OfficeId { get; set; }

    public string OfficeName { get; set; } = string.Empty;

    public Guid OfficeUnitId { get; set; }

    public string OfficeCode { get; set; } = string.Empty;

    public Guid DeviceId { get; set; }

    public int DeviceNo { get; set; }

    public int EmployeeNo { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public InstallationRole Role { get; set; }

    public SyncScope SyncScope { get; set; }

    public int CycleStartDay { get; set; }

    public string NumberingFormat { get; set; } = string.Empty;

    public string SetupVersion { get; set; } = string.Empty;

    public DateTime ActivatedAt { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    public DateTime BuildDate { get; set; }

    public byte[] OrgX25519Pub { get; set; } = [];

    public byte[] OrgEd25519Pub { get; set; } = [];
}

/// <summary>One node of the four-layer org structure (org ← department ← section ← unit), from the setup file.</summary>
public sealed class OrgUnit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? ParentId { get; set; }

    public OrgUnitLevel Level { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? HeadName { get; set; }

    public string? HeadTitle { get; set; }

    public string? OfficeCode { get; set; }

    public int SortOrder { get; set; }

    public byte[]? X25519Pub { get; set; }
}

/// <summary>A known office or org device, or a paired phone.</summary>
public sealed class Device
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? UnitId { get; set; }

    public int DeviceNo { get; set; }

    public int EmployeeNo { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public InstallationRole Role { get; set; }

    public DeviceKind Kind { get; set; }

    public byte[] Ed25519Pub { get; set; } = [];

    public byte[] X25519Pub { get; set; } = [];

    public byte[] Certificate { get; set; } = [];

    public DateTime IssuedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime? PairedAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public SyncScope SyncScope { get; set; }
}

/// <summary>Single-row operating account (one account per installation, no permission system).</summary>
public sealed class Account
{
    /// <summary>Fixed literal key so EF has a primary key for this single-row table; not a DATA-MODEL column.</summary>
    public string Id { get; set; } = SingletonId;

    public const string SingletonId = "singleton";

    public Guid EmployeeId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public Guid? PhotoDocumentId { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public int AutoLockMinutes { get; set; } = 10;
}

/// <summary>A typed setting value, stored as JSON. Local table, not synced.</summary>
public sealed class Setting
{
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON-encoded value.</summary>
    public string Value { get; set; } = "null";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Per-kind, per-year official numbering sequence for this device. Local table, not synced.</summary>
public sealed class OfficialNumber
{
    public InOutDirection Kind { get; set; }

    public int Year { get; set; }

    public int LastSeq { get; set; }

    public DateTime LastDate { get; set; }
}

/// <summary>
/// An audit trail entry for a sensitive operation. Local and append-only (DATA-MODEL.md §1,
/// decision of 2026-09-16): it carries none of the shared synced columns and has no change_log
/// trigger, and it is read-only once written. B6 exports it by its own <see cref="At"/> range;
/// on import an entry is inserted when its id is absent and is never updated, so audit entries
/// never conflict and are never rewritten.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime At { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string SummaryAr { get; set; } = string.Empty;

    /// <summary>JSON-encoded details, never containing secrets.</summary>
    public string? Details { get; set; }
}

/// <summary>A bell / attention-center notification.</summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DueAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime? DismissedAt { get; set; }

    public string? Source { get; set; }
}

/// <summary>One clock-sanity check result (ARCHITECTURE.md §9 / AGREEMENT item 20).</summary>
public sealed class ClockCheck
{
    /// <summary>Synthetic auto-increment id; DATA-MODEL lists no natural key for this history log.</summary>
    public long Id { get; set; }

    public DateTime At { get; set; }

    public ClockVerdict Verdict { get; set; }

    public string? Details { get; set; }
}

/// <summary>One health-center component status snapshot (W12).</summary>
public sealed class HealthSnapshot
{
    /// <summary>Synthetic auto-increment id; DATA-MODEL lists no natural key for this history log.</summary>
    public long Id { get; set; }

    public string Component { get; set; } = string.Empty;

    public HealthStatus Status { get; set; }

    public string? MessageAr { get; set; }

    public string? Action { get; set; }

    public DateTime CheckedAt { get; set; }
}
