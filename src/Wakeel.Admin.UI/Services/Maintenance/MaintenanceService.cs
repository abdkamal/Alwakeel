using System.Globalization;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Maintenance;

/// <summary>Why a maintenance action could not be carried out.</summary>
public enum MaintenanceRefusal
{
    /// <summary>It could.</summary>
    None,

    /// <summary>There is no organisation, so nothing can be verified against anything.</summary>
    NoOrganisation,

    /// <summary>There is no such file or folder.</summary>
    NotFound,

    /// <summary>The file is not one of the product's own.</summary>
    NotOurs,

    /// <summary>The file is damaged, or was cut short while it was being copied.</summary>
    Unreadable,

    /// <summary>The file belongs to a different organisation, so this tool cannot open it.</summary>
    OtherOrganisation,

    /// <summary>No such device.</summary>
    DeviceNotFound,

    /// <summary>The recovery file could not be written.</summary>
    CouldNotWrite,
}

/// <summary>One line of what was found, as A09 lists it.</summary>
/// <param name="Label">What was looked at.</param>
/// <param name="Value">What was found.</param>
/// <param name="Ok">Whether it held up; false draws the line as a problem.</param>
public sealed record MaintenanceFinding(string Label, string Value, bool Ok = true);

/// <summary>One thing inside an opened file.</summary>
/// <param name="Name">What it is called inside the file.</param>
/// <param name="Size">How big it is, in bytes.</param>
public sealed record MaintenanceItem(string Name, long Size);

/// <summary>What opening one file or one folder found.</summary>
/// <param name="Title">The headline: what was opened.</param>
/// <param name="Subtitle">Where it came from.</param>
/// <param name="Findings">Every check that was made, in the order it was made.</param>
/// <param name="Items">What the file carries, when it carries separate things.</param>
/// <param name="Ok">Whether everything held up.</param>
public sealed record MaintenanceReport(
    string Title,
    string Subtitle,
    IReadOnlyList<MaintenanceFinding> Findings,
    IReadOnlyList<MaintenanceItem> Items,
    bool Ok);

/// <summary>
/// A09 «الصيانة»: the three things that can only be done from the organisation's own keys — reading a
/// copy of a الوكيل installation, opening a correspondence whose office key nobody here has, and
/// giving a person their account back.
/// </summary>
/// <remarks>
/// <para>
/// Every one of the product's own files carries a copy of its content key sealed to the organisation
/// (<see cref="ContainerKeySource.ForAdmin"/>), which is what makes maintenance possible at all: the
/// administrator never needs a person's password or their computer. That is also why every action
/// here is written to the operations log the moment it happens — a tool that can open anything must
/// leave a record of everything it opened.
/// </para>
/// <para>
/// Nothing here changes a file it opens. Reading is reading; the one action that writes is recovering
/// an account, and that writes a new setup file rather than touching the installation.
/// </para>
/// </remarks>
public sealed class MaintenanceService
{
    /// <summary>Entries larger than this are listed but not opened, so a backup is never unpacked here.</summary>
    private const long MaxReadableEntry = 512 * 1024;

    private readonly AdminKeyService _org;
    private readonly AdminDeviceService _devices;
    private readonly AdminDeviceKeyService _keys;
    private readonly SetupExportService _export;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminPaths _paths;
    private readonly TimeProvider _time;

    public MaintenanceService(
        AdminKeyService org,
        AdminDeviceService devices,
        AdminDeviceKeyService keys,
        SetupExportService export,
        AdminAuditService audit,
        AdminSession session,
        AdminPaths paths,
        TimeProvider time)
    {
        _org = org;
        _devices = devices;
        _keys = keys;
        _export = export;
        _audit = audit;
        _session = session;
        _paths = paths;
        _time = time;
    }

    /// <summary>The kinds of file A09 can be pointed at.</summary>
    public static IReadOnlyList<string> OpenableExtensions { get; } =
    [
        ContainerKinds.Extension(ContainerKind.Backup),
        ContainerKinds.Extension(ContainerKind.Msg),
    ];

