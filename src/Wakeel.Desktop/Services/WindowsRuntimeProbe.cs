using Microsoft.Web.WebView2.Core;
using Wakeel.Core.Services;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IRuntimeProbe"/>: whether the display components الوكيل renders
/// its screens through are installed, and their version — the runtime card of the health center
/// (W12).
/// </summary>
/// <remarks>
/// <para>
/// The answer comes from the display stack's own "which version is available" query, which is a
/// registry lookup and does not start a browser process. It is the same check the application
/// implicitly makes when it creates its window, so a fault here explains a window that would fail
/// to render rather than reporting something unrelated.
/// </para>
/// <para>
/// The version string is returned for the log and for support, but the card the user sees says
/// only «جاهزة» or «مكوّنات العرض ناقصة» — a build number in front of the user would be exactly the
/// technical noise AGREEMENT item 15 rules out.
/// </para>
/// </remarks>
public sealed class WindowsRuntimeProbe : IRuntimeProbe
{
    public Task<RuntimeInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Detect());
    }

    private static RuntimeInfo Detect()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? RuntimeInfo.Missing : new RuntimeInfo(true, version);
        }
        catch (Exception)
        {
            // The query throws when nothing is installed, which is precisely the "missing" answer.
            return RuntimeInfo.Missing;
        }
    }
}
