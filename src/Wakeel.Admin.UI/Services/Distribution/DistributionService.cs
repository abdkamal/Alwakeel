using System.Globalization;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Distribution;

/// <summary>One office that has to be told about something, and the computers in it.</summary>
/// <param name="OfficeId">The office.</param>
/// <param name="Name">Its name.</param>
/// <param name="OfficeCode">Its inventory code.</param>
/// <param name="Devices">The computers a new file would be made for.</param>
/// <param name="Blocked">The computers that cannot be given one, each with the reason.</param>
public sealed record DistributionTarget(
    string OfficeId,
    string Name,
    string OfficeCode,
    IReadOnlyList<AdminDevice> Devices,
    IReadOnlyList<(AdminDevice Device, string Reason)> Blocked);

/// <summary>One finished file of a batch, with the password that opens it.</summary>
/// <param name="OfficeName">Which office it is for.</param>
/// <param name="OfficeCode">That office's code.</param>
/// <param name="DeviceNo">Which computer in it.</param>
/// <param name="EmployeeName">Who works at that computer.</param>
/// <param name="FileName">The file's name.</param>
/// <param name="FilePath">Where it was written.</param>
/// <param name="PackagePassword">The sixteen characters that open it, shown once.</param>
public sealed record DistributionFile(
    string OfficeName,
    string OfficeCode,
    int DeviceNo,
    string EmployeeName,
    string FileName,
    string FilePath,
    string PackagePassword);

/// <summary>What one batch produced.</summary>
/// <param name="Files">Every file that was made, each with its own password.</param>
/// <param name="Failures">Every computer that could not be given one, with the reason in Arabic.</param>
public sealed record DistributionRun(
    IReadOnlyList<DistributionFile> Files,
    IReadOnlyList<string> Failures);

/// <summary>
/// A10 «توزيع التحديثات»: what the offices have not been told yet, and the one action that tells
/// them — a fresh setup file for every computer of every affected office, made in one go.
/// </summary>
/// <remarks>
/// <para>
/// A change to the organisation itself or to the structure reaches every office, because the whole
/// tree travels in every setup file. A change to one office or to one of its computers reaches that
/// office alone. That is the whole of the rule, and it is why a batch made after renaming a section
/// covers everybody while a batch made after registering a computer covers one corridor.
/// </para>
/// <para>
/// Each file gets its own password, shown once on this screen and never stored: a single password for
/// a whole batch would mean that one office's copy opens another office's file.
/// </para>
/// </remarks>
public sealed class DistributionService
{
    private readonly AdminDb _db;
    private readonly AdminPendingChanges _pending;
    private readonly AdminDeviceService _devices;
    private readonly SetupExportService _export;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly TimeProvider _time;

    public DistributionService(
        AdminDb db,
        AdminPendingChanges pending,
        AdminDeviceService devices,
        SetupExportService export,
        AdminAuditService audit,
        AdminSession session,
        TimeProvider time)
    {
        _db = db;
        _pending = pending;
        _devices = devices;
        _export = export;
        _audit = audit;
        _session = session;
        _time = time;
    }

    /// <summary>Everything the offices have not been told about yet, newest first.</summary>
    public IReadOnlyList<AdminPendingChange> Waiting() => _pending.Open(200);

    /// <summary>When a file was last made for any computer of this office, or null for never.</summary>
    public DateTimeOffset? LastExport(string officeId)
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        var at = _db.ScalarText(
            """
            SELECT MAX(e.exported_at) FROM setup_exports e
            JOIN devices d ON d.id = e.device_id
            WHERE d.office_id = $id;
            """,
            ("$id", officeId));

