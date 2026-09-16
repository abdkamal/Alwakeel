using System.Windows;
using System.Windows.Threading;
using Wakeel.Admin.UI.Services;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminWindow"/>: A01's «الخروج» closes the tool's window, which
/// ends the application.
/// </summary>
public sealed class WpfAdminWindow : IAdminWindow
{
    private readonly Dispatcher _dispatcher;

    public WpfAdminWindow(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool CanClose => true;

    /// <inheritdoc />
    public void Close() => _dispatcher.InvokeAsync(() => Application.Current?.MainWindow?.Close());
}
