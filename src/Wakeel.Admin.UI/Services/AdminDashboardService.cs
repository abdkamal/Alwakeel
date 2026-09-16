using System.Globalization;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Text;
using Wakeel.Design.Bidi;

namespace Wakeel.Admin.UI.Services;

/// <summary>What kind of thing a dashboard alert is about.</summary>
public enum AdminAlertKind
{
    /// <summary>An office that has never had a setup file made for it.</summary>
    OfficeWithoutSetup,

    /// <summary>Structure edits nobody has been told about yet.</summary>
    UndistributedChanges,

    /// <summary>A device that was revoked but is still listed in its office.</summary>
    RevokedDevice,
}

/// <summary>One line of the alerts panel.</summary>
/// <param name="Kind">What it is about.</param>
/// <param name="Title">The heading a person reads.</param>
/// <param name="Detail">The sentence underneath.</param>
public sealed record AdminAlert(AdminAlertKind Kind, string Title, string Detail);

/// <summary>One line of the structure summary: a department and what is inside it.</summary>
/// <param name="Name">The department's name.</param>
/// <param name="Sections">How many sections it has.</param>
/// <param name="Units">How many units.</param>
/// <param name="Offices">How many of those are offices.</param>
/// <param name="OfficesWaiting">How many of those offices have no setup file yet.</param>
public sealed record AdminBranchSummary(string Name, int Sections, int Units, int Offices, int OfficesWaiting);

/// <summary>Everything A03 draws, read in one pass.</summary>
/// <param name="OrgName">The organisation's full name, or empty before A04 has run.</param>
/// <param name="Departments">Number of دوائر.</param>
/// <param name="Sections">Number of أقسام.</param>
/// <param name="Units">Number of وحدات.</param>
/// <param name="Offices">
/// Number of offices that are in service. THE RULE, one for the whole tool: an office counts on
/// this board only while the unit it hangs from and every unit above it is still in service. A
/// department that has been switched off takes its sections, its units, its offices, their devices
/// and their waiting-for-setup alerts off the board with it — so nothing here can ever name a
/// branch the structure summary no longer draws. The rows stay in the tables and keep their
/// history; only the board stops counting them, and admin-2 inherits this one rule rather than
/// having to invent a second.
/// </param>
/// <param name="ActivatedOffices">Offices in service that already have a setup file.</param>
/// <param name="Devices">Devices in the offices that are in service, by the same rule.</param>
/// <param name="RevokedDevices">How many of those are revoked.</param>
/// <param name="PendingChanges">Structure edits nobody has been told about yet.</param>
/// <param name="LastExportAt">When a setup file was last exported.</param>
/// <param name="LastExportOffice">Which office it was for.</param>
/// <param name="Branches">One line per department.</param>
/// <param name="Alerts">
/// The alert lines that are actually drawn. The list is deliberately short — a panel listing a
/// hundred offices one under the other would be unreadable — so nothing may be counted from it.
/// The figures the panel states come from <see cref="OfficesWaiting"/>, <see cref="PendingChanges"/>
/// and <see cref="RevokedDevices"/>, which are read from the tables and never cut off, so the panel
/// and the cards above it can never disagree about the same organisation.
/// </param>
public sealed record AdminDashboard(
    string OrgName,
    int Departments,
    int Sections,
    int Units,
    int Offices,
    int ActivatedOffices,
    int Devices,
    int RevokedDevices,
    int PendingChanges,
    DateTimeOffset? LastExportAt,
    string? LastExportOffice,
    IReadOnlyList<AdminBranchSummary> Branches,
    IReadOnlyList<AdminAlert> Alerts)
{
    /// <summary>Devices that are not revoked.</summary>
    public int ActiveDevices => Math.Max(0, Devices - RevokedDevices);

    /// <summary>Offices still waiting for their first setup file.</summary>
    public int OfficesWaiting => Math.Max(0, Offices - ActivatedOffices);

    /// <summary>Everything wanting attention, counted from the tables rather than from the drawn list.</summary>
    public int AlertCount => OfficesWaiting + PendingChanges + RevokedDevices;

    /// <summary>Whether the structure is still empty, which A03 shows as an invitation, not an error.</summary>
    public bool IsEmpty => Departments == 0 && Offices == 0 && Devices == 0;

    /// <summary>An empty board, for a tool that is locked or has no organisation yet.</summary>
    public static AdminDashboard Empty { get; } =
        new(string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, null, null, [], []);
}

