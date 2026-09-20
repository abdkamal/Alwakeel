using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Export;

/// <summary>Why a setup file could not be made.</summary>
public enum ExportRefusal
{
    /// <summary>It could.</summary>
    None,

    /// <summary>There is no organisation yet, so there is nothing to describe or to sign with.</summary>
    NoOrganisation,

    /// <summary>No such device.</summary>
    DeviceNotFound,

    /// <summary>The device has been shut out; it is never given a new file.</summary>
    DeviceRevoked,

    /// <summary>The device has no employee on it, so the file would not say who works there.</summary>
    NoAccount,

    /// <summary>The office has no key yet, and a file without one is of no use to anybody.</summary>
    NoOfficeKey,

    /// <summary>The structure is not in a state that can be written down.</summary>
    StructureIncomplete,

    /// <summary>The password offered is not one this product issues.</summary>
    PasswordInvalid,

    /// <summary>The file could not be written to the tool's own folder.</summary>
    CouldNotWrite,
}

/// <summary>Which of the four optional attachments the administrator chose to send.</summary>
/// <param name="Logo">The organisation's logo.</param>
/// <param name="Guide">The guide.</param>
/// <param name="ReportTemplate">The monthly report template.</param>
/// <param name="LetterTemplate">The official correspondence template.</param>
public sealed record SetupExportOptions(bool Logo, bool Guide, bool ReportTemplate, bool LetterTemplate)
{
    /// <summary>Everything the organisation actually has; what the wizard opens on.</summary>
    public static SetupExportOptions All { get; } = new(true, true, true, true);
}

/// <summary>What a setup file for one device would carry, as step two of the wizard lists it.</summary>
/// <param name="DeviceId">The device the file is for.</param>
/// <param name="OfficeId">Its office.</param>
/// <param name="OfficeUnitId">The structure node that office sits in.</param>
/// <param name="OfficeName">The office's name.</param>
/// <param name="OfficeCode">Its inventory code.</param>
/// <param name="DeviceNo">The device's number inside the office.</param>
/// <param name="EmployeeName">Who works at it.</param>
/// <param name="EmployeeNo">That person's number.</param>
/// <param name="Role">manager, secretary or custodian.</param>
/// <param name="Sequence">Which file for this device this would be; one for the first.</param>
/// <param name="SeedsHeld">Whether the device's key seed is still here, waiting for its first file.</param>
/// <param name="OfficeKeyVersion">Which issue of the office key travels; zero means there is none.</param>
/// <param name="Departments">How many departments the structure carries.</param>
/// <param name="Sections">How many sections.</param>
/// <param name="Units">How many units.</param>
/// <param name="CycleStartDay">The day the financial cycle starts on.</param>
/// <param name="NumberingFormat">The official-number format in force.</param>
/// <param name="LogoBytes">How big the logo is, or zero when there is none.</param>
/// <param name="ReportTemplateBytes">How big the report template is, or zero.</param>
/// <param name="LetterTemplateBytes">How big the letter template is.</param>
/// <param name="UsesBuiltInLetterTemplate">Whether that letter template is the one built into the tool.</param>
/// <param name="RevokedDevices">How many devices the signed revocation list names.</param>
public sealed record SetupExportPlan(
    string DeviceId,
    string OfficeId,
    string OfficeUnitId,
    string OfficeName,
    string OfficeCode,
    int DeviceNo,
    string EmployeeName,
    int EmployeeNo,
    string Role,
    long Sequence,
    bool SeedsHeld,
    int OfficeKeyVersion,
    int Departments,
    int Sections,
    int Units,
    int CycleStartDay,
    string NumberingFormat,
    long LogoBytes,
    long ReportTemplateBytes,
    long LetterTemplateBytes,
    bool UsesBuiltInLetterTemplate,
    int RevokedDevices)
{
    /// <summary>Whether the office has a key at all.</summary>
    public bool HasOfficeKey => OfficeKeyVersion > 0;

    /// <summary>Whether this would be the device's very first file.</summary>
    public bool IsFirstFile => Sequence == 1;

    /// <summary>Whether a logo can travel.</summary>
    public bool HasLogo => LogoBytes > 0;

    /// <summary>Whether a report template can travel.</summary>
    public bool HasReportTemplate => ReportTemplateBytes > 0;
}

/// <summary>A finished setup file.</summary>
/// <param name="FilePath">Where it was written.</param>
/// <param name="FileName">Its name.</param>
/// <param name="Sequence">Which file for this device it is.</param>
/// <param name="Includes">What it carries.</param>
/// <param name="KeysRenewed">
/// Whether the device was given a fresh key pair on the way out, because its previous seed had
/// already left the tool in an earlier file.
/// </param>
public sealed record SetupExportResult(
    string FilePath,
    string FileName,
    long Sequence,
    SetupIncludes Includes,
    bool KeysRenewed);

