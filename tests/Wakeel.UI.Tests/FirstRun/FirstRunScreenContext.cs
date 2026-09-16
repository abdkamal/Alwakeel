using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Tests.FirstRun;

/// <summary>
/// One exported setup file, built once for the whole suite. Producing it costs a real Argon2id
/// derivation of the package password — the writer will not take a cheaper setting from outside its
/// own assembly, and rightly so — and the screens all want the same file, so it is written once and
/// every test opens that one.
/// </summary>
internal static class SharedSetupFile
{
    private static readonly Lock Gate = new();
    private static string? _path;
    private static string? _password;
    private static DeviceIdentity? _org;

    internal const string OrgId = "ORG-1";
    internal const string OrgName = "هيئة الشؤون الإدارية";
    internal const string OfficeUnitId = "U-UNIT";
    internal const string OfficeName = "وحدة السكرتارية";
    internal const string OfficeCode = "OF-01";
    internal const string DeviceId = "PC-1";
    internal const string EmployeeName = "أحمد الخطيب";
    internal const string JobTitle = "سكرتير";

    internal static DateTimeOffset ExportedAt { get; } = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The path of the one exported file; written on first use.</summary>
    internal static string Path
    {
        get
        {
            Build();
            return _path!;
        }
    }

    /// <summary>The package password the administrator wrote on the accompanying paper.</summary>
    internal static string Password
    {
        get
        {
            Build();
            return _password!;
        }
    }

    private static void Build()
    {
        lock (Gate)
        {
            if (_path is not null)
            {
                return;
            }

            var folder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wakeel-ui-tests",
                Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(folder);

            _org = DeviceIdentity.Generate();
            using var device = DeviceIdentity.Generate();
            var password = PackagePassword.New();

            var certificate = DeviceCertificate.Issue(
                new DeviceCertificateBody(
                    OrgId,
                    OfficeUnitId,
                    DeviceId,
                    1,
                    2,
                    SetupRoles.Secretary,
                    Wakeel.Crypto.DeviceKind.Pc,
                    device.SigningPublicKeyText,
                    device.AgreementPublicKeyText,
                    ExportedAt,
                    OrgId),
                _org);

            var content = new SetupContent(
                SetupContent.CurrentFormatVersion,
                ExportedAt,
                1,
                new SetupOrg(
                    OrgId,
                    OrgName,
                    _org.SigningPublicKeyText,
                    _org.AgreementPublicKeyText,
                    1,
                    "YYYYMMDD/DESSS"),
                [
                    new SetupUnit("U-ORG", null, 1, OrgName, "رئيس الهيئة", "سالم العامري", null),
                    new SetupUnit("U-DEPT", "U-ORG", 2, "دائرة الشؤون الإدارية", "مدير الدائرة", "خالد الهاشمي", null),
                    new SetupUnit("U-SEC", "U-DEPT", 3, "قسم المتابعة", "رئيس القسم", "ليلى المنصوري", null),
                    new SetupUnit(OfficeUnitId, "U-SEC", 4, OfficeName, "رئيس الوحدة", "نور السالمي", OfficeCode),
                ],
                new SetupOffice(OfficeUnitId, OfficeCode),
                new SetupDevice(DeviceId, 1, SetupRoles.Secretary, SetupSyncScopes.Full, certificate),
                device.Export(),
                new SetupEmployee(EmployeeName, 2, JobTitle),
                Base64Url.Encode(RandomBytes.Next(SetupContent.OfficeKeySize)),
                null,
                new SetupIncludes(Logo: true, Guide: false, ReportTemplate: false, LetterTemplate: false));

            var path = System.IO.Path.Combine(folder, "office-device" + ContainerKinds.Extension(ContainerKind.Setup));
            SetupPackageWriter.Write(path, new SetupWriteRequest
            {
                Content = content,
                PackagePassword = password,
                OrgIdentity = _org,
                Logo = () => new MemoryStream(OnePixelPng, writable: false),
                StagingDirectory = System.IO.Path.Combine(folder, "staging"),
                Time = new FixedTime(ExportedAt),
            });

            _password = password;
            _path = path;
        }
    }

    /// <summary>The smallest thing that is honestly a PNG: one opaque pixel.</summary>
    internal static byte[] OnePixelPng { get; } =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}

