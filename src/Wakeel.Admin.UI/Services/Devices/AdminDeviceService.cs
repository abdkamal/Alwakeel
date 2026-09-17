using System.Globalization;
using Microsoft.Data.Sqlite;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI.Services.Devices;

/// <summary>The three roles an account can have (ARCHITECTURE.md §2).</summary>
public static class DeviceRoles
{
    /// <summary>مدير المكتب.</summary>
    public const string Manager = "manager";

    /// <summary>سكرتير.</summary>
    public const string Secretary = "secretary";

    /// <summary>موظف العُهد.</summary>
    public const string Custodian = "custodian";

    /// <summary>The three, in the order the screens offer them.</summary>
    public static IReadOnlyList<string> All { get; } = [Manager, Secretary, Custodian];

    /// <summary>Whether a piece of text names one of the three.</summary>
    public static bool IsValid(string? role) => role is Manager or Secretary or Custodian;
}

/// <summary>How much of the organisation's work a device keeps in step with.</summary>
public static class SyncScopes
{
    /// <summary>Everything the office does.</summary>
    public const string Full = "full";

    /// <summary>The custody register alone.</summary>
    public const string Custody = "custody";

    /// <summary>Both, in the order the screens offer them.</summary>
    public static IReadOnlyList<string> All { get; } = [Full, Custody];

    /// <summary>Whether a piece of text names one of the two.</summary>
    public static bool IsValid(string? scope) => scope is Full or Custody;
}

/// <summary>An office as A06 lists it.</summary>
/// <param name="Id">Identifier of the office.</param>
/// <param name="UnitId">The structure node it is.</param>
/// <param name="Name">Its name.</param>
/// <param name="OfficeCode">Its inventory code (AGREEMENT item 45).</param>
/// <param name="Path">Where it sits in the structure, from the organisation downwards.</param>
/// <param name="Devices">How many devices are registered in it.</param>
/// <param name="ActiveDevices">How many of those have not been revoked.</param>
/// <param name="Phones">How many of them are paired phones.</param>
/// <param name="KeyVersion">Which issue of the office key is in force; zero means none yet.</param>
/// <param name="ActivatedAt">When its first setup file was made.</param>
/// <param name="IsOutOfService">Whether it or something above it has been switched off.</param>
public sealed record AdminOffice(
    string Id,
    string UnitId,
    string Name,
    string OfficeCode,
    string Path,
    int Devices,
    int ActiveDevices,
    int Phones,
    int KeyVersion,
    DateTimeOffset? ActivatedAt,
    bool IsOutOfService)
{
    /// <summary>Whether the office has a key yet.</summary>
    public bool HasKey => KeyVersion > 0;
}

/// <summary>A device and the account sitting at it.</summary>
/// <param name="Id">Identifier of the device.</param>
/// <param name="OfficeId">The office it is in.</param>
/// <param name="DeviceNo">Its number inside that office, 1 to 9.</param>
/// <param name="IsPhone">Whether it is a paired phone rather than a computer.</param>
/// <param name="Role">manager, secretary or custodian; empty when <paramref name="HasAccount"/> is false.</param>
/// <param name="EmployeeName">Who sits at it; empty when there is no account row.</param>
/// <param name="EmployeeNo">That person's number, 1 to 9; zero when there is no account row.</param>
/// <param name="SyncScope">full or custody; empty when there is no account row.</param>
/// <param name="Status">pending, active or revoked; empty when there is no account row.</param>
/// <param name="IssuedAt">When its certificate was issued.</param>
/// <param name="RevokedAt">When it was shut out, if it was.</param>
/// <param name="PairedAt">When the phone was paired, for a phone.</param>
/// <param name="LastSyncAt">When it last kept in step.</param>
/// <param name="SeedsHeld">Whether its key seed is still waiting in the tool for a first setup file.</param>
/// <param name="ExportedAt">When its setup file was last made.</param>
/// <param name="HasAccount">
/// Whether the device actually has an account row. A registration writes the device and its account
/// one after the other, so a run that stopped in between leaves a device with no employee, no role
/// and no scope. That half-made state is carried here rather than smoothed over with defaults: the
/// screens draw it as half made instead of telling the administrator it is a manager.
/// </param>
public sealed record AdminDevice(
    string Id,
    string OfficeId,
    int DeviceNo,
    bool IsPhone,
    string Role,
    string EmployeeName,
    int EmployeeNo,
    string SyncScope,
    string Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? PairedAt,
    DateTimeOffset? LastSyncAt,
    bool SeedsHeld,
    DateTimeOffset? ExportedAt,
    bool HasAccount = true)
{
    /// <summary>Whether it is still allowed in.</summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Whether its account has been activated.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.Ordinal);

    /// <summary>Whether it is waiting for its account to be activated.</summary>
    public bool IsPending => string.Equals(Status, "pending", StringComparison.Ordinal);
}

