using System.Security.Cryptography;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.UI.Services.Account;

/// <summary>Where the single operating account stands right now.</summary>
public enum SessionState
{
    /// <summary>No installation has been activated on this machine yet: W02 is the whole product.</summary>
    NotActivated,

    /// <summary>An installation exists but nobody has signed in: W05.</summary>
    SignedOut,

    /// <summary>Signed in and working.</summary>
    Open,

    /// <summary>Signed in, then locked by the idle timer: W06 sits over whatever screen was open.</summary>
    Locked,
}

/// <summary>
/// The one live session of الوكيل: the open database, the vault key while it is open, and who the
/// installation says is working. It is a singleton because there is exactly one account per
/// installation (AGREEMENT item 7) and exactly one window.
/// </summary>
/// <remarks>
/// The database key is deliberately not held here. It is handed to <see cref="DbSession"/>, which
/// gives it to the connection, and the caller's copy is wiped immediately; re-opening after a lock
/// derives it again from the password or from the machine wrap. The vault key has to stay for as
/// long as documents can be opened, and it is wiped the moment the session locks or ends.
/// </remarks>
public sealed class AccountSession : IDisposable
{
    private readonly IClock _clock;
    private DbSession? _db;
    private byte[]? _vaultKey;
    private bool _disposed;

    public AccountSession(WakeelPaths paths, IClock clock)
    {
        Paths = paths;
        _clock = clock;
        LastActivityUtc = clock.UtcNow;
    }

    /// <summary>Raised whenever the state, the identity or the activity stamp changed.</summary>
    public event Action? Changed;

    /// <summary>This installation's folder layout.</summary>
    public WakeelPaths Paths { get; }

    /// <summary>Where the account stands.</summary>
    public SessionState State { get; private set; } = SessionState.NotActivated;

    /// <summary>The installation identity, once a session has been opened at least once.</summary>
    public Installation? Installation { get; private set; }

    /// <summary>The operating account row, as it was when the session last opened.</summary>
    public Core.Data.Entities.Account? Account { get; private set; }

    /// <summary>Minutes of inactivity before the automatic lock (AGREEMENT item 7).</summary>
    public int AutoLockMinutes { get; private set; } = InstallationService.DefaultAutoLockMinutes;

    /// <summary>When the person last did anything; the idle timer measures from here.</summary>
    public DateTime LastActivityUtc { get; private set; }

    /// <summary>When the session locked itself, for the sentence W06 shows.</summary>
    public DateTime? LockedAtUtc { get; private set; }

    /// <summary>The open database. Throws while the session is locked or signed out.</summary>
    public WakeelDb Db =>
        _db is { IsLocked: false }
            ? _db.Db
            : throw new InvalidOperationException("The session is not open.");

    /// <summary>The vault key, for reading and writing stored files. Throws unless the session is open.</summary>
    public ReadOnlySpan<byte> VaultKey =>
        _vaultKey ?? throw new InvalidOperationException("The session is not open.");

    /// <summary>Whether the screen may show anything but the sign-in or first-run path.</summary>
    public bool IsOpen => State == SessionState.Open;

    /// <summary>Whether the lock overlay (W06) belongs over the current screen.</summary>
    public bool IsLocked => State == SessionState.Locked;

    /// <summary>The audit log of the open session.</summary>
    public IAuditService Audit => new AuditService(Db, _clock);

    /// <summary>Identity and settings of the open session, for the sign-in and lock screens.</summary>
    public IInstallationService Installations => new InstallationService(Db, _clock);

    /// <summary>Settings of the open session.</summary>
    public ISettingsService Settings => new SettingsService(Db, _clock);

    /// <summary>Told by the shell that the person did something; resets the idle timer.</summary>
    public void MarkActivity()
    {
        if (State != SessionState.Open)
        {
            return;
        }

        LastActivityUtc = _clock.UtcNow;
    }

    /// <summary>How long the session has been idle.</summary>
    public TimeSpan IdleFor() => _clock.UtcNow - LastActivityUtc;

    /// <summary>
    /// Takes over an open database and the keys that opened it. Called by the activation, sign-in
    /// and recovery services; the session owns everything handed over from here on.
    /// </summary>
    internal void Adopt(
        DbSession database,
        byte[] vaultKey,
        Installation installation,
        Core.Data.Entities.Account? account,
        int autoLockMinutes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ReferenceEquals(_db, database))
        {
            _db?.Dispose();
        }

        WipeVaultKey();
        _db = database;
        _vaultKey = vaultKey;
        Installation = installation;
        Account = account;
        AutoLockMinutes = autoLockMinutes;
        State = SessionState.Open;
        LockedAtUtc = null;
        LastActivityUtc = _clock.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes the database handle and wipes the vault key, keeping only who was signed in so W06 can
    /// greet them by name. Nothing on disk is touched.
    /// </summary>
    public void Lock()
    {
        if (State != SessionState.Open)
        {
            return;
        }

        _db?.Lock();
        WipeVaultKey();
        LockedAtUtc = _clock.UtcNow;
        State = SessionState.Locked;
        Changed?.Invoke();
    }

    /// <summary>Re-opens the database after the person proved who they are again.</summary>
    internal void Unlock(byte[] dbKey, byte[] vaultKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var database = _db ?? throw new InvalidOperationException("There is no locked session to open.");

        database.Unlock(dbKey);
        CryptographicOperations.ZeroMemory(dbKey);
        WipeVaultKey();
        _vaultKey = vaultKey;
        State = SessionState.Open;
        LockedAtUtc = null;
        LastActivityUtc = _clock.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>Ends the session completely: the database is closed and every key is wiped.</summary>
    public void SignOut(bool activated = true)
    {
        _db?.Dispose();
        _db = null;
        WipeVaultKey();
        Installation = null;
        Account = null;
        LockedAtUtc = null;
        State = activated ? SessionState.SignedOut : SessionState.NotActivated;
        Changed?.Invoke();
    }

    /// <summary>Records what the startup check found on disk, before anybody signed in.</summary>
    public void ReportActivated(bool activated)
    {
        if (State is SessionState.Open or SessionState.Locked)
        {
            return;
        }

        var next = activated ? SessionState.SignedOut : SessionState.NotActivated;
        if (next == State)
        {
            return;
        }

        State = next;
        Changed?.Invoke();
    }

    /// <summary>Refreshes the cached account row after a sign-in or a recovery changed it.</summary>
    internal void RefreshAccount(Core.Data.Entities.Account? account, int autoLockMinutes)
    {
        Account = account;
        AutoLockMinutes = autoLockMinutes;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _db?.Dispose();
        _db = null;
        WipeVaultKey();
    }

    private void WipeVaultKey()
    {
        if (_vaultKey is { } key)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _vaultKey = null;
    }
}
