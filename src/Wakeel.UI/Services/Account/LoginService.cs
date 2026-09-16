using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>How a sign-in attempt ended.</summary>
public enum SignInOutcome
{
    Success,

    /// <summary>The password did not open the stored key.</summary>
    WrongPassword,

    /// <summary>Too many wrong passwords in a row; the screen refuses to try for a while.</summary>
    LockedOut,

    /// <summary>This installation has no key file — nothing here can be opened by anyone.</summary>
    KeysMissing,

    /// <summary>The key opened but the database behind it would not.</summary>
    DatabaseUnreadable,
}

/// <summary>What the sign-in screen shows after an attempt.</summary>
public sealed record SignInResult
{
    public required SignInOutcome Outcome { get; init; }

    /// <summary>The Arabic sentence under the password field, or null when the attempt succeeded.</summary>
    public string? Message { get; init; }

    /// <summary>Which attempt this was, counting from one.</summary>
    public int Attempt { get; init; }

    /// <summary>How many wrong passwords in a row are tolerated.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>How many attempts are left before the temporary lock-out.</summary>
    public int Remaining { get; init; }

    /// <summary>Minutes of the lock-out that is coming, or the one currently in force.</summary>
    public int LockMinutes { get; init; }

    /// <summary>When the current lock-out ends, when there is one.</summary>
    public DateTimeOffset? LockedUntil { get; init; }

    public bool Succeeded => Outcome == SignInOutcome.Success;

    /// <summary>The line «المحاولة 3 من 5 • بقيت محاولتان...», or null when there is nothing to count.</summary>
    public string? AttemptLine => Outcome == SignInOutcome.WrongPassword && Attempt > 0
        ? Ar.FirstRun.Login.AttemptLine(Attempt, MaxAttempts, Remaining, LockMinutes)
        : null;
}

/// <summary>
/// W05 and W07: the password, the attempt counter, the temporary lock-out, and what happens once a
/// password actually opens the installation.
/// </summary>
/// <remarks>
/// The attempt counter cannot live where DATA-MODEL.md puts it and nowhere else, because a wrong
/// password never opens the database the <c>account</c> row sits in. So it is kept in the sealed
/// sign-in card next to the keys, and mirrored into the <c>account</c> row — together with one audit
/// entry summarising the wrong attempts — the next time a right password does open the database.
/// </remarks>
public sealed class LoginService
{
    private readonly WakeelPaths _paths;
    private readonly IPlatformProtector _protector;
    private readonly TimeProvider _time;
    private readonly IClock _clock;
    private readonly AccountSession _session;
    private readonly SignInProfileStore _profiles;
    private readonly AccountOptions _options;

    public LoginService(
        WakeelPaths paths,
        IPlatformProtector protector,
        TimeProvider time,
        IClock clock,
        AccountSession session,
        SignInProfileStore profiles,
        AccountOptions options)
    {
        _paths = paths;
        _protector = protector;
        _time = time;
        _clock = clock;
        _session = session;
        _profiles = profiles;
        _options = options;
    }

    /// <summary>Whether this machine carries an installation at all (key file plus database).</summary>
    public bool IsActivated => File.Exists(_paths.InstallationKeyPath) && File.Exists(_paths.DbPath);

    /// <summary>The sealed card the sign-in screen draws itself from.</summary>
    public SignInProfile Profile() => _profiles.Load();

    /// <summary>The lock-out currently in force, or null when sign in is allowed right now.</summary>
    public DateTimeOffset? LockedUntil()
    {
        var profile = _profiles.Load();
        return profile.LockedUntil is { } until && until > _time.GetUtcNow() ? until : null;
    }

