using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Services;

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
    public void AddWakeelCore_ResolvesEveryRegisteredService(Type serviceType)
    {
        using var scope = _provider.CreateScope();
        var resolved = scope.ServiceProvider.GetService(serviceType);
        Assert.NotNull(resolved);
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
