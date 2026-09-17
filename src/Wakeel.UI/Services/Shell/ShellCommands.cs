namespace Wakeel.UI.Services.Shell;

/// <summary>
/// The few things a routed page asks the shell around it to do: open the quick-entry dialog (W94)
/// and drop the bell's panel (W10). Both live in <c>Layout/MainLayout.razor</c>, which a page has no
/// reference to — a layout only ever hands its page <c>@Body</c> — so the request travels through
/// this shared object instead, the same way <see cref="PageHeaderState"/> carries the header upward.
/// </summary>
public sealed class ShellCommands
{
    /// <summary>Raised when a page asks for the quick-entry dialog.</summary>
    public event Action? QuickCaptureRequested;

    /// <summary>Raised when a page asks for the notification panel.</summary>
    public event Action? NotificationsRequested;

    /// <summary>«إدخال سريع» on W08 (and Ctrl+N, which the shell handles itself).</summary>
    public void RequestQuickCapture() => QuickCaptureRequested?.Invoke();

    /// <summary>Opens the bell's panel from somewhere other than the bell.</summary>
    public void RequestNotifications() => NotificationsRequested?.Invoke();
}
