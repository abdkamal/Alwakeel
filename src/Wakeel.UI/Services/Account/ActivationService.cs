using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;
using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>How an attempt to turn a checked setup file into an installation ended.</summary>
public enum ActivationOutcome
{
    Success,

    /// <summary>This machine already carries an installation; nothing at all was written.</summary>
    AlreadyActivated,

    /// <summary>The disk ran out of room while the installation was being written.</summary>
    DiskFull,

    /// <summary>Anything else that stopped the installation from being built.</summary>
    Failed,
}

/// <summary>What W04 shows after «ابدأ العمل».</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Message">The Arabic sentence to show, or null when it succeeded.</param>
public readonly record struct ActivationResult(ActivationOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome == ActivationOutcome.Success;
}

/// <summary>
/// W04: turns a setup file that passed every check into a working installation — the three wraps of
/// each key plus the administrator's, the key file, the database and its identity rows, the stored
/// logo and guide, and the sealed sign-in card the next start reads.
/// </summary>
/// <remarks>
/// Every secret it handles is short lived on purpose. The two random keys are wiped as soon as the
/// database holds one and the session holds the other; the recovery code exists only for as long as
/// the sheet is on screen; the setup file's staging folder — which contains the device's own private
/// seeds and the office key in the clear — is deleted before this method returns.
/// </remarks>
public sealed class ActivationService
{
    private readonly WakeelPaths _paths;
    private readonly IPlatformProtector _protector;
    private readonly TimeProvider _time;
    private readonly IClock _clock;
    private readonly AccountSession _session;
    private readonly SetupInspectionService _inspection;
    private readonly SignInProfileStore _profiles;
    private readonly AccountOptions _options;

    private RecoveryCode? _code;

    public ActivationService(
        WakeelPaths paths,
        IPlatformProtector protector,
        TimeProvider time,
        IClock clock,
        AccountSession session,
        SetupInspectionService inspection,
        SignInProfileStore profiles,
        AccountOptions options)
    {
        _paths = paths;
        _protector = protector;
        _time = time;
        _clock = clock;
        _session = session;
        _inspection = inspection;
        _profiles = profiles;
        _options = options;
    }

    /// <summary>The sheet currently on screen, or null before W04 opened or after it finished.</summary>
    public RecoverySheet? Sheet { get; private set; }

    /// <summary>Whether a checked, acceptable setup file is waiting to be turned into an installation.</summary>
    public bool CanActivate => _inspection.Package is not null && _inspection.Summary is not null;

    /// <summary>
    /// Whether this machine already carries an installation. Activation is refused while it does,
    /// because the key file it would write holds wraps of a brand-new random database key, and the
    /// database already on disk does not open with it: the working account would be lost with the
    /// file that was overwritten (ARCHITECTURE.md §3).
    /// </summary>
    public bool AlreadyActivated =>
        System.IO.File.Exists(_paths.InstallationKeyPath) || System.IO.File.Exists(_paths.DbPath);

    /// <summary>
    /// Produces the recovery code and its sheet, which W04 shows while the person is still choosing
    /// a password. Calling it again returns the same sheet, so a re-render never silently swaps the
    /// code out from under a sheet that has already been printed.
    /// </summary>
    public RecoverySheet Begin()
    {
        if (Sheet is { } existing)
        {
            return existing;
        }

        var summary = _inspection.Summary
            ?? throw new InvalidOperationException("No setup file has been accepted yet.");

        _code = RecoveryCode.Generate();
        Sheet = new RecoverySheet
        {
            CodeDisplay = _code.Display,
            QrDataUrl = RecoveryQr.ToDataUrl(_code),
            IssuedAt = _time.GetUtcNow(),
            OrgName = summary.OrgName,
            OfficeName = summary.OfficeName,
            EmployeeName = summary.EmployeeName,
            DeviceNo = summary.DeviceNo,
            LogoDataUrl = summary.LogoDataUrl,
        };

        return Sheet;
    }

    /// <summary>Drops the sheet and the code behind it without activating anything (the «رجوع» button).</summary>
    public void Abandon()
    {
        Sheet = null;
        _code = null;
    }

    /// <summary>
    /// Creates the installation from the accepted setup file and the chosen account password, and
    /// leaves the session open on the other side of it.
    /// </summary>
    public async Task<ActivationResult> ActivateAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var package = _inspection.Package
            ?? throw new InvalidOperationException("No setup file has been accepted yet.");
        var code = _code
            ?? throw new InvalidOperationException("No recovery code has been produced yet.");

