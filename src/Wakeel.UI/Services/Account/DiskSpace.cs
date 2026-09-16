namespace Wakeel.UI.Services.Account;

/// <summary>
/// Tells the one write failure a person can actually do something about — a full disk — apart from
/// every other reason a file refuses to be written. The first run writes the key file, the database
/// and up to four stored documents in a row, so «قرص ممتلئ» is a state the screens must be able to
/// name on its own instead of hiding it inside a general failure (B1 acceptance criteria).
/// </summary>
public static class DiskSpace
{
    /// <summary>The Windows error for a disk with no room left, as it reaches managed code.</summary>
    private const int ErrorDiskFull = 0x70;

    /// <summary>The same condition reported against an already-open handle.</summary>
    private const int ErrorHandleDiskFull = 0x27;

    /// <summary>Whether this failure was the disk running out of room rather than anything else.</summary>
    public static bool IsFull(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException)
            {
                var code = current.HResult & 0xFFFF;
                if (code is ErrorDiskFull or ErrorHandleDiskFull)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
