namespace Wakeel.Admin.UI.Services;

/// <summary>
/// The two things a screen sometimes has to ask of the window around it: close the tool, and stand
/// between the window's own close button and something that cannot be undone. A01's «الخروج» is the
/// first caller — somebody who opened the tool by mistake on a machine with no organisation should
/// be able to leave without creating one — and A01's recovery sheet is the first guard.
/// </summary>
public interface IAdminWindow
{
    /// <summary>Whether this host can close itself at all.</summary>
    bool CanClose { get; }

    /// <summary>Closes the tool.</summary>
    void Close();

    /// <summary>
    /// Puts a screen between the window's own close button and the tool shutting down, or takes the
    /// guard away again with null.
    /// </summary>
    /// <param name="guard">
    /// Asked, on the window's own thread, whether the tool may close. Returning true lets it close;
    /// returning false cancels the close, and the screen that registered the guard is expected to
    /// have put its own question on screen instead. A screen MUST clear its guard when it goes away,
    /// and before it closes the tool deliberately, or nothing would ever close.
    /// </param>
    void SetCloseGuard(Func<bool>? guard);
}

/// <summary>The window for hosts that have none: nothing closes, and the screen leaves its button out.</summary>
public sealed class NoAdminWindow : IAdminWindow
{
    /// <inheritdoc />
    public bool CanClose => false;

    /// <summary>
    /// The guard a screen registered, so a test can ask it exactly what the real window's close
    /// button asks.
    /// </summary>
    public Func<bool>? CloseGuard { get; private set; }

    /// <inheritdoc />
    public void Close()
    {
        // A test host has no window to close, and pretending otherwise would end the test run.
    }

    /// <inheritdoc />
    public void SetCloseGuard(Func<bool>? guard) => CloseGuard = guard;
}
