namespace Wakeel.Core.Services;

/// <summary>
/// Runs one background pass on the context that owns the database session, and completes when the
/// pass has finished.
/// </summary>
/// <param name="pass">The work to run; it reads and writes the shared <see cref="Data.WakeelDb"/>.</param>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> الوكيل keeps exactly one open database session per signed-in account,
/// and the shell's screens use it from the renderer's thread. A background timer that touched the
/// same session from a thread-pool thread would overlap whatever the screen is doing, and a
/// database context cannot serve two operations at once: the overlap fails the pass, and can
/// leave the session's change tracker in a state the next screen trips over. So a background pass
/// never runs itself — it hands itself to this delegate, and the shell supplies a dispatcher that
/// runs it where the screens run.
/// </para>
/// <para>
/// <b>What a Blazor shell supplies.</b> <c>ComponentBase.InvokeAsync</c> (or
/// <c>Dispatcher.InvokeAsync</c> on the renderer) already has exactly this shape, so the shell
/// passes the method group straight in and nothing else is needed.
/// </para>
/// <para>
/// <b>Exceptions.</b> The dispatcher should let the pass's exception surface; the caller
/// (<see cref="IClockGuard"/>, <see cref="IReminderScheduler"/>) already treats a failed pass as
/// transient and retries on its next interval.
/// </para>
/// </remarks>
public delegate Task BackgroundPassDispatcher(Func<Task> pass);

/// <summary>Ready-made <see cref="BackgroundPassDispatcher"/> values.</summary>
public static class BackgroundPass
{
    /// <summary>
    /// Runs the pass right where the timer fired, with no marshalling at all.
    /// </summary>
    /// <remarks>
    /// Only safe when nothing else uses the same database session at the same time — a test that
    /// drives the pass by hand, or a host with no user interface. A shell that renders screens
    /// over the same session must supply its renderer's dispatcher instead; see
    /// <see cref="BackgroundPassDispatcher"/>.
    /// </remarks>
    public static BackgroundPassDispatcher Inline { get; } = pass => pass();
}