        // Checked before a single byte is written. The key file is replaced wholesale by the write
        // below, so a refusal that arrived any later — when the database declined the new key, say —
        // would already have destroyed the only wraps that open the installation standing here.
        if (AlreadyActivated)
        {
            return new ActivationResult(ActivationOutcome.AlreadyActivated, Ar.FirstRun.Account.AlreadyActivated);
        }

        var content = package.Content;
        var now = _time.GetUtcNow();

        _paths.EnsureDirectories();

        var kdf = _options.Kdf ?? Argon2Kdf.AutoTune(_options.KdfTargetMs, _time);
        var dbKey = RandomBytes.Next(Aead.KeySize);
        var vaultKey = RandomBytes.Next(Aead.KeySize);

        // Activation is all or nothing. Until the session is holding a finished installation, every
        // file this method put on disk is swept away again on the way out, so a refusal leaves
        // exactly the machine that walked in: otherwise a half-written installation would answer
        // «مفعّل مسبقًا» to every later attempt while no password on earth opens it, and there would
        // be no way out of that from inside the product.
        var committed = false;

        // Every vault blob StoreAttachmentsAsync writes lands here as it is written, so a failure
        // partway through — the third attachment, say — can be undone as completely as the key file
        // and the database: a hash added here but never reached by a rollback would be dead bytes no
        // later installation could read or account for, because the key that sealed them is gone too.
        var writtenVaultHashes = new List<string>();

        try
        {
            WriteKeyFile(content, password, code, kdf, dbKey, vaultKey);

            var database = DbSession.Open(_paths, dbKey, _clock);
            try
            {
                var installation = await SeedAsync(database.Db, content, now, cancellationToken).ConfigureAwait(false);
                await StoreAttachmentsAsync(database.Db, package, vaultKey, installation, writtenVaultHashes, cancellationToken)
                    .ConfigureAwait(false);

                var account = await database.Db.Account.AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                SaveProfile(content, package, now);

                // The session takes the vault key over; the database key is nobody's business once
                // the connection that needed it has been opened.
                _session.Adopt(database, vaultKey, installation, account, InstallationService.DefaultAutoLockMinutes);
                vaultKey = [];
                committed = true;
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }
        catch (IOException exception) when (DiskSpace.IsFull(exception))
        {
            // The one write failure the person can act on themselves, so it is named rather than
            // folded into the general «تعذّر إنشاء الحساب» (B1 acceptance criteria). A full disk is
            // also the failure most likely to be retried, which is exactly why the half-written
            // installation must be gone before the sentence appears.
            RollBack(committed, writtenVaultHashes);
            return new ActivationResult(ActivationOutcome.DiskFull, Ar.FirstRun.Setup.DiskFull);
        }
        catch
        {
            RollBack(committed, writtenVaultHashes);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dbKey);
            CryptographicOperations.ZeroMemory(vaultKey);
        }

