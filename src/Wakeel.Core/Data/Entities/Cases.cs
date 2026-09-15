namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §6 — legal/administrative cases, their timeline events, and involved parties.

/// <summary>A legal or administrative case.</summary>
public sealed class Case : SyncedEntity
{
    public string CaseNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public Guid? PartyId { get; set; }

    /// <summary>Free-text stage (no fixed value list in DATA-MODEL.md).</summary>
    public string? Stage { get; set; }

    public DateTime? NextHearingAt { get; set; }

    public string? ResponsibleName { get; set; }

    public CaseStatus Status { get; set; }

    public string? Notes { get; set; }
}

/// <summary>One entry on a case's timeline.</summary>
public sealed class CaseEvent : SyncedEntity
{
    public Guid CaseId { get; set; }

    public DateTime At { get; set; }

    public CaseEventKind Kind { get; set; }

    public string? Description { get; set; }
}

/// <summary>Links a party to a case with a role.</summary>
public sealed class CaseParty : SyncedEntity
{
    public Guid CaseId { get; set; }

    public Guid PartyId { get; set; }

    /// <summary>Free-text role (no fixed value list in DATA-MODEL.md).</summary>
    public string? Role { get; set; }
}
