namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §2 — external directory. Internal parties are org_units; correspondence
// distinguishes external/internal via CounterpartyKind.

/// <summary>An external party (ministry, company, person, …) in the directory.</summary>
public sealed class Party : SyncedEntity
{
    public string Name { get; set; } = string.Empty;

    public PartyKind Kind { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }

    /// <summary>X25519 public key used to encrypt outgoing <c>.wakeel-msg</c> packages to this party.</summary>
    public byte[]? X25519Pub { get; set; }

    public string? QrToken { get; set; }
}

/// <summary>A point-in-time snapshot of a party's name; correspondence stores the name as of issuance.</summary>
public sealed class PartyName : SyncedEntity
{
    public Guid PartyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }
}