/// <summary>Why a device could not be registered or changed.</summary>
public enum DeviceRefusal
{
    /// <summary>It could.</summary>
    None,

    /// <summary>No such office.</summary>
    OfficeNotFound,

    /// <summary>No such device.</summary>
    DeviceNotFound,

    /// <summary>The device number is outside one to nine.</summary>
    DeviceNoOutOfRange,

    /// <summary>Another device in this office already carries that number.</summary>
    DeviceNoTaken,

    /// <summary>The employee number is outside one to nine.</summary>
    EmployeeNoOutOfRange,

    /// <summary>Nobody was named.</summary>
    EmployeeNameRequired,

    /// <summary>The role is not one of the three.</summary>
    RoleInvalid,

    /// <summary>The sync scope is neither everything nor the custody register.</summary>
    ScopeInvalid,

    /// <summary>A phone is what the computer that paired it says it is; the tool only shows it.</summary>
    PhoneIsReadOnly,

    /// <summary>The device has already been shut out.</summary>
    AlreadyRevoked,

    /// <summary>Its setup file has already gone out, so it can only be shut out, never unmade.</summary>
    AlreadyExported,

    /// <summary>There is no organisation, so nothing can be certified.</summary>
    NoOrganisation,

    /// <summary>
    /// The tool could not write the device down. Nothing about what was typed was wrong: something
    /// on this computer stopped the writing, and the half-made device was taken back out again.
    /// </summary>
    CouldNotSave,
}

/// <summary>
/// A06 «المكاتب والأجهزة»: which computers sit in which office, who works at each of them, and how
/// much each keeps in step with.
/// </summary>
/// <remarks>
/// <para>
/// Registering a computer is also the moment it gets its identity: a key pair, sealed into the
/// tool, and a certificate signed by the organisation (A07's work, done here so the person filling
/// in a device's details never has to go and issue something afterwards for it to be real).
/// </para>
/// <para>
/// Phones are different in kind: a phone joins by being paired with the computer it belongs to, and
/// nothing about it is decided in this tool. They are listed with everything else and refused for
/// every kind of edit, so the register is complete without pretending the tool owns them.
/// </para>
/// </remarks>
public sealed class AdminDeviceService
{
    /// <summary>The smallest device or employee number.</summary>
    public const int MinNumber = 1;

    /// <summary>The largest: nine devices in an office and nine people at them (ARCHITECTURE.md §2).</summary>
    public const int MaxNumber = 9;

    private readonly AdminDb _db;
    private readonly AdminDeviceKeyService _keys;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPendingChanges _pending;
    private readonly TimeProvider _time;

    public AdminDeviceService(
        AdminDb db,
        AdminDeviceKeyService keys,
        AdminAuditService audit,
        AdminSession session,
        AdminPendingChanges pending,
        TimeProvider time)
    {
        _db = db;
        _keys = keys;
        _audit = audit;
        _session = session;
        _pending = pending;
        _time = time;
    }

