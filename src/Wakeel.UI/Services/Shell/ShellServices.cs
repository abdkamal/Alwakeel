using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Services;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Services.Shell;

/// <summary>
/// The daily shell's window onto the Wakeel.Core services of B2 (attention, badges,
/// notifications, the clock guard, the health center and quick capture).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a scope of its own.</b> Those services are registered Scoped and take
/// <see cref="Wakeel.Core.Data.WakeelDb"/> in their constructors, and the database object does not
/// survive a lock: <see cref="AccountSession.Lock"/> disposes it and the unlock builds a new one.
/// A service resolved once for the whole window would therefore keep a handle on a database that
/// has been closed. So this class owns one <see cref="IServiceScope"/> per OPEN session: it is
/// created when the session opens (sign-in, activation, unlock) and disposed when it closes (lock,
/// sign-out), which also stops the timers those services hold.
/// </para>
/// <para>
/// <b>Nothing is resolved while the session is shut.</b> <see cref="IsOpen"/> is false between a
/// lock and the unlock that follows it, and every accessor throws rather than hand out a service
/// over a closed database. Screens read <see cref="IsOpen"/> first and draw their error state
/// instead.
/// </para>
/// </remarks>
public sealed class ShellServices : IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AccountSession _session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IServiceScope? _scope;
    private bool _disposed;

    public ShellServices(IServiceScopeFactory scopes, AccountSession session)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(session);

        _scopes = scopes;
        _session = session;
        _session.Changed += HandleSessionChanged;
        Sync();
    }

    /// <summary>Raised after the session opened or closed, so the shell can start or stop its background work.</summary>
    public event Action? Changed;

    /// <summary>Whether the session is open and the services below may be used.</summary>
    public bool IsOpen => _scope is not null;

    /// <summary>The attention center's data (W08, W09).</summary>
    public IAttentionService Attention => Require<IAttentionService>();

    /// <summary>Sidebar, group and bell badge counts.</summary>
    public IBadgeService Badges => Require<IBadgeService>();

    /// <summary>The bell panel's notifications (W10).</summary>
    public INotificationService Notifications => Require<INotificationService>();

    /// <summary>The clock banner (W11).</summary>
    public IClockGuard Clock => Require<IClockGuard>();

    /// <summary>The health center (W12).</summary>
    public IHealthService Health => Require<IHealthService>();

    /// <summary>The quick-capture dialog's back end (W94).</summary>
    public IQuickCaptureService QuickCapture => Require<IQuickCaptureService>();

    /// <summary>The minute tick the reminder scheduler runs on.</summary>
    public IMinuteTicker Ticker => Require<IMinuteTicker>();

    /// <summary>The reminder scheduler (meetings, due dates, the financial cycle, backups).</summary>
    public IReminderScheduler Reminders => Require<IReminderScheduler>();

    /// <summary>Settings of the open session.</summary>
    public ISettingsService Settings => Require<ISettingsService>();

    /// <summary>Financial-cycle arithmetic, for the monthly-report banner on W08.</summary>
    public IFinancialCycleService Cycles => Require<IFinancialCycleService>();

    /// <summary>Resolves any other service of the open session, or null while it is shut.</summary>
    public T? Find<T>() where T : class => _scope?.ServiceProvider.GetService<T>();

    /// <summary>
    /// Runs <paramref name="work"/> with exclusive use of the open session's database object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every service above shares ONE <see cref="Wakeel.Core.Data.WakeelDb"/> — the session's — and
    /// that object refuses a second operation while a first is still running. The shell's own
    /// background work (the clock guard's re-check, the badge counts, the reminder scheduler's
    /// passes) therefore has to take turns with the foreground operation that may be running on the
    /// same database at the same moment: the unlock and the sign-out both write an audit line
    /// through it, and both raise the very event that would otherwise start the background work
    /// mid-write.
    /// </para>
    /// <para>
    /// This gate is that turn-taking. It is a plain one-at-a-time lock rather than a transaction:
    /// it serialises use of the database object, it does not group the writes inside it.
    /// </para>
    /// </remarks>
    public async Task RunExclusiveAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await work().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc cref="RunExclusiveAsync(Func{Task})"/>
    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            return await work().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the same gate if the database is free this instant;
    /// returns whether it ran.
    /// </summary>
    /// <remarks>
    /// For the one caller that cannot wait and cannot be asynchronous: the lock, which has to close
    /// the database now — from the idle timer's thread as well as from the user menu. Its audit line
    /// is written only if the database is free; an unwritten line is never a reason to leave an
    /// unattended machine open, and nothing here ever blocks the thread it is called on.
    /// </remarks>
    public bool TryRunExclusive(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var entered = _gate.WaitAsync(TimeSpan.Zero);
        if (!entered.IsCompletedSuccessfully || !entered.Result)
        {
            return false;
        }

        try
        {
            work();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= HandleSessionChanged;
        _scope?.Dispose();
        _scope = null;
        _gate.Dispose();
    }

    private T Require<T>() where T : notnull =>
        (_scope ?? throw new InvalidOperationException("The session is not open."))
            .ServiceProvider.GetRequiredService<T>();

    private void HandleSessionChanged() => Sync();

    private void Sync()
    {
        if (_disposed)
        {
            return;
        }

        var wanted = _session.IsOpen;
        if (wanted == IsOpen)
        {
            return;
        }

        if (wanted)
        {
            _scope = _scopes.CreateScope();
        }
        else
        {
            var scope = _scope;
            _scope = null;
            scope?.Dispose();
        }

        Changed?.Invoke();
    }
}
