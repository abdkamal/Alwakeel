using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>One line of the check list W03 draws, already in the words the person reads.</summary>
/// <param name="Item">Which item of the file this line is about.</param>
/// <param name="Status">Whether it held, failed, or is simply not part of this file.</param>
/// <param name="Label">The Arabic name of the line.</param>
/// <param name="Value">What the file says about it, or why it was refused.</param>
public sealed record SetupCheckLine(SetupCheckItem Item, SetupCheckStatus Status, string Label, string Value);

/// <summary>What W03 shows about the organisation the file describes, once it could be read at all.</summary>
public sealed record SetupSummary
{
    public required string OrgName { get; init; }

    public required string OfficeName { get; init; }

    public required string OfficeCode { get; init; }

    public required int DeviceNo { get; init; }

    public required string EmployeeName { get; init; }

    public required int EmployeeNo { get; init; }

    public required string JobTitle { get; init; }

    public required string RoleLabel { get; init; }

    public required string ScopeLabel { get; init; }

    public required long ExportSeq { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>The organisation logo as a data URL, or null when the file carries none.</summary>
    public string? LogoDataUrl { get; init; }
}

/// <summary>A setup file copied into the installation's own folder, with what the file picker knew about it.</summary>
/// <param name="Path">Where the copy lives; it is deleted once the installation has been built from it.</param>
/// <param name="FileName">The name the person chose it under.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="ModifiedAt">Its last-write time, as reported by the picker.</param>
public sealed record StagedSetupFile(string Path, string FileName, long Size, DateTimeOffset ModifiedAt);

/// <summary>
/// W02 and W03: takes the file the person chose, copies it somewhere only this installation can
/// read, and asks <see cref="SetupPackageReader"/> what it thinks of it. Nothing is written to the
/// installation and no key is created here — that is <see cref="ActivationService"/>'s job, and it
/// only runs once the person has pressed «تفعيل هذه النسخة».
/// </summary>
public sealed class SetupInspectionService : IDisposable
{
    /// <summary>Largest file W02 will accept, as the drop zone states.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>The only extension W02 accepts.</summary>
    public const string Extension = ".wakeel-setup";

    private readonly WakeelPaths _paths;
    private readonly TimeProvider _time;
    private readonly AccountSession _session;
    private SetupInspection? _inspection;
    private bool _disposed;

    public SetupInspectionService(WakeelPaths paths, TimeProvider time, AccountSession session)
    {
        _paths = paths;
        _time = time;
        _session = session;
    }

    /// <summary>The file currently under examination, or null before one was chosen.</summary>
    public StagedSetupFile? File { get; private set; }

    /// <summary>Every line of the last check, in display order; empty before the first check.</summary>
    public IReadOnlyList<SetupCheckLine> Lines { get; private set; } = [];

    /// <summary>What the file says about the organisation, once it could be decrypted.</summary>
    public SetupSummary? Summary { get; private set; }

    /// <summary>When the last check ran.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>Whether the last check ended with a file that may activate this installation.</summary>
    public bool IsAcceptable => _inspection?.IsAcceptable == true;

    /// <summary>The Arabic sentence explaining the first refusal, or null when nothing was refused.</summary>
    public string? RefusalMessage { get; private set; }

    /// <summary>The opened file, for <see cref="ActivationService"/>. Null until a check succeeded.</summary>
    internal SetupPackage? Package => IsAcceptable ? _inspection?.Package : null;

    /// <summary>Where staged copies live: inside the installation root, never in the machine's temp folder.</summary>
    public string StagingDirectory => System.IO.Path.Combine(_paths.StagingDir, "setup");

    /// <summary>
    /// Copies the chosen file into the installation's own staging folder. It is copied rather than
    /// read in place because the source may be a removable medium the person unplugs a second later,
    /// and because the reader needs to seek through it more than once.
    /// </summary>
    public async Task<StagedSetupFile> StageAsync(
        Stream content,
        string fileName,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Reset();
        Directory.CreateDirectory(StagingDirectory);
        var target = System.IO.Path.Combine(StagingDirectory, "incoming" + Extension);

        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var size = new FileInfo(target).Length;
        File = new StagedSetupFile(target, fileName, size, modifiedAt);
        return File;
    }

    /// <summary>Whether a chosen file's name and length can possibly be a setup file.</summary>
    public static string? Reject(string fileName, long size)
    {
        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return Ar.FirstRun.Setup.WrongExtension;
        }

        return size > MaxFileBytes ? Ar.FirstRun.Setup.FileTooLarge : null;
    }

