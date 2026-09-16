using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Design;

namespace Wakeel.Admin.UI;

/// <summary>
/// The administration tool's composition root. One call from the host wires the whole tool; each
/// sub-package adds its own area through its own extension method below, so the three of them never
/// have to edit the same block.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything «مدير نظام الوكيل» needs: the shared design system, the tool's folders
    /// and database, and every area's services.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="paths">Where the tool keeps its things; the host resolves this from
    /// <c>--data-folder</c> or the shipped ProgramData location.</param>
    public static IServiceCollection AddWakeelAdmin(this IServiceCollection services, AdminPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(paths);

        // One window, one open database, one signed-in administrator: these are the tool, not a
        // per-page concern, so they are singletons rather than scoped.
        services.TryAddSingleton<AdminDb>();
        services.TryAddSingleton<AdminSession>();
        services.TryAddSingleton<AdminAuditService>();
        services.TryAddSingleton<AdminKeyService>();
        services.TryAddSingleton<AdminPendingChanges>();
        services.TryAddSingleton<AdminDashboardService>();

        services.AddWakeelDesign();
        services.TryAddScoped<AdminPageHeaderState>();

        services.AddAdminAccount();
        services.AddAdminOrganisation();
        services.AddAdminStructure();
        services.AddAdminDevices();
        services.AddAdminKeys();

        return services;
    }

    /// <summary>
    /// admin-1's own area: the administrator account, the organisation recovery sheet, signing in,
    /// and the two host-supplied capabilities those screens need (printing and reading a picture).
    /// </summary>
    public static IServiceCollection AddAdminAccount(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AdminOptions>();
        services.TryAddSingleton<AdminAccountService>();

        // Both have a do-nothing implementation so a test host — or a machine whose imaging stack
        // refuses — renders the screen and says so in words instead of failing. A real host
        // registers its own before calling this, and TryAdd leaves it in place.
        services.TryAddSingleton<IAdminWindow, NoAdminWindow>();
        services.TryAddSingleton<IAdminPrintService, NoAdminPrintService>();
        services.TryAddSingleton<IAdminImagePixels, NoAdminImagePixels>();
        services.TryAddSingleton<AdminQrImageReader>();

        return services;
    }

    /// <summary>
    /// admin-2's A04: the organisation's identity, and the one host-supplied capability that screen
    /// needs — cutting a chosen picture down to a square for the logo.
    /// </summary>
    public static IServiceCollection AddAdminOrganisation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAdminImageSquareCrop, NoAdminImageSquareCrop>();
        services.TryAddSingleton<AdminOrgService>();

        return services;
    }

    /// <summary>admin-2's A05: the four-layer structure and which of its nodes are offices.</summary>
    public static IServiceCollection AddAdminStructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<AdminStructureService>();
        return services;
    }

    /// <summary>admin-2's A06: the devices of each office and who sits at them.</summary>
    public static IServiceCollection AddAdminDevices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<AdminDeviceService>();
        return services;
    }

    /// <summary>admin-2's A07: device certificates, office keys, recovery and revocation.</summary>
    public static IServiceCollection AddAdminKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<AdminDeviceKeyService>();
        return services;
    }
}
