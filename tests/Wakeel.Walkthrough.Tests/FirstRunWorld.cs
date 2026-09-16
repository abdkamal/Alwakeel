using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.UI.Services.Account;

namespace Wakeel.Walkthrough.Tests;

/// <summary>A clock the whole walkthrough shares, so lock-outs and idle timers can be pushed forward.</summary>
internal sealed class MovableTime : TimeProvider
{
    private DateTimeOffset _now;

    internal MovableTime(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan by) => _now += by;

    internal void Set(DateTimeOffset at) => _now = at;
}

/// <summary>
/// One machine, from an empty disk to a working installation. It plays both sides of the first run:
/// the administrator tool that produces a <c>.wakeel-setup</c> file, and the الوكيل installation
/// that inspects it, activates itself from it, signs in, locks, and recovers.
/// </summary>
/// <remarks>
/// Everything happens under a folder of its own, so the suite never touches the machine's real
/// installation. The derivation cost is pinned to its cheapest legal setting: what these tests prove
/// is that a password opens what it should and nothing else, not that Argon2id is expensive.
/// </remarks>
internal sealed class FirstRunWorld : IDisposable
{
    internal const string OrgId = "ORG-1";
    internal const string OrgName = "هيئة الشؤون الإدارية";
    internal const string OfficeUnitId = "U-UNIT";
    internal const string OfficeCode = "OF-01";
    internal const string DeviceId = "PC-1";
    internal const string EmployeeName = "أحمد الخطيب";
    internal const string JobTitle = "سكرتير";
    internal const string OfficeName = "وحدة السكرتارية";

    /// <summary>The password W04 is driven with; long enough to satisfy the strength rule.</summary>
    internal const string FirstPassword = "كلمة-المرور-الأولى-2026";

    /// <summary>The password recovery sets in W07.</summary>
    internal const string SecondPassword = "كلمة-المرور-الجديدة-2026";

    private static readonly IReadOnlyList<SetupUnit> Structure =
    [
        new SetupUnit("U-ORG", null, 1, OrgName, "رئيس الهيئة", "سالم العامري", null),
        new SetupUnit("U-DEPT", "U-ORG", 2, "دائرة الشؤون الإدارية", "مدير الدائرة", "خالد الهاشمي", null),
        new SetupUnit("U-SEC", "U-DEPT", 3, "قسم المتابعة", "رئيس القسم", "ليلى المنصوري", null),
        new SetupUnit(OfficeUnitId, "U-SEC", 4, OfficeName, "رئيس الوحدة", "نور السالمي", OfficeCode),
    ];

    private readonly DeviceIdentity _org;
    private readonly DeviceIdentity _device;
    private readonly ServiceProvider _provider;
    private bool _disposed;