    /// <summary>Signs in with the account password, opening the database and the session behind it.</summary>
    public async Task<SignInResult> SignInAsync(string? password, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var profile = _profiles.Load();

        if (profile.LockedUntil is { } until && until > now)
        {
            return LockedOutResult(until, now);
        }

        if (!IsActivated)
        {
            return new SignInResult { Outcome = SignInOutcome.KeysMissing, Message = Ar.FirstRun.Login.KeysMissing };
        }

        InstallationKeyFile keyFile;
        try
        {
            keyFile = InstallationKeyFile.Load(_paths.InstallationKeyPath);
        }
        catch (CryptoException)
        {
            return new SignInResult { Outcome = SignInOutcome.KeysMissing, Message = Ar.FirstRun.Login.KeysMissing };
        }

        if (keyFile.FindDbKeyWrap(KeyWrapKind.Password) is not { } dbWrap
            || keyFile.FindVaultKeyWrap(KeyWrapKind.Password) is not { } vaultWrap)
        {
            return new SignInResult { Outcome = SignInOutcome.KeysMissing, Message = Ar.FirstRun.Login.KeysMissing };
        }

        // Declared out here so that the failure path zeroes them too: a key file written by two
        // different passwords opens the first wrap and then refuses the second, and the plaintext
        // key the first one produced must not survive that.
        var dbKey = Array.Empty<byte>();
        var vaultKey = Array.Empty<byte>();
        try
        {
            dbKey = KeyWraps.OpenWithPassword(dbWrap, password ?? string.Empty, KeyWraps.DbKeyContext);
            vaultKey = KeyWraps.OpenWithPassword(vaultWrap, password ?? string.Empty, KeyWraps.VaultKeyContext);
        }
        catch (CryptoException exception)
        {
            CryptographicOperations.ZeroMemory(dbKey);
            CryptographicOperations.ZeroMemory(vaultKey);

            // A wrong password is the everyday case and is counted. Anything else means the stored
            // key itself is damaged — a truncated file, two disagreeing salts, a kind this build
            // does not know — and that is a state with its own sentence and its own action, not an
            // exception escaping into the screen (ARCHITECTURE.md §10).
            return exception.Code == ErrorCode.WrongPassword
                ? RecordFailure(profile, now)
                : new SignInResult
                {
                    Outcome = SignInOutcome.DatabaseUnreadable,
                    Message = Ar.FirstRun.Login.DatabaseUnreadable,
                };
        }

        try
        {
            var database = DbSession.Open(_paths, dbKey, _clock);
            try
            {
                await OpenSessionAsync(database, profile, vaultKey, now, cancellationToken).ConfigureAwait(false);
                vaultKey = [];
                return new SignInResult { Outcome = SignInOutcome.Success, MaxAttempts = _options.MaxAttempts };
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException
                                              or IOException or UnauthorizedAccessException)
        {
            return new SignInResult
            {
                Outcome = SignInOutcome.DatabaseUnreadable,
                Message = Ar.FirstRun.Login.DatabaseUnreadable,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dbKey);
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    /// <summary>
    /// Everything that happens once a password has opened the installation: the identity is read,
    /// the wrong attempts that could not be logged at the time are written as one entry, the account
    /// row is brought back in line with the sealed card, and the session opens.
    /// </summary>
    internal async Task OpenSessionAsync(
        DbSession database,
        SignInProfile profile,
        byte[] vaultKey,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool afterLock = false,
        string? extraAuditSummary = null)
    {
        var db = database.Db;
        var installations = new InstallationService(db, _clock);
        var installation = await installations.GetInstallationAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The database carries no installation identity.");

        var audit = new AuditService(db, _clock);
        var actor = installation.EmployeeName;

        if (profile.UnloggedFailures > 0)
        {
            await audit.LogAsync(
                actor,
                "account.sign_in_failed",
                Ar.FirstRun.Audit.FailedAttempts(profile.UnloggedFailures),
                "account",
                installation.DeviceId,
                new { attempts = profile.UnloggedFailures },
                cancellationToken).ConfigureAwait(false);
        }

        if (extraAuditSummary is not null)
        {
            await audit.LogAsync(
                actor,
                "account.recover",
                extraAuditSummary,
                "account",
                installation.DeviceId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await audit.LogAsync(
            actor,
            afterLock ? "account.unlock" : "account.sign_in",
            afterLock ? Ar.FirstRun.Audit.SignedInAfterLock : Ar.FirstRun.Audit.SignedIn,
            "account",
            installation.DeviceId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await installations.SetFailedAttemptsAsync(0, null, cancellationToken).ConfigureAwait(false);
        var account = await db.Account.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var autoLock = await installations.GetAutoLockMinutesAsync(cancellationToken).ConfigureAwait(false);

        profile.FailedAttempts = 0;
        profile.UnloggedFailures = 0;
        profile.LockedUntil = null;
        profile.LockRounds = 0;
        profile.LastSignInAt = now;
        profile.OrgName = installation.OrgName;
        profile.OfficeName = installation.OfficeName;
        profile.OfficeCode = installation.OfficeCode;
        profile.EmployeeName = installation.EmployeeName;
        profile.EmployeeNo = installation.EmployeeNo;
        profile.DeviceNo = installation.DeviceNo;
        _profiles.Save(profile);

        _session.Adopt(database, vaultKey, installation, account, autoLock);
    }

    /// <summary>
    /// The temporary lock-out in force right now, as the result a screen would show, or null when
    /// there is none. The lock overlay of W06 asks this too: an unattended machine that locked
    /// itself must not become the one place where passwords may be guessed without limit.
    /// </summary>
    internal SignInResult? LockOutInForce(DateTimeOffset now)
    {
        var profile = _profiles.Load();
        return profile.LockedUntil is { } until && until > now ? LockedOutResult(until, now) : null;
    }

    /// <summary>Counts one wrong password against the account and says what the screen should show.</summary>
    internal SignInResult RecordWrongPassword(DateTimeOffset now) => RecordFailure(_profiles.Load(), now);

    private SignInResult RecordFailure(SignInProfile profile, DateTimeOffset now)
    {
        profile.FailedAttempts++;
        profile.UnloggedFailures++;

        // The counter lives beside the keys, and on a machine somebody else can also use, a removed
        // card is a counter back at zero. It cannot be made unremovable from in here, but it can be
        // made worthless: a machine that is already activated and has no card serves at least the
        // second wait rather than the first, so throwing the card away buys nothing back.
        if (profile.Missing && IsActivated)
        {
            profile.LockRounds = Math.Max(profile.LockRounds, 1);
        }

        if (profile.FailedAttempts >= _options.MaxAttempts)
        {
            profile.LockRounds++;
            var minutes = LockMinutesFor(profile.LockRounds);
            profile.LockedUntil = now.AddMinutes(minutes);
            profile.FailedAttempts = 0;
            _profiles.Save(profile);
            return LockedOutResult(profile.LockedUntil.Value, now);
        }

        _profiles.Save(profile);
        return new SignInResult
        {
            Outcome = SignInOutcome.WrongPassword,
            Message = Ar.FirstRun.Login.WrongPassword,
            Attempt = profile.FailedAttempts,
            MaxAttempts = _options.MaxAttempts,
            Remaining = _options.MaxAttempts - profile.FailedAttempts,
            LockMinutes = LockMinutesFor(profile.LockRounds + 1),
        };
    }

    private SignInResult LockedOutResult(DateTimeOffset until, DateTimeOffset now)
    {
        var left = until - now;
        var minutes = (int)Math.Ceiling(Math.Max(left.TotalMinutes, 0));
        return new SignInResult
        {
            Outcome = SignInOutcome.LockedOut,
            Message = minutes <= 0 ? Ar.FirstRun.Login.LockedOutNow : Ar.FirstRun.Login.LockedOut(minutes),
            MaxAttempts = _options.MaxAttempts,
            LockMinutes = minutes,
            LockedUntil = until,
        };
    }

    /// <summary>The wait after the given number of served lock-outs: it doubles, and then it stops.</summary>
    private int LockMinutesFor(int rounds)
    {
        var minutes = (long)_options.LockOutMinutes << Math.Clamp(rounds - 1, 0, 10);
        return (int)Math.Min(minutes, AccountOptions.MaxLockOutMinutes);
    }
}
