namespace Wakeel.Core.Data.Entities;

// DATA-MODEL.md §5 — meetings, their attendees, and manually-added calendar appointments.
// The merged calendar view (meetings + appointments + commitment/task due dates + cycle
// reminders) is computed, not stored.

/// <summary>A scheduled meeting.</summary>
public sealed class Meeting : SyncedEntity
{
    public string Title { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    public int DurationMin { get; set; }

    public string? Location { get; set; }

    /// <summary>JSON-encoded ordered agenda.</summary>
    public string? Agenda { get; set; }

    public string? MinutesText { get; set; }

    public MeetingStatus Status { get; set; }

    /// <summary>Minutes before start to remind; <c>null</c> means "use the settings default" (AGREEMENT item 56).</summary>
    public int? ReminderMinutes { get; set; }

    public Guid? CorrespondenceId { get; set; }

    public Guid? CaseId { get; set; }

    public bool ReportInclude { get; set; }

    public bool ReportHighlight { get; set; }

    public string? ReportComment { get; set; }
}

/// <summary>An attendee (employee, external party, or org unit) of a meeting.</summary>
public sealed class MeetingAttendee : SyncedEntity
{
    public Guid MeetingId { get; set; }

    public AttendeeKind Kind { get; set; }

    /// <summary>Id of the employee/party/unit referenced, when known.</summary>
    public Guid? RefId { get; set; }

    public string Name { get; set; } = string.Empty;

    public AttendeeRole Role { get; set; }

    public bool Attended { get; set; }
}

/// <summary>A manually-added calendar appointment.</summary>
public sealed class Appointment : SyncedEntity
{
    public string Title { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    public string? Notes { get; set; }

    public int? ReminderMinutes { get; set; }

    public AppointmentStatus Status { get; set; }
}