    internal FirstRunWorld()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "wakeel-walkthrough",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);

        // The administrator's side of the first run: the organisation's own key pair, the device's,
        // and the certificate the organisation issues for that device.
        _org = DeviceIdentity.Generate();
        _device = DeviceIdentity.Generate();
        Seeds = _device.Export();
        OfficeKey = RandomBytes.Next(SetupContent.OfficeKeySize);
        PackagePasswordText = PackagePassword.New();
        Time = new MovableTime(Start);

        Certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId,
                OfficeUnitId,
                DeviceId,
                1,
                2,
                SetupRoles.Secretary,
                Wakeel.Crypto.DeviceKind.Pc,
                _device.SigningPublicKeyText,
                _device.AgreementPublicKeyText,
                Start,
                OrgId),
            _org);

        Paths = WakeelPaths.ForRoot(Path.Combine(Root, "installation"));
        Paths.EnsureDirectories();

        var services = new ServiceCollection();
        services.AddWakeelCore(options =>
        {
            options.Paths = Paths;
            options.TimeProvider = Time;
        });
        services.AddWakeelAccount(options =>
        {
            options.Kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(Argon2Params.SaltSize));
            options.BuildDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        });

        _provider = services.BuildServiceProvider();
    }

    /// <summary>The instant the whole walkthrough starts from.</summary>
    internal static DateTimeOffset Start { get; } = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The folder holding both the exported setup file and the installation.</summary>
    internal string Root { get; }

    internal WakeelPaths Paths { get; }

    internal MovableTime Time { get; }

    internal DeviceSeeds Seeds { get; }

    internal DeviceCertificate Certificate { get; }

    internal byte[] OfficeKey { get; }

    /// <summary>The password the administrator wrote on the paper that travels with the file.</summary>
    internal string PackagePasswordText { get; }

    internal SetupInspectionService Inspection => _provider.GetRequiredService<SetupInspectionService>();

    internal ActivationService Activation => _provider.GetRequiredService<ActivationService>();

    internal LoginService Login => _provider.GetRequiredService<LoginService>();

    internal LockService Lock => _provider.GetRequiredService<LockService>();

    internal RecoveryService Recovery => _provider.GetRequiredService<RecoveryService>();

    internal AccountSession Session => _provider.GetRequiredService<AccountSession>();

    internal SignInProfileStore Profiles => _provider.GetRequiredService<SignInProfileStore>();

    internal AccountOptions Options => _provider.GetRequiredService<AccountOptions>();

    /// <summary>The bytes of the logo the administrator attached, so tests can look for them again.</summary>
    internal static byte[] Logo { get; } = SamplePng();

    internal static byte[] Guide { get; } = "دليل الاستخدام"u8.ToArray();

    internal static byte[] ReportTemplate { get; } = "قالب التقرير الشهري"u8.ToArray();

    internal static byte[] LetterTemplate { get; } = "قالب الكتاب الرسمي"u8.ToArray();

    /// <summary>The content of the setup file the administrator tool would export.</summary>
    internal SetupContent Content(long exportSeq = 1, DateTimeOffset? exportedAt = null) =>
        new(
            SetupContent.CurrentFormatVersion,
            exportedAt ?? Start,
            exportSeq,
            new SetupOrg(
                OrgId,
                OrgName,
                _org.SigningPublicKeyText,
                _org.AgreementPublicKeyText,
                1,
                "YYYYMMDD/DESSS"),
            Structure,
            new SetupOffice(OfficeUnitId, OfficeCode),
            new SetupDevice(DeviceId, 1, SetupRoles.Secretary, SetupSyncScopes.Full, Certificate),
            Seeds,
            new SetupEmployee(EmployeeName, 2, JobTitle),
            Base64Url.Encode(OfficeKey),
            null,
            new SetupIncludes(true, true, true, true));

    /// <summary>
    /// The same office's setup file, issued by the same organisation for a different machine — what
    /// lands on the wrong computer when two envelopes are swapped.
    /// </summary>
    internal SetupContent ContentForAnotherDevice(string deviceId, long exportSeq)
    {
        using var other = DeviceIdentity.Generate();
        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId,
                OfficeUnitId,
                deviceId,
                2,
                3,
                SetupRoles.Secretary,
                Wakeel.Crypto.DeviceKind.Pc,
                other.SigningPublicKeyText,
                other.AgreementPublicKeyText,
                Start,
                OrgId),
            _org);

        return Content(exportSeq) with
        {
            Device = new SetupDevice(deviceId, 2, SetupRoles.Secretary, SetupSyncScopes.Full, certificate),
            DeviceSeed = other.Export(),
            Employee = new SetupEmployee("سعيد البلوشي", 3, JobTitle),
        };
    }

    /// <summary>
    /// Writes the setup file the administrator would hand over on a memory stick — the office's
    /// logo, the guide, the monthly-report template and the official-letter template included.
    /// </summary>
    internal string Export(
        SetupContent? content = null,
        string? password = null,
        DateTimeOffset? at = null,
        string name = "office-device")
    {
        var path = Path.Combine(Root, name + ContainerKinds.Extension(ContainerKind.Setup));
        var staging = Path.Combine(Root, "admin-staging");
        Directory.CreateDirectory(staging);

        SetupPackageWriter.Write(path, new SetupWriteRequest
        {
            Content = content ?? Content(),
            PackagePassword = password ?? PackagePasswordText,
            OrgIdentity = _org,
            Logo = () => new MemoryStream(Logo, writable: false),
            Guide = () => new MemoryStream(Guide, writable: false),
            ReportTemplate = () => new MemoryStream(ReportTemplate, writable: false),
            LetterTemplate = () => new MemoryStream(LetterTemplate, writable: false),
            StagingDirectory = staging,
            Time = new MovableTime(at ?? Start),
        });

        return path;
    }

    /// <summary>Hands an exported file to W02 exactly as the file picker would.</summary>
    internal async Task<StagedSetupFile> ChooseAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await Inspection.StageAsync(
            stream,
            Path.GetFileName(path),
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
    }

    /// <summary>Everything a log file would ever hold for this run, concatenated.</summary>
    internal string LogText()
    {
        if (!Directory.Exists(Paths.LogsDir))
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(Paths.LogsDir, "*", SearchOption.AllDirectories))
        {
            text.AppendLine(ReadAllTextShared(file));
        }

        return text.ToString();
    }

    /// <summary>Every byte the installation wrote anywhere, for the "no secret in the clear" check.</summary>
    internal IEnumerable<(string Path, byte[] Bytes)> AllFiles()
    {
        foreach (var file in Directory.EnumerateFiles(Paths.Root, "*", SearchOption.AllDirectories))
        {
            byte[] bytes;
            try
            {
                bytes = ReadAllBytesShared(file);
            }
            catch (IOException)
            {
                continue;
            }

            yield return (file, bytes);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _provider.Dispose();
        _org.Dispose();
        _device.Dispose();

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A database file the runner has not let go of yet must not fail a passing test; the
            // machine's temp folder is cleaned by the machine.
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The smallest thing that is honestly a PNG: one opaque pixel.</summary>
    private static byte[] SamplePng() =>
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
