using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Admin.UI;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Audit;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Distribution;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Maintenance;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
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

        // admin-2's four areas, wired exactly as AddWakeelAdmin() wires them so a service test and
        // a screen test are looking at the same tool rather than two copies of it.
        Pending = new AdminPendingChanges(Db, Time);
        Org = new AdminOrgService(Db, Audit, Session, Pending, Time);
        Structure = new AdminStructureService(Db, Audit, Session, Pending, Time);
        DeviceKeys = new AdminDeviceKeyService(Db, Keys, Audit, Session, Pending, Paths, Time);
        DeviceRegistry = new AdminDeviceService(Db, DeviceKeys, Audit, Session, Pending, Time);

        // admin-3's four areas, wired the same way.
        Exports = new SetupExportService(
            Db, Keys, Org, Structure, DeviceRegistry, DeviceKeys, Audit, Session, Paths, Time);
        Maintenance = new MaintenanceService(
            Keys, DeviceRegistry, DeviceKeys, Exports, Audit, Session, Paths, Time);
        Distribution = new DistributionService(Db, Pending, DeviceRegistry, Exports, Audit, Session, Time);
        AuditLog = new AdminAuditQuery(Db, Audit);
        FileDialog = new TestFileDialog(Path.Combine(_root, "chosen"));
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

    protected AdminPendingChanges Pending { get; }

    protected AdminOrgService Org { get; }

    protected AdminStructureService Structure { get; }

    protected AdminDeviceKeyService DeviceKeys { get; }

    protected AdminDeviceService DeviceRegistry { get; }

    protected SetupExportService Exports { get; }

    protected MaintenanceService Maintenance { get; }

    protected DistributionService Distribution { get; }

    protected AdminAuditQuery AuditLog { get; }

    protected TestFileDialog FileDialog { get; }

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
        context.Services.AddSingleton(Pending);
        context.Services.AddSingleton(Org);
        context.Services.AddSingleton(Structure);
        context.Services.AddSingleton(DeviceKeys);
        context.Services.AddSingleton(DeviceRegistry);
        context.Services.AddSingleton(Exports);
        context.Services.AddSingleton(Maintenance);
        context.Services.AddSingleton(Distribution);
        context.Services.AddSingleton(AuditLog);
        context.Services.AddSingleton<IAdminFileDialog>(FileDialog);
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

/// <summary>
/// A file chooser that answers without a window: it says yes to a place inside the test's own
/// folder, and remembers what it was asked, so a screen that saves a copy can be proved to have
/// asked rather than to have written somewhere of its own choosing.
/// </summary>
public sealed class TestFileDialog : IAdminFileDialog
{
    private readonly string _folder;

    public TestFileDialog(string folder)
    {
        _folder = folder;
    }

    /// <summary>What the caller last offered as a name.</summary>
    public string? LastSuggestedName { get; private set; }

    /// <summary>Where the chooser last said the copy should go.</summary>
    public string? LastChosenPath { get; private set; }

    /// <summary>What the caller last asked to be shown in a file window.</summary>
    public string? LastRevealed { get; private set; }

    /// <summary>The answer the next question gets; a test sets it to false to prove the way out.</summary>
    public bool Answers { get; set; } = true;

    /// <summary>The file or folder the next «which one?» question returns.</summary>
    public string? NextChoice { get; set; }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public AdminFileChoice AskWhereToSave(string suggestedFileName, string filterLabel, string extension)
    {
        LastSuggestedName = suggestedFileName;
        if (!Answers)
        {
            return AdminFileChoice.None;
        }

        Directory.CreateDirectory(_folder);
        LastChosenPath = Path.Combine(_folder, suggestedFileName);
        return new AdminFileChoice(true, LastChosenPath);
    }

    /// <inheritdoc />
    public AdminFileChoice AskWhichFile(string filterLabel, IReadOnlyList<string> extensions) =>
        Answers && NextChoice is not null ? new AdminFileChoice(true, NextChoice) : AdminFileChoice.None;

    /// <inheritdoc />
    public AdminFileChoice AskWhichFolder(string prompt) =>
        Answers && NextChoice is not null ? new AdminFileChoice(true, NextChoice) : AdminFileChoice.None;

    /// <inheritdoc />
    public void Reveal(string path) => LastRevealed = path;
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
