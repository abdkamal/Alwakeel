using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI;

/// <summary>
/// Every route the administration tool has, in one place, and which tab of the strip each area
/// belongs to.
/// </summary>
/// <remarks>
/// admin-1 builds the shell, the account screens and the dashboard; the routes of the other areas
/// are named here from the start so the tab strip is complete and each later sub-package only has
/// to add the page at the address that is already written down. A tab whose screen does not exist
/// yet resolves to null and the strip simply stays where it is rather than landing on an empty page.
/// </remarks>
public static class AdminRoutes
{
    /// <summary>A01 — the first run, before there is an account.</summary>
    public const string FirstRun = "/first-run";

    /// <summary>A02 — signing in.</summary>
    public const string SignIn = "/sign-in";

    /// <summary>A03 — the organisation dashboard, which is also where the tool opens.</summary>
    public const string Dashboard = "/";

    /// <summary>A04 — the organisation and its identity (admin-2).</summary>
    public const string Organisation = "/organisation";

    /// <summary>A05 — the structure (admin-2).</summary>
    public const string Structure = "/structure";

    /// <summary>A06 — offices and devices (admin-2).</summary>
    public const string OfficesAndDevices = "/offices";

    /// <summary>A07 — accounts and keys (admin-2).</summary>
    public const string AccountsAndKeys = "/accounts";

    /// <summary>A08 — exporting a setup file (admin-3).</summary>
    public const string SetupExport = "/export";

    /// <summary>A09 — maintenance (admin-3).</summary>
    public const string Maintenance = "/maintenance";

    /// <summary>A10 — distributing updates (admin-3).</summary>
    public const string Distribution = "/distribution";

    /// <summary>A11 — the operations log (admin-3).</summary>
    public const string AuditLog = "/audit";

    /// <summary>
    /// The route behind a tab label, or null while that area's screen has not been built yet.
    /// </summary>
    public static string? ForTab(string tab) => tab switch
    {
        AdminAr.Tabs.Organisation => Exists(Organisation),
        AdminAr.Tabs.Structure => Exists(Structure),
        AdminAr.Tabs.OfficesAndDevices => Exists(OfficesAndDevices),
        AdminAr.Tabs.AccountsAndKeys => Exists(AccountsAndKeys),
        AdminAr.Tabs.SetupExport => Exists(SetupExport),
        AdminAr.Tabs.Maintenance => Exists(Maintenance),
        AdminAr.Tabs.AuditLog => Exists(AuditLog),
        _ => null,
    };

    /// <summary>
    /// The routes that actually have a page behind them right now. admin-2 and admin-3 add their own
    /// as they build them; until then the strip draws the tab but going nowhere is better than
    /// landing on «الصفحة غير موجودة».
    /// </summary>
    private static readonly HashSet<string> Built = new(StringComparer.Ordinal)
    {
        Dashboard, FirstRun, SignIn,
    };

    private static string? Exists(string route) => Built.Contains(route) ? route : null;
}
