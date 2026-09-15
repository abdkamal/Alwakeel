namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §11 — sync, backup and phone bookkeeping. These tables describe the sync
// process itself; they are local to the device and are never themselves synced.

/// <summary>
/// One row change recorded by an AFTER INSERT/UPDATE trigger on a synced table. There is no
/// delete operation: an official row is never physically deleted, so hiding one is recorded here
/// as an update (DATA-MODEL.md §0).
/// </summary>
public sealed class ChangeLogEntry
{
    /// <summary>Auto-increment sequence, the primary key.</summary>
    public long Seq { get; set; }

    public string TableName { get; set; } = string.Empty;

    public Guid RowId { get; set; }

    public ChangeOperation Op { get; set; }

    public DateTime At { get; set; }

    /// <summary>
    /// The device that performed the operation locally (<c>installation.device_id</c>), NOT the
    /// row's own <c>origin_device</c> — confirmed in DATA-MODEL.md §11 on 2026-09-16 — so that an
    /// export "from date to date" selects exactly what changed on this device.
    /// </summary>
    public string Device { get; set; } = string.Empty;
}

/// <summary>An export or import of a date-range sync/phone/exchange package.</summary>
public sealed class SyncPackage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public ExportImportDirection Direction { get; set; }

    public SyncPackageKind Kind { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public Guid? TargetDeviceId { get; set; }

    public Guid? SourceDeviceId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>JSON-encoded per-table row counts.</summary>
    public string? Counts { get; set; }

    public SyncPackageStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AppliedAt { get; set; }
}

/// <summary>A detected sync conflict, holding both versions; resolution is always manual, never automatic.</summary>
public sealed class SyncConflict
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid PackageId { get; set; }

    public string TableName { get; set; } = string.Empty;

    public Guid RowId { get; set; }

    /// <summary>JSON snapshot of the local row.</summary>
    public string Ours { get; set; } = "{}";

    /// <summary>JSON snapshot of the imported row.</summary>
    public string Theirs { get; set; } = "{}";

    public ConflictResolution? Resolution { get; set; }

    public DateTime? ResolvedAt { get; set; }
}

/// <summary>One outbox/inbox entry of the phone USB transfer queue.</summary>
public sealed class PhoneQueueEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public PhoneDirection Direction { get; set; }

    public int Seq { get; set; }

    public string FileName { get; set; } = string.Empty;

    public PhoneQueueStatus Status { get; set; }

    public DateTime At { get; set; }

    /// <summary>JSON-encoded package contents summary.</summary>
    public string? Items { get; set; }
}

/// <summary>A phone pairing session (QR + one-time short code).</summary>
public sealed class PairingSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string TokenHash { get; set; } = string.Empty;

    public string ShortCodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public PairingStatus Status { get; set; }

    public Guid? PhoneDeviceId { get; set; }
}

/// <summary>A document pinned for the phone (kept available offline).</summary>
public sealed class PinnedFile
{
    public Guid DocumentId { get; set; }

    public DateTime PinnedAt { get; set; }

    public DateTime? SentAt { get; set; }
}

/// <summary>One backup produced by الوكيل.</summary>
public sealed class Backup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime At { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public bool IncludesVault { get; set; }

    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>One restore-from-backup operation and its numbering sequence-integrity check (AGREEMENT item 37).</summary>
public sealed class RestoreLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime At { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public DateTime BackupAt { get; set; }

    public SequenceCheck SequenceCheck { get; set; }

    public string? Details { get; set; }
}
