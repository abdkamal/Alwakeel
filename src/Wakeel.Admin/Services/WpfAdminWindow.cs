using System.Windows;
using System.Windows.Threading;
using Wakeel.Admin.UI.Services;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminWindow"/>: A01's «الخروج» closes the tool's window, which
/// ends the application, and a screen may register a guard that the window's own close button has
/// to get past first.
/// </summary>
public sealed class WpfAdminWindow : IAdminWindow
{
    private readonly Dispatcher _dispatcher;
    private Func<bool>? _guard;

    public WpfAdminWindow(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool CanClose => true;

    /// <inheritdoc />
    public void Close() => _dispatcher.InvokeAsync(() => Application.Current?.MainWindow?.Close());

    /// <inheritdoc />
    public void SetCloseGuard(Func<bool>? guard) => _guard = guard;

    /// <summary>
    /// Whether the window may close right now. With no guard registered it always may; with one the
    /// screen decides, and a screen that says no has put its own question on screen instead.
    /// </summary>
    /// <remarks>
    /// The guard runs on the window's thread, which is also the thread the screens render on, so it
    /// may touch the component that registered it. A guard that throws is treated as no guard at
    /// all: a tool that can never be closed would be worse than one that closes without asking.
    /// </remarks>
    public bool MayClose()
    {
        var guard = _guard;
        if (guard is null)
        {
            return true;
        }

        try
        {
            return guard();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return true;
        }
    }
}