    /// <summary>Every office, with where it sits in the structure and what is in it.</summary>
    public IReadOnlyList<AdminOffice> ListOffices()
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var offices = new List<AdminOffice>();
        using var command = _db.Command(
            """
            SELECT o.id, o.unit_id, o.name, o.office_code, o.key_version, o.activated_at,
                   (SELECT COUNT(*) FROM devices d WHERE d.office_id = o.id),
                   (SELECT COUNT(*) FROM devices d WHERE d.office_id = o.id AND d.revoked_at IS NULL),
                   (SELECT COUNT(*) FROM devices d WHERE d.office_id = o.id AND d.kind = 'phone'),
                   u.name, p1.name, p2.name, p3.name,
                   (u.disabled_at IS NOT NULL
                    OR p1.disabled_at IS NOT NULL
                    OR p2.disabled_at IS NOT NULL
                    OR p3.disabled_at IS NOT NULL)
            FROM offices o
            JOIN org_units u ON u.id = o.unit_id
            LEFT JOIN org_units p1 ON p1.id = u.parent_id
            LEFT JOIN org_units p2 ON p2.id = p1.parent_id
            LEFT JOIN org_units p3 ON p3.id = p2.parent_id
            ORDER BY o.office_code;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // The path reads from the organisation downwards, which is how a person says where a
            // room is; the joins walked upwards, so the names are reversed on the way out.
            var chain = new[] { Text(reader, 12), Text(reader, 11), Text(reader, 10), Text(reader, 9) }
                .Where(name => name.Length > 0)
                .ToArray();

            offices.Add(new AdminOffice(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                string.Join(" ← ", chain),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(4),
                reader.IsDBNull(5)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetBoolean(13)));
        }

        return offices;
    }

    /// <summary>One office, or null when there is no such office.</summary>
    public AdminOffice? ReadOffice(string officeId) =>
        ListOffices().FirstOrDefault(o => string.Equals(o.Id, officeId, StringComparison.Ordinal));

    /// <summary>The devices of one office, by number.</summary>
    public IReadOnlyList<AdminDevice> ListDevices(string officeId)
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var devices = new List<AdminDevice>();
        using var command = _db.Command(
            """
            SELECT d.id, d.office_id, d.device_no, d.kind,
                   a.role, a.employee_name, a.employee_no, a.sync_scope, a.status,
                   d.issued_at, d.revoked_at, d.paired_at, d.last_sync_at,
                   d.sealed_seeds IS NOT NULL,
                   (SELECT MAX(e.exported_at) FROM setup_exports e WHERE e.device_id = d.id),
                   a.device_id IS NOT NULL
            FROM devices d
            LEFT JOIN accounts a ON a.device_id = d.id
            WHERE d.office_id = $office
            ORDER BY d.kind DESC, d.device_no;
            """);
        command.Parameters.AddWithValue("$office", officeId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // No default for a device whose account row is missing: the screens are told there is
            // none, and say so, rather than being handed «مدير المكتب · الموظف 1».
            var hasAccount = reader.GetBoolean(15);

            devices.Add(new AdminDevice(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                string.Equals(reader.GetString(3), "phone", StringComparison.Ordinal),
                hasAccount ? reader.GetString(4) : string.Empty,
                hasAccount ? reader.GetString(5) : string.Empty,
                hasAccount ? reader.GetInt32(6) : 0,
                hasAccount ? reader.GetString(7) : string.Empty,
                hasAccount ? reader.GetString(8) : string.Empty,
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Instant(reader, 10),
                Instant(reader, 11),
                Instant(reader, 12),
                reader.GetBoolean(13),
                Instant(reader, 14),
                hasAccount));
        }

        return devices;
    }

    /// <summary>Every device in the organisation, for A07's list of accounts.</summary>
    public IReadOnlyList<AdminDevice> ListAllDevices() =>
        ListOffices().SelectMany(office => ListDevices(office.Id)).ToList();

    /// <summary>
    /// The next free device number in an office, or null when all nine are taken. The screen offers
    /// it so that filling the form in is one decision fewer.
    /// </summary>
    public int? NextDeviceNo(string officeId)
    {
        var taken = ListDevices(officeId).Select(d => d.DeviceNo).ToHashSet();
        for (var number = MinNumber; number <= MaxNumber; number++)
        {
            if (!taken.Contains(number))
            {
                return number;
            }
        }

        return null;
    }

    /// <summary>Registers a computer in an office and gives it its identity.</summary>
    public DeviceRefusal AddDevice(
        string officeId,
        int deviceNo,
        string role,
        string employeeName,
        int employeeNo,
        string syncScope,
        out string? deviceId)
    {
        deviceId = null;

        var office = ReadOffice(officeId);
        if (office is null)
        {
            return DeviceRefusal.OfficeNotFound;
        }

        if (Check(deviceNo, role, employeeName, employeeNo, syncScope) is var refusal and not DeviceRefusal.None)
        {
            return refusal;
        }

        if (ListDevices(officeId).Any(d => d.DeviceNo == deviceNo))
        {
            return DeviceRefusal.DeviceNoTaken;
        }

        switch (_keys.IssueDevice(officeId, deviceNo, employeeNo, role, out var newId))
        {
            case KeyRefusal.NoOrganisation:
                return DeviceRefusal.NoOrganisation;
            case KeyRefusal.OfficeNotFound:
                return DeviceRefusal.OfficeNotFound;
        }

        if (newId is null)
        {
            return DeviceRefusal.OfficeNotFound;
        }

        var now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        try
        {
            _db.Execute(
                """
                INSERT INTO accounts(id, device_id, employee_name, employee_no, role, sync_scope, status, created_at, updated_at)
                VALUES ($id, $device, $name, $no, $role, $scope, 'pending', $at, $at);
                """,
                ("$id", Guid.CreateVersion7().ToString()),
                ("$device", newId),
                ("$name", employeeName.Trim()),
                ("$no", employeeNo),
                ("$role", role),
                ("$scope", syncScope),
                ("$at", now));
        }
        catch (SqliteException)
        {
            // The device row is only half a device without its account; take it back out rather
            // than leaving a machine nobody sits at. The device number was already checked and
            // accepted above, so whatever went wrong here is not about it and must not be reported
            // as though it were.
            _db.Execute("DELETE FROM devices WHERE id = $id;", ("$id", newId));
            return DeviceRefusal.CouldNotSave;
        }

        deviceId = newId;
        Record(newId, "device_added", AdminAr.Devices.Log.Added(office.Name, deviceNo, employeeName.Trim()));
        return DeviceRefusal.None;
    }

    /// <summary>Changes who sits at a device, in what role, and how much it keeps in step with.</summary>
    public DeviceRefusal UpdateDevice(
        string deviceId,
        string role,
        string employeeName,
        int employeeNo,
        string syncScope)
    {
        var device = FindDevice(deviceId);
        if (device is null)
        {
            return DeviceRefusal.DeviceNotFound;
        }

        if (device.IsPhone)
        {
            return DeviceRefusal.PhoneIsReadOnly;
        }

        if (device.IsRevoked)
        {
            return DeviceRefusal.AlreadyRevoked;
        }

        if (Check(device.DeviceNo, role, employeeName, employeeNo, syncScope) is var refusal and not DeviceRefusal.None)
        {
            return refusal;
        }

        var now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        _db.Execute(
            """
            UPDATE accounts
            SET employee_name = $name, employee_no = $no, role = $role, sync_scope = $scope, updated_at = $at
            WHERE device_id = $device;
            """,
            ("$device", deviceId),
            ("$name", employeeName.Trim()),
            ("$no", employeeNo),
            ("$role", role),
            ("$scope", syncScope),
            ("$at", now));

        // The role and the employee number are written into the certificate, so a change to either
        // means the certificate has to say the new thing. The keys stay as they are: the device is
        // not a different device, it is the same one described correctly.
        if (!string.Equals(device.Role, role, StringComparison.Ordinal) || device.EmployeeNo != employeeNo)
        {
            _keys.ReCertify(deviceId, employeeNo, role);
        }

        var office = ReadOffice(device.OfficeId);
        Record(
            deviceId,
            "device_updated",
            AdminAr.Devices.Log.Updated(office?.Name ?? string.Empty, device.DeviceNo, employeeName.Trim()));
        return DeviceRefusal.None;
    }

    /// <summary>
    /// Takes a device out of the register for good. Only a device whose setup file has never been
    /// made may go: once a machine is out there, the answer is to shut it out, not to forget it.
    /// </summary>
    public DeviceRefusal RemoveDevice(string deviceId)
    {
        var device = FindDevice(deviceId);
        if (device is null)
        {
            return DeviceRefusal.DeviceNotFound;
        }

        if (device.IsPhone)
        {
            return DeviceRefusal.PhoneIsReadOnly;
        }

        if (device.ExportedAt is not null || !device.SeedsHeld)
        {
            return DeviceRefusal.AlreadyExported;
        }

        var office = ReadOffice(device.OfficeId);

        _db.Execute("DELETE FROM accounts WHERE device_id = $id;", ("$id", deviceId));
        _db.Execute("DELETE FROM devices WHERE id = $id;", ("$id", deviceId));
        _db.Execute(
            "DELETE FROM pending_changes WHERE entity_type = 'device' AND entity_id = $id AND distributed_at IS NULL;",
            ("$id", deviceId));

        _audit.Write(
            _session.AdminName,
            "device_removed",
            AdminAr.Devices.Log.Removed(office?.Name ?? string.Empty, device.DeviceNo),
            entityType: "device",
            entityId: deviceId);

        return DeviceRefusal.None;
    }

    /// <summary>
    /// Activates the account at a device: the person has their setup file and the tool now counts
    /// them as working. A07 does this for موظف العُهد, and for anybody else who has been handed a
    /// setup file.
    /// </summary>
    public DeviceRefusal ActivateAccount(string deviceId)
    {
        var device = FindDevice(deviceId);
        if (device is null)
        {
            return DeviceRefusal.DeviceNotFound;
        }

        if (device.IsRevoked)
        {
            return DeviceRefusal.AlreadyRevoked;
        }

        var now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        _db.Execute(
            "UPDATE accounts SET status = 'active', updated_at = $at WHERE device_id = $id;",
            ("$id", deviceId),
            ("$at", now));

        var office = ReadOffice(device.OfficeId);
        _audit.Write(
            _session.AdminName,
            "account_activated",
            AdminAr.Keys.Log.AccountActivated(device.EmployeeName, office?.Name ?? string.Empty),
            entityType: "device",
            entityId: deviceId);

        return DeviceRefusal.None;
    }

    /// <summary>One device anywhere in the organisation, or null.</summary>
    public AdminDevice? FindDevice(string deviceId) =>
        ListAllDevices().FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

    private static DeviceRefusal Check(int deviceNo, string role, string employeeName, int employeeNo, string syncScope)
    {
        if (deviceNo is < MinNumber or > MaxNumber)
        {
            return DeviceRefusal.DeviceNoOutOfRange;
        }

        if (!DeviceRoles.IsValid(role))
        {
            return DeviceRefusal.RoleInvalid;
        }

        if (string.IsNullOrWhiteSpace(employeeName))
        {
            return DeviceRefusal.EmployeeNameRequired;
        }

        if (employeeNo is < MinNumber or > MaxNumber)
        {
            return DeviceRefusal.EmployeeNoOutOfRange;
        }

        return SyncScopes.IsValid(syncScope) ? DeviceRefusal.None : DeviceRefusal.ScopeInvalid;
    }

    private static string Text(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? string.Empty : reader.GetString(column);

    private static DateTimeOffset? Instant(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column)
            ? null
            : DateTimeOffset.Parse(reader.GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private void Record(string deviceId, string action, string summary)
    {
        _audit.Write(_session.AdminName, action, summary, entityType: "device", entityId: deviceId);
        _pending.Add("device", deviceId, summary);
    }
}
