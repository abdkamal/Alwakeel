using System.Globalization;
using Microsoft.Data.Sqlite;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI.Services.Structure;

/// <summary>The four fixed layers of the structure (AGREEMENT item 6).</summary>
public enum OrgLevel
{
    /// <summary>هيئة — the organisation itself, the single root.</summary>
    Org,

    /// <summary>دائرة.</summary>
    Department,

    /// <summary>قسم.</summary>
    Section,

    /// <summary>وحدة.</summary>
    Unit,
}

/// <summary>One node of the structure, with everything the tree draws about it.</summary>
/// <param name="Id">Identifier of the node.</param>
/// <param name="ParentId">Which node it hangs from, or null for the root.</param>
/// <param name="Level">Which of the four layers it is.</param>
/// <param name="Name">Its name.</param>
/// <param name="HeadName">The name of whoever heads it.</param>
/// <param name="HeadTitle">That person's job title.</param>
/// <param name="OfficeCode">Its inventory code (AGREEMENT item 45), or null.</param>
/// <param name="SortOrder">Where it sits among its siblings.</param>
/// <param name="IsDisabled">Whether it has been switched off.</param>
/// <param name="IsOffice">Whether it is an office — a place with a computer in it.</param>
/// <param name="DeviceCount">How many devices are registered in it.</param>
/// <param name="ActiveDeviceCount">How many of those have not been revoked.</param>
public sealed record OrgNode(
    string Id,
    string? ParentId,
    OrgLevel Level,
    string Name,
    string? HeadName,
    string? HeadTitle,
    string? OfficeCode,
    int SortOrder,
    bool IsDisabled,
    bool IsOffice,
    int DeviceCount,
    int ActiveDeviceCount);

/// <summary>Why the structure refused a change.</summary>
public enum StructureRefusal
{
    /// <summary>It did not.</summary>
    None,

    /// <summary>The name is empty.</summary>
    NameRequired,

    /// <summary>Another child of the same parent already carries this name.</summary>
    DuplicateName,

    /// <summary>The node does not exist.</summary>
    NotFound,

    /// <summary>The parent does not exist.</summary>
    ParentNotFound,

    /// <summary>A section cannot hang from a unit, and nothing at all may hang from a unit.</summary>
    WrongLevel,

    /// <summary>Moving a node inside its own branch would cut the branch off the tree.</summary>
    WouldLoop,

    /// <summary>It still has devices that have not been revoked.</summary>
    HasActiveDevices,

    /// <summary>It still has things hanging from it.</summary>
    HasChildren,

    /// <summary>The inventory code is empty or already belongs to another office.</summary>
    OfficeCodeTaken,

    /// <summary>There is no organisation yet, so there is nothing to build a structure on.</summary>
    NoOrganisation,
}

/// <summary>
/// A05 «الهيكلية»: the editable tree of exactly four fixed layers — هيئة ← دائرة ← قسم ← وحدة —
/// and which of its nodes are offices.
/// </summary>
/// <remarks>
/// <para>
/// The layers are fixed, so this never asks what level something is: it works it out from the
/// parent and refuses anything else. A department hangs from the organisation, a section from a
/// department, a unit from a section, and nothing hangs from a unit.
/// </para>
/// <para>
/// Nothing is ever quietly deleted. A node that is no longer used is switched off, which takes it
/// and everything beneath it off the dashboard while leaving its history intact; deleting is only
/// offered for a node that has nothing hanging from it and no device that has ever been issued, and
/// a node with a device that has not been revoked is refused outright (A05's «لا حذف لعنصر له
/// أجهزة مفعّلة»).
/// </para>
/// </remarks>
public sealed class AdminStructureService
{
    private readonly AdminDb _db;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPendingChanges _pending;
    private readonly TimeProvider _time;

    public AdminStructureService(
        AdminDb db,
        AdminAuditService audit,
        AdminSession session,
        AdminPendingChanges pending,
        TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _session = session;
        _pending = pending;
        _time = time;
    }

    /// <summary>The level a child of <paramref name="parent"/> must be, or null when nothing may hang from it.</summary>
    public static OrgLevel? ChildLevelOf(OrgLevel parent) => parent switch
    {
        OrgLevel.Org => OrgLevel.Department,
        OrgLevel.Department => OrgLevel.Section,
        OrgLevel.Section => OrgLevel.Unit,
        _ => null,
    };

    /// <summary>
    /// The root node, made on first use and named after the organisation. The tree needs somewhere
    /// for the departments to hang from, and the organisation is that place.
    /// </summary>
    public OrgNode? EnsureRoot()
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        var existing = ReadAll().FirstOrDefault(n => n.Level == OrgLevel.Org);
        if (existing is not null)
        {
            return existing;
        }

