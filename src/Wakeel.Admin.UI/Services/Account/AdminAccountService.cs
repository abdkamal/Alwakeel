using System.Globalization;
using System.Security.Cryptography;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Account;

/// <summary>Why creating the administrator account, signing in, or recovering could not go ahead.</summary>
public enum AdminAccountRefusal
{
    /// <summary>Nothing was refused.</summary>
    None,

    /// <summary>The password is shorter or plainer than the tool accepts.</summary>
    WeakPassword,

    /// <summary>The password and its confirmation are not the same.</summary>
    PasswordMismatch,

    /// <summary>A name the screen asks for was left empty.</summary>
    MissingName,

    /// <summary>An account already exists on this computer.</summary>
    AlreadyCreated,

    /// <summary>No account exists on this computer yet.</summary>
    NotCreated,

    /// <summary>The password does not open the stored wrap.</summary>
    WrongPassword,

    /// <summary>Too many wrong passwords in a row; the temporary lock-out is running.</summary>
    LockedOut,

    /// <summary>The text entered is not a recovery code at all.</summary>
    MalformedRecoveryCode,

    /// <summary>The recovery code is well formed but does not open this account.</summary>
    WrongRecoveryCode,

    /// <summary>The tool could not write to its own folder, or its files are unreadable.</summary>
    StorageFailed,
}

/// <summary>What creating the account produced.</summary>
/// <param name="Refusal">Why it did not happen, or <see cref="AdminAccountRefusal.None"/>.</param>
/// <param name="Sheet">The recovery sheet, shown once and then dropped.</param>
public readonly record struct AdminCreateResult(AdminAccountRefusal Refusal, AdminRecoverySheet? Sheet = null)
{
    /// <summary>Whether the account now exists.</summary>
    public bool Succeeded => Refusal == AdminAccountRefusal.None;
}

/// <summary>What a sign-in attempt produced.</summary>
/// <param name="Refusal">Why it did not happen, or <see cref="AdminAccountRefusal.None"/>.</param>
/// <param name="AttemptsRemaining">Wrong passwords still tolerated before the lock-out.</param>
/// <param name="LockedUntil">When the running lock-out ends.</param>
public readonly record struct AdminSignInResult(
    AdminAccountRefusal Refusal,
    int AttemptsRemaining = 0,
    DateTimeOffset? LockedUntil = null)
{
    /// <summary>Whether the tool is now unlocked.</summary>
    public bool Succeeded => Refusal == AdminAccountRefusal.None;
}

/// <summary>
/// The administrator account: creating it with the organisation recovery sheet (A01), signing in
/// with the attempt counter and the doubling temporary lock-out (A02), and replacing a forgotten
/// password with the printed sheet (A02's recovery).
/// </summary>
/// <remarks>
/// <para>
/// <c>admin.db</c> is opened with a random key that exists only inside two wraps in
/// <c>keys\admin.key</c>: one the password opens through Argon2id, one the twenty-character
/// recovery code opens the same way. Nothing derives the database key from the password directly,
/// so changing the password is re-wrapping the same key rather than re-encrypting the database, and
/// the recovery sheet keeps working across a password change.
/// </para>
/// <para>
/// The attempt counter and the lock-out live in the key file, not in the database, because they
/// have to be read and written while the password is still wrong — at which moment the database
/// cannot be opened at all. A counter that a restart could erase would make the lock-out theatre.
/// </para>
/// <para>
/// Passwords and codes are held in <see cref="string"/> parameters for as long as one call takes
/// and never stored, logged, or put in the database; the derived key bytes are wiped as soon as the
/// database is open.
/// </para>
/// </remarks>
public sealed class AdminAccountService
{
    /// <summary>
    /// How many lock-outs at most are queued for the operations log while the database is shut.
    /// Twenty is far more than a forgetful person ever produces and far fewer than somebody sitting
    /// at the machine guessing could bury the log under.
    /// </summary>
    public const int MaxLockOutsToReport = 20;

    private readonly AdminPaths _paths;
    private readonly AdminDb _db;
    private readonly AdminKeyService _keys;
    private readonly AdminAuditService _audit;
    private readonly AdminSession _session;
    private readonly AdminOptions _options;
    private readonly TimeProvider _time;

    public AdminAccountService(
        AdminPaths paths,
        AdminDb db,
        AdminKeyService keys,
        AdminAuditService audit,
        AdminSession session,
        AdminOptions options,
        TimeProvider time)
    {
        _paths = paths;
        _db = db;
        _keys = keys;
        _audit = audit;
        _session = session;
        _options = options;
        _time = time;
    }

