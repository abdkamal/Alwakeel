using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>How an attempt to recover the account with the printed sheet ended.</summary>
public enum RecoveryOutcome
{
    Success,

    /// <summary>The text is not a complete recovery code, or its check character disagrees.</summary>
    CodeIncomplete,

    /// <summary>A well formed code that does not open this installation.</summary>
    CodeWrong,

    /// <summary>This installation has no recovery wrap — nothing here can be recovered this way.</summary>
    KeysMissing,

    /// <summary>The chosen new password is not acceptable.</summary>
    PasswordRejected,

    /// <summary>The key opened but the database behind it would not.</summary>
    DatabaseUnreadable,

    /// <summary>Too many wrong codes in a row; the dialog waits out the same pause the sign-in screen does.</summary>
    LockedOut,

    /// <summary>
    /// The new password and the new sheet are real and on disk, but the installation did not open
    /// afterwards, so this attempt ends outside the workspace instead of inside it.
    /// </summary>
    RecoveredNotSignedIn,
}

/// <summary>What the recovery dialog shows after an attempt.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Message">The Arabic sentence to show, or null when it succeeded.</param>
/// <param name="NewSheet">The freshly issued sheet, when a new one was asked for.</param>
public sealed record RecoveryResult(RecoveryOutcome Outcome, string? Message, RecoverySheet? NewSheet = null)
{
    public bool Succeeded => Outcome == RecoveryOutcome.Success;
}

/// <summary>
/// W07: the printed recovery sheet is the second of the three human secrets (AGREEMENT item 17) and
/// the only way back into an account whose password is gone. It opens the recovery wrap, writes a
/// fresh password wrap over the old one — which is what makes the forgotten password stop working —
/// and optionally issues a new sheet, retiring the code that was just used.
/// </summary>
public sealed class RecoveryService
{
    private readonly WakeelPaths _paths;
    private readonly IPlatformProtector _protector;
    private readonly TimeProvider _time;
    private readonly IClock _clock;
    private readonly AccountSession _session;
    private readonly SignInProfileStore _profiles;
    private readonly LoginService _login;
    private readonly AccountOptions _options;

    /// <summary>The last wrong code already counted, so re-checking the same text costs no attempt.</summary>
    private string? _lastCountedCode;

    public RecoveryService(
        WakeelPaths paths,
        IPlatformProtector protector,
        TimeProvider time,
        IClock clock,
        AccountSession session,
        SignInProfileStore profiles,
        LoginService login,
        AccountOptions options)
    {
        _paths = paths;
        _protector = protector;
        _time = time;
        _clock = clock;
        _session = session;
        _profiles = profiles;
        _login = login;
        _options = options;
    }

    /// <summary>
    /// Checks a typed or scanned code without changing anything, so the dialog can confirm the first
    /// of its two steps before asking for a new password.
    /// </summary>
    public RecoveryOutcome Verify(string? codeText)
    {
        if (!RecoveryCode.TryParse(codeText, out var code) || code is null)
        {
            return RecoveryOutcome.CodeIncomplete;
        }

        // The sheet is a secret like the password, and it is tried against the same key file, so it
        // waits out the same pause. Without this the dialog would be the one place in the product
        // where a secret may be guessed at without limit.
        if (_login.LockOutInForce(_time.GetUtcNow()) is not null)
        {
            return RecoveryOutcome.LockedOut;
        }

        InstallationKeyFile keyFile;
        try
        {
            keyFile = InstallationKeyFile.Load(_paths.InstallationKeyPath);
        }
        catch (CryptoException)
        {
            return RecoveryOutcome.KeysMissing;
        }

        if (keyFile.FindDbKeyWrap(KeyWrapKind.Recovery) is not { } wrap)
        {
            return RecoveryOutcome.KeysMissing;
        }

        try
        {
            var key = KeyWraps.OpenWithRecoveryCode(wrap, code, KeyWraps.DbKeyContext);
            CryptographicOperations.ZeroMemory(key);
            return RecoveryOutcome.Success;
        }
        catch (CryptoException exception)
        {
            // A code that simply does not fit is one thing; a stored key that is damaged — truncated,
            // missing its derivation settings, carrying two different salts — is another, and both
            // must leave here as a state the dialog can draw rather than as an exception thrown out
            // of a keystroke handler (ARCHITECTURE.md §10).
            if (exception.Code != ErrorCode.WrongPassword)
            {
                return RecoveryOutcome.KeysMissing;
            }

            CountWrongCode(codeText);
            return RecoveryOutcome.CodeWrong;
        }
    }