/// <summary>
/// A clock that does not move, so a screenshot of a screen is the same every run. Its timers never
/// fire on their own either: a test that needs one to elapse calls <see cref="ManualTimer.Fire"/>
/// on <see cref="LastTimer"/> itself, deterministically, instead of waiting on a real system timer.
/// </summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>The most recently created timer.</summary>
    internal ManualTimer? LastTimer { get; private set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        LastTimer = timer;
        return timer;
    }

    /// <summary>An <see cref="ITimer"/> a test fires by hand; it never schedules real callbacks.</summary>
    internal sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        internal bool Disposed { get; private set; }

        internal void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A bUnit context with an installation folder of its own and the account services registered over
/// it, so the six first-run screens can be rendered exactly as the host renders them.
/// </summary>
public abstract class FirstRunScreenContext : WakeelTestContext
{
    private readonly string _root;
    private bool _disposed;

    protected FirstRunScreenContext()
    {
        _root = Path.Combine(Path.GetTempPath(), "wakeel-ui-tests", Guid.NewGuid().ToString("n"));
        Paths = WakeelPaths.ForRoot(_root);
        Paths.EnsureDirectories();

        Services.AddWakeelCore(options =>
        {
            options.Paths = Paths;
            options.TimeProvider = Time;
        });
        Services.AddWakeelAccount(options =>
        {
            // Cheapest legal cost: these tests are about what the screen draws, not about how long
            // a password takes to check.
            options.Kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(Argon2Params.SaltSize));
            options.BuildDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        });
    }

    /// <summary>The clock every screen draws its date line from.</summary>
    protected TimeProvider Time { get; } = new FixedTime(new DateTimeOffset(2026, 9, 16, 10, 24, 0, TimeSpan.Zero));

    protected WakeelPaths Paths { get; }

    protected SetupInspectionService Inspection => Services.GetRequiredService<SetupInspectionService>();

    protected ActivationService Activation => Services.GetRequiredService<ActivationService>();

    protected LoginService Login => Services.GetRequiredService<LoginService>();

    protected LockService Locks => Services.GetRequiredService<LockService>();

    protected AccountSession Session => Services.GetRequiredService<AccountSession>();

    protected SignInProfileStore Profiles => Services.GetRequiredService<SignInProfileStore>();

    /// <summary>Hands the shared setup file to the inspection service, as the file picker would.</summary>
    protected async Task<StagedSetupFile> StageAsync()
    {
        await using var stream = File.OpenRead(SharedSetupFile.Path);
        return await Inspection.StageAsync(
            stream,
            Path.GetFileName(SharedSetupFile.Path),
            SharedSetupFile.ExportedAt);
    }

    /// <summary>Stages the shared file and runs the check list, as W02 does when «فحص الحزمة» is pressed.</summary>
    protected async Task InspectAsync(SetupExpectations? expectations = null)
    {
        await StageAsync();
        if (expectations is null)
        {
            Inspection.Inspect(SharedSetupFile.Password);
        }
        else
        {
            Inspection.Inspect(SharedSetupFile.Password, expectations);
        }
    }

    /// <summary>The account password the activated installation in these tests is built with.</summary>
    protected const string AccountPassword = "كلمة-المرور-الأولى-2026";

    /// <summary>
    /// Runs W02–W04 head-on, so W05, W06 and W07 have a real installation to draw. Each of them
    /// checks that one exists before drawing anything — a sign-in screen for an installation that is
    /// not there sends the person back to the first run — so there is no shortcut worth taking.
    /// Returns the recovery code the sheet showed.
    /// </summary>
    protected async Task<string> ActivateAsync()
    {
        await InspectAsync();
        var sheet = Activation.Begin();
        await Activation.ActivateAsync(AccountPassword);
        return sheet.CodeDisplay;
    }

    /// <summary>The sealed card W05 and W06 draw the person's name and office from.</summary>
    protected void SaveProfile(Action<SignInProfile>? change = null)
    {
        var profile = new SignInProfile
        {
            OrgName = SharedSetupFile.OrgName,
            OfficeName = SharedSetupFile.OfficeName,
            OfficeCode = SharedSetupFile.OfficeCode,
            EmployeeName = SharedSetupFile.EmployeeName,
            JobTitle = SharedSetupFile.JobTitle,
            EmployeeNo = 2,
            DeviceNo = 1,
            Logo = SharedSetupFile.OnePixelPng,
        };

        change?.Invoke(profile);
        Profiles.Save(profile);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A database handle the runner has not released yet is no reason to fail a green test.
        }
    }
}
