using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// A temporary installation with the B3-2 document services wired over it: one database, one
/// vault under a temporary folder, one controllable clock, and seams for the fakes a test needs
/// (a scanner that answers whatever the test wants, a page binder). Every world writes to its own
/// temporary folder — never to the real <c>C:\ProgramData\Wakeel</c>.
/// </summary>
internal sealed class DocumentsWorld : IDisposable
{
    private readonly string _root;

    public DocumentsWorld(DateTime? now = null, IScanner? scanner = null, IScanPdfWriter? pdf = null)
    {
        Clock = new TestClock { UtcNow = now ?? new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc) };
        Session = TestHelpers.OpenNewSession(out _root, out _, Clock);
        Paths = WakeelPaths.ForRoot(_root);
        Paths.EnsureDirectories();

        Installation = TestHelpers.SeedInstallation(
            Db,
            activatedAt: Clock.UtcNow.AddYears(-1),
            buildDate: Clock.UtcNow.AddYears(-1));

        Keys = new TestVaultKeys();
        Vault = new VaultStore(Paths, Keys);
        Ids = new IdGenerator();
        Audit = new AuditService(Db, Clock);
        Documents = new DocumentService(Db, Vault, Clock, Ids, Audit, scanner, pdf);
    }

    public TestClock Clock { get; }

    public DbSession Session { get; }

    public WakeelDb Db => Session.Db;

    public WakeelPaths Paths { get; }

    public string Root => _root;

    public Installation Installation { get; }

    public TestVaultKeys Keys { get; }

    public VaultStore Vault { get; }

    public IIdGenerator Ids { get; }

    public IAuditService Audit { get; }

    public IDocumentService Documents { get; }

    /// <summary>The file on disk that holds one stored document's bytes.</summary>
    public string VaultFile(string sha256Hex) => Paths.VaultFilePath(sha256Hex);

    public void Dispose()
    {
        Session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }
}

/// <summary>A vault key that exists for the length of one test and is the same every time it is asked for.</summary>
internal sealed class TestVaultKeys : IVaultKeyProvider
{
    private readonly byte[] _key = TestHelpers.NewKey();

    public ReadOnlySpan<byte> VaultKey => _key;
}

/// <summary>
/// A scanner the test drives: it answers with exactly the pages it was handed. It reports whether
/// it is available the way the real WIA scanner does — <see cref="IsAvailable"/> is what it saw
/// last time it looked, so it is <c>false</c> until something has actually asked it to look.
/// </summary>
internal sealed class FakeScanner(params byte[][] pages) : IScanner
{
    private readonly List<byte[]> _pages = [.. pages];
    private bool _saw;

    /// <summary>What the next scan will answer.</summary>
    public ScanState State { get; set; } = ScanState.Ok;

    /// <summary>What the last scan was asked for, so a test can assert the settings reached the driver.</summary>
    public ScanSettings? LastSettings { get; private set; }

    /// <summary>Whether this machine really has a scanner attached, whatever has been asked so far.</summary>
    public bool HasDevice { get; set; } = true;

    /// <summary>What was seen last time the machine was looked at — <c>false</c> before anybody looked.</summary>
    public bool IsAvailable => _saw;

    public Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        _saw |= HasDevice;
        return Task.FromResult<IReadOnlyList<ScannerDevice>>(
            HasDevice ? [new ScannerDevice("test-1", "ماسح تجريبي")] : []);
    }

    public Task<ScanResult> PreviewAsync(ScanSettings settings, CancellationToken cancellationToken = default) =>
        ScanAsync(settings with { Dpi = ScanSettings.PreviewDpi, MultiPage = false }, null, cancellationToken);

    public Task<ScanResult> ScanAsync(
        ScanSettings settings,
        IProgress<int>? pageScanned = null,
        CancellationToken cancellationToken = default)
    {
        LastSettings = settings;
        if (!HasDevice)
        {
            return Task.FromResult(ScanResult.NoScanner);
        }

        _saw = true;

        var taken = settings.MultiPage ? _pages : _pages.Take(1).ToList();
        var result = new List<ScannedPage>();
        foreach (var page in taken)
        {
            result.Add(new ScannedPage(page, DocumentMediaTypes.Png));
            pageScanned?.Report(result.Count);
        }

        return Task.FromResult(new ScanResult(State, result));
    }
}
