using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Core.Services.Documents;
using Wakeel.Ocr;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// The container the desktop host builds, assembled here without a window: Core's document
/// services, the OCR package's reader and queue, and the two letter services B3-1b had to leave
/// unregistered until the vault existed. A registration that cannot be resolved is a product that
/// does not start, which no other test in this package would catch.
/// </summary>
public sealed class DocumentRegistrationTests : IDisposable
{
    private readonly string _root;
    private readonly DbSession _session;
    private readonly ServiceProvider _provider;

    public DocumentRegistrationTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _);

        var services = new ServiceCollection();
        services.AddWakeelCore(options => options.Paths = WakeelPaths.ForRoot(_root));

        // Exactly what App.xaml.cs supplies: the open database, the session's vault key, the
        // machine's scanner, and the OCR package.
        services.AddScoped(_ => _session.Db);
        services.AddScoped<IVaultKeyProvider, TestVaultKeys>();
        services.AddSingleton<IScanner>(new FakeScanner());
        services.AddWakeelOcr();

        // And the builder that writes the referral print copy, which sits on the vault.
        services.AddScoped<IDerivedDocumentBuilder, DerivedDocumentBuilder>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    public void Dispose()
    {
        _provider.Dispose();
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Theory]
    [InlineData(typeof(IDocumentStore))]
    [InlineData(typeof(IDocumentService))]
    [InlineData(typeof(ILetterDocumentStore))]
    [InlineData(typeof(IDerivedDocumentBuilder))]
    [InlineData(typeof(IOcrEngine))]
    [InlineData(typeof(IOcrQueue))]
    [InlineData(typeof(IScanPdfWriter))]
    public void Every_service_the_documents_screens_ask_for_resolves(Type serviceType)
    {
        using var scope = _provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService(serviceType));
    }

    [Fact]
    public void The_vault_and_the_document_service_belong_to_the_session_and_not_to_the_application()
    {
        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();

        // A vault key belongs to an open session, so a store that outlived one would be holding a
        // key the lock was supposed to have taken away.
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IDocumentStore>(),
            second.ServiceProvider.GetRequiredService<IDocumentStore>());

        Assert.Same(
            first.ServiceProvider.GetRequiredService<IDocumentStore>(),
            first.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public void The_reader_is_built_once_for_the_whole_application()
    {
        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();

        // Building a Tesseract engine costs about a second; one per session — let alone one per
        // document — is a cost the office would feel.
        Assert.Same(
            first.ServiceProvider.GetRequiredService<IOcrEngine>(),
            second.ServiceProvider.GetRequiredService<IOcrEngine>());
    }

    [Fact]
    public async Task The_document_service_finds_the_scanner_and_the_page_binder_the_host_registered()
    {
        using var scope = _provider.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        // The optional dependencies are supplied by hand in AddWakeelCore, because the container
        // ignores C# default parameter values; if that ever stops being true, this is where it
        // shows.
        Assert.NotEmpty(await documents.ListScannersAsync());
    }

    [Fact]
    public void Without_a_vault_key_the_container_still_builds_and_only_the_vault_refuses()
    {
        // A host that forgot the key provider must fail where the mistake is, not silently hand
        // out a store that reads nothing.
        var services = new ServiceCollection();
        services.AddWakeelCore(options => options.Paths = WakeelPaths.ForRoot(_root));
        services.AddScoped(_ => _session.Db);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }
}
