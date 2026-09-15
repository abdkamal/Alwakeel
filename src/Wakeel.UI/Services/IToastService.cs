using Wakeel.UI.Components;

namespace Wakeel.UI.Services;

/// <summary>
/// Queues transient toast notifications for WToastHost to render. Scoped per Blazor circuit/host
/// so each open window has its own toast queue.
/// </summary>
public interface IToastService
{
    /// <summary>Currently queued toasts, oldest first.</summary>
    IReadOnlyList<WToastMessage> Toasts { get; }

    /// <summary>Raised whenever the queue changes, so WToastHost can re-render.</summary>
    event Action? Changed;

    /// <summary>Queues a toast with the given Arabic message and semantic variant.</summary>
    void Show(string text, WSemanticVariant variant = WSemanticVariant.Neutral);

    /// <summary>Removes a toast (called by WToastHost after its auto-dismiss timer, or on manual close).</summary>
    void Dismiss(Guid id);
}