/// <summary>
/// The one line the top bar of the shell shows: the organisation and how big it is. Small on
/// purpose — the shell re-reads it on every navigation, and it has no business paying for the
/// alerts and the department breakdown A03 needs.
/// </summary>
/// <param name="OrgName">The organisation's full name, or empty before A04 has run.</param>
/// <param name="Departments">Number of دوائر.</param>
/// <param name="Sections">Number of أقسام.</param>
/// <param name="Units">Number of وحدات.</param>
public sealed record AdminOrgLine(string OrgName, int Departments, int Sections, int Units)
{
    /// <summary>The line for a tool that is locked or has no organisation yet.</summary>
    public static AdminOrgLine Empty { get; } = new(string.Empty, 0, 0, 0);
}

/// <summary>
/// Reads the dashboard of A03 straight out of <c>admin.db</c>.
/// </summary>
/// <remarks>
/// Every figure comes from the tables, never from a stored total, so the board is right the moment
/// a structure edit or an export lands. The tables that admin-2 and admin-3 fill are already in the
/// schema, so this reads zeroes rather than failing while those sub-packages are still to come, and
/// starts showing real numbers the moment they write their first row — no change needed here.
/// </remarks>
public sealed class AdminDashboardService
{
    /// <summary>
    /// Offices that count on this board: an office whose unit, and every unit above it, is still in
    /// service. The structure is exactly four fixed layers (AGREEMENT item 6) — هيئة ← دائرة ← قسم
    /// ← وحدة — and an office may hang from any of the lower three, so three joins upwards reach the
    /// top and no recursive query is needed. A missing ancestor leaves its columns null, and
    /// <c>NULL IS NULL</c> is true, so a department hanging straight off the organisation passes.
    /// </summary>
    private const string FromLiveOffices =
        """
        FROM offices o
        JOIN org_units u ON u.id = o.unit_id
        LEFT JOIN org_units p1 ON p1.id = u.parent_id
        LEFT JOIN org_units p2 ON p2.id = p1.parent_id
        LEFT JOIN org_units p3 ON p3.id = p2.parent_id
        WHERE u.disabled_at IS NULL
          AND p1.disabled_at IS NULL
          AND p2.disabled_at IS NULL
          AND p3.disabled_at IS NULL
        """;

    /// <summary>Devices that count on this board: those sitting in an office that counts.</summary>
    private const string FromLiveDevices =
        """
         FROM devices v
         JOIN offices o ON o.id = v.office_id
         JOIN org_units u ON u.id = o.unit_id
         LEFT JOIN org_units p1 ON p1.id = u.parent_id
         LEFT JOIN org_units p2 ON p2.id = p1.parent_id
         LEFT JOIN org_units p3 ON p3.id = p2.parent_id
         WHERE u.disabled_at IS NULL
           AND p1.disabled_at IS NULL
           AND p2.disabled_at IS NULL
           AND p3.disabled_at IS NULL
         """;

    private readonly AdminDb _db;
    private readonly AdminKeyService _keys;

    public AdminDashboardService(AdminDb db, AdminKeyService keys)
    {
        _db = db;
        _keys = keys;
    }

    /// <summary>Reads the whole board.</summary>
    public AdminDashboard Read()
    {
        if (!_db.IsOpen)
        {
            return AdminDashboard.Empty;
        }

        var org = _keys.ReadOrganisation();
        var departments = (int)_db.Scalar("SELECT COUNT(*) FROM org_units WHERE level = 'department' AND disabled_at IS NULL;");
        var sections = (int)_db.Scalar("SELECT COUNT(*) FROM org_units WHERE level = 'section' AND disabled_at IS NULL;");
        var units = (int)_db.Scalar("SELECT COUNT(*) FROM org_units WHERE level = 'unit' AND disabled_at IS NULL;");
        var offices = (int)_db.Scalar($"SELECT COUNT(*) {FromLiveOffices};");
        var activated = (int)_db.Scalar($"SELECT COUNT(*) {FromLiveOffices} AND o.activated_at IS NOT NULL;");
        var devices = (int)_db.Scalar($"SELECT COUNT(*) {FromLiveDevices};");
        var revoked = (int)_db.Scalar($"SELECT COUNT(*) {FromLiveDevices} AND v.revoked_at IS NOT NULL;");
        var pending = (int)_db.Scalar("SELECT COUNT(*) FROM pending_changes WHERE distributed_at IS NULL;");

        var (lastExportAt, lastExportOffice) = ReadLastExport();

        return new AdminDashboard(
            org?.Name ?? string.Empty,
            departments,
            sections,
            units,
            offices,
            activated,
            devices,
            revoked,
            pending,
            lastExportAt,
            lastExportOffice,
            ReadBranches(),
            ReadAlerts());
    }

