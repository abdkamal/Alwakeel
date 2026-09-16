using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Setting keys owned by the account and the session (DATA-MODEL.md §1 <c>settings</c>).</summary>
public static class AccountSettingKeys
{
    /// <summary>Minutes of inactivity before the session locks itself (AGREEMENT item 7, default 10).</summary>
    public const string AutoLockMinutes = "security.auto_lock_minutes";

    /// <summary>Export sequence of the setup file this installation was last built from.</summary>
    public const string SetupExportSeq = "setup.export_seq";

    /// <summary>When the administrator exported the setup file this installation was built from (ISO-8601 UTC).</summary>
    public const string SetupExportedAt = "setup.exported_at";

    /// <summary>
    /// The device identifier exactly as the setup file spelled it. The installation row keeps that
    /// identity as a <c>Guid</c> like every other column, but a later setup file has to be judged
    /// against the spelling it actually carries — so that a file meant for the machine next door is
    /// refused, and a newer file for this very machine is not.
    /// </summary>
    public const string SetupDeviceId = "setup.device_id";

    /// <summary>Identifier of the stored user guide that arrived with the setup file, when one did.</summary>
    public const string GuideDocumentId = "setup.guide_document_id";

    /// <summary>Identifier of the stored monthly-report template that arrived with the setup file.</summary>
    public const string ReportTemplateDocumentId = "setup.report_template_document_id";

    /// <summary>Identifier of the stored official-letter template that arrived with the setup file.</summary>
    public const string LetterTemplateDocumentId = "setup.letter_template_document_id";
}

/// <summary>One node of the organisation structure as the setup file described it.</summary>
public sealed record OrgUnitSeed(
    Guid Id,
    Guid? ParentId,
    OrgUnitLevel Level,
    string Name,
    string? HeadTitle,
    string? HeadName,
    string? OfficeCode,
    int SortOrder);

/// <summary>
/// Everything an installation needs in order to exist, in the shape the database stores it. It is
/// deliberately plain data: <c>Wakeel.Core</c> knows nothing about setup files, containers or
/// signatures — the application layer opens the file, verifies it and hands the result over here.
/// </summary>
public sealed record InstallationSeed
{
    public required Guid OrgId { get; init; }

    public required string OrgName { get; init; }

    /// <summary>The organisation signing key this installation pins; a later file carrying another one is refused.</summary>
    public required byte[] OrgEd25519Pub { get; init; }

    /// <summary>The organisation key the administrator's copy of the database key is sealed to.</summary>
    public required byte[] OrgX25519Pub { get; init; }

    public required Guid OfficeUnitId { get; init; }

    public required string OfficeName { get; init; }

    public required string OfficeCode { get; init; }

    public required Guid DeviceId { get; init; }

    public required int DeviceNo { get; init; }

    public required byte[] DeviceEd25519Pub { get; init; }

    public required byte[] DeviceX25519Pub { get; init; }

    /// <summary>The device certificate as it travelled, stored verbatim so it can be re-sent unchanged.</summary>
    public required byte[] DeviceCertificate { get; init; }

    public required DateTime DeviceIssuedAt { get; init; }

    public required int EmployeeNo { get; init; }

    public required string EmployeeName { get; init; }

    public required string EmployeeJobTitle { get; init; }

    public required InstallationRole Role { get; init; }

    public required SyncScope SyncScope { get; init; }

    public required int CycleStartDay { get; init; }

    public required string NumberingFormat { get; init; }

    /// <summary>The export sequence of the file, kept as the installation's setup version.</summary>
    public required string SetupVersion { get; init; }

    public required IReadOnlyList<OrgUnitSeed> Units { get; init; }

    public required string AppVersion { get; init; }

    public required DateTime BuildDate { get; init; }

    /// <summary>Settings written in the same transaction, key to JSON-encoded value.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The Arabic sentence written to the audit log for the activation. It is supplied by the caller
    /// because every word a person reads lives in the presentation layer's <c>Ar.*</c> tables
    /// (ARCHITECTURE.md §12), and this project carries none of them.
    /// </summary>
    public required string ActivationAuditSummary { get; init; }
}

