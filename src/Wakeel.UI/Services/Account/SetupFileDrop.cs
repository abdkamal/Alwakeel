namespace Wakeel.UI.Services.Account;

/// <summary>
/// A file dragged from a folder window onto الوكيل and let go there (W02). The host window is what
/// receives such a drop, and this is how it reaches the screen.
/// </summary>
/// <param name="Path">Where the dropped file is; it is read, never moved.</param>
public sealed record DroppedFile(string Path)
{
    /// <summary>The name shown next to the drop zone.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The bridge between the host window's drag and drop and the first-run screen. It is a singleton
/// so a file let go over the window reaches whichever screen is open at that moment, and it carries
/// the last drop until a screen takes it, so a drop that lands a moment before the screen is drawn
/// is not lost.
/// </summary>
public sealed class SetupFileDrop
{
    private readonly Lock _gate = new();
    private DroppedFile? _pending;

    /// <summary>Raised on whatever thread the host dropped from; the screen marshals it itself.</summary>
    public event Action<DroppedFile>? Dropped;

    /// <summary>Told by the host that a file was let go over the window.</summary>
    public void Offer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var file = new DroppedFile(path);
        lock (_gate)
        {
            _pending = file;
        }

        Dropped?.Invoke(file);
    }

    /// <summary>
    /// Takes the drop that is waiting, if there is one, and clears it. A screen calls this when it
    /// opens, so a file let go while the window was still starting up is still picked up.
    /// </summary>
    public DroppedFile? TakePending()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = null;
            return pending;
        }
    }

    /// <summary>Forgets a drop nobody took, so it cannot surface on an unrelated screen later.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending = null;
        }
    }
}
