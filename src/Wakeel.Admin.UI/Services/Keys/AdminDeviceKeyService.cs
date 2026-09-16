using System.Globalization;
using System.Security.Cryptography;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Keys;

/// <summary>Why a key or certificate action was refused.</summary>
public enum KeyRefusal
{
    /// <summary>It was not.</summary>
    None,

    /// <summary>There is no organisation, so nothing can be signed.</summary>
    NoOrganisation,

    /// <summary>No such office.</summary>
    OfficeNotFound,

    /// <summary>No such device.</summary>
    DeviceNotFound,

    /// <summary>The device has already been revoked; nothing more happens to it.</summary>
    AlreadyRevoked,

    /// <summary>The office has no key yet, so nothing can be wrapped for a device in it.</summary>
    NoOfficeKey,

    /// <summary>The seeds have already left the tool in a setup file and are gone from here.</summary>
    SeedsAlreadyExported,

    /// <summary>No such account.</summary>
    AccountNotFound,
}

/// <summary>What the tool knows about one office key.</summary>
/// <param name="OfficeId">The office it belongs to.</param>
/// <param name="Version">How many times it has been issued; zero means never.</param>
/// <param name="RotatedAt">When it was last issued or rotated.</param>
public sealed record OfficeKeyInfo(string OfficeId, int Version, DateTimeOffset? RotatedAt)
{
    /// <summary>Whether the office has a key at all.</summary>
    public bool Exists => Version > 0;
}

/// <summary>
/// A07 «الحسابات والمفاتيح»: everything about a device's identity — issuing its certificate,
/// re-issuing it when an account has to be recovered, the office key it shares with its neighbours,
/// and the signed list of the devices that are no longer allowed in (AGREEMENT item 24).
/// </summary>
/// <remarks>
/// <para>
/// A device gets its own key pair here, and only here. The private seeds are sealed with the
/// database key the moment they are made and never held in memory longer than the call that made
/// them; they leave the tool exactly once, inside the first setup file for that device, and are
/// wiped from the database on the way out (<see cref="TakeSeedsForExport"/>). After that the tool
/// holds the device's public keys and its certificate, and nothing that could impersonate it.
/// </para>
/// <para>
/// The office key is the key the computers of one office share. It is made here, sealed here, and
/// travels to each device wrapped to that device's own public key, so a setup file read on the
/// wrong machine yields nothing.
/// </para>
/// <para>
/// Revoking is the one action with an effect outside the tool: it produces a fresh revocation list
/// signed by the organisation, which every later setup file carries. The list holds device
/// identifiers and instants, nothing else, so it is written out as it stands.
/// </para>
/// </remarks>
public sealed class AdminDeviceKeyService
{
    /// <summary>How long an office key is.</summary>
    public const int OfficeKeySize = 32;

    /// <summary>The label the office key wrap for a device is sealed under.</summary>
    public const string OfficeKeyWrapContext = "wakeel.setup.office-key";

    /// <summary>The name of the file the signed revocation list is kept in.</summary>
    public const string RevocationFileName = "revocations.json";

    private readonly AdminDb _db;
    private readonly AdminKeyService _org;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPendingChanges _pending;
    private readonly AdminPaths _paths;
    private readonly TimeProvider _time;

    public AdminDeviceKeyService(
        AdminDb db,
        AdminKeyService org,
        AdminAuditService audit,
        AdminSession session,
        AdminPendingChanges pending,
        AdminPaths paths,
        TimeProvider time)
    {
        _db = db;
        _org = org;
        _audit = audit;
        _session = session;
        _pending = pending;
        _paths = paths;
        _time = time;
    }

    /// <summary>
    /// Makes a device's key pair and issues its certificate under the organisation key. The seeds
    /// are sealed into the row; nothing readable leaves this method.
    /// </summary>
    /// <param name="officeId">The office the device sits in.</param>
    /// <param name="deviceNo">Its number inside that office, 1 to 9.</param>
    /// <param name="employeeNo">The employee number written into the certificate.</param>
    /// <param name="role">manager, secretary or custodian.</param>
    /// <param name="deviceId">The new device's identifier.</param>
    public KeyRefusal IssueDevice(
        string officeId,
        int deviceNo,
        int employeeNo,
        string role,
        out string? deviceId)
    {
        deviceId = null;

        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return KeyRefusal.NoOrganisation;
        }

        if (_db.Scalar("SELECT COUNT(*) FROM offices WHERE id = $id;", ("$id", officeId)) == 0)
        {
            return KeyRefusal.OfficeNotFound;
        }

        var now = _time.GetUtcNow();
        var id = Guid.CreateVersion7().ToString();

