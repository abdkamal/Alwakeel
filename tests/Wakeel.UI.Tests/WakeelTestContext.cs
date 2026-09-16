using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Design;
using Wakeel.Core.Services;
using Wakeel.UI.Services;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Tests;

/// <summary>
/// Shared bUnit context that registers the DI services every Wakeel.UI component expects to find
/// (mirroring what Wakeel.Desktop's App.xaml.cs registers): the Wakeel.Design UI services
/// (in-memory IUiStateStore, ThemeService, IToastService — see AddWakeelDesign) plus the
/// application-specific PageHeaderState. IJSRuntime and NavigationManager are already provided by
/// bUnit itself (Loose-mode JSInterop returns defaults for calls this test suite does not explicitly
/// set up).
/// </summary>
public abstract class WakeelTestContext : BunitContext
{
    /// <summary>This test's own installation folder, removed again when it finishes.</summary>
    private readonly string _root;

    protected WakeelTestContext()
    {
        // Loose mode lets JS interop calls (theme application, dialog focus trap, etc.) go through with
        // default return values instead of throwing, while still recording each invocation so a test can
        // assert against it (see Gallery_ThemeToggle_ChangesDataThemeViaJsInterop).
        JSInterop.Mode = Bunit.JSRuntimeMode.Loose;

        Services.AddWakeelDesign();
        Services.AddScoped<PageHeaderState>();

        // MainLayout hosts the automatic lock of W06, so rendering the shell at all now wants the
        // account services behind it. They are registered here exactly as the host registers them.
        // A context that needs an installation folder of its own (see FirstRunScreenContext)
        // registers a second set over these; the later registration is the one that answers.
        //
        // The folder is a temporary one of this test's own. Left at its default, WakeelPaths would
        // point every UI test at C:\ProgramData\Wakeel — the operator's real installation — and a
        // test that so much as asked whether this machine is activated would be reading it.
        _root = Path.Combine(Path.GetTempPath(), "wakeel-ui-tests", Guid.NewGuid().ToString("N"));
        Services.AddWakeelCore(options => options.Paths = WakeelPaths.ForRoot(_root));
        Services.AddWakeelAccount();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary folder the machine still holds open is the operating system's problem,
            // never a failed test.
        }
    }
}
