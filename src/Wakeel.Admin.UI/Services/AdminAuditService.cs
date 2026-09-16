using System.Globalization;
using System.Text.Json;
using Wakeel.Admin.UI.Data;

namespace Wakeel.Admin.UI.Services;

/// <summary>One entry of the operations log, as A11 will show it.</summary>
/// <param name="Id">Identifier of the row.</param>
/// <param name="At">When it happened, in UTC.</param>
/// <param name="Actor">Who did it, by name.</param>
/// <param name="Action">A stable English token naming the kind of action, for filtering.</param>
/// <param name="EntityType">What it was done to, when it was done to something.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="SummaryAr">The Arabic sentence a person reads.</param>
/// <param name="Details">Extra facts as JSON, never a secret.</param>
public sealed record AdminAuditEntry(
    string Id,
    DateTimeOffset At,
    string Actor,
    string Action,
    string? EntityType,
    string? EntityId,
    string SummaryAr,
    string? Details);

/// <summary>
/// Writes the administration tool's operations log (DATA-MODEL.md §13 <c>audit_log</c>, AGREEMENT
/// item 42). Append only: nothing here ever updates or deletes a row.
/// </summary>
/// <remarks>
/// <para>
/// This is the writer alone. The screen that reads it back, searches it and exports it is A11, and
/// that belongs to admin-3; the reading helper below exists so the dashboard can show «آخر تصدير»
/// and so the tests can prove what was written.
/// </para>
/// <para>
/// No secret may ever reach a row. Details are built from a small dictionary of plain facts by the
/// caller, and <see cref="Write"/> redacts the value of any field whose name says it holds secret
/// material rather than trusting every caller to remember. A password, a key, a seed or a recovery
/// code has no business in a log that is meant to be read out loud — and a log line is never a
/// reason to refuse the action the person actually asked for, so the row itself still goes in.
/// </para>
/// </remarks>
public sealed class AdminAuditService
{
    /// <summary>Actor name for a row written before anybody has said who they are.</summary>
    private const string UnknownActor = "الأداة";

    /// <summary>What a redacted detail value is written as, so the row still says a field was there.</summary>
    private const string Redacted = "—";

    /// <summary>
    /// Name segments that mark secret material wherever they appear in a details key. Matched as
    /// whole segments and without regard to case, so <c>newPassword</c>, <c>new_password</c> and
    /// <c>PASSWORD</c> are all caught while an ordinary word that merely contains one of them is
    /// not.
    /// </summary>
    private static readonly string[] SecretSegments =
    [
        "password", "passphrase", "secret", "seed", "recovery", "wrap", "token", "mnemonic",
        "private", "credential", "pin",
    ];

    /// <summary>
    /// Segments that only mark a name as metadata: the figures about a key, never the key. They let
    /// <c>key_version</c> and <c>key_changed_at</c> through while <c>officeKey</c> stays out.
    /// </summary>
    private static readonly string[] MetadataSegments =
    [
        "version", "id", "at", "on", "count", "kind", "type", "name", "status", "length", "changed",
        "rotated", "generated", "expires", "issued",
    ];

    private readonly AdminDb _db;
    private readonly TimeProvider _time;

    public AdminAuditService(AdminDb db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>
    /// Appends one entry. Returns without writing anything when the database is not open — a log
    /// that cannot be written is never a reason to stop the thing the person actually asked for.
    /// </summary>
    /// <param name="actor">Who did it, by name.</param>
    /// <param name="action">A stable English token naming the kind of action.</param>
    /// <param name="summaryAr">The Arabic sentence a person reads in A11.</param>
    /// <param name="entityType">What it was done to, when it was done to something.</param>
    /// <param name="entityId">Which one.</param>
    /// <param name="details">Plain facts to keep alongside; never secrets.</param>
    public void Write(
        string actor,
        string action,
        string summaryAr,
        string? entityType = null,
        string? entityId = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryAr);

        if (!_db.IsOpen)
        {
            return;
        }

        var detailsJson = SerializeDetails(details);

        _db.Execute(
            """
            INSERT INTO audit_log(id, at, actor, action, entity_type, entity_id, summary_ar, details)
            VALUES ($id, $at, $actor, $action, $entityType, $entityId, $summary, $details);
            """,
            ("$id", Guid.CreateVersion7().ToString()),
            ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)),
            ("$actor", string.IsNullOrWhiteSpace(actor) ? UnknownActor : actor),
            ("$action", action),
            ("$entityType", entityType),
            ("$entityId", entityId),
            ("$summary", summaryAr),
            ("$details", detailsJson));
    }

    /// <summary>The most recent entries, newest first. A11 replaces this with its own searching view.</summary>
    public IReadOnlyList<AdminAuditEntry> Recent(int limit = 50)
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var entries = new List<AdminAuditEntry>();
        using var command = _db.Command(
            """
            SELECT id, at, actor, action, entity_type, entity_id, summary_ar, details
            FROM audit_log
            ORDER BY at DESC, id DESC
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new AdminAuditEntry(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return entries;
    }

    private static string? SerializeDetails(IReadOnlyDictionary<string, string>? details)
    {
        if (details is null || details.Count == 0)
        {
            return null;
        }

        var safe = new Dictionary<string, string>(details.Count, StringComparer.Ordinal);
        foreach (var pair in details)
        {
            safe[pair.Key] = LooksLikeSecret(pair.Key) ? Redacted : pair.Value;
        }

        return JsonSerializer.Serialize(safe);
    }

    /// <summary>
    /// Decides whether a details key names secret material. The name is cut into segments at
    /// <c>_</c>, <c>-</c>, <c>.</c>, spaces and camel-case boundaries, and each segment is compared
    /// whole, so ordinary field names survive:
    /// <list type="bullet">
    /// <item><description><c>office_code</c>, <c>device_code</c> — a code a person reads off a screen, kept.</description></item>
    /// <item><description><c>key_version</c>, <c>key_changed_at</c> — figures about a key, kept.</description></item>
    /// <item><description><c>recoveryCode</c>, <c>new_password</c>, <c>officeKey</c>, <c>code</c> — material, redacted.</description></item>
    /// </list>
    /// A name that looks like a secret is never a reason to refuse the action the person asked for,
    /// so the row is still written and only the value is replaced.
    /// </summary>
    public static bool LooksLikeSecret(string key)
    {
        var segments = SplitSegments(key);
        if (segments.Count == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (SecretSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // "key" on its own, or at the end of a name, is the key itself; leading and followed by a
        // metadata word it is only a fact about the key.
        var keyAt = segments.FindLastIndex(s => string.Equals(s, "key", StringComparison.OrdinalIgnoreCase));
        if (keyAt >= 0)
        {
            var qualified = keyAt < segments.Count - 1
                && segments.Skip(keyAt + 1).Any(s => MetadataSegments.Contains(s, StringComparer.OrdinalIgnoreCase));
            if (!qualified)
            {
                return true;
            }
        }

        // "code" alone is the recovery code; qualified by what it identifies it is a label.
        return segments.Count == 1
            && string.Equals(segments[0], "code", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitSegments(string key)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in key)
        {
            if (character is '_' or '-' or '.' or ' ')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(character) && current.Length > 0 && !char.IsUpper(current[^1]))
            {
                Flush();
            }

            current.Append(character);
        }

        Flush();
        return segments;

        void Flush()
        {
            if (current.Length > 0)
            {
                segments.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