/// <summary>
/// A08 «تصدير ملف الإعداد»: turns everything the tool knows about one device into the single file
/// that lets that computer begin — and records that it went out.
/// </summary>
/// <remarks>
/// <para>
/// A device's private key seed lives in the tool only until its first file is made; it leaves inside
/// that file and is wiped here (AGREEMENT item 44). A second file for the same device therefore
/// cannot carry the same seed, because the tool no longer has it, so the device is given a fresh key
/// pair and a fresh certificate on the way out. That is not a workaround but the safer rule: making
/// a new file for a computer always makes every older file for it useless, so a setup file that went
/// astray cannot be used to stand up a second copy of that computer.
/// </para>
/// <para>
/// Nothing here writes a secret anywhere but into the encrypted payload of the file itself. The
/// package password is never stored, never logged and never kept beyond the screen that shows it;
/// the office key and the device seed are wiped from memory as soon as the file is closed.
/// </para>
/// </remarks>
public sealed class SetupExportService
{
    /// <summary>The extension every setup file carries.</summary>
    public static readonly string Extension = ContainerKinds.Extension(ContainerKind.Setup);

    private readonly AdminDb _db;
    private readonly AdminKeyService _org;
    private readonly AdminOrgService _identity;
    private readonly AdminStructureService _structure;
    private readonly AdminDeviceService _devices;
    private readonly AdminDeviceKeyService _keys;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPaths _paths;
    private readonly TimeProvider _time;

    public SetupExportService(
        AdminDb db,
        AdminKeyService org,
        AdminOrgService identity,
        AdminStructureService structure,
        AdminDeviceService devices,
        AdminDeviceKeyService keys,
        AdminAuditService audit,
        AdminSession session,
        AdminPaths paths,
        TimeProvider time)
    {
        _db = db;
        _org = org;
        _identity = identity;
        _structure = structure;
        _devices = devices;
        _keys = keys;
        _audit = audit;
        _session = session;
        _paths = paths;
        _time = time;
    }

    /// <summary>What a file for this device would carry, or null when there is no such device.</summary>
    public SetupExportPlan? Plan(string deviceId)
    {
        var device = _devices.FindDevice(deviceId);
        if (device is null)
        {
            return null;
        }

        var office = _devices.ReadOffice(device.OfficeId);
        var identity = _identity.Read();
        var nodes = _structure.ReadAll();
        var officeUnitId = _db.IsOpen
            ? _db.ScalarText("SELECT unit_id FROM offices WHERE id = $id;", ("$id", device.OfficeId)) ?? string.Empty
            : string.Empty;

        var letter = _identity.ReadLetterTemplate();
        var report = _identity.ReadReportTemplate();

        return new SetupExportPlan(
            device.Id,
            device.OfficeId,
            officeUnitId,
            office?.Name ?? string.Empty,
            office?.OfficeCode ?? string.Empty,
            device.DeviceNo,
            device.EmployeeName,
            device.EmployeeNo,
            device.Role,
            NextSequence(deviceId),
            device.SeedsHeld,
            office?.KeyVersion ?? 0,
            nodes.Count(node => node.Level == OrgLevel.Department),
            nodes.Count(node => node.Level == OrgLevel.Section),
            nodes.Count(node => node.Level == OrgLevel.Unit),
            identity?.CycleStartDay ?? 1,
            identity?.NumberingFormat ?? AdminKeyService.DefaultNumberingFormat,
            identity?.Logo?.Bytes.Length ?? 0,
            report?.Bytes.Length ?? 0,
            letter.Bytes.Length,
            identity?.UsesBuiltInLetterTemplate ?? true,
            RevokedDeviceCount());
    }

    /// <summary>The name the file will be offered under: office, device and day.</summary>
    public string FileNameFor(SetupExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var day = _time.GetUtcNow().ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"{Sanitize(plan.OfficeCode)}-{plan.DeviceNo.ToString(CultureInfo.InvariantCulture)}-{day}{Extension}";
    }

    /// <summary>
    /// Writes the file. Everything that can refuse does so before any key material is touched, and a
    /// write that fails puts the device's seed back where it was.
    /// </summary>
    /// <param name="deviceId">The device the file is for.</param>
    /// <param name="options">Which of the four attachments to include.</param>
    /// <param name="packagePassword">The sixteen characters the wizard showed.</param>
    /// <param name="result">The finished file.</param>
    public ExportRefusal Export(
        string deviceId,
        SetupExportOptions options,
        string packagePassword,
        out SetupExportResult? result)
    {
        ArgumentNullException.ThrowIfNull(options);
        result = null;

        if (!PackagePassword.IsWellFormed(packagePassword))
        {
            return ExportRefusal.PasswordInvalid;
        }

        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return ExportRefusal.NoOrganisation;
        }

