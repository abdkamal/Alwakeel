using System.Security.Cryptography;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// W06: the idle timer that closes the database after the inactivity the settings name (AGREEMENT
/// item 7, ten minutes by default), and the password check that opens it again.
/// </summary>
/// <remarks>
/// Re-entry follows ARCHITECTURE.md §3 exactly: the password is what proves who is at the keyboard,
/// and the machine wrap — which is bound to this machine and useless anywhere else — is what hands
/// the keys back quickly. The machine wrap alone never opens anything, because it is only ever
/// consulted after the password has already opened its own wrap in the same session.
/// </remarks>
public sealed class LockService : IDisposable
{
    private readonly AccountSession _session;
    private readonly WakeelPaths _paths;
    private readonly IPlatformProtector _protector;
    private readonly TimeProvider _time;
    private readonly IClock _clock;
    private readonly SignInProfileStore _profiles;
    private readonly LoginService _login;
    private ITimer? _timer;
    private bool _disposed;

    public LockService(
        AccountSession session,
        WakeelPaths paths,
        IPlatformProtector protector,
        TimeProvider time,
        IClock clock,
        SignInProfileStore profiles,
        LoginService login)
    {
        _session = session;
        _paths = paths;
        _protector = protector;
        _time = time;
        _clock = clock;
        _profiles = profiles;
        _login = login;
    }

    /// <summary>How often the idle timer looks at the clock.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>Starts watching for inactivity. Calling it twice does nothing the second time.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer ??= _time.CreateTimer(_ => Tick(), null, TickInterval, TickInterval);
    }

    /// <summary>
    /// Locks the session if it has been idle for longer than the settings allow. Public so the
    /// walkthrough suite can drive it without waiting out ten real minutes.
    /// </summary>
    public bool LockIfIdle()
    {
        if (!_session.IsOpen)
        {
            return false;
        }

        var limit = TimeSpan.FromMinutes(Math.Max(1, _session.AutoLockMinutes));
        if (_session.IdleFor() < limit)
        {
            return false;
        }

        LockNow();
        return true;
    }

    /// <summary>
    /// Closes the database handle and wipes the vault key. Nothing is written first: every screen
    /// saves its draft as it goes (ARCHITECTURE.md §10), which is exactly what W06 promises.
    /// </summary>
    public void LockNow()
    {
        if (!_session.IsOpen)
        {
            return;
        }

        // Written while the database is still open, because once it closes there is nowhere to
        // write it; a failure here must never stop the lock from happening.
        try
        {
            _session.Db.AuditLog.Add(new Core.Data.Entities.AuditLogEntry
            {
                At = _clock.UtcNow,
                Actor = _session.Installation?.EmployeeName ?? Ar.FirstRun.Audit.ActorSystem,
                Action = "account.lock",
                EntityType = "account",
                EntityId = _session.Installation?.DeviceId,
                SummaryAr = Ar.FirstRun.Audit.Locked,
            });
            _session.Db.SaveChanges();
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException
                                              or InvalidOperationException or IOException)
        {
            // The session locks regardless; an unwritten line in the log is never a reason to leave
            // an unattended machine open.
        }

        _session.Lock();
    }

    /// <summary>
    /// Opens the locked session again. The password is checked against its own wrap; the keys then
    /// come from the machine wrap, and from the password wrap when this machine no longer has one.
    /// </summary>
    public async Task<SignInResult> UnlockAsync(string? password, CancellationToken cancellationToken = default)
    {
        if (!_session.IsLocked)
        {
            return new SignInResult { Outcome = SignInOutcome.Success };
        }

        var now = _time.GetUtcNow();

        // The overlay counts and waits exactly as the sign-in screen does. An unattended machine
        // that locked itself is the likeliest one to be guessed at, so it must not be the one place
        // where guessing costs nothing.
        if (_login.LockOutInForce(now) is { } standing)
        {
            return standing;
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

        // Declared out here so the failure path zeroes them too; see LoginService.SignInAsync.
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

            return exception.Code == ErrorCode.WrongPassword
                ? _login.RecordWrongPassword(now)
                : new SignInResult
                {
                    Outcome = SignInOutcome.DatabaseUnreadable,
                    Message = Ar.FirstRun.Login.DatabaseUnreadable,
                };
        }

        // Now that the person has proved who they are, the machine's own copy is what actually
        // re-opens the session — that is the single purpose the machine wrap exists for.
        var machineDbKey = TryMachine(keyFile.FindDbKeyWrap(KeyWrapKind.Machine), KeyWraps.DbKeyContext) ?? dbKey;
        var machineVaultKey = TryMachine(keyFile.FindVaultKeyWrap(KeyWrapKind.Machine), KeyWraps.VaultKeyContext) ?? vaultKey;

        try
        {
            _session.Unlock(machineDbKey, machineVaultKey);
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // Nobody took these over, so they are wiped here rather than left in memory: the session
            // only owns the machine copies once Unlock has actually returned.
            CryptographicOperations.ZeroMemory(machineDbKey);
            CryptographicOperations.ZeroMemory(machineVaultKey);

            return new SignInResult
            {
                Outcome = SignInOutcome.DatabaseUnreadable,
                Message = Ar.FirstRun.Login.DatabaseUnreadable,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dbKey);
            if (!ReferenceEquals(machineVaultKey, vaultKey))
            {
                CryptographicOperations.ZeroMemory(vaultKey);
            }
        }

        var audit = new AuditService(_session.Db, _clock);
        var name = _session.Installation?.EmployeeName ?? Ar.FirstRun.Audit.ActorSystem;
        var profile = _profiles.Load();

        // The wrong passwords tried against the overlay could not be logged while the database was
        // shut; now that it is open again they are written as one entry, exactly as a sign-in does.
        if (profile.UnloggedFailures > 0)
        {
            await audit.LogAsync(
                name,
                "account.sign_in_failed",
                Ar.FirstRun.Audit.FailedAttempts(profile.UnloggedFailures),
                "account",
                _session.Installation?.DeviceId,
                new { attempts = profile.UnloggedFailures },
                cancellationToken).ConfigureAwait(false);
        }

        await audit.LogAsync(
            name,
            "account.unlock",
            Ar.FirstRun.Audit.SignedInAfterLock,
            "account",
            _session.Installation?.DeviceId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        profile.FailedAttempts = 0;
        profile.UnloggedFailures = 0;
        profile.LockedUntil = null;
        profile.LockRounds = 0;
        profile.LastSignInAt = now;
        _profiles.Save(profile);

        return new SignInResult { Outcome = SignInOutcome.Success };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        try
        {
            LockIfIdle();
        }
        catch (ObjectDisposedException)
        {
            // The window closed between the tick firing and this running; there is nothing to lock.
        }
    }

    private byte[]? TryMachine(KeyWrap? wrap, string context)
    {
        if (wrap is null)
        {
            return null;
        }

        try
        {
            return KeyWraps.OpenWithMachine(wrap, _protector, context);
        }
        catch (CryptoException)
        {
            // A machine wrap this machine can no longer open (a restored profile, a new user account)
            // is not a failure: the password already produced the same keys a moment ago.
            return null;
        }
    }
}