    /// <summary>
    /// Opens one of the product's own files with the organisation key and says what is in it. The
    /// payload is never unpacked to disk: the manifest is checked, the administrator's copy of the
    /// content key is opened on the smallest item to prove it works, and the rest is only listed.
    /// </summary>
    public MaintenanceRefusal OpenFile(string path, out MaintenanceReport? report)
    {
        report = null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return MaintenanceRefusal.NotFound;
        }

        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return MaintenanceRefusal.NoOrganisation;
        }

        ContainerKind kind;
        try
        {
            kind = ContainerKinds.FromPath(path);
        }
        catch (CryptoException)
        {
            return MaintenanceRefusal.NotOurs;
        }

        var signingKey = Base64Url.Decode(org.SigningPublicKeyText);

        try
        {
            Directory.CreateDirectory(_paths.StagingFolder);
            using var reader = ContainerReader.Open(path, new ContainerOpenOptions
            {
                ExpectedKind = kind,
                OrgSigningPublicKey = signingKey,
                Revocations = _keys.ReadSavedRevocationList(),
                StagingDirectory = _paths.StagingFolder,
                Time = _time,
            });

            var manifest = reader.Manifest;
            if (!string.Equals(manifest.Producer.Body.OrgId, org.Id, StringComparison.Ordinal))
            {
                return MaintenanceRefusal.OtherOrganisation;
            }

            var findings = new List<MaintenanceFinding>
            {
                new(AdminAr.Maintenance.CheckKind, AdminAr.Maintenance.KindName(kind)),
                new(AdminAr.Maintenance.CheckSignature, AdminAr.Maintenance.SignatureOk),
                new(AdminAr.Maintenance.CheckOrganisation, org.Name),
                new(AdminAr.Maintenance.CheckProducer, ProducerLine(manifest.Producer)),
                new(AdminAr.Maintenance.CheckCreated, AdminAr.Maintenance.Day(manifest.CreatedAt)),
            };

            var items = manifest.Entries
                .Select(entry => new MaintenanceItem(entry.Name, entry.Size))
                .ToList();

            findings.Add(ReadOneItem(reader, manifest, org));

            report = new MaintenanceReport(
                AdminAr.Maintenance.KindName(kind),
                Path.GetFileName(path),
                findings,
                items,
                findings.TrueForAll(finding => finding.Ok));

            Log("maintenance_file_opened", AdminAr.Maintenance.Log.Opened(Path.GetFileName(path)));
            return MaintenanceRefusal.None;
        }
        catch (CryptoException)
        {
            return MaintenanceRefusal.Unreadable;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MaintenanceRefusal.Unreadable;
        }
    }

    /// <summary>
    /// Looks over a copy of a الوكيل installation's folder: which of the product's own files are in
    /// it, and whether each one still verifies against the organisation key.
    /// </summary>
    public MaintenanceRefusal OpenFolder(string path, out MaintenanceReport? report)
    {
        report = null;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return MaintenanceRefusal.NotFound;
        }

        var org = _org.ReadOrganisation();
        if (org is null)
        {
            return MaintenanceRefusal.NoOrganisation;
        }

        List<string> files;
        try
        {
            files = [.. Directory
                .EnumerateFiles(path, "*.wakeel-*", SearchOption.AllDirectories)
                .Take(500)
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MaintenanceRefusal.Unreadable;
        }

        var items = new List<MaintenanceItem>();
        var sound = 0;
        var damaged = 0;

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            items.Add(new MaintenanceItem(Path.GetFileName(file), info.Exists ? info.Length : 0));
            if (Verifies(file, org))
            {
                sound++;
            }
            else
            {
                damaged++;
            }
        }

        var findings = new List<MaintenanceFinding>
        {
            new(AdminAr.Maintenance.CheckOrganisation, org.Name),
            new(AdminAr.Maintenance.CheckFileCount, AdminAr.Maintenance.FileCount(files.Count)),
            new(AdminAr.Maintenance.CheckSound, AdminAr.Maintenance.FileCount(sound)),
            new(AdminAr.Maintenance.CheckDamaged, AdminAr.Maintenance.FileCount(damaged), damaged == 0),
        };

        report = new MaintenanceReport(
            AdminAr.Maintenance.FolderTitle,
            path,
            findings,
            items,
            damaged == 0);

        Log("maintenance_folder_opened", AdminAr.Maintenance.Log.FolderOpened(files.Count));
        return MaintenanceRefusal.None;
    }

    /// <summary>
    /// Gives a person their account back: the computer is issued a fresh identity and a recovery
    /// setup file is written for it. Whoever receives it goes through the first run again and
    /// chooses a new password there, which is what makes the new password wrap; nothing of the old
    /// password is ever known here.
    /// </summary>
    /// <param name="deviceId">The device whose account is being recovered.</param>
    /// <param name="packagePassword">The password of the recovery file, shown once by the screen.</param>
    /// <param name="result">The file that was written.</param>
    public MaintenanceRefusal RecoverAccount(
        string deviceId,
        string packagePassword,
        out SetupExportResult? result)
    {
        result = null;

        var device = _devices.FindDevice(deviceId);
        if (device is null)
        {
            return MaintenanceRefusal.DeviceNotFound;
        }

        if (device.IsRevoked || !device.HasAccount)
        {
            return MaintenanceRefusal.DeviceNotFound;
        }

        if (_keys.ReIssueDevice(deviceId) != KeyRefusal.None)
        {
            return MaintenanceRefusal.DeviceNotFound;
        }

        var refusal = _export.Export(deviceId, SetupExportOptions.All, packagePassword, out result);
        if (refusal != ExportRefusal.None || result is null)
        {
            return refusal == ExportRefusal.CouldNotWrite
                ? MaintenanceRefusal.CouldNotWrite
                : MaintenanceRefusal.DeviceNotFound;
        }

        var office = _devices.ReadOffice(device.OfficeId);
        Log(
            "account_recovered",
            AdminAr.Maintenance.Log.Recovered(office?.Name ?? string.Empty, device.DeviceNo),
            entityId: deviceId);

        return MaintenanceRefusal.None;
    }

    /// <summary>
    /// Opens the smallest thing in the file with the organisation's own copy of its key. One item is
    /// enough to prove the key works, and stopping there is what keeps a backup of many gigabytes
    /// from being unpacked onto this computer merely to be looked at.
    /// </summary>
    private MaintenanceFinding ReadOneItem(ContainerReader reader, ContainerManifest manifest, AdminOrgInfo org)
    {
        var smallest = manifest.Entries
            .Where(entry => entry.Size <= MaxReadableEntry)
            .MinBy(entry => entry.Size);

        if (smallest is null)
        {
            return new MaintenanceFinding(AdminAr.Maintenance.CheckContent, AdminAr.Maintenance.ContentTooBig);
        }

        try
        {
            using var identity = _org.OpenOrgIdentity();
            var bytes = reader.ReadEntry(smallest.Name, ContainerKeySource.ForAdmin(identity));
            return new MaintenanceFinding(
                AdminAr.Maintenance.CheckContent,
                AdminAr.Maintenance.ContentOk(bytes.Length));
        }
        catch (CryptoException)
        {
            return new MaintenanceFinding(
                AdminAr.Maintenance.CheckContent,
                AdminAr.Maintenance.ContentUnreadable,
                Ok: false);
        }
        catch (InvalidOperationException)
        {
            // No organisation keys to open it with; said plainly rather than as a failure of the file.
            return new MaintenanceFinding(
                AdminAr.Maintenance.CheckContent,
                AdminAr.Maintenance.ContentUnreadable,
                Ok: false);
        }
    }

    private bool Verifies(string file, AdminOrgInfo org)
    {
        try
        {
            using var reader = ContainerReader.Open(file, new ContainerOpenOptions
            {
                ExpectedKind = ContainerKinds.FromPath(file),
                OrgSigningPublicKey = Base64Url.Decode(org.SigningPublicKeyText),
                StagingDirectory = _paths.StagingFolder,
                Time = _time,
            });

            return string.Equals(reader.Manifest.Producer.Body.OrgId, org.Id, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is CryptoException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ProducerLine(DeviceCertificate producer) =>
        AdminAr.Maintenance.Producer(producer.Body.DeviceNo, AdminAr.Devices.RoleName(producer.Body.Role));

    private void Log(string action, string summary, string? entityId = null) =>
        _audit.Write(
            _session.AdminName,
            action,
            summary,
            entityType: entityId is null ? null : "device",
            entityId: entityId,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["at"] = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            });
}