    /// <summary>
    /// Counts one wrong code against the same attempt counter the password uses. The dialog checks
    /// the text it has after every keystroke, so the same wrong code must not be counted twice: only
    /// a code different from the last one counted is a fresh attempt.
    /// </summary>
    private void CountWrongCode(string? codeText)
    {
        // A successful password sign-in resets the shared attempt counter (LoginService.OpenSessionAsync);
        // once that has happened, a code that was already counted against the old streak is a fresh
        // attempt again, not a repeat of the same keystroke.
        if (_login.Profile().FailedAttempts == 0)
        {
            _lastCountedCode = null;
        }

        var normalized = RecoveryCode.Normalize(codeText);
        if (string.Equals(normalized, _lastCountedCode, StringComparison.Ordinal))
        {
            return;
        }

        _lastCountedCode = normalized;
        _login.RecordWrongPassword(_time.GetUtcNow());
    }

    /// <summary>
    /// The sentence for a verification outcome, including the wait a standing lock-out still has to
    /// run, which only an open service can work out.
    /// </summary>
    public string Explain(RecoveryOutcome outcome)
    {
        if (outcome != RecoveryOutcome.LockedOut)
        {
            return Describe(outcome);
        }

        if (_login.LockedUntil() is not { } until)
        {
            return Ar.FirstRun.Login.LockedOutNow;
        }

        var minutes = (int)Math.Ceiling(Math.Max((until - _time.GetUtcNow()).TotalMinutes, 0));
        return minutes <= 0 ? Ar.FirstRun.Login.LockedOutNow : Ar.FirstRun.Login.LockedOut(minutes);
    }

    /// <summary>The Arabic sentence for a verification outcome.</summary>
    public static string Describe(RecoveryOutcome outcome) => outcome switch
    {
        RecoveryOutcome.Success => Ar.FirstRun.Recovery.CodeVerified,
        RecoveryOutcome.CodeIncomplete => Ar.FirstRun.Recovery.CodeIncomplete,
        RecoveryOutcome.CodeWrong => Ar.FirstRun.Recovery.CodeWrong,
        RecoveryOutcome.KeysMissing => Ar.FirstRun.Login.KeysMissing,
        RecoveryOutcome.PasswordRejected => Ar.FirstRun.Account.ConfirmMismatch,
        RecoveryOutcome.LockedOut => Ar.FirstRun.Login.LockedOutNow,
        RecoveryOutcome.RecoveredNotSignedIn => Ar.FirstRun.Recovery.SucceededNotSignedIn,
        _ => Ar.FirstRun.Recovery.Failed,
    };

