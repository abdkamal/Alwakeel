namespace Wakeel.Core.Data;

/// <summary>
/// One stored value that could not be read back as the type its column declares — for example a
/// truncated or corrupted ISO-8601 timestamp in a NOT NULL text column.
/// </summary>
/// <param name="Table">Table the value was read from, e.g. <c>correspondence</c>.</param>
/// <param name="Column">Column the value was read from, e.g. <c>created_at</c>.</param>
/// <param name="Text">The raw text as stored, so the row can be found and repaired.</param>
public sealed record DataIntegrityEvent(string Table, string Column, string Text);

/// <summary>
/// Observable sink for data-integrity problems detected while materializing rows. Reading must
/// never crash a list (a single corrupt value would otherwise take down a whole screen through
/// an exception raised inside EF materialization), so the readers stay tolerant and report here
/// instead of throwing. <see cref="WakeelDb"/> forwards every event to its
/// <c>ILogger&lt;WakeelDb&gt;</c> as a warning, and the health center (W12) can read
/// <see cref="Recent"/> to show the user that some stored values need attention.
/// </summary>
/// <remarks>
/// The sink is static because the readers that feed it are EF value converters, which are part
/// of the model and have no access to the context instance or to dependency injection. It keeps
/// at most <see cref="Capacity"/> of the most recent events; everything else is the subscribers'
/// responsibility.
/// </remarks>
public static class DataIntegrityLog
{
    /// <summary>Maximum number of events kept in <see cref="Recent"/>; the oldest are dropped first.</summary>
    public const int Capacity = 100;

    private static readonly Lock Gate = new();
    private static readonly Queue<DataIntegrityEvent> Events = new();

    /// <summary>Raised once for each reported event, on the thread that detected it.</summary>
    public static event Action<DataIntegrityEvent>? Reported;

    /// <summary>The most recent events, oldest first (at most <see cref="Capacity"/>).</summary>
    public static IReadOnlyList<DataIntegrityEvent> Recent
    {
        get
        {
            lock (Gate)
            {
                return [.. Events];
            }
        }
    }

    /// <summary>Records one unreadable stored value and notifies the subscribers.</summary>
    public static void Report(string table, string column, string text)
    {
        var integrityEvent = new DataIntegrityEvent(table, column, text);
        lock (Gate)
        {
            Events.Enqueue(integrityEvent);
            while (Events.Count > Capacity)
            {
                _ = Events.Dequeue();
            }
        }

        Reported?.Invoke(integrityEvent);
    }

    /// <summary>Clears the retained events (the health center after the user acknowledges them, and tests).</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Events.Clear();
        }
    }
}
