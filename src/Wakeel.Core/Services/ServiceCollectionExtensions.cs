using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <see cref="IFinancialCycleService"/>, <see cref="IClockCheckService"/>,
    /// <see cref="IInstallationService"/>, <see cref="IErrorMapper"/>) and the daily-shell
    /// services of B2 (<see cref="IAttentionService"/>, <see cref="IBadgeService"/>,
    /// <see cref="INotificationService"/>, <see cref="IReminderScheduler"/>,
    /// <see cref="IClockGuard"/>, <see cref="IHealthService"/>, <see cref="IQuickCaptureService"/>,
    /// <see cref="IMinuteTicker"/> and the health-center probes),
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
        services.AddScoped<IInstallationService, InstallationService>();

        // B2 — the daily shell's services. All Scoped for the same reason as the rest: they read
        // and write through WakeelDb, which only exists once a DbSession is open. The clock guard,
        // the badge service and the quick-capture service each hold a little session state (the
        // banner's «تجاهل مؤقتًا» flag, the last badge snapshot, the live undo tokens); a scope in
        // الوكيل is the user's session, so that is exactly where that state belongs.
        services.AddScoped<IAttentionService, AttentionService>();
        services.AddScoped<IBadgeService, BadgeService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReminderScheduler, ReminderScheduler>();
        services.AddScoped<IClockGuard, ClockGuard>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IQuickCaptureService, QuickCaptureService>();

        // B3-1 — the correspondence services. Scoped for the same reason as the rest: they read
        // and write through WakeelDb, which only exists once a DbSession is open.
        services.AddScoped<Correspondence.IDuplicateDetector, Correspondence.DuplicateDetector>();
        services.AddScoped<Correspondence.ICorrespondenceService, Correspondence.CorrespondenceService>();
        services.AddScoped<Correspondence.IFollowUpService, Correspondence.FollowUpService>();
        services.AddScoped<Correspondence.ICorrectionService, Correspondence.CorrectionService>();
        services.AddScoped<Correspondence.IExchangeService, Correspondence.ExchangeService>();

        // The referral service takes an OPTIONAL document builder: the letter package registers
        // one (OpenXML on the official template) and an installation without it still records
        // referrals, simply producing no print copy. The container's constructor selection
        // ignores C# default parameter values, so the optional dependency is supplied by hand.
        services.AddScoped<Correspondence.IReferralService>(provider => new Correspondence.ReferralService(
            provider.GetRequiredService<WakeelDb>(),
            provider.GetRequiredService<IAuditService>(),
            provider.GetService<Correspondence.IDerivedDocumentBuilder>()));

        // B3-2 — the vault and the documents. The vault is Scoped because it asks
        // IVaultKeyProvider for the open session's key, and a key belongs to a session; the host
        // registers the provider (the desktop shell bridges it to the account session). The
        // scanner and the PDF binder are OPTIONAL: Core has neither WIA nor an imaging library,
        // and an installation without them still imports files from the disk and from the phone.
        services.AddScoped<Documents.IDocumentStore, Documents.VaultStore>();
        services.AddScoped<Documents.IDocumentService>(provider => new Documents.DocumentService(
            provider.GetRequiredService<WakeelDb>(),
            provider.GetRequiredService<Documents.IDocumentStore>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IIdGenerator>(),
            provider.GetRequiredService<IAuditService>(),
            provider.GetService<Documents.IScanner>(),
            provider.GetService<Documents.IScanPdfWriter>()));

        // The letter package's window onto the vault, which B3-1b could not implement because the
        // vault did not exist yet. Registering it here is what lets the desktop host register
        // IDerivedDocumentBuilder over it, so the referral print copy of AGREEMENT item 31 is
        // actually produced.
        services.AddScoped<Correspondence.ILetterDocumentStore, Documents.LetterDocumentStore>();

        // The minute tick is Scoped like everything it drives. A singleton ticker would outlive
        // the session it was started for: after a sign-out its timer would keep firing into the
        // previous session's scheduler — whose database session has closed — and the container
        // would never dispose it. Scoped means the tick stops when the session does.
        services.AddScoped<IMinuteTicker, TimeProviderMinuteTicker>();

        // Platform probes for the health center. These are the "nothing is available" answers;
        // the Windows host replaces each with a real probe of its own (see
        // Wakeel.Desktop/App.xaml.cs), so Core never depends on a Windows-only API and every
        // check can be driven from a test with a fake. TryAdd, so a host that registered its own
        // probe before calling AddWakeelCore keeps it.
        services.TryAddSingleton<IWordProbe, UnavailableProbes>();
        services.TryAddSingleton<IScannerProbe, UnavailableProbes>();
        services.TryAddSingleton<IDiskSpaceProbe, UnavailableProbes>();
        services.TryAddSingleton<IRuntimeProbe, UnavailableProbes>();

        return services;
    }
}
