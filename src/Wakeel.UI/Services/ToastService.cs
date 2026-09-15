using System.Collections.Concurrent;
using Wakeel.UI.Components;

namespace Wakeel.UI.Services;

/// <inheritdoc cref="IToastService" />
public sealed class ToastService : IToastService
{
    private readonly ConcurrentQueue<WToastMessage> _order = new();
    private readonly ConcurrentDictionary<Guid, WToastMessage> _byId = new();

    /// <inheritdoc />
    public IReadOnlyList<WToastMessage> Toasts => _order.Where(t => _byId.ContainsKey(t.Id)).ToList();

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void Show(string text, WSemanticVariant variant = WSemanticVariant.Neutral)
    {
        var toast = new WToastMessage(Guid.NewGuid(), text, variant);
        _byId[toast.Id] = toast;
        _order.Enqueue(toast);
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Dismiss(Guid id)
    {
        if (_byId.TryRemove(id, out _))
        {
            // _order only ever grows by enqueueing; drain the stale head here so it does not keep
            // accumulating dismissed entries for the lifetime of the session.
            while (_order.TryPeek(out var head) && !_byId.ContainsKey(head.Id))
            {
                _order.TryDequeue(out _);
            }

            Changed?.Invoke();
        }
    }
}