        using var identity = DeviceIdentity.Generate();
        using var orgIdentity = _org.OpenOrgIdentity();

        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                org.Id,
                officeId,
                id,
                deviceNo,
                employeeNo,
                role,
                DeviceKind.Pc,
                identity.SigningPublicKeyText,
                identity.AgreementPublicKeyText,
                now,
                org.Id),
            orgIdentity);

        var sealedSeeds = SealSeeds(identity);

        _db.Execute(
            """
            INSERT INTO devices(
                id, office_id, device_no, kind, ed25519_pub, x25519_pub, certificate, sealed_seeds,
                issued_at, created_at, updated_at)
            VALUES ($id, $office, $no, 'pc', $ed, $x, $certificate, $seeds, $at, $at, $at);
            """,
            ("$id", id),
            ("$office", officeId),
            ("$no", deviceNo),
            ("$ed", identity.SigningPublicKeyText),
            ("$x", identity.AgreementPublicKeyText),
            ("$certificate", CanonicalJson.Serialize(certificate)),
            ("$seeds", sealedSeeds),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        deviceId = id;
        return KeyRefusal.None;
    }

    /// <summary>
    /// Re-issues a device's certificate on a brand new key pair: the account was lost with the
    /// computer, and whatever is on the old machine must stop working. The old keys are simply
    /// replaced, and the setup file that carries the new ones has to be made again.
    /// </summary>
    public KeyRefusal ReIssueDevice(string deviceId)
    {
        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return KeyRefusal.NoOrganisation;
        }

        var row = ReadDeviceRow(deviceId);
        if (row is null)
        {
            return KeyRefusal.DeviceNotFound;
        }

        if (row.Value.RevokedAt is not null)
        {
            return KeyRefusal.AlreadyRevoked;
        }

        var now = _time.GetUtcNow();
        using var identity = DeviceIdentity.Generate();
        using var orgIdentity = _org.OpenOrgIdentity();

        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                org.Id,
                row.Value.OfficeId,
                deviceId,
                row.Value.DeviceNo,
                row.Value.EmployeeNo,
                row.Value.Role,
                DeviceKind.Pc,
                identity.SigningPublicKeyText,
                identity.AgreementPublicKeyText,
                now,
                org.Id),
            orgIdentity);

        _db.Execute(
            """
            UPDATE devices
            SET ed25519_pub = $ed, x25519_pub = $x, certificate = $certificate, sealed_seeds = $seeds,
                seeds_exported_at = NULL, issued_at = $at, updated_at = $at
            WHERE id = $id;
            """,
            ("$id", deviceId),
            ("$ed", identity.SigningPublicKeyText),
            ("$x", identity.AgreementPublicKeyText),
            ("$certificate", CanonicalJson.Serialize(certificate)),
            ("$seeds", SealSeeds(identity)),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        Record(deviceId, "device_reissued", AdminAr.Keys.Log.ReIssued(row.Value.OfficeName, row.Value.DeviceNo));
        return KeyRefusal.None;
    }

    /// <summary>
    /// Issues the certificate again on the keys the device already has, because what it says about
    /// the device has changed — a different role, or a different employee number. The device keeps
    /// working with what it has until the new setup file reaches it; nothing is invalidated, only
    /// restated.
    /// </summary>
    public KeyRefusal ReCertify(string deviceId, int employeeNo, string role)
    {
        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return KeyRefusal.NoOrganisation;
        }

        var row = ReadDeviceRow(deviceId);
        if (row is null)
        {
            return KeyRefusal.DeviceNotFound;
        }

        if (row.Value.RevokedAt is not null)
        {
            return KeyRefusal.AlreadyRevoked;
        }

        var signingPublicKey = _db.ScalarText("SELECT ed25519_pub FROM devices WHERE id = $id;", ("$id", deviceId));
        if (signingPublicKey is null)
        {
            return KeyRefusal.DeviceNotFound;
        }

        var now = _time.GetUtcNow();
        using var orgIdentity = _org.OpenOrgIdentity();

        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                org.Id,
                row.Value.OfficeId,
                deviceId,
                row.Value.DeviceNo,
                employeeNo,
                role,
                DeviceKind.Pc,
                signingPublicKey,
                row.Value.AgreementPublicKey,
                now,
                org.Id),
            orgIdentity);

        _db.Execute(
            "UPDATE devices SET certificate = $certificate, issued_at = $at, updated_at = $at WHERE id = $id;",
            ("$id", deviceId),
            ("$certificate", CanonicalJson.Serialize(certificate)),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        return KeyRefusal.None;
    }

    /// <summary>The certificate as it was issued, or null for a device that has none.</summary>
    public DeviceCertificate? ReadCertificate(string deviceId)
    {
        var text = _db.IsOpen
            ? _db.ScalarText("SELECT certificate FROM devices WHERE id = $id;", ("$id", deviceId))
            : null;
        return text is null ? null : CanonicalJson.Deserialize<DeviceCertificate>(text);
    }

    /// <summary>
    /// Hands the device's key seeds over to whoever is writing its setup file, and wipes them from
    /// the tool in the same breath. They may be taken exactly once: after that the tool cannot
    /// impersonate the device, and a lost setup file means re-issuing rather than re-exporting.
    /// </summary>
    public KeyRefusal TakeSeedsForExport(string deviceId, out DeviceSeeds? seeds)
    {
        seeds = null;

        if (!_db.IsOpen)
        {
            return KeyRefusal.DeviceNotFound;
        }

        using var command = _db.Command(
            "SELECT sealed_seeds, seeds_exported_at FROM devices WHERE id = $id;");
        command.Parameters.AddWithValue("$id", deviceId);

        byte[]? sealedSeeds;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return KeyRefusal.DeviceNotFound;
            }

            if (reader.IsDBNull(0))
            {
                return KeyRefusal.SeedsAlreadyExported;
            }

            sealedSeeds = (byte[])reader.GetValue(0);
        }

        var plain = _db.Unseal(sealedSeeds);
        try
        {
            seeds = CanonicalJson.Deserialize<DeviceSeeds>(plain);
        }
        finally
        {
            Array.Clear(plain);
        }

        _db.Execute(
            "UPDATE devices SET sealed_seeds = NULL, seeds_exported_at = $at, updated_at = $at WHERE id = $id;",
            ("$id", deviceId),
            ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));

        return KeyRefusal.None;
    }

    /// <summary>Whether the device's seeds are still in the tool, waiting for its first setup file.</summary>
    public bool SeedsStillHeld(string deviceId) =>
        _db.IsOpen
        && _db.Scalar(
            "SELECT COUNT(*) FROM devices WHERE id = $id AND sealed_seeds IS NOT NULL;",
            ("$id", deviceId)) > 0;

    /// <summary>What the tool knows about an office's key.</summary>
    public OfficeKeyInfo? ReadOfficeKey(string officeId)
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        using var command = _db.Command(
            "SELECT key_version, key_rotated_at FROM offices WHERE id = $id;");
        command.Parameters.AddWithValue("$id", officeId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OfficeKeyInfo(
            officeId,
            reader.GetInt32(0),
            reader.IsDBNull(1)
                ? null
                : DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// Issues an office key, or rotates the one that is there. Either way every computer in that
    /// office needs a new setup file before it can read anything the others write, which is why the
    /// screen says so before this is called.
    /// </summary>
    public KeyRefusal IssueOrRotateOfficeKey(string officeId, out int version)
    {
        version = 0;

        var current = ReadOfficeKey(officeId);
        if (current is null)
        {
            return KeyRefusal.OfficeNotFound;
        }

        var key = RandomBytes.Next(OfficeKeySize);
        byte[] sealedKey;
        try
        {
            sealedKey = _db.Seal(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        version = current.Version + 1;
        var now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        _db.Execute(
            """
            UPDATE offices
            SET sealed_key = $key, key_version = $version, key_rotated_at = $at, updated_at = $at
            WHERE id = $id;
            """,
            ("$id", officeId),
            ("$key", sealedKey),
            ("$version", version),
            ("$at", now));

        var officeName = _db.ScalarText("SELECT name FROM offices WHERE id = $id;", ("$id", officeId)) ?? string.Empty;
        var summary = current.Exists
            ? AdminAr.Keys.Log.OfficeKeyRotated(officeName, version)
            : AdminAr.Keys.Log.OfficeKeyIssued(officeName);

        _audit.Write(
            _session.AdminName,
            current.Exists ? "office_key_rotated" : "office_key_issued",
            summary,
            entityType: "office",
            entityId: officeId,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key_version"] = version.ToString(CultureInfo.InvariantCulture),
            });
        _pending.Add("office", officeId, summary);

        return KeyRefusal.None;
    }

    /// <summary>
    /// The office key wrapped to one device's own public key — the «غلاف» a setup file carries.
    /// Nothing readable comes out of here: the key is sealed to the device before it is returned.
    /// </summary>
    public KeyRefusal WrapOfficeKeyFor(string deviceId, out byte[] wrap)
    {
        wrap = [];

        var row = ReadDeviceRow(deviceId);
        if (row is null)
        {
            return KeyRefusal.DeviceNotFound;
        }

        using var command = _db.Command("SELECT sealed_key FROM offices WHERE id = $id;");
        command.Parameters.AddWithValue("$id", row.Value.OfficeId);
        if (command.ExecuteScalar() is not byte[] sealedKey || sealedKey.Length == 0)
        {
            return KeyRefusal.NoOfficeKey;
        }

        var key = _db.Unseal(sealedKey);
        try
        {
            wrap = DeviceIdentity.SealFor(Base64Url.Decode(row.Value.AgreementPublicKey), key, OfficeKeyWrapContext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return KeyRefusal.None;
    }

    /// <summary>
    /// Shuts a specific device out (AGREEMENT item 24) and writes a fresh signed revocation list.
    /// The device's row stays where it is: the office needs to be able to see that the machine in
    /// the corner is the one that was shut out.
    /// </summary>
    public KeyRefusal Revoke(string deviceId, out RevocationList? list)
    {
        list = null;

        var row = ReadDeviceRow(deviceId);
        if (row is null)
        {
            return KeyRefusal.DeviceNotFound;
        }

        if (row.Value.RevokedAt is not null)
        {
            return KeyRefusal.AlreadyRevoked;
        }

        var now = _time.GetUtcNow();
        _db.Execute(
            """
            UPDATE devices SET revoked_at = $at, sealed_seeds = NULL, updated_at = $at WHERE id = $id;
            """,
            ("$id", deviceId),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        _db.Execute(
            "UPDATE accounts SET status = 'revoked', updated_at = $at WHERE device_id = $id;",
            ("$id", deviceId),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        list = BuildRevocationList();
        if (list is not null)
        {
            SaveRevocationList(list);
        }

        var summary = AdminAr.Keys.Log.Revoked(row.Value.OfficeName, row.Value.DeviceNo);
        _audit.Write(
            _session.AdminName,
            "device_revoked",
            summary,
            entityType: "device",
            entityId: deviceId,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["office_code"] = row.Value.OfficeCode,
                ["device_no"] = row.Value.DeviceNo.ToString(CultureInfo.InvariantCulture),
            });
        _pending.Add("device", deviceId, summary);

        return KeyRefusal.None;
    }

    /// <summary>
    /// The signed list of every revoked device as it stands, or null before there is an
    /// organisation to sign it.
    /// </summary>
    public RevocationList? BuildRevocationList()
    {
        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return null;
        }

        var entries = new List<RevocationEntry>();
        using (var command = _db.Command(
                   "SELECT id, revoked_at FROM devices WHERE revoked_at IS NOT NULL ORDER BY revoked_at, id;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                entries.Add(new RevocationEntry(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }
        }

        using var orgIdentity = _org.OpenOrgIdentity();
        return RevocationList.Issue(new RevocationListBody(org.Id, _time.GetUtcNow(), entries), orgIdentity);
    }

    /// <summary>The list as it was last written out, or null when none has been.</summary>
    public RevocationList? ReadSavedRevocationList()
    {
        var file = Path.Combine(_paths.KeysFolder, RevocationFileName);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return CanonicalJson.Deserialize<RevocationList>(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptoException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the signed list beside the tool's keys. It holds identifiers and instants and no
    /// secret at all, so it is stored as it stands and every setup file made afterwards carries it.
    /// </summary>
    private void SaveRevocationList(RevocationList list)
    {
        try
        {
            Directory.CreateDirectory(_paths.KeysFolder);
            File.WriteAllText(Path.Combine(_paths.KeysFolder, RevocationFileName), CanonicalJson.Serialize(list));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The device is shut out in the database either way; the file is only the copy the next
            // setup file is built from, and it is rebuilt from the table whenever it is needed.
        }
    }

    private byte[] SealSeeds(DeviceIdentity identity)
    {
        var seeds = identity.Export();
        var plain = CanonicalJson.SerializeToUtf8Bytes(seeds);
        try
        {
            return _db.Seal(plain);
        }
        finally
        {
            Array.Clear(plain);
            Array.Clear(seeds.SigningSeed);
            Array.Clear(seeds.AgreementSeed);
        }
    }

    private void Record(string deviceId, string action, string summary)
    {
        _audit.Write(_session.AdminName, action, summary, entityType: "device", entityId: deviceId);
        _pending.Add("device", deviceId, summary);
    }

    private readonly record struct DeviceRow(
        string Id,
        string OfficeId,
        string OfficeName,
        string OfficeCode,
        int DeviceNo,
        int EmployeeNo,
        string Role,
        string AgreementPublicKey,
        DateTimeOffset? RevokedAt);

    private DeviceRow? ReadDeviceRow(string deviceId)
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        using var command = _db.Command(
            """
            SELECT d.id, d.office_id, o.name, o.office_code, d.device_no,
                   COALESCE(a.employee_no, 1), COALESCE(a.role, 'manager'), d.x25519_pub, d.revoked_at
            FROM devices d
            JOIN offices o ON o.id = d.office_id
            LEFT JOIN accounts a ON a.device_id = d.id
            WHERE d.id = $id;
            """);
        command.Parameters.AddWithValue("$id", deviceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DeviceRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8)
                ? null
                : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