        var device = _devices.FindDevice(deviceId);
        if (device is null)
        {
            return ExportRefusal.DeviceNotFound;
        }

        if (device.IsRevoked)
        {
            return ExportRefusal.DeviceRevoked;
        }

        if (!device.HasAccount)
        {
            return ExportRefusal.NoAccount;
        }

        var plan = Plan(deviceId);
        if (plan is null || string.IsNullOrEmpty(plan.OfficeUnitId) || string.IsNullOrEmpty(plan.OfficeCode))
        {
            return ExportRefusal.StructureIncomplete;
        }

        var units = ReadUnits();
        if (units is null || !units.Any(unit => string.Equals(unit.Id, plan.OfficeUnitId, StringComparison.Ordinal)))
        {
            return ExportRefusal.StructureIncomplete;
        }

        if (_keys.ReadOfficeKeyForExport(plan.OfficeId, out var officeKey) != KeyRefusal.None)
        {
            return ExportRefusal.NoOfficeKey;
        }

        try
        {
            return Write(org, plan, units, officeKey, options, packagePassword, out result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(officeKey);
        }
    }

    /// <summary>Every setup file that has ever gone out for this device, newest first.</summary>
    public IReadOnlyList<(long Sequence, DateTimeOffset At, string FileName)> HistoryOf(string deviceId)
    {
        if (!_db.IsOpen)
        {
            return [];
        }

        var rows = new List<(long, DateTimeOffset, string)>();
        using var command = _db.Command(
            """
            SELECT version, exported_at, file_name FROM setup_exports
            WHERE device_id = $id ORDER BY version DESC;
            """);
        command.Parameters.AddWithValue("$id", deviceId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2)));
        }

        return rows;
    }

    private ExportRefusal Write(
        AdminOrgInfo org,
        SetupExportPlan plan,
        IReadOnlyList<SetupUnit> units,
        byte[] officeKey,
        SetupExportOptions options,
        string packagePassword,
        out SetupExportResult? result)
    {
        result = null;

        // The seed has to be the private half of the keys the certificate names. When it is still
        // here that is already true; when it went out with an earlier file the device is given a new
        // pair, which is also what makes every older file for it stop working.
        var renewed = false;
        if (!_keys.SeedsStillHeld(plan.DeviceId))
        {
            if (_keys.ReIssueDevice(plan.DeviceId) != KeyRefusal.None)
            {
                return ExportRefusal.DeviceNotFound;
            }

            renewed = true;
        }

        if (_keys.TakeSeedsForExport(plan.DeviceId, out var seeds) != KeyRefusal.None || seeds is null)
        {
            return ExportRefusal.DeviceNotFound;
        }

        var certificate = _keys.ReadCertificate(plan.DeviceId);
        if (certificate is null)
        {
            _keys.ReturnSeeds(plan.DeviceId, seeds);
            return ExportRefusal.DeviceNotFound;
        }

        var now = _time.GetUtcNow();
        var guide = options.Guide ? AdminGuidePdf.Build(org.Name, plan.OfficeName, now) : null;
        var logo = options.Logo ? _identity.Read()?.Logo?.Bytes : null;
        var report = options.ReportTemplate ? _identity.ReadReportTemplate()?.Bytes : null;
        var letter = options.LetterTemplate ? _identity.ReadLetterTemplate().Bytes : null;

        var includes = new SetupIncludes(
            logo is { Length: > 0 },
            guide is { Length: > 0 },
            report is { Length: > 0 },
            letter is { Length: > 0 });

        var content = new SetupContent(
            SetupContent.CurrentFormatVersion,
            now,
            plan.Sequence,
            new SetupOrg(
                org.Id,
                org.Name,
                org.SigningPublicKeyText,
                org.AgreementPublicKeyText,
                plan.CycleStartDay,
                plan.NumberingFormat),
            units,
            new SetupOffice(plan.OfficeUnitId, plan.OfficeCode),
            new SetupDevice(
                plan.DeviceId,
                plan.DeviceNo,
                plan.Role,
                ScopeOf(plan.DeviceId),
                certificate),
            seeds,
            new SetupEmployee(plan.EmployeeName, plan.EmployeeNo, AdminAr.Devices.RoleName(plan.Role)),
            Base64Url.Encode(officeKey),
            RevocationForExport(),
            includes);

        var fileName = FileNameFor(plan);
        var path = Path.Combine(_paths.ExportsFolder, fileName);

        try
        {
            Directory.CreateDirectory(_paths.ExportsFolder);
            Directory.CreateDirectory(_paths.StagingFolder);
            path = FreePath(path);

            using (var orgIdentity = _org.OpenOrgIdentity())
            {
                SetupPackageWriter.Write(path, new SetupWriteRequest
                {
                    Content = content,
                    PackagePassword = packagePassword,
                    OrgIdentity = orgIdentity,
                    Logo = Source(logo),
                    Guide = Source(guide),
                    ReportTemplate = Source(report),
                    LetterTemplate = Source(letter),
                    StagingDirectory = _paths.StagingFolder,
                    Time = _time,
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptoException)
        {
            // Nothing was handed over, so nothing is lost: the seed goes back and the device is
            // exactly where it was before the attempt.
            _keys.ReturnSeeds(plan.DeviceId, seeds);
            TryDelete(path);
            return ExportRefusal.CouldNotWrite;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seeds.SigningSeed);
            CryptographicOperations.ZeroMemory(seeds.AgreementSeed);
        }

        fileName = Path.GetFileName(path);
        Record(plan, fileName, includes, now);

        result = new SetupExportResult(path, fileName, plan.Sequence, includes, renewed);
        return ExportRefusal.None;
    }

    /// <summary>The structure as a setup file writes it, or null when it has no root.</summary>
    private IReadOnlyList<SetupUnit>? ReadUnits()
    {
        var nodes = _structure.ReadAll();
        if (nodes.Count == 0 || !nodes.Any(node => node.Level == OrgLevel.Org))
        {
            return null;
        }

        return [.. nodes.Select(node => new SetupUnit(
            node.Id,
            node.ParentId,
            node.Level switch
            {
                OrgLevel.Org => 1,
                OrgLevel.Department => 2,
                OrgLevel.Section => 3,
                _ => 4,
            },
            node.Name,
            node.HeadTitle,
            node.HeadName,
            node.OfficeCode))];
    }

    /// <summary>The signed revocation list, or nothing at all while no device has ever been shut out.</summary>
    private RevocationList? RevocationForExport() =>
        RevokedDeviceCount() == 0 ? null : _keys.ReadSavedRevocationList() ?? _keys.BuildRevocationList();

    private int RevokedDeviceCount() =>
        _db.IsOpen ? (int)_db.Scalar("SELECT COUNT(*) FROM devices WHERE revoked_at IS NOT NULL;") : 0;

    private string ScopeOf(string deviceId) =>
        _db.ScalarText("SELECT sync_scope FROM accounts WHERE device_id = $id;", ("$id", deviceId))
        ?? SetupSyncScopes.Full;

    private long NextSequence(string deviceId) =>
        _db.IsOpen
            ? _db.Scalar(
                "SELECT COALESCE(MAX(version), 0) + 1 FROM setup_exports WHERE device_id = $id;",
                ("$id", deviceId))
            : 1;

    private void Record(SetupExportPlan plan, string fileName, SetupIncludes includes, DateTimeOffset now)
    {
        var at = now.ToString("O", CultureInfo.InvariantCulture);

        _db.Execute(
            """
            INSERT INTO setup_exports(id, device_id, version, exported_at, file_name, includes)
            VALUES ($id, $device, $version, $at, $name, $includes);
            """,
            ("$id", Guid.CreateVersion7().ToString()),
            ("$device", plan.DeviceId),
            ("$version", plan.Sequence),
            ("$at", at),
            ("$name", fileName),
            ("$includes", JsonSerializer.Serialize(includes)));

        // The office counts as activated from its first file onwards, which is what the dashboard's
        // «مكاتب بلا ملف إعداد» is counting.
        _db.Execute(
            "UPDATE offices SET activated_at = COALESCE(activated_at, $at), updated_at = $at WHERE id = $id;",
            ("$id", plan.OfficeId),
            ("$at", at));

        _audit.Write(
            _session.AdminName,
            "setup_exported",
            AdminAr.Export.Log(plan.OfficeName, plan.DeviceNo, fileName),
            entityType: "device",
            entityId: plan.DeviceId,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["office_code"] = plan.OfficeCode,
                ["device_no"] = plan.DeviceNo.ToString(CultureInfo.InvariantCulture),
                ["file_name"] = fileName,
                ["export_version"] = plan.Sequence.ToString(CultureInfo.InvariantCulture),
            });
    }

    private static Func<Stream>? Source(byte[]? bytes) =>
        bytes is { Length: > 0 } ? () => new MemoryStream(bytes, writable: false) : null;

    /// <summary>A name that is safe on this file system, with nothing of the office code lost silently.</summary>
    private static string Sanitize(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. text.Select(character => invalid.Contains(character) ? '-' : character)]).Trim();
        return cleaned.Length == 0 ? "office" : cleaned;
    }

    /// <summary>The same name with a number after it when a file of that name is already there.</summary>
    private static string FreePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(folder, $"{name}-{index.ToString(CultureInfo.InvariantCulture)}{Extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A half written file left behind is untidy, not dangerous: it carries no readable
            // secret, and the next export writes beside it rather than over it.
        }
    }
}