        return at is null
            ? null
            : DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    /// <summary>How many waiting changes an office is actually waiting for.</summary>
    public int WaitingFor(string officeId)
    {
        var count = 0;
        foreach (var change in Waiting())
        {
            var reaches = change.EntityType switch
            {
                "office" => string.Equals(change.EntityId, officeId, StringComparison.Ordinal),
                "device" => _devices.FindDevice(change.EntityId) is { } device
                    && string.Equals(device.OfficeId, officeId, StringComparison.Ordinal),
                _ => true,
            };

            if (reaches)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The offices those changes reach, each with the computers in it.</summary>
    public IReadOnlyList<DistributionTarget> Targets()
    {
        var waiting = Waiting();
        if (waiting.Count == 0)
        {
            return [];
        }

        var offices = _devices.ListOffices();
        var everywhere = waiting.Any(change => change.EntityType is "org" or "unit");
        var wanted = new HashSet<string>(StringComparer.Ordinal);

        if (!everywhere)
        {
            foreach (var change in waiting)
            {
                switch (change.EntityType)
                {
                    case "office":
                        wanted.Add(change.EntityId);
                        break;
                    case "device":
                        var device = _devices.FindDevice(change.EntityId);
                        if (device is not null)
                        {
                            wanted.Add(device.OfficeId);
                        }

                        break;
                    default:
                        // An entity this build does not know about is treated as reaching everybody,
                        // because being told twice costs a file and being told never costs the work.
                        everywhere = true;
                        break;
                }
            }
        }

        var targets = new List<DistributionTarget>();
        foreach (var office in offices)
        {
            if (!everywhere && !wanted.Contains(office.Id))
            {
                continue;
            }

            if (office.IsOutOfService)
            {
                continue;
            }

            var ready = new List<AdminDevice>();
            var blocked = new List<(AdminDevice, string)>();

            foreach (var device in _devices.ListDevices(office.Id))
            {
                if (device.IsPhone)
                {
                    continue;
                }

                if (device.IsRevoked)
                {
                    blocked.Add((device, AdminAr.Distribution.BlockedRevoked));
                }
                else if (!device.HasAccount)
                {
                    blocked.Add((device, AdminAr.Distribution.BlockedNoAccount));
                }
                else if (!office.HasKey)
                {
                    blocked.Add((device, AdminAr.Distribution.BlockedNoOfficeKey));
                }
                else
                {
                    ready.Add(device);
                }
            }

            if (ready.Count > 0 || blocked.Count > 0)
            {
                targets.Add(new DistributionTarget(office.Id, office.Name, office.OfficeCode, ready, blocked));
            }
        }

        return targets;
    }

    /// <summary>
    /// Makes a fresh setup file for every computer of the offices named, each with its own password,
    /// and marks the changes those offices were waiting for as sent.
    /// </summary>
    /// <param name="officeIds">The offices to cover; empty means every affected office.</param>
    public DistributionRun Distribute(IReadOnlyCollection<string>? officeIds = null)
    {
        var targets = Targets();
        if (officeIds is { Count: > 0 })
        {
            var chosen = new HashSet<string>(officeIds, StringComparer.Ordinal);
            targets = [.. targets.Where(target => chosen.Contains(target.OfficeId))];
        }

        var files = new List<DistributionFile>();
        var failures = new List<string>();

        foreach (var target in targets)
        {
            foreach (var device in target.Devices)
            {
                var password = PackagePassword.New();
                var refusal = _export.Export(device.Id, SetupExportOptions.All, password, out var result);
                if (refusal != ExportRefusal.None || result is null)
                {
                    failures.Add(AdminAr.Distribution.Failure(target.Name, device.DeviceNo));
                    continue;
                }

                files.Add(new DistributionFile(
                    target.Name,
                    target.OfficeCode,
                    device.DeviceNo,
                    device.EmployeeName,
                    result.FileName,
                    result.FilePath,
                    PackagePassword.Display(password)));
            }

            foreach (var (device, reason) in target.Blocked)
            {
                failures.Add(AdminAr.Distribution.Blocked(target.Name, device.DeviceNo, reason));
            }
        }

        if (files.Count > 0)
        {
            MarkSent([.. targets.Select(target => target.OfficeId)]);
            _audit.Write(
                _session.AdminName,
                "changes_distributed",
                AdminAr.Distribution.Log(files.Count, targets.Count),
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["file_count"] = files.Count.ToString(CultureInfo.InvariantCulture),
                    ["office_count"] = targets.Count.ToString(CultureInfo.InvariantCulture),
                });
        }

        return new DistributionRun(files, failures);
    }

    /// <summary>
    /// Closes the waiting rows the batch answered. A change that reaches every office is only closed
    /// once every office was actually covered; anything narrower is closed for the offices covered.
    /// </summary>
    private void MarkSent(IReadOnlyCollection<string> coveredOffices)
    {
        if (!_db.IsOpen)
        {
            return;
        }

        var covered = new HashSet<string>(coveredOffices, StringComparer.Ordinal);
        var everyOffice = _devices.ListOffices()
            .Where(office => !office.IsOutOfService)
            .All(office => covered.Contains(office.Id));

        var at = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        foreach (var change in Waiting())
        {
            var answered = change.EntityType switch
            {
                "org" or "unit" => everyOffice,
                "office" => covered.Contains(change.EntityId),
                "device" => _devices.FindDevice(change.EntityId) is { } device && covered.Contains(device.OfficeId),
                _ => everyOffice,
            };

            if (answered)
            {
                _db.Execute(
                    "UPDATE pending_changes SET distributed_at = $at WHERE id = $id;",
                    ("$id", change.Id),
                    ("$at", at));
            }
        }
    }
}
