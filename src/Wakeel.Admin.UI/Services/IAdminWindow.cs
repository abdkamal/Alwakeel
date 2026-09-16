namespace Wakeel.Admin.UI.Services;

/// <summary>
/// The one thing a screen sometimes has to ask of the window around it: close the tool. A01's
/// «الخروج» is the only caller in this sub-package — somebody who opened the tool by mistake on a
/// machine with no organisation should be able to leave without creating one.
/// </summary>
public interface IAdminWindow
{
    /// <summary>Whether this host can close itself at all.</summary>
    bool CanClose { get; }

    /// <summary>Closes the tool.</summary>
    void Close();
}

/// <summary>The window for hosts that have none: nothing closes, and the screen leaves its button out.</summary>
public sealed class NoAdminWindow : IAdminWindow
{
    /// <inheritdoc />
    public bool CanClose => false;

    /// <inheritdoc />
    public void Close()
    {
        // A test host has no window to close, and pretending otherwise would end the test run.
    }
}
