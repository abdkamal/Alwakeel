namespace Wakeel.Core.Data.Entities;

/// <summary>
/// Common columns carried by every synced ("official") table per DATA-MODEL.md §0:
/// identity, creation/update stamps, the device that created the row, the local edit
/// counter, the last version shared with other devices (for conflict detection), and a
/// soft-delete marker. Official records are never physically deleted.
/// </summary>
public abstract class SyncedEntity
{
    /// <summary>UUID v7 primary key, generated on the device that created the row.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Identifier of the device that created the row.</summary>
    public string OriginDevice { get; set; } = string.Empty;

    /// <summary>Counter incremented on every local edit.</summary>
    public long RowVersion { get; set; }

    /// <summary>Last version shared with other devices; used for sync conflict detection.</summary>
    public long BaseVersion { get; set; }

    /// <summary>UTC soft-delete timestamp; <c>null</c> while the row is active. Official rows are never hard-deleted.</summary>
    public DateTime? DeletedAt { get; set; }
}
