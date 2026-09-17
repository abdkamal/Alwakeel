using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.UI.Services.Shell;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// Registers the account services of الوكيل: the first run (W02–W04), signing in (W05, W07), the
/// automatic lock (W06) and recovery (W07).
/// </summary>
/// <remarks>
/// Everything here is a singleton, because there is one installation, one account and one window.
/// The two platform-bound pieces — <see cref="IPlatformProtector"/> and <see cref="IPrintService"/> —
/// are registered with <c>TryAdd</c>, so a host that has a real one registers it first and this
/// method leaves it alone; a host that has none gets an implementation that says so honestly rather
/// than one that pretends.
/// </remarks>
public static class AccountServiceCollectionExtensions
{
    /// <summary>Adds the account services on top of <c>AddWakeelCore</c>.</summary>
    public static IServiceCollection AddWakeelAccount(
        this IServiceCollection services,
        Action<AccountOptions>? configure = null)
    {
        var options = new AccountOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // A host that never registers a real protector would otherwise silently ship machine wraps
        // that bind to nothing; the null protector exists for tests and says so in its own name.
        services.TryAddSingleton<IPlatformProtector, NullPlatformProtector>();
        services.TryAddSingleton<IPrintService, NoPrintService>();
        services.TryAddSingleton<IImagePixels, NoImagePixels>();

        services.TryAddSingleton<AccountSession>(provider => new AccountSession(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IClock>()));

        services.TryAddSingleton(provider => new SignInProfileStore(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IPlatformProtector>()));

        services.TryAddSingleton(provider => new SetupInspectionService(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<AccountSession>()));

        services.TryAddSingleton(provider => new ActivationService(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IPlatformProtector>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<AccountSession>(),
            provider.GetRequiredService<SetupInspectionService>(),
            provider.GetRequiredService<SignInProfileStore>(),
            provider.GetRequiredService<AccountOptions>()));

        services.TryAddSingleton(provider => new LoginService(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IPlatformProtector>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<AccountSession>(),
            provider.GetRequiredService<SignInProfileStore>(),
            provider.GetRequiredService<AccountOptions>()));

        services.TryAddSingleton(provider => new LockService(
            provider.GetRequiredService<AccountSession>(),
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IPlatformProtector>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<SignInProfileStore>(),
            provider.GetRequiredService<LoginService>(),
            provider.GetRequiredService<ShellServices>));

        services.TryAddSingleton(provider => new RecoveryService(
            provider.GetRequiredService<WakeelPaths>(),
            provider.GetRequiredService<IPlatformProtector>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<AccountSession>(),
            provider.GetRequiredService<SignInProfileStore>(),
            provider.GetRequiredService<LoginService>(),
            provider.GetRequiredService<AccountOptions>()));

        services.TryAddSingleton(provider => new QrImageReader(provider.GetRequiredService<IImagePixels>()));

        // The open database itself, so the Scoped services of Wakeel.Core (the attention center,
        // the badges, the notifications, the clock guard, the health center, quick capture) can be
        // resolved at all: AddWakeelCore deliberately leaves WakeelDb to the host, because it only
        // exists once a key derived from the account password has opened a DbSession. Scoped, not
        // singleton: AccountSession.Db is a different object after every lock, and ShellServices
        // gives each open session a scope of its own so nothing outlives the session it belongs to.
        services.TryAddScoped(provider => provider.GetRequiredService<AccountSession>().Db);

        // The daily shell's window onto those services (B2 screens W08–W12, W94).
        services.TryAddSingleton<ShellServices>();

        // What a routed page asks the layout around it to do (open W94, drop the W10 panel).
        services.TryAddSingleton<ShellCommands>();

        // «تأكيد»/«رفض» on a phone expense, straight from the attention center (AGREEMENT item 50).
        services.TryAddScoped<IPhoneExpenseReview, PhoneExpenseReview>();

        // What only the machine can do for W11 and W12. TryAdd, so the desktop host's real
        // implementations — registered before this call — stay in place; a host that has none gets
        // the honest "this machine cannot" answers instead of a pretence.
        services.TryAddSingleton<ISystemSettingsLauncher, NoHostBridge>();
        services.TryAddSingleton<IFileSaveService, NoHostBridge>();

        // The host window hands a dropped setup file over here; W02 listens on the other side.
        services.TryAddSingleton<SetupFileDrop>();

        return services;
    }
}
