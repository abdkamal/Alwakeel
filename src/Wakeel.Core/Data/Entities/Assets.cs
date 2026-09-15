namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §8 — assets and custody. An asset keeps its identity across office transfers
// (AGREEMENT item 45), so its inventory number carries the originating office code.

/// <summary>A tracked asset.</summary>
public sealed class Asset : SyncedEntity
{
    /// <summary>"&lt;office_code&gt;-&lt;seq&gt;", unique org-wide.</summary>
    public string InventoryNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Free-text category (no fixed value list in DATA-MODEL.md).</summary>
    public string? Category { get; set; }

    public AssetStatus Status { get; set; }

    public string? Location { get; set; }

    public Guid? CustodianEmployeeId { get; set; }

    public DateTime? AcquiredAt { get; set; }

    /// <summary>Agorot (1 ₪ = 100).</summary>
    public long? Value { get; set; }

    public string? Notes { get; set; }

    public string? QrToken { get; set; }

    public string? OriginOfficeCode { get; set; }
}

/// <summary>One entry on an asset's custody timeline (append-only; conflicts are rare by construction).</summary>
public sealed class CustodyMovement : SyncedEntity
{
    public Guid AssetId { get; set; }

    public CustodyMovementKind Kind { get; set; }

    public Guid? FromEmployeeId { get; set; }

    public Guid? ToEmployeeId { get; set; }

    public DateTime At { get; set; }

    public string? Note { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Guid? ReceiptDocumentId { get; set; }
}

/// <summary>A signed inter-office asset transfer package (AGREEMENT item 46).</summary>
public sealed class AssetTransfer : SyncedEntity
{
    public InOutDirection Direction { get; set; }

    public Guid? OtherOfficeUnitId { get; set; }

    /// <summary>JSON array of asset ids.</summary>
    public string AssetIds { get; set; } = "[]";

    public AssetTransferStatus Status { get; set; }

    public string? PackageFile { get; set; }

    public DateTime At { get; set; }

    public DateTime? DecidedAt { get; set; }
}

/// <summary>One physical inventory-count session.</summary>
public sealed class InventorySession : SyncedEntity
{
    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>Free-text status (no fixed value list in DATA-MODEL.md).</summary>
    public string? Status { get; set; }

    public string? ExportedFile { get; set; }
}

/// <summary>One asset's check result within an inventory session.</summary>
public sealed class InventoryItem : SyncedEntity
{
    public Guid SessionId { get; set; }

    public Guid AssetId { get; set; }

    public InventoryResult Result { get; set; }

    public DateTime CheckedAt { get; set; }

    public InventoryVia Via { get; set; }
}