        var orgName = _db.ScalarText("SELECT name FROM org LIMIT 1;");
        if (orgName is null)
        {
            return null;
        }

        var id = Guid.CreateVersion7().ToString();
        var now = Now();
        _db.Execute(
            """
            INSERT INTO org_units(id, parent_id, level, name, sort_order, created_at, updated_at)
            VALUES ($id, NULL, 'org', $name, 0, $at, $at);
            """,
            ("$id", id),
            ("$name", orgName),
            ("$at", now));

        return ReadOne(id);
    }

    /// <summary>
    /// The root's name follows the organisation's: renaming the organisation on A04 and finding the
    /// tree still headed by the old name would be two answers to the same question.
    /// </summary>
    public void SyncRootName()
    {
        if (!_db.IsOpen)
        {
            return;
        }

        var orgName = _db.ScalarText("SELECT name FROM org LIMIT 1;");
        if (orgName is null)
        {
            return;
        }

        _db.Execute(
            "UPDATE org_units SET name = $name, updated_at = $at WHERE level = 'org' AND name <> $name;",
            ("$name", orgName),
            ("$at", Now()));
    }

    /// <summary>Every node, in the order the tree draws them under each parent.</summary>
    public IReadOnlyList<OrgNode> ReadAll()
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var nodes = new List<OrgNode>();
        using var command = _db.Command(
            """
            SELECT u.id, u.parent_id, u.level, u.name, u.head_name, u.head_title,
                   COALESCE(o.office_code, u.office_code), u.sort_order, u.disabled_at,
                   o.id IS NOT NULL,
                   (SELECT COUNT(*) FROM devices d WHERE d.office_id = o.id),
                   (SELECT COUNT(*) FROM devices d WHERE d.office_id = o.id AND d.revoked_at IS NULL)
            FROM org_units u
            LEFT JOIN offices o ON o.unit_id = u.id
            ORDER BY u.sort_order, u.name;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            nodes.Add(new OrgNode(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseLevel(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7),
                !reader.IsDBNull(8),
                reader.GetBoolean(9),
                reader.GetInt32(10),
                reader.GetInt32(11)));
        }

        return nodes;
    }

    /// <summary>One node, or null when there is no such node.</summary>
    public OrgNode? ReadOne(string id) =>
        ReadAll().FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    /// <summary>The children of a node, in order.</summary>
    public IReadOnlyList<OrgNode> ChildrenOf(string parentId) =>
        ReadAll().Where(n => string.Equals(n.ParentId, parentId, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Adds a node under <paramref name="parentId"/>. Its level is decided by the parent's, so the
    /// four layers cannot be got wrong.
    /// </summary>
    public StructureRefusal Add(
        string parentId,
        string name,
        string? headName,
        string? headTitle,
        out string? newId)
    {
        newId = null;

        var parent = ReadOne(parentId);
        if (parent is null)
        {
            return StructureRefusal.ParentNotFound;
        }

        if (ChildLevelOf(parent.Level) is not { } level)
        {
            return StructureRefusal.WrongLevel;
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return StructureRefusal.NameRequired;
        }

        if (ChildrenOf(parentId).Any(c => string.Equals(c.Name, trimmed, StringComparison.Ordinal)))
        {
            return StructureRefusal.DuplicateName;
        }

        var id = Guid.CreateVersion7().ToString();
        var order = ChildrenOf(parentId).Count == 0 ? 0 : ChildrenOf(parentId).Max(c => c.SortOrder) + 1;
        var now = Now();

        try
        {
            _db.Execute(
                """
                INSERT INTO org_units(id, parent_id, level, name, head_name, head_title, sort_order, created_at, updated_at)
                VALUES ($id, $parent, $level, $name, $headName, $headTitle, $order, $at, $at);
                """,
                ("$id", id),
                ("$parent", parentId),
                ("$level", LevelText(level)),
                ("$name", trimmed),
                ("$headName", Blank(headName)),
                ("$headTitle", Blank(headTitle)),
                ("$order", order),
                ("$at", now));
        }
        catch (SqliteException)
        {
            // The sibling-name index is the second guard behind the check above: two windows of the
            // same tool could otherwise race each other into the same name.
            return StructureRefusal.DuplicateName;
        }

        newId = id;
        Record(id, "structure_added", AdminAr.Structure.Log.Added(level, trimmed));
        return StructureRefusal.None;
    }

    /// <summary>Changes a node's name, its head, and its inventory code.</summary>
    public StructureRefusal Update(string id, string name, string? headName, string? headTitle)
    {
        var node = ReadOne(id);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return StructureRefusal.NameRequired;
        }

        var siblings = node.ParentId is null
            ? ReadAll().Where(n => n.ParentId is null)
            : ChildrenOf(node.ParentId);

        if (siblings.Any(c => !string.Equals(c.Id, id, StringComparison.Ordinal)
                              && string.Equals(c.Name, trimmed, StringComparison.Ordinal)))
        {
            return StructureRefusal.DuplicateName;
        }

        try
        {
            _db.Execute(
                """
                UPDATE org_units
                SET name = $name, head_name = $headName, head_title = $headTitle, updated_at = $at
                WHERE id = $id;
                """,
                ("$id", id),
                ("$name", trimmed),
                ("$headName", Blank(headName)),
                ("$headTitle", Blank(headTitle)),
                ("$at", Now()));
        }
        catch (SqliteException)
        {
            return StructureRefusal.DuplicateName;
        }

        // An office takes its name from the unit it is in; letting the two drift would mean the
        // dashboard and the tree naming the same room differently.
        _db.Execute(
            "UPDATE offices SET name = $name, updated_at = $at WHERE unit_id = $id;",
            ("$id", id),
            ("$name", trimmed),
            ("$at", Now()));

        Record(id, "structure_updated", AdminAr.Structure.Log.Updated(node.Level, trimmed));
        return StructureRefusal.None;
    }

    /// <summary>Moves a node to another parent of the layer above it.</summary>
    public StructureRefusal Move(string id, string newParentId)
    {
        var node = ReadOne(id);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        var parent = ReadOne(newParentId);
        if (parent is null)
        {
            return StructureRefusal.ParentNotFound;
        }

        if (ChildLevelOf(parent.Level) != node.Level)
        {
            return StructureRefusal.WrongLevel;
        }

        if (string.Equals(id, newParentId, StringComparison.Ordinal) || IsDescendantOf(newParentId, id))
        {
            return StructureRefusal.WouldLoop;
        }

        if (ChildrenOf(newParentId).Any(c => !string.Equals(c.Id, id, StringComparison.Ordinal)
                                             && string.Equals(c.Name, node.Name, StringComparison.Ordinal)))
        {
            return StructureRefusal.DuplicateName;
        }

        try
        {
            _db.Execute(
                "UPDATE org_units SET parent_id = $parent, updated_at = $at WHERE id = $id;",
                ("$id", id),
                ("$parent", newParentId),
                ("$at", Now()));
        }
        catch (SqliteException)
        {
            return StructureRefusal.DuplicateName;
        }

        Record(id, "structure_moved", AdminAr.Structure.Log.Moved(node.Name, parent.Name));
        return StructureRefusal.None;
    }

    /// <summary>Switches a node off, which takes it and everything beneath it out of service.</summary>
    public StructureRefusal Disable(string id)
    {
        var node = ReadOne(id);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        if (node.Level == OrgLevel.Org)
        {
            return StructureRefusal.WrongLevel;
        }

        _db.Execute(
            "UPDATE org_units SET disabled_at = $at, updated_at = $at WHERE id = $id AND disabled_at IS NULL;",
            ("$id", id),
            ("$at", Now()));

        Record(id, "structure_disabled", AdminAr.Structure.Log.Disabled(node.Level, node.Name));
        return StructureRefusal.None;
    }

    /// <summary>Puts a node back in service.</summary>
    public StructureRefusal Enable(string id)
    {
        var node = ReadOne(id);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        _db.Execute(
            "UPDATE org_units SET disabled_at = NULL, updated_at = $at WHERE id = $id;",
            ("$id", id),
            ("$at", Now()));

        Record(id, "structure_enabled", AdminAr.Structure.Log.Enabled(node.Level, node.Name));
        return StructureRefusal.None;
    }

    /// <summary>
    /// Removes a node for good. Refused while anything hangs from it, while it is an office with a
    /// device that has not been revoked, or when it is the root.
    /// </summary>
    public StructureRefusal Delete(string id)
    {
        var node = ReadOne(id);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        if (node.Level == OrgLevel.Org)
        {
            return StructureRefusal.WrongLevel;
        }

        if (ChildrenOf(id).Count > 0)
        {
            return StructureRefusal.HasChildren;
        }

        if (node.ActiveDeviceCount > 0)
        {
            return StructureRefusal.HasActiveDevices;
        }

        if (node.DeviceCount > 0)
        {
            // Revoked devices are the record of what was once here, and removing the office would
            // remove the room those devices point at.
            return StructureRefusal.HasActiveDevices;
        }

        _db.Execute("DELETE FROM offices WHERE unit_id = $id;", ("$id", id));
        _db.Execute("DELETE FROM org_units WHERE id = $id;", ("$id", id));
        _db.Execute(
            "DELETE FROM pending_changes WHERE entity_type = 'unit' AND entity_id = $id AND distributed_at IS NULL;",
            ("$id", id));

        _audit.Write(
            _session.AdminName,
            "structure_deleted",
            AdminAr.Structure.Log.Deleted(node.Level, node.Name),
            entityType: "unit",
            entityId: id);

        return StructureRefusal.None;
    }

    /// <summary>
    /// Marks a node as an office and gives it its inventory code (AGREEMENT item 45), or changes the
    /// code of one that is already an office.
    /// </summary>
    public StructureRefusal SetOffice(string unitId, string officeCode)
    {
        var node = ReadOne(unitId);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        if (node.Level == OrgLevel.Org)
        {
            return StructureRefusal.WrongLevel;
        }

        var code = (officeCode ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return StructureRefusal.OfficeCodeTaken;
        }

        var takenBy = _db.ScalarText(
            "SELECT unit_id FROM offices WHERE office_code = $code LIMIT 1;",
            ("$code", code));
        if (takenBy is not null && !string.Equals(takenBy, unitId, StringComparison.Ordinal))
        {
            return StructureRefusal.OfficeCodeTaken;
        }

        var now = Now();
        if (node.IsOffice)
        {
            _db.Execute(
                "UPDATE offices SET office_code = $code, name = $name, updated_at = $at WHERE unit_id = $id;",
                ("$id", unitId),
                ("$code", code),
                ("$name", node.Name),
                ("$at", now));
        }
        else
        {
            _db.Execute(
                """
                INSERT INTO offices(id, unit_id, name, office_code, created_at, updated_at)
                VALUES ($id, $unit, $name, $code, $at, $at);
                """,
                ("$id", Guid.CreateVersion7().ToString()),
                ("$unit", unitId),
                ("$name", node.Name),
                ("$code", code),
                ("$at", now));
        }

        _db.Execute(
            "UPDATE org_units SET office_code = $code, updated_at = $at WHERE id = $id;",
            ("$id", unitId),
            ("$code", code),
            ("$at", now));

        Record(unitId, "office_set", AdminAr.Structure.Log.OfficeSet(node.Name, code));
        return StructureRefusal.None;
    }

    /// <summary>Stops treating a node as an office. Refused while it still has any device.</summary>
    public StructureRefusal ClearOffice(string unitId)
    {
        var node = ReadOne(unitId);
        if (node is null)
        {
            return StructureRefusal.NotFound;
        }

        if (node.DeviceCount > 0)
        {
            return StructureRefusal.HasActiveDevices;
        }

        _db.Execute("DELETE FROM offices WHERE unit_id = $id;", ("$id", unitId));
        _db.Execute(
            "UPDATE org_units SET office_code = NULL, updated_at = $at WHERE id = $id;",
            ("$id", unitId),
            ("$at", Now()));

        Record(unitId, "office_cleared", AdminAr.Structure.Log.OfficeCleared(node.Name));
        return StructureRefusal.None;
    }

    /// <summary>Whether <paramref name="id"/> sits somewhere beneath <paramref name="ancestorId"/>.</summary>
    public bool IsDescendantOf(string id, string ancestorId)
    {
        var all = ReadAll().ToDictionary(n => n.Id, StringComparer.Ordinal);
        var at = id;

        // The tree is four layers deep, so the walk is bounded; the counter is there only so a row
        // that somehow points at itself cannot spin forever.
        for (var step = 0; step < 8; step++)
        {
            if (!all.TryGetValue(at, out var node) || node.ParentId is null)
            {
                return false;
            }

            if (string.Equals(node.ParentId, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            at = node.ParentId;
        }

        return false;
    }

    /// <summary>Whether a node is out of service, either itself or through something above it.</summary>
    public bool IsOutOfService(IReadOnlyList<OrgNode> all, OrgNode node)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(node);

        var byId = all.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var at = node;

        for (var step = 0; step < 8; step++)
        {
            if (at.IsDisabled)
            {
                return true;
            }

            if (at.ParentId is null || !byId.TryGetValue(at.ParentId, out var parent))
            {
                return false;
            }

            at = parent;
        }

        return false;
    }

    private static string LevelText(OrgLevel level) => level switch
    {
        OrgLevel.Org => "org",
        OrgLevel.Department => "department",
        OrgLevel.Section => "section",
        _ => "unit",
    };

    private static OrgLevel ParseLevel(string text) => text switch
    {
        "org" => OrgLevel.Org,
        "department" => OrgLevel.Department,
        "section" => OrgLevel.Section,
        _ => OrgLevel.Unit,
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string Now() => _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private void Record(string id, string action, string summary)
    {
        _audit.Write(_session.AdminName, action, summary, entityType: "unit", entityId: id);
        _pending.Add("unit", id, summary);
    }
}