    /// <summary>
    /// Just what the top bar says: the organisation's name and the three structure figures. The
    /// shell reads this on every navigation, so it takes one pass over the structure table instead
    /// of the whole board.
    /// </summary>
    public AdminOrgLine ReadOrgLine()
    {
        if (!_db.IsOpen)
        {
            return AdminOrgLine.Empty;
        }

        var departments = 0;
        var sections = 0;
        var units = 0;

        using var command = _db.Command(
            """
            SELECT level, COUNT(*)
            FROM org_units
            WHERE disabled_at IS NULL AND level IN ('department', 'section', 'unit')
            GROUP BY level;
            """);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var count = reader.GetInt32(1);
                switch (reader.GetString(0))
                {
                    case "department":
                        departments = count;
                        break;
                    case "section":
                        sections = count;
                        break;
                    case "unit":
                        units = count;
                        break;
                }
            }
        }

        return new AdminOrgLine(_keys.ReadOrganisation()?.Name ?? string.Empty, departments, sections, units);
    }

    private (DateTimeOffset? At, string? Office) ReadLastExport()
    {
        using var command = _db.Command(
            $"""
             SELECT e.exported_at, o.name
             FROM setup_exports e
             JOIN devices d ON d.id = e.device_id
             JOIN offices o ON o.id = d.office_id
             WHERE EXISTS (SELECT 1 {FromLiveDevices} AND v.id = d.id)
             ORDER BY e.exported_at DESC
             LIMIT 1;
             """);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }

        var at = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return (at, reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private List<AdminBranchSummary> ReadBranches()
    {
        var branches = new List<AdminBranchSummary>();

        // One row per department, with everything beneath it counted through the parent chain. The
        // structure is exactly four fixed layers (AGREEMENT item 6), so two joins reach the bottom
        // and no recursive query is needed.
        using var command = _db.Command(
            """
            SELECT d.name,
                   COUNT(DISTINCT s.id)                                        AS sections,
                   COUNT(DISTINCT u.id)                                        AS units,
                   COUNT(DISTINCT o.id)                                        AS offices,
                   COUNT(DISTINCT CASE WHEN o.activated_at IS NULL THEN o.id END) AS waiting
            FROM org_units d
            LEFT JOIN org_units s ON s.parent_id = d.id AND s.level = 'section' AND s.disabled_at IS NULL
            LEFT JOIN org_units u ON u.parent_id = s.id AND u.level = 'unit' AND u.disabled_at IS NULL
            LEFT JOIN offices  o ON o.unit_id IN (d.id, s.id, u.id)
            WHERE d.level = 'department' AND d.disabled_at IS NULL
            GROUP BY d.id, d.name, d.sort_order
            ORDER BY d.sort_order, d.name;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            branches.Add(new AdminBranchSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return branches;
    }

    /// <summary>
    /// The alert lines the panel draws. Each query stops at twenty rows, because the panel is a
    /// glance and not a register — «عرض الكل» leads to the place that lists them all. Nothing on
    /// screen counts these rows: the figures come from the uncapped totals on
    /// <see cref="AdminDashboard"/>, so a hundred waiting offices are still reported as a hundred.
    /// </summary>
    private List<AdminAlert> ReadAlerts()
    {
        var alerts = new List<AdminAlert>();

        using (var command = _db.Command(
                   $"""
                    SELECT o.name, u.name
                    {FromLiveOffices}
                      AND o.activated_at IS NULL
                    ORDER BY o.name
                    LIMIT 20;
                    """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var office = reader.GetString(0);
                var unit = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                alerts.Add(new AdminAlert(
                    AdminAlertKind.OfficeWithoutSetup,
                    office,
                    unit.Length == 0
                        ? AdminAr.Dashboard.AlertOfficeNoSetup
                        : $"{unit} — {AdminAr.Dashboard.AlertOfficeNoSetup}"));
            }
        }

        using (var command = _db.Command(
                   """
                   SELECT summary_ar
                   FROM pending_changes
                   WHERE distributed_at IS NULL
                   ORDER BY created_at DESC
                   LIMIT 20;
                   """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                alerts.Add(new AdminAlert(
                    AdminAlertKind.UndistributedChanges,
                    AdminAr.Dashboard.AlertPendingChanges,
                    reader.GetString(0)));
            }
        }

        using (var command = _db.Command(
                   $"""
                    SELECT o.name, v.device_no
                    {FromLiveDevices}
                      AND v.revoked_at IS NOT NULL
                    ORDER BY o.name, v.device_no
                    LIMIT 20;
                    """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                alerts.Add(new AdminAlert(
                    AdminAlertKind.RevokedDevice,
                    reader.GetString(0),
                    Bidi.Wrap($"{AdminAr.Dashboard.AlertRevokedDevice} (الجهاز {reader.GetInt32(1).ToString(CultureInfo.InvariantCulture)})")));
            }
        }

        return alerts;
    }
}