    /// <summary>
    /// Runs the check list for a machine that has never been activated (W02): nothing is pinned yet,
    /// so the organisation key the file carries is the one it is judged against.
    /// </summary>
    public void Inspect(string packagePassword) =>
        Inspect(packagePassword, SetupExpectations.FirstRun(StagingDirectory));

    /// <summary>
    /// What this machine expects of a setup file right now. On a machine that has never been
    /// activated nothing is pinned. On one that already carries an installation — the person is
    /// applying an updated file to the same device — the organisation key pinned at activation, the
    /// device the installation belongs to and the export sequence already applied are all supplied,
    /// which is what makes «حزمة لجهاز آخر» and «حزمة أقدم من المثبَّتة» reachable at all and what
    /// enforces ARCHITECTURE.md §3's promise that a file signed by another organisation is refused.
    /// </summary>
    public async Task<SetupExpectations> ExpectationsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_session.IsOpen || _session.Installation is not { } installation)
        {
            return SetupExpectations.FirstRun(StagingDirectory);
        }

        long? appliedSeq = null;
        var deviceId = installation.DeviceId.ToString();
        try
        {
            var settings = _session.Settings;
            var seq = await settings
                .GetAsync(AccountSettingKeys.SetupExportSeq, 0L, cancellationToken).ConfigureAwait(false);
            if (seq > 0)
            {
                appliedSeq = seq;
            }

            var recorded = await settings
                .GetAsync(AccountSettingKeys.SetupDeviceId, string.Empty, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(recorded))
            {
                deviceId = recorded;
            }
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException
                                              or InvalidOperationException or System.Text.Json.JsonException)
        {
            // An unreadable setting must never stop a file from being examined: the pinned
            // organisation key still holds, and a missing sequence only means nothing is judged older.
        }

        return new SetupExpectations
        {
            PinnedOrgSigningPub = installation.OrgEd25519Pub,
            InstalledDeviceId = deviceId,
            InstalledExportSeq = appliedSeq,
            StagingDirectory = StagingDirectory,
        };
    }

    /// <summary>
    /// Runs the whole check list against the staged file and the password, and turns the result into
    /// the lines W03 shows. It never throws for anything the screen is meant to display.
    /// </summary>
    public void Inspect(string packagePassword, SetupExpectations expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var staged = File ?? throw new InvalidOperationException("No setup file has been staged yet.");

        DisposeInspection();
        Directory.CreateDirectory(StagingDirectory);

        var reading = new SetupExpectations
        {
            PinnedOrgSigningPub = expectations.PinnedOrgSigningPub,
            InstalledDeviceId = expectations.InstalledDeviceId,
            InstalledExportSeq = expectations.InstalledExportSeq,
            MaxFutureSkew = expectations.MaxFutureSkew,
            StagingDirectory = System.IO.Path.Combine(StagingDirectory, "payload"),
        };

        _inspection = SetupPackageReader.Inspect(staged.Path, packagePassword, reading, _time);
        CheckedAt = _time.GetUtcNow();

        var package = _inspection.Package;
        Summary = package is null ? null : BuildSummary(package);
        Lines = BuildLines(_inspection.Checks, Summary);
        RefusalMessage = _inspection.Checks.FirstFailure is { } failure
            ? SetupRefusals.Describe(failure.Item, failure.Error)
            : null;
    }

    /// <summary>Forgets the current file and its verdicts, and removes the staged copy from disk.</summary>
    public void Reset()
    {
        DisposeInspection();
        Lines = [];
        Summary = null;
        CheckedAt = null;
        RefusalMessage = null;

        var staged = File;
        File = null;
        if (staged is not null)
        {
            DeleteQuietly(staged.Path);
        }
    }

    /// <summary>
    /// Removes everything the check left behind: the staged copy of the file and the folder the
    /// payload was decrypted into. Called once the installation has been built, because what is in
    /// there is the device's own private seeds and the office key, in the clear.
    /// </summary>
    public void ClearStaging()
    {
        DisposeInspection();
        File = null;
        Lines = [];
        Summary = null;
        RefusalMessage = null;

        try
        {
            if (Directory.Exists(StagingDirectory))
            {
                Directory.Delete(StagingDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file still held open by the machine's own indexer must never fail an activation that
            // already succeeded; the folder is emptied again on the next run.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeInspection();
    }

    private void DisposeInspection()
    {
        _inspection?.Dispose();
        _inspection = null;
    }

    /// <summary>
    /// What the organisation card and the check list may say about the file. It is built from the
    /// package's <see cref="SetupPackage.Description"/> rather than from its content on purpose:
    /// the description is the part of <c>setup.json</c> that a refused file may still show — whose
    /// organisation, which office, which device — while the device seed and the office key behind
    /// it stay sealed until every check has passed. W03 has to name the organisation precisely when
    /// it is refusing the file, so that a person can see the file was meant for someone else.
    /// </summary>
    private static SetupSummary BuildSummary(SetupPackage package)
    {
        var description = package.Description;
        var unit = description.Units.FirstOrDefault(u =>
            string.Equals(u.Id, description.Office.UnitId, StringComparison.Ordinal));

        string? logo = null;
        try
        {
            var bytes = package.IsAcceptable
                ? package.ReadLogo()
                : package.ReadLogoPreviewFromRejectedFile();

            if (bytes is { Length: > 0 })
            {
                logo = "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
        }
        catch (CryptoException)
        {
            // A logo that will not decrypt is already reported as a failed line of the check list;
            // the card simply falls back to the organisation's initial.
        }

        return new SetupSummary
        {
            OrgName = description.Org.Name,
            OfficeName = unit?.Name ?? description.Org.Name,
            OfficeCode = description.Office.OfficeCode,
            DeviceNo = description.Device.DeviceNo,
            EmployeeName = description.Employee.Name,
            EmployeeNo = description.Employee.EmployeeNo,
            JobTitle = description.Employee.JobTitle,
            RoleLabel = SetupRefusals.RoleOf(description.Device.Role),
            ScopeLabel = SetupRefusals.ScopeOf(description.Device.SyncScope),
            ExportSeq = description.ExportSeq,
            ExportedAt = description.ExportedAt,
            LogoDataUrl = logo,
        };
    }

    private static IReadOnlyList<SetupCheckLine> BuildLines(SetupCheckResult result, SetupSummary? summary)
    {
        var lines = new List<SetupCheckLine>(result.Checks.Count);
        foreach (var check in result.Checks)
        {
            lines.Add(new SetupCheckLine(
                check.Item,
                check.Status,
                SetupRefusals.LabelOf(check.Item),
                ValueOf(check, summary)));
        }

        return lines;
    }

    private static string ValueOf(SetupCheck check, SetupSummary? summary)
    {
        if (check.Status == SetupCheckStatus.Failed)
        {
            return SetupRefusals.Describe(check.Item, check.Error);
        }

        if (check.Status == SetupCheckStatus.Absent)
        {
            return check.Item switch
            {
                SetupCheckItem.Logo => Ar.FirstRun.Check.ValueLogoAbsent,
                SetupCheckItem.Guide => Ar.FirstRun.Check.ValueGuideAbsent,
                SetupCheckItem.ReportTemplate => Ar.FirstRun.Check.ValueReportTemplateAbsent,
                SetupCheckItem.LetterTemplate => Ar.FirstRun.Check.ValueLetterTemplateAbsent,
                _ => Ar.FirstRun.Check.ValueRevocationAbsent,
            };
        }

        return check.Item switch
        {
            SetupCheckItem.Package => Ar.FirstRun.Check.ValuePackageOk,
            SetupCheckItem.Signature => Ar.FirstRun.Check.ValueSignatureOk,
            SetupCheckItem.Organisation => summary is null
                ? Ar.FirstRun.Check.ValuePackageOk
                : Ar.FirstRun.Check.ValueOrganisation(summary.OrgName),
            SetupCheckItem.Office => summary is null
                ? Ar.FirstRun.Check.ValuePackageOk
                : Ar.FirstRun.Check.ValueOffice(summary.OfficeName, summary.OfficeCode),
            SetupCheckItem.Device => summary is null
                ? Ar.FirstRun.Check.ValuePackageOk
                : Ar.FirstRun.Check.ValueDevice(summary.DeviceNo),
            SetupCheckItem.Employee => summary is null
                ? Ar.FirstRun.Check.ValuePackageOk
                : Ar.FirstRun.Check.ValueEmployee(summary.EmployeeName, summary.EmployeeNo, summary.JobTitle),
            SetupCheckItem.OfficeKey => Ar.FirstRun.Check.ValueOfficeKeyOk,
            SetupCheckItem.Logo => Ar.FirstRun.Check.ValueLogoOk,
            SetupCheckItem.Guide => Ar.FirstRun.Check.ValueGuideOk,
            SetupCheckItem.ReportTemplate => Ar.FirstRun.Check.ValueReportTemplateOk,
            SetupCheckItem.LetterTemplate => Ar.FirstRun.Check.ValueLetterTemplateOk,
            _ => Ar.FirstRun.Check.ValueRevocationOk,
        };
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a staged copy left behind is overwritten by the next choice anyway.
        }
    }
}
