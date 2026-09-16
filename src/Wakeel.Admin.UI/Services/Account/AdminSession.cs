namespace Wakeel.Admin.UI.Services.Account;

/// <summary>
/// Who is at the administration tool right now. One object for the whole window: the shell reads it
/// to decide whether to draw the bar and the tabs at all, and every screen reads the name it stamps
/// on the operations log.
/// </summary>
/// <remarks>
/// It holds nothing secret — a name and a flag. The thing that actually makes the tool usable is
/// the open database in <c>AdminDb</c>, and that closes with the same call that clears this.
/// </remarks>
public sealed class AdminSession
{
    /// <summary>Raised when somebody signs in or out, so the shell can redraw.</summary>
    public event Action? Changed;

    /// <summary>Whether the tool is unlocked.</summary>
    public bool IsSignedIn { get; private set; }

    /// <summary>The administrator's name, for the top bar and the operations log.</summary>
    public string AdminName { get; private set; } = string.Empty;

    /// <summary>Records that the administrator is now in.</summary>
    public void SignIn(string adminName)
    {
        AdminName = adminName ?? string.Empty;
        IsSignedIn = true;
        Changed?.Invoke();
    }

    /// <summary>Records that nobody is in.</summary>
    public void SignOut()
    {
        if (!IsSignedIn && AdminName.Length == 0)
        {
            return;
        }

        IsSignedIn = false;
        AdminName = string.Empty;
        Changed?.Invoke();
    }
}