/// <summary>
/// Reads and writes the single-row identity of this installation and its single operating account
/// (DATA-MODEL.md §1, AGREEMENT item 7: one account per installation, no permission system).
/// Creating the identity is the last step of the first run; everything else here is bookkeeping the
/// sign-in, lock and recovery paths do afterwards.
/// </summary>
public interface IInstallationService
{
    /// <summary>Whether this database already carries an installation identity.</summary>
    Task<bool> IsActivatedAsync(CancellationToken cancellationToken = default);

    /// <summary>The installation row, or null on a database that has never been activated.</summary>
    Task<Installation?> GetInstallationAsync(CancellationToken cancellationToken = default);

    /// <summary>The single operating account, or null before the first run finished.</summary>
    Task<Account?> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the installation identity, the organisation structure, this device, the operating
    /// account and the seed settings in one transaction, and records it in the audit log. Refuses to
    /// run twice: an installation identity is written exactly once in the life of a database.
    /// </summary>
    Task<Installation> CreateAsync(
        InstallationSeed seed,
        DateTime activatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Minutes of inactivity before the session locks (settings first, then the account row).</summary>
    Task<int> GetAutoLockMinutesAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the attempt counter and the temporary lock-out the sign-in screen enforces.</summary>
    Task SetFailedAttemptsAsync(int attempts, DateTime? lockedUntil, CancellationToken cancellationToken = default);

    /// <summary>Records that the account password changed, and clears the attempt counter.</summary>
    Task SetPasswordChangedAtAsync(DateTime changedAt, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IInstallationService"/>
public sealed class InstallationService(WakeelDb db, IClock clock) : IInstallationService
{
    /// <summary>Default minutes of inactivity before the automatic lock (AGREEMENT item 7).</summary>
    public const int DefaultAutoLockMinutes = 10;

    public async Task<bool> IsActivatedAsync(CancellationToken cancellationToken = default) =>
        await db.Installation.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Installation?> GetInstallationAsync(CancellationToken cancellationToken = default) =>
        await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Account?> GetAccountAsync(CancellationToken cancellationToken = default) =>
        await db.Account.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Installation> CreateAsync(
        InstallationSeed seed,
        DateTime activatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (await IsActivatedAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("This database already carries an installation identity.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var installation = new Installation
        {
            OrgId = seed.OrgId,
            OrgName = seed.OrgName,
            OfficeId = seed.OfficeUnitId,
            OfficeName = seed.OfficeName,
            OfficeUnitId = seed.OfficeUnitId,
            OfficeCode = seed.OfficeCode,
            DeviceId = seed.DeviceId,
            DeviceNo = seed.DeviceNo,
            EmployeeNo = seed.EmployeeNo,
            EmployeeName = seed.EmployeeName,
            Role = seed.Role,
            SyncScope = seed.SyncScope,
            CycleStartDay = seed.CycleStartDay,
            NumberingFormat = seed.NumberingFormat,
            SetupVersion = seed.SetupVersion,
            ActivatedAt = Utc(activatedAt),
            AppVersion = seed.AppVersion,
            BuildDate = Utc(seed.BuildDate),
            OrgX25519Pub = seed.OrgX25519Pub,
            OrgEd25519Pub = seed.OrgEd25519Pub,
        };
        db.Installation.Add(installation);

        // The structure is written before anything that points into it, and each layer before the
        // one below it: a unit names its parent and a device names its unit, and the database
        // refuses a row whose target is not there yet. The model declares no navigations between
        // these three tables, so the order has to be made explicit here rather than inferred.
        foreach (var unit in OrderedUnits(seed.Units))
        {
            db.OrgUnits.Add(new OrgUnit
            {
                Id = unit.Id,
                ParentId = unit.ParentId,
                Level = unit.Level,
                Name = unit.Name,
                HeadName = unit.HeadName,
                HeadTitle = unit.HeadTitle,
                OfficeCode = unit.OfficeCode,
                SortOrder = unit.SortOrder,
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        db.Devices.Add(new Device
        {
            Id = seed.DeviceId,
            UnitId = seed.OfficeUnitId,
            DeviceNo = seed.DeviceNo,
            EmployeeNo = seed.EmployeeNo,
            EmployeeName = seed.EmployeeName,
            Role = seed.Role,
            Kind = DeviceKind.Pc,
            Ed25519Pub = seed.DeviceEd25519Pub,
            X25519Pub = seed.DeviceX25519Pub,
            Certificate = seed.DeviceCertificate,
            IssuedAt = Utc(seed.DeviceIssuedAt),
            SyncScope = seed.SyncScope,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.Account.Add(new Account
        {
            // There is no employees table row yet on a fresh installation (that arrives with B5), so
            // the account points at a stable identifier derived from the device and the employee
            // number — the pair the organisation itself treats as the person's identity in this office.
            EmployeeId = EmployeeIdFor(seed.DeviceId, seed.EmployeeNo),
            DisplayName = seed.EmployeeName,
            PasswordChangedAt = Utc(activatedAt),
            FailedAttempts = 0,
            LockedUntil = null,
            AutoLockMinutes = DefaultAutoLockMinutes,
        });

        var now = clock.UtcNow;
        foreach (var setting in seed.Settings)
        {
            db.Settings.Add(new Setting { Key = setting.Key, Value = setting.Value, UpdatedAt = now });
        }

        db.AuditLog.Add(new AuditLogEntry
        {
            At = now,
            Actor = seed.EmployeeName,
            Action = "installation.activate",
            EntityType = "installation",
            EntityId = seed.DeviceId,
            SummaryAr = seed.ActivationAuditSummary,
            Details = null,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return installation;
    }

    public async Task<int> GetAutoLockMinutesAsync(CancellationToken cancellationToken = default)
    {
        var account = await db.Account.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var fallback = account?.AutoLockMinutes ?? DefaultAutoLockMinutes;

        var row = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AccountSettingKeys.AutoLockMinutes, cancellationToken)
            .ConfigureAwait(false);

        if (row is null || !int.TryParse(row.Value, out var minutes))
        {
            return fallback;
        }

        return minutes is > 0 and <= 240 ? minutes : fallback;
    }

    public async Task SetFailedAttemptsAsync(
        int attempts,
        DateTime? lockedUntil,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Account.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        account.FailedAttempts = Math.Max(0, attempts);
        account.LockedUntil = lockedUntil is { } value ? Utc(value) : null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPasswordChangedAtAsync(DateTime changedAt, CancellationToken cancellationToken = default)
    {
        var account = await db.Account.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        account.PasswordChangedAt = Utc(changedAt);
        account.FailedAttempts = 0;
        account.LockedUntil = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A stable identifier for the person operating this installation, folded out of the device id
    /// and the employee number so it is the same on every machine that ever rebuilds this account.
    /// </summary>
    public static Guid EmployeeIdFor(Guid deviceId, int employeeNo)
    {
        Span<byte> material = stackalloc byte[20];
        deviceId.TryWriteBytes(material[..16]);
        BitConverter.TryWriteBytes(material[16..], employeeNo);
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(material, hash);
        return new Guid(hash[..16]);
    }

    /// <summary>
    /// The structure in an order every unit can actually be written in: a parent always before the
    /// children that name it. A unit whose parent is not in the list at all is treated as a root —
    /// the setup file is checked for a whole structure long before it reaches here, and refusing to
    /// write the rest of a hierarchy over one stray line would help nobody.
    /// </summary>
    private static IReadOnlyList<OrgUnitSeed> OrderedUnits(IReadOnlyList<OrgUnitSeed> units)
    {
        var byId = units.ToDictionary(unit => unit.Id);
        var ordered = new List<OrgUnitSeed>(units.Count);
        var written = new HashSet<Guid>();

        void Write(OrgUnitSeed unit)
        {
            if (!written.Add(unit.Id))
            {
                return;
            }

            if (unit.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
            {
                // Marked before the parent is followed, so a structure that somehow points back at
                // itself stops here rather than running out of stack.
                Write(parent);
            }

            ordered.Add(unit);
        }

        foreach (var unit in units)
        {
            Write(unit);
        }

        return ordered;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