    /// <summary>
    /// Sets a new account password from the recovery sheet and signs in with it. The old password
    /// stops working the moment this returns, because its wrap is gone.
    /// </summary>
    public async Task<RecoveryResult> RecoverAsync(
        string? codeText,
        string newPassword,
        bool issueNewSheet,
        CancellationToken cancellationToken = default)
    {
        if (!PasswordStrength.IsAcceptable(newPassword))
        {
            return new RecoveryResult(RecoveryOutcome.PasswordRejected, Ar.FirstRun.Account.RuleLength);
        }

        if (!RecoveryCode.TryParse(codeText, out var code) || code is null)
        {
            return new RecoveryResult(RecoveryOutcome.CodeIncomplete, Ar.FirstRun.Recovery.CodeIncomplete);
        }

        if (_login.LockOutInForce(_time.GetUtcNow()) is not null)
        {
            return new RecoveryResult(RecoveryOutcome.LockedOut, Explain(RecoveryOutcome.LockedOut));
        }

        InstallationKeyFile keyFile;
        try
        {
            keyFile = InstallationKeyFile.Load(_paths.InstallationKeyPath);
        }
        catch (CryptoException)
        {
            return new RecoveryResult(RecoveryOutcome.KeysMissing, Ar.FirstRun.Login.KeysMissing);
        }

        if (keyFile.FindDbKeyWrap(KeyWrapKind.Recovery) is not { } dbWrap
            || keyFile.FindVaultKeyWrap(KeyWrapKind.Recovery) is not { } vaultWrap)
        {
            return new RecoveryResult(RecoveryOutcome.KeysMissing, Ar.FirstRun.Login.KeysMissing);
        }

        // Declared out here so the failure path zeroes them too; see LoginService.SignInAsync.
        var dbKey = Array.Empty<byte>();
        var vaultKey = Array.Empty<byte>();
        try
        {
            dbKey = KeyWraps.OpenWithRecoveryCode(dbWrap, code, KeyWraps.DbKeyContext);
            vaultKey = KeyWraps.OpenWithRecoveryCode(vaultWrap, code, KeyWraps.VaultKeyContext);
        }
        catch (CryptoException exception)
        {
            CryptographicOperations.ZeroMemory(dbKey);
            CryptographicOperations.ZeroMemory(vaultKey);

            if (exception.Code != ErrorCode.WrongPassword)
            {
                return new RecoveryResult(RecoveryOutcome.KeysMissing, Ar.FirstRun.Login.KeysMissing);
            }

            CountWrongCode(codeText);
            return new RecoveryResult(RecoveryOutcome.CodeWrong, Ar.FirstRun.Recovery.CodeWrong);
        }

        var now = _time.GetUtcNow();
        RecoverySheet? newSheet = null;

        // Set the moment the replaced wraps reach the disk. After that the recovery has happened
        // whatever else goes wrong, and reporting a failure would be a lie that costs the person the
        // old password, the old sheet and the replacement code all at once.
        var saved = false;

        try
        {
            // Nothing is written until the installation has actually opened with the recovered key:
            // a database that refuses to open is the one failure likely enough to plan for, and it
            // must not cost the person a working key file on the way past.
            var database = DbSession.Open(_paths, dbKey, _clock);
            try
            {
                var kdf = _options.Kdf ?? Argon2Kdf.AutoTune(_options.KdfTargetMs, _time);

                // The forgotten password's wrap is replaced, not kept alongside: one password opens
                // this installation at a time, and the one just forgotten is not it any more.
                keyFile.SetDbKeyWrap(
                    KeyWraps.FromPassword(newPassword, dbKey, kdf.WithFreshSalt(), KeyWraps.DbKeyContext));
                keyFile.SetVaultKeyWrap(
                    KeyWraps.FromPassword(newPassword, vaultKey, kdf.WithFreshSalt(), KeyWraps.VaultKeyContext));

                // The machine wrap is re-made too, so the next automatic lock opens against keys this
                // machine sealed itself rather than whatever a restored profile left behind.
                keyFile.SetDbKeyWrap(KeyWraps.FromMachine(_protector, dbKey, KeyWraps.DbKeyContext));
                keyFile.SetVaultKeyWrap(KeyWraps.FromMachine(_protector, vaultKey, KeyWraps.VaultKeyContext));

                RecoveryCode? replacement = null;
                if (issueNewSheet)
                {
                    replacement = RecoveryCode.Generate();
                    keyFile.SetDbKeyWrap(
                        KeyWraps.FromRecoveryCode(replacement, dbKey, kdf.WithFreshSalt(), KeyWraps.DbKeyContext));
                    keyFile.SetVaultKeyWrap(
                        KeyWraps.FromRecoveryCode(replacement, vaultKey, kdf.WithFreshSalt(), KeyWraps.VaultKeyContext));
                }

                keyFile.Save(_paths.InstallationKeyPath, _time);
                saved = true;

                var profile = _profiles.Load();
                if (replacement is not null)
                {
                    profile.RecoveryIssuedAt = now;
                    newSheet = new RecoverySheet
                    {
                        CodeDisplay = replacement.Display,
                        QrDataUrl = RecoveryQr.ToDataUrl(replacement),
                        IssuedAt = now,
                        OrgName = profile.OrgName,
                        OfficeName = profile.OfficeName,
                        EmployeeName = profile.EmployeeName,
                        DeviceNo = profile.DeviceNo,
                        LogoDataUrl = profile.LogoDataUrl,
                    };
                }

                var summary = replacement is null
                    ? Ar.FirstRun.Audit.Recovered
                    : Ar.FirstRun.Audit.Recovered + " • " + Ar.FirstRun.Audit.RecoverySheetReissued;

                await _login.OpenSessionAsync(
                    database,
                    profile,
                    vaultKey,
                    now,
                    cancellationToken,
                    afterLock: false,
                    extraAuditSummary: summary).ConfigureAwait(false);
                vaultKey = [];

                var installations = new InstallationService(_session.Db, _clock);
                await installations.SetPasswordChangedAtAsync(now.UtcDateTime, cancellationToken).ConfigureAwait(false);
                var account = await _session.Db.Account.AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                _session.RefreshAccount(account, _session.AutoLockMinutes);
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (!saved && exception is Microsoft.Data.Sqlite.SqliteException
                                              or InvalidOperationException or IOException
                                              or UnauthorizedAccessException)
        {
            // Nothing reached the disk, so the old password and the old sheet still open this
            // installation and the attempt cost the person nothing.
            return new RecoveryResult(RecoveryOutcome.DatabaseUnreadable, Ar.FirstRun.Recovery.Failed);
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException
                                              or InvalidOperationException or IOException
                                              or UnauthorizedAccessException)
        {
            // The new password is already the only one that opens this installation, so this is never
            // reported as a failure: the new sheet comes back to be printed, together with the one
            // sentence saying the workspace did not open and الوكيل has to be started again.
            return new RecoveryResult(
                RecoveryOutcome.RecoveredNotSignedIn,
                Ar.FirstRun.Recovery.SucceededNotSignedIn,
                newSheet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dbKey);
            CryptographicOperations.ZeroMemory(vaultKey);
        }

        // The counter's memory of the last wrong code must not outlive a recovery that succeeded — a
        // stale entry costing no attempt on a later, unrelated brute-force run is a defect, not a
        // convenience, even though it never fires against fresh codes.
        _lastCountedCode = null;

        return new RecoveryResult(RecoveryOutcome.Success, Ar.FirstRun.Recovery.Succeeded, newSheet);
    }
}