        // The staged payload holds the device's own private seeds and the office key in the clear;
        // it has done its job and must not outlive the activation by a single restart.
        _inspection.ClearStaging();
        Sheet = null;
        _code = null;
        return new ActivationResult(ActivationOutcome.Success);
    }

    /// <summary>
    /// Undoes a half-finished activation. <see cref="AlreadyActivated"/> guaranteed that neither the
    /// key file nor the database existed when this attempt started, so deleting both restores the
    /// machine byte for byte. Any vault blob already written under the abandoned vault key is removed
    /// too, or its bytes would sit there unreadable by anything once the wrapped key that opened them
    /// is gone. A file that refuses to go is left alone rather than allowed to hide the failure that
    /// brought us here.
    /// </summary>
    private void RollBack(bool committed, IReadOnlyCollection<string> writtenVaultHashes)
    {
        if (committed)
        {
            return;
        }

        Delete(_paths.InstallationKeyPath);
        Delete(_paths.InstallationKeyPath + ".tmp");
        Delete(_paths.InstallationKeyPath + ".bak");

        // SQLite leaves its journal beside the database; all three go together or the next attempt
        // opens a file that is neither empty nor readable.
        Delete(_paths.DbPath);
        Delete(_paths.DbPath + "-wal");
        Delete(_paths.DbPath + "-shm");
        Delete(_paths.DbPath + "-journal");

        foreach (var hash in writtenVaultHashes)
        {
            VaultStore.Delete(_paths, hash);
            Delete(_paths.VaultFilePath(hash) + ".tmp");
        }

        static void Delete(string path)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void WriteKeyFile(
        SetupContent content,
        string password,
        RecoveryCode code,
        Argon2Params kdf,
        byte[] dbKey,
        byte[] vaultKey)
    {
        var orgAgreementKey = Base64Url.Decode(content.Org.X25519Pub);
        var keyFile = InstallationKeyFile.Create(_time);

        // Three independent ways to open each key, plus the administrator's own (ARCHITECTURE.md §3).
        // Every wrap gets its own salt, so two wraps of the same key never derive the same bytes.
        keyFile.SetDbKeyWrap(KeyWraps.FromPassword(password, dbKey, kdf.WithFreshSalt(), KeyWraps.DbKeyContext));
        keyFile.SetDbKeyWrap(KeyWraps.FromRecoveryCode(code, dbKey, kdf.WithFreshSalt(), KeyWraps.DbKeyContext));
        keyFile.SetDbKeyWrap(KeyWraps.FromMachine(_protector, dbKey, KeyWraps.DbKeyContext));
        keyFile.SetDbKeyWrap(KeyWraps.ForAdmin(orgAgreementKey, dbKey, KeyWraps.DbKeyContext));

        keyFile.SetVaultKeyWrap(KeyWraps.FromPassword(password, vaultKey, kdf.WithFreshSalt(), KeyWraps.VaultKeyContext));
        keyFile.SetVaultKeyWrap(KeyWraps.FromRecoveryCode(code, vaultKey, kdf.WithFreshSalt(), KeyWraps.VaultKeyContext));
        keyFile.SetVaultKeyWrap(KeyWraps.FromMachine(_protector, vaultKey, KeyWraps.VaultKeyContext));
        keyFile.SetVaultKeyWrap(KeyWraps.ForAdmin(orgAgreementKey, vaultKey, KeyWraps.VaultKeyContext));

        // The device's private seeds move out of the setup file and into the key file, sealed with
        // the database key, so the only copy left on the machine is one the password opens.
        keyFile.SetDeviceSeeds(dbKey, content.DeviceSeed);
        keyFile.Save(_paths.InstallationKeyPath, _time);
    }

    private async Task<Installation> SeedAsync(
        WakeelDb db,
        SetupContent content,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var certificate = content.Device.Certificate;
        var officeUnit = content.OfficeUnit();

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AccountSettingKeys.AutoLockMinutes] =
                InstallationService.DefaultAutoLockMinutes.ToString(CultureInfo.InvariantCulture),
            [AccountSettingKeys.SetupExportSeq] = content.ExportSeq.ToString(CultureInfo.InvariantCulture),
            [AccountSettingKeys.SetupExportedAt] = JsonSerializer.Serialize(content.ExportedAt),

            // Kept verbatim so a later setup file can be compared against the spelling this one used.
            [AccountSettingKeys.SetupDeviceId] = JsonSerializer.Serialize(content.Device.Id),
        };

        var seed = new InstallationSeed
        {
            OrgId = SetupIds.ToGuid(content.Org.Id),
            OrgName = content.Org.Name,
            OrgEd25519Pub = Base64Url.Decode(content.Org.SigningPub),
            OrgX25519Pub = Base64Url.Decode(content.Org.X25519Pub),
            OfficeUnitId = SetupIds.ToGuid(content.Office.UnitId),
            OfficeName = officeUnit?.Name ?? content.Org.Name,
            OfficeCode = content.Office.OfficeCode,
            DeviceId = SetupIds.ToGuid(content.Device.Id),
            DeviceNo = content.Device.DeviceNo,
            DeviceEd25519Pub = certificate.SigningPublicKey,
            DeviceX25519Pub = certificate.AgreementPublicKey,
            DeviceCertificate = CanonicalJson.SerializeToUtf8Bytes(certificate),
            DeviceIssuedAt = certificate.Body.IssuedAt.UtcDateTime,
            EmployeeNo = content.Employee.EmployeeNo,
            EmployeeName = content.Employee.Name,
            EmployeeJobTitle = content.Employee.JobTitle,
            Role = RoleOf(content.Device.Role),
            SyncScope = content.Device.SyncScope == SetupSyncScopes.Custody ? SyncScope.Custody : SyncScope.Full,
            CycleStartDay = content.Org.CycleStartDay,
            NumberingFormat = content.Org.NumberingFormat,
            SetupVersion = content.ExportSeq.ToString(CultureInfo.InvariantCulture),
            Units = BuildUnits(content),
            AppVersion = _options.AppVersion,
            BuildDate = _options.BuildDate.UtcDateTime,
            Settings = settings,
            ActivationAuditSummary = Ar.FirstRun.Audit.Activated,
        };

        var installations = new InstallationService(db, _clock);
        return await installations.CreateAsync(seed, now.UtcDateTime, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreAttachmentsAsync(
        WakeelDb db,
        SetupPackage package,
        byte[] vaultKey,
        Installation installation,
        ICollection<string> writtenVaultHashes,
        CancellationToken cancellationToken)
    {
        var settings = new SettingsService(db, _clock);

        var logoId = await StoreAsync(db, vaultKey, package.ReadLogo(), "logo.png", "image/png", writtenVaultHashes, cancellationToken)
            .ConfigureAwait(false);
        if (logoId is { } logo)
        {
            var row = await db.Installation.FirstAsync(cancellationToken).ConfigureAwait(false);
            row.LogoDocumentId = logo;
            installation.LogoDocumentId = logo;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await RecordAsync(
            AccountSettingKeys.GuideDocumentId,
            await StoreAsync(db, vaultKey, package.ReadGuide(), "guide.pdf", "application/pdf", writtenVaultHashes, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);

        await RecordAsync(
            AccountSettingKeys.ReportTemplateDocumentId,
            await StoreAsync(
                db,
                vaultKey,
                package.ReadReportTemplate(),
                "report-template.docx",
                WordMime,
                writtenVaultHashes,
                cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        await RecordAsync(
            AccountSettingKeys.LetterTemplateDocumentId,
            await StoreAsync(
                db,
                vaultKey,
                package.ReadLetterTemplate(),
                "letter-template.docx",
                WordMime,
                writtenVaultHashes,
                cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        async Task RecordAsync(string key, Guid? documentId)
        {
            if (documentId is { } id)
            {
                await settings.SetAsync(key, id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private async Task<Guid?> StoreAsync(
        WakeelDb db,
        byte[] vaultKey,
        byte[]? content,
        string name,
        string mime,
        ICollection<string> writtenVaultHashes,
        CancellationToken cancellationToken)
    {
        if (content is not { Length: > 0 })
        {
            return null;
        }

        var hash = VaultStore.Write(_paths, vaultKey, content);
        writtenVaultHashes.Add(hash);
        var document = new Document
        {
            Sha256 = hash,
            Size = content.Length,
            Mime = mime,
            OriginalName = name,
            Source = DocumentSource.Import,
            PageCount = 0,
            OcrStatus = OcrStatus.Unsupported,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return document.Id;
    }

    private void SaveProfile(SetupContent content, SetupPackage package, DateTimeOffset now)
    {
        var officeUnit = content.OfficeUnit();
        _profiles.Save(new SignInProfile
        {
            OrgName = content.Org.Name,
            OfficeName = officeUnit?.Name ?? content.Org.Name,
            OfficeCode = content.Office.OfficeCode,
            EmployeeName = content.Employee.Name,
            JobTitle = content.Employee.JobTitle,
            EmployeeNo = content.Employee.EmployeeNo,
            DeviceNo = content.Device.DeviceNo,
            Logo = package.ReadLogo(),
            FailedAttempts = 0,
            LockedUntil = null,
            UnloggedFailures = 0,
            RecoveryIssuedAt = Sheet?.IssuedAt ?? now,
            LastSignInAt = now,
        });
    }

    private static IReadOnlyList<OrgUnitSeed> BuildUnits(SetupContent content)
    {
        var units = new List<OrgUnitSeed>(content.Units.Count);
        var order = 0;
        foreach (var unit in content.Units)
        {
            units.Add(new OrgUnitSeed(
                SetupIds.ToGuid(unit.Id),
                string.IsNullOrEmpty(unit.ParentId) ? null : SetupIds.ToGuid(unit.ParentId),
                LevelOf(unit.Level),
                unit.Name,
                unit.HeadTitle,
                unit.HeadName,
                unit.OfficeCode,
                order++));
        }

        return units;
    }

    /// <summary>
    /// The setup file numbers the four fixed layers from one; the database names them
    /// (DATA-MODEL.md §1). Anything outside the four is impossible — the reader refuses such a file
    /// before this code ever sees it — so the deepest layer is the safe landing.
    /// </summary>
    private static OrgUnitLevel LevelOf(int level) => level switch
    {
        1 => OrgUnitLevel.Org,
        2 => OrgUnitLevel.Department,
        3 => OrgUnitLevel.Section,
        _ => OrgUnitLevel.Unit,
    };

    /// <summary>
    /// The three working roles of a computer account. The setup file and the certificate call the
    /// first one «manager»; the database enumeration calls the same role
    /// <see cref="InstallationRole.Director"/>, which is the name B0 gave it.
    /// </summary>
    private static InstallationRole RoleOf(string role) => role switch
    {
        SetupRoles.Secretary => InstallationRole.Secretary,
        SetupRoles.Custodian => InstallationRole.Custodian,
        _ => InstallationRole.Director,
    };
}
