using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Admin.UI;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A whole administration tool on a folder of its own, thrown away when the test finishes.
/// </summary>
/// <remarks>
/// <para>
/// The services are registered exactly as <c>App.xaml.cs</c> registers them, minus the three the
/// Windows host supplies (printing, reading a picture, closing the window), so what the tests
/// exercise is the same wiring the tool ships with.
/// </para>
/// <para>
/// The Argon2id cost is pinned to the cheapest the library accepts. The shipped tool measures the
/// machine instead; a test that did the same would spend a second of work per password for no
/// reason, and the thing under test is the wrapping, not the cost.
/// </para>
/// </remarks>
public abstract class AdminTestContext : IDisposable
{
    private readonly string _root;
    private bool _disposed;

    protected AdminTestContext()
    {
        _root = Path.Combine(Path.GetTempPath(), "wakeel-admin-tests", Guid.NewGuid().ToString("N"));
        Paths = AdminPaths.ForRoot(_root);
        Paths.EnsureDirectories();

        Time = new TestClock(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));
        Options = new AdminOptions
        {
            Kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(Argon2Params.SaltSize)),
            MachineName = "PLN-PC-01",
        };

        Db = new AdminDb(Paths);
        Session = new AdminSession();
        Audit = new AdminAuditService(Db, Time);
        Keys = new AdminKeyService(Db, Time);
        Dashboard = new AdminDashboardService(Db, Keys);
        Accounts = new AdminAccountService(Paths, Db, Keys, Audit, Session, Options, Time);
    }

    /// <summary>A password that satisfies every rule A01 prints.</summary>
    protected const string GoodPassword = "Wakeel!2026#Admin";

    /// <summary>A second one, for proving a password change.</summary>
    protected const string OtherPassword = "Wakeel!2027#Admin";

    protected AdminPaths Paths { get; }

    protected TestClock Time { get; }

    protected AdminOptions Options { get; }

    protected AdminDb Db { get; }

    protected AdminSession Session { get; }

    protected AdminAuditService Audit { get; }

    protected AdminKeyService Keys { get; }

    protected AdminDashboardService Dashboard { get; }

    protected AdminAccountService Accounts { get; }

    /// <summary>Creates the account the way A01 does, and returns the sheet it showed once.</summary>
    protected AdminRecoverySheet CreateAccount(
        string password = GoodPassword,
        string adminName = "سامي الحاج",
        string orgName = "هيئة تنمية المناطق الريفية")
    {
        var result = Accounts.Create(password, password, adminName, orgName);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Sheet);
        return result.Sheet;
    }

    /// <summary>Registers this same tool into a bUnit container, for the screen tests.</summary>
    protected void RegisterInto(BunitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Services.AddSingleton(Paths);
        context.Services.AddSingleton<TimeProvider>(Time);
        context.Services.AddSingleton(Options);
        context.Services.AddSingleton(Db);
        context.Services.AddSingleton(Session);
        context.Services.AddSingleton(Audit);
        context.Services.AddSingleton(Keys);
        context.Services.AddSingleton(Dashboard);
        context.Services.AddSingleton(Accounts);
        context.Services.AddWakeelAdmin(Paths);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!disposing)
        {
            return;
        }

        Db.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file the operating system has not let go of yet is not worth failing a test over;
            // the folder sits under the temporary directory and goes with it.
        }
    }
}

/// <summary>A clock the test moves by hand, so a lock-out's timing can be watched without waiting.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset start)
    {
        _now = start;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan span) => _now = _now.Add(span);
}
