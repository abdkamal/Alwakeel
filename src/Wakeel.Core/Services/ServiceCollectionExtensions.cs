using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;

namespace Wakeel.Core.Services;

/// <summary>Options for <see cref="ServiceCollectionExtensions.AddWakeelCore"/>.</summary>
public sealed class WakeelCoreOptions
{
    /// <summary>Installation directory layout; defaults to the machine's ProgramData\Wakeel.</summary>
    public WakeelPaths Paths { get; set; } = WakeelPaths.Default();

    /// <summary>Clock source; defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>Dependency-injection registration for Wakeel.Core.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Wakeel.Core services (<see cref="IClock"/>, <see cref="IIdGenerator"/>,
    /// <see cref="IAuditService"/>, <see cref="ISettingsService"/>, <see cref="IOfficialNumberService"/>,
    /// <see cref="IFinancialCycleService"/>, <see cref="IClockCheckService"/>, <see cref="IErrorMapper"/>)
    /// and <see cref="WakeelPaths"/>. The database-backed services are Scoped and resolve
    /// <see cref="WakeelDb"/> from the container — the host application is responsible for
    /// registering <see cref="WakeelDb"/> itself once a <see cref="DbSession"/> is open (the
    /// session requires a database key derived from the account password, which is not
    /// available at DI-configuration time).
    /// </summary>
    public static IServiceCollection AddWakeelCore(this IServiceCollection services, Action<WakeelCoreOptions>? configure = null)
    {
        var options = new WakeelCoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options.Paths);
        services.AddSingleton(options.TimeProvider);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, IdGenerator>();
        services.AddSingleton<IErrorMapper, ErrorMapper>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IOfficialNumberService, OfficialNumberService>();
        services.AddScoped<IFinancialCycleService, FinancialCycleService>();
        services.AddScoped<IClockCheckService, ClockCheckService>();

        return services;
    }
}