    /// <summary>Whether this computer already carries an administrator account.</summary>
    public bool IsCreated => AdminKeyFile.Exists(_paths.KeyFile);

    /// <summary>
    /// Whether the account file is there but this computer cannot make sense of it — a half-written
    /// save, a file edited by hand, a file from a newer build. It is a different thing from having
    /// no account at all: the screens must not send the person off to create one over the top of it,
    /// they must say the tool's own data could not be read and point at «الصيانة».
    /// </summary>
    public bool KeyFileUnreadable => AdminKeyFile.Exists(_paths.KeyFile) && TryLoadKeyFile() is null;

    /// <summary>The administrator's name, as far as the sign-in screen may know it.</summary>
    public string AdminName => TryLoadKeyFile()?.AdminName ?? string.Empty;

    /// <summary>
    /// The organisation's name, as far as the sign-in screen may know it — the database that holds
    /// the rest of what is known about the organisation is still shut at that point.
    /// </summary>
    public string OrgName => TryLoadKeyFile()?.OrgName ?? string.Empty;

    /// <summary>When the running temporary lock-out ends, or null when the door is open.</summary>
    public DateTimeOffset? LockedUntil
    {
        get
        {
            var file = TryLoadKeyFile();
            return file?.LockedUntil > _time.GetUtcNow() ? file.LockedUntil : null;
        }
    }

    /// <summary>Seconds still to wait, or zero when there is no lock-out running.</summary>
    public int LockedSecondsRemaining => SecondsUntil(LockedUntil);

    /// <summary>Wrong passwords still tolerated before the next temporary lock-out.</summary>
    public int AttemptsRemaining
    {
        get
        {
            var file = TryLoadKeyFile();
            return file is null ? _options.MaxAttempts : Math.Max(0, _options.MaxAttempts - file.FailedAttempts);
        }
    }

