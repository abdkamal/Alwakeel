using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>Verifies <see cref="ServiceCollectionExtensions.AddWakeelCore"/> registers every documented service and it resolves.</summary>
public sealed class ServiceRegistrationTests : IDisposable
{
    private readonly string _root;
    private readonly DbSession _session;
    private readonly ServiceProvider _provider;

    public ServiceRegistrationTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _);

        var services = new ServiceCollection();
        services.AddWakeelCore(options => options.Paths = WakeelPaths.ForRoot(_root));

        // The host is responsible for registering WakeelDb itself (see AddWakeelCore's XML
        // doc): it needs a database key only available once a DbSession is open.
        services.AddScoped(_ => _session.Db);

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public void AddWakeelCore_RegistersPathsAndTimeProvider_AsSingletons()
    {
        Assert.Same(_provider.GetRequiredService<WakeelPaths>(), _provider.GetRequiredService<WakeelPaths>());
        Assert.NotNull(_provider.GetService<TimeProvider>());
    }

    [Theory]
    [InlineData(typeof(IClock))]
    [InlineData(typeof(IIdGenerator))]
    [InlineData(typeof(IErrorMapper))]
    [InlineData(typeof(IAuditService))]
    [InlineData(typeof(ISettingsService))]
    [InlineData(typeof(IOfficialNumberService))]
    [InlineData(typeof(IFinancialCycleService))]
    [InlineData(typeof(IClockCheckService))]
    [InlineData(typeof(IAttentionService))]
    [InlineData(typeof(IBadgeService))]
    [InlineData(typeof(INotificationService))]
    [InlineData(typeof(IReminderScheduler))]
    [InlineData(typeof(IClockGuard))]
    [InlineData(typeof(IHealthService))]
    [InlineData(typeof(IQuickCaptureService))]
    [InlineData(typeof(IMinuteTicker))]
    [InlineData(typeof(IWordProbe))]
    [InlineData(typeof(IScannerProbe))]
    [InlineData(typeof(IDiskSpaceProbe))]
    [InlineData(typeof(IRuntimeProbe))]

    // B3-1 — the correspondence services. IReferralService is registered through a factory
    // because its document builder is optional, so resolving it is worth proving too.
    [InlineData(typeof(IDuplicateDetector))]
    [InlineData(typeof(ICorrespondenceService))]
    [InlineData(typeof(IReferralService))]
    [InlineData(typeof(IFollowUpService))]
    [InlineData(typeof(ICorrectionService))]
    [InlineData(typeof(IExchangeService))]
    public void AddWakeelCore_ResolvesEveryRegisteredService(Type serviceType)
    {
        using var scope = _provider.CreateScope();
        var resolved = scope.ServiceProvider.GetService(serviceType);
        Assert.NotNull(resolved);
    }

    [Fact]
    public void AddWakeelCore_LeavesAPlatformProbeTheHostRegisteredFirstInPlace()
    {
        // Wakeel.Desktop registers its Windows probes before calling AddWakeelCore; Core's own
        // TryAdd must not replace them with its "nothing is available" fallbacks.
        var services = new ServiceCollection();
        var hostProbe = new AlwaysInstalledWordProbe();
        services.AddSingleton<IWordProbe>(hostProbe);
        services.AddWakeelCore(options => options.Paths = WakeelPaths.ForRoot(_root));
        services.AddScoped(_ => _session.Db);

        using var provider = services.BuildServiceProvider();

        Assert.Same(hostProbe, provider.GetRequiredService<IWordProbe>());
    }

    [Fact]
    public void AddWakeelCore_RegistersTheDailyShellServicesAsScoped_SoEachSessionHasItsOwn()
    {
        // The clock guard, the badge service and the quick-capture service hold session state
        // (the dismissed banner, the last badge numbers, the live undo tokens). Two scopes must
        // not share them.
        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();

        Assert.NotSame(first.ServiceProvider.GetRequiredService<IClockGuard>(), second.ServiceProvider.GetRequiredService<IClockGuard>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IBadgeService>(), second.ServiceProvider.GetRequiredService<IBadgeService>());
        Assert.Same(first.ServiceProvider.GetRequiredService<IBadgeService>(), first.ServiceProvider.GetRequiredService<IBadgeService>());
    }

    private sealed class AlwaysInstalledWordProbe : IWordProbe
    {
        public Task<WordInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordInfo(true, "Office 2016 أو أحدث"));
    }

    [Fact]
    public void AddWakeelCore_WithoutConfigure_UsesDefaultPathsAndSystemTimeProvider()
    {
        var services = new ServiceCollection();
        services.AddWakeelCore();
        using var provider = services.BuildServiceProvider();

        var paths = provider.GetRequiredService<WakeelPaths>();
        Assert.Equal(WakeelPaths.Default().Root, paths.Root);
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }
}
