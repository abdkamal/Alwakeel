using System.Globalization;
using Wakeel.Admin.UI.Data;

namespace Wakeel.Admin.UI.Services;

/// <summary>One thing the offices have not been told about yet.</summary>
/// <param name="Id">Identifier of the row.</param>
/// <param name="EntityType">What changed: the organisation, a unit, an office, a device.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="SummaryAr">The sentence a person reads on the dashboard and in A10.</param>
/// <param name="CreatedAt">When it was noted.</param>
public sealed record AdminPendingChange(
    string Id,
    string EntityType,
    string EntityId,
    string SummaryAr,
    DateTimeOffset CreatedAt);

/// <summary>
/// The waiting list: everything that has been changed in the tool but has not yet reached the
/// computers that need to know (DATA-MODEL.md §13 <c>pending_changes</c>, A10).
/// </summary>
/// <remarks>
/// <para>
/// One row per thing, not one per edit. Renaming a section three times before lunch is still one
/// section that needs to go out, and a dashboard that counted three would be telling the person
/// there is three times as much work waiting as there is. So a note about something that is already
/// waiting replaces the note rather than joining it, and the newest wording wins.
/// </para>
/// <para>
/// Rows already sent out are left exactly where they are: they are the record of what went out and
/// when, and A10 reads them back. Only the undistributed note for the same thing is replaced.
/// </para>
/// </remarks>
public sealed class AdminPendingChanges
{
    private readonly AdminDb _db;
    private readonly TimeProvider _time;

    public AdminPendingChanges(AdminDb db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>
    /// Notes that something needs to go out. Replaces any note about the same thing that has not
    /// gone out yet.
    /// </summary>
    public void Add(string entityType, string entityId, string summaryAr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryAr);

        if (!_db.IsOpen)
        {
            return;
        }

        _db.Execute(
            """
            DELETE FROM pending_changes
            WHERE entity_type = $type AND entity_id = $id AND distributed_at IS NULL;
            """,
            ("$type", entityType),
            ("$id", entityId));

        _db.Execute(
            """
            INSERT INTO pending_changes(id, entity_type, entity_id, summary_ar, created_at)
            VALUES ($rowId, $type, $id, $summary, $at);
            """,
            ("$rowId", Guid.CreateVersion7().ToString()),
            ("$type", entityType),
            ("$id", entityId),
            ("$summary", summaryAr),
            ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));
    }

    /// <summary>Everything still waiting, newest first.</summary>
    public IReadOnlyList<AdminPendingChange> Open(int limit = 100)
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var rows = new List<AdminPendingChange>();
        using var command = _db.Command(
            """
            SELECT id, entity_type, entity_id, summary_ar, created_at
            FROM pending_changes
            WHERE distributed_at IS NULL
            ORDER BY created_at DESC, id DESC
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new AdminPendingChange(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return rows;
    }

    /// <summary>How many things are waiting.</summary>
    public int OpenCount =>
        _db.IsOpen ? (int)_db.Scalar("SELECT COUNT(*) FROM pending_changes WHERE distributed_at IS NULL;") : 0;
}