    /// <summary>
    /// A01: creates the administrator account, generates the organisation keys and the organisation
    /// root certificate, creates <c>admin.db</c>, and returns the recovery sheet to be shown once.
    /// </summary>
    public AdminCreateResult Create(string password, string confirmation, string adminName, string orgName)
    {
        if (IsCreated)
        {
            return new AdminCreateResult(AdminAccountRefusal.AlreadyCreated);
        }

        if (string.IsNullOrWhiteSpace(adminName) || string.IsNullOrWhiteSpace(orgName))
        {
            return new AdminCreateResult(AdminAccountRefusal.MissingName);
        }

        if (!AdminPasswordStrength.IsAcceptable(password))
        {
            return new AdminCreateResult(AdminAccountRefusal.WeakPassword);
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            return new AdminCreateResult(AdminAccountRefusal.PasswordMismatch);
        }

        var now = _time.GetUtcNow();
        var kdf = _options.Kdf ?? Argon2Kdf.AutoTune(_options.KdfTargetMs, _time);
        var databaseKey = RandomBytes.Next(32);
        var recoveryCode = RecoveryCode.Generate();

        try
        {
            _paths.EnsureDirectories();

            var keyFile = AdminKeyFile.Create(_time);
            keyFile.AdminName = adminName.Trim();
            keyFile.OrgName = orgName.Trim();
            keyFile.RecoveryIssuedAt = now;
            keyFile.SetDbKeyWrap(KeyWraps.FromPassword(password, databaseKey, kdf, AdminKeyFile.DbKeyContext));
            keyFile.SetDbKeyWrap(KeyWraps.FromRecoveryCode(
                recoveryCode, databaseKey, kdf.WithFreshSalt(), AdminKeyFile.DbKeyContext));

            // A database left behind by an interrupted earlier run has no key file beside it, so
            // nothing in it can ever be opened again. It is moved out of the way instead of being
            // left to make this attempt — and every retry after it — fail.
            SetAsideOrphanedDatabase();

            // The database is built first. Only once it stands does the key file appear, so an
            // interrupted first run leaves nothing that looks like an account nobody can open.
            _db.Open(databaseKey);
            try
            {
                _keys.CreateOrganisation(orgName);
                WriteAccountRow(adminName.Trim(), now);

                // No backup: this wrap set is brand new, and a backup left by an abandoned earlier
                // attempt would be a way of opening a database that is no longer there — or one
                // that was just set aside. Nothing on disk may open anything but this account.
                keyFile.Save(_paths.KeyFile, _time, keepBackup: false);
                RemoveRetiredKeyFileBackup();
            }
            catch
            {
                _db.Close();
                TryDeleteDatabase();
                throw;
            }

            _session.SignIn(keyFile.AdminName);
            _audit.Write(
                keyFile.AdminName,
                "admin.account.created",
                AdminAr.AccountAudit.AccountCreated,
                entityType: "org",
                entityId: _keys.ReadOrganisation()?.Id);
            // The label of the sheet, not the sheet: it says which piece of paper is in the safe and
            // nothing about what is printed on it.
            var sheetNumber = AdminSheetNumber.Next(now);
            _audit.Write(
                keyFile.AdminName,
                "admin.recovery.issued",
                AdminAr.AccountAudit.RecoverySheetIssued,
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sheet_number"] = sheetNumber,
                });

            return new AdminCreateResult(AdminAccountRefusal.None, new AdminRecoverySheet
            {
                CodeDisplay = recoveryCode.Display,
                QrDataUrl = AdminRecoveryQr.ToDataUrl(recoveryCode),
                IssuedAt = now,
                OrgName = orgName.Trim(),
                AdminName = keyFile.AdminName,
                SheetNumber = sheetNumber,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or CryptoException or InvalidOperationException
                                              or Microsoft.Data.Sqlite.SqliteException)
        {
            // A folder that refuses to be written to, a full disk, a half-written file from an
            // interrupted earlier run. A01 says so in words and offers to try again.
            return new AdminCreateResult(AdminAccountRefusal.StorageFailed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
            CryptographicOperations.ZeroMemory(recoveryCode.KeyMaterial);
        }
    }

    /// <summary>A02: opens the tool with the administrator password.</summary>
    public AdminSignInResult SignIn(string password)
    {
        var keyFile = TryLoadKeyFile();
        if (keyFile?.PasswordWrap is null)
        {
            // A file that is there but unreadable is not «no account yet»: saying so would send
            // A02 to the first-run screen, which bounces straight back here because the file does
            // exist, and the person would watch the screen flicker instead of being told anything.
            return new AdminSignInResult(AbsentOrUnreadable());
        }

        var now = _time.GetUtcNow();
        if (keyFile.LockedUntil > now)
        {
            return new AdminSignInResult(AdminAccountRefusal.LockedOut, 0, keyFile.LockedUntil);
        }

        byte[] databaseKey;
        try
        {
            databaseKey = KeyWraps.OpenWithPassword(keyFile.PasswordWrap, password, AdminKeyFile.DbKeyContext);
        }
        catch (CryptoException exception)
        {
            // A wrong password (or a wrap somebody has tampered with) is the person's business and
            // costs them an attempt. Every other complaint — a wrap missing its settings, two
            // disagreeing salts, a wrap from a newer build — is the stored file being unreadable,
            // and must be said in words rather than thrown out of the click handler.
            return exception.Code is ErrorCode.WrongPassword or ErrorCode.Tampered
                ? RecordWrongPassword(keyFile, now)
                : new AdminSignInResult(AdminAccountRefusal.StorageFailed);
        }

        try
        {
            _db.Open(databaseKey);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return new AdminSignInResult(AdminAccountRefusal.StorageFailed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
        }

        var failuresToReport = keyFile.FailedAttempts;
        var lockOutsToReport = keyFile.LockOutsToReport;
        ClearAttempts(keyFile);
        _session.SignIn(keyFile.AdminName);

        _audit.Write(keyFile.AdminName, "admin.signin", AdminAr.AccountAudit.SignedIn);
        WriteServedLockOuts(keyFile.AdminName, lockOutsToReport);
        if (failuresToReport > 0)
        {
            _audit.Write(
                keyFile.AdminName,
                "admin.signin.failed",
                AdminAr.AccountAudit.SignInFailed,
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["attempts"] = failuresToReport.ToString(CultureInfo.InvariantCulture),
                });
        }

        return new AdminSignInResult(AdminAccountRefusal.None, _options.MaxAttempts);
    }

    /// <summary>Closes the database and forgets who was signed in.</summary>
    public void SignOut()
    {
        if (_session.IsSignedIn)
        {
            _audit.Write(_session.AdminName, "admin.signout", AdminAr.AccountAudit.SignedOut);
        }

        _db.Close();
        _session.SignOut();
    }

    /// <summary>
    /// A02's recovery: the printed organisation code opens the same database key, a new password
    /// wrap replaces the old one, and the tool opens. The recovery sheet itself stays valid.
    /// </summary>
    public AdminSignInResult Recover(string recoveryCodeText, string newPassword, string confirmation)
    {
        var keyFile = TryLoadKeyFile();
        if (keyFile?.RecoveryWrap is null)
        {
            return new AdminSignInResult(AbsentOrUnreadable());
        }

        if (!RecoveryCode.TryParse(recoveryCodeText, out var code) || code is null)
        {
            return new AdminSignInResult(AdminAccountRefusal.MalformedRecoveryCode);
        }

        if (!AdminPasswordStrength.IsAcceptable(newPassword))
        {
            return new AdminSignInResult(AdminAccountRefusal.WeakPassword);
        }

        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
        {
            return new AdminSignInResult(AdminAccountRefusal.PasswordMismatch);
        }

        var lockOutsToReport = 0;
        byte[] databaseKey;
        try
        {
            databaseKey = KeyWraps.OpenWithRecoveryCode(keyFile.RecoveryWrap, code, AdminKeyFile.DbKeyContext);
        }
        catch (CryptoException exception)
        {
            // Same split as SignIn: a code that does not open the wrap is the person's business,
            // anything else means the stored file itself cannot be read.
            return new AdminSignInResult(exception.Code is ErrorCode.WrongPassword or ErrorCode.Tampered
                ? AdminAccountRefusal.WrongRecoveryCode
                : AdminAccountRefusal.StorageFailed);
        }

        try
        {
            // The database is opened before the new password is written down. If opening fails the
            // person is told so and their old password still works; the other order would change
            // the password silently underneath a screen that only says it could not read the data.
            _db.Open(databaseKey);

            var kdf = _options.Kdf ?? Argon2Kdf.AutoTune(_options.KdfTargetMs, _time);
            keyFile.SetDbKeyWrap(KeyWraps.FromPassword(newPassword, databaseKey, kdf, AdminKeyFile.DbKeyContext));
            keyFile.FailedAttempts = 0;
            keyFile.LockOutRound = 0;
            keyFile.LockedUntil = null;
            lockOutsToReport = keyFile.LockOutsToReport;
            keyFile.LockOutsToReport = 0;

            // No backup, and any older one goes: the password being replaced here is usually one
            // that was forgotten or seen by somebody else, and a backup would keep its wrap of this
            // very same database key sitting on disk, opening the organisation for good.
            keyFile.Save(_paths.KeyFile, _time, keepBackup: false);
            RemoveRetiredKeyFileBackup();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Whether the database refused to open or the new wrap refused to be written, nothing
            // half-opened is left behind and the old password is still the one that works.
            _db.Close();
            return new AdminSignInResult(AdminAccountRefusal.StorageFailed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
        }

        _session.SignIn(keyFile.AdminName);
        WritePasswordChanged(_time.GetUtcNow());
        WriteServedLockOuts(keyFile.AdminName, lockOutsToReport);
        _audit.Write(keyFile.AdminName, "admin.password.recovered", AdminAr.AccountAudit.PasswordRecovered);

        return new AdminSignInResult(AdminAccountRefusal.None, _options.MaxAttempts);
    }

    /// <summary>Seconds between now and <paramref name="until"/>, rounded up, never below zero.</summary>
    public int SecondsUntil(DateTimeOffset? until)
    {
        if (until is null)
        {
            return 0;
        }

        var remaining = until.Value - _time.GetUtcNow();
        return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
    }

    /// <summary>
    /// Which refusal to give when the key file did not yield the wrap that was wanted: there is no
    /// account on this computer at all, or there is one whose file cannot be read.
    /// </summary>
    private AdminAccountRefusal AbsentOrUnreadable() => AdminKeyFile.Exists(_paths.KeyFile)
        ? AdminAccountRefusal.StorageFailed
        : AdminAccountRefusal.NotCreated;

    private AdminSignInResult RecordWrongPassword(AdminKeyFile keyFile, DateTimeOffset now)
    {
        keyFile.FailedAttempts++;

        if (keyFile.FailedAttempts >= _options.MaxAttempts)
        {
            keyFile.LockOutRound++;
            // The log cannot be written now — the database is shut, which is the whole point of the
            // lock-out — so the count waits in the key file until something opens it. It is capped:
            // somebody who keeps triggering lock-outs at the machine must not be able to queue an
            // unbounded pile of identical rows that all land at once on the one screen meant to make
            // an attack visible. Past the cap the rows stop, not the lock-out.
            keyFile.LockOutsToReport = Math.Min(keyFile.LockOutsToReport + 1, MaxLockOutsToReport);
            keyFile.FailedAttempts = 0;
            keyFile.LockedUntil = now.AddSeconds(_options.LockOutSecondsForRound(keyFile.LockOutRound));
            TrySaveKeyFile(keyFile);
            return new AdminSignInResult(AdminAccountRefusal.LockedOut, 0, keyFile.LockedUntil);
        }

        TrySaveKeyFile(keyFile);
        return new AdminSignInResult(
            AdminAccountRefusal.WrongPassword,
            Math.Max(0, _options.MaxAttempts - keyFile.FailedAttempts));
    }

    private void ClearAttempts(AdminKeyFile keyFile)
    {
        if (keyFile is { FailedAttempts: 0, LockOutRound: 0, LockedUntil: null, LockOutsToReport: 0 })
        {
            return;
        }

        keyFile.FailedAttempts = 0;
        keyFile.LockOutRound = 0;
        keyFile.LockedUntil = null;
        keyFile.LockOutsToReport = 0;
        TrySaveKeyFile(keyFile);
    }

    /// <summary>
    /// Writes one operations-log row per temporary lock-out that was served while the database was
    /// shut. It is the one security event of A02 that could otherwise leave no trace at all: the
    /// lock-out happens precisely when nothing can be written, so it is counted then and recorded
    /// the moment a password or a recovery code opens the database again.
    /// </summary>
    private void WriteServedLockOuts(string adminName, int lockOuts)
    {
        for (var index = 0; index < lockOuts; index++)
        {
            _audit.Write(adminName, "admin.signin.lockedout", AdminAr.AccountAudit.LockedOut);
        }
    }

    /// <summary>
    /// Removes <c>admin.key.bak</c> after a save that changed the wraps. <see cref="AdminKeyFile.Save"/>
    /// already does this; doing it again here costs nothing and means a backup that was locked for a
    /// moment does not quietly outlive the wrap it holds.
    /// </summary>
    private void RemoveRetiredKeyFileBackup()
    {
        try
        {
            var backup = _paths.KeyFile + ".bak";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Same discipline as TryDeleteDatabase: a file that will not be deleted right now is
            // not a reason to refuse the person in front of the screen.
        }
    }

    private void WriteAccountRow(string adminName, DateTimeOffset now)
    {
        var stamp = now.ToString("O", CultureInfo.InvariantCulture);
        _db.Execute(
            """
            INSERT INTO admin_account(id, display_name, password_changed_at, created_at)
            VALUES (1, $name, $at, $at);
            """,
            ("$name", adminName),
            ("$at", stamp));
    }

    private void WritePasswordChanged(DateTimeOffset now)
    {
        if (_db.IsOpen)
        {
            _db.Execute(
                "UPDATE admin_account SET password_changed_at = $at WHERE id = 1;",
                ("$at", now.ToString("O", CultureInfo.InvariantCulture)));
        }
    }

    private AdminKeyFile? TryLoadKeyFile()
    {
        try
        {
            return AdminKeyFile.Exists(_paths.KeyFile) ? AdminKeyFile.Load(_paths.KeyFile) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptoException)
        {
            // A file this computer cannot read is the same to the screens as none at all; A02 then
            // says the tool's data could not be read and points at «الصيانة».
            return null;
        }
    }

    private void TrySaveKeyFile(AdminKeyFile keyFile)
    {
        try
        {
            keyFile.Save(_paths.KeyFile, _time);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A counter that could not be written back is not a reason to refuse to answer the
            // person in front of the screen; the next attempt simply starts from what is on disk.
        }
    }

    private void TryDeleteDatabase()
    {
        try
        {
            if (File.Exists(_paths.DatabaseFile))
            {
                File.Delete(_paths.DatabaseFile);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leaving a stray file behind is better than throwing over it; the next attempt sets
            // it aside before it opens its own.
        }
    }

    /// <summary>
    /// Moves a database file that has no key file beside it out of the way. Without the key file
    /// there are no wraps, so that database can never be opened by anybody again and keeping it
    /// only stops a fresh attempt from succeeding. It is renamed rather than deleted so that the
    /// bytes are still there if somebody later finds the key file they thought they had lost.
    /// </summary>
    private void SetAsideOrphanedDatabase()
    {
        try
        {
            if (!File.Exists(_paths.DatabaseFile) || File.Exists(_paths.KeyFile))
            {
                return;
            }

            var stamp = _time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var target = Path.Combine(_paths.Root, $"admin.db.orphaned-{stamp}");
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(_paths.DatabaseFile, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // If it cannot be moved it is deleted instead, and if that fails too the attempt below
            // refuses in words rather than throwing.
            TryDeleteDatabase();
        }
    }
}
