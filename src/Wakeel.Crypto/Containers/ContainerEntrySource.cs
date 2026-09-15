namespace Wakeel.Crypto;

/// <summary>One logical file handed to the container writer.</summary>
public sealed class ContainerEntrySource
{
    /// <summary>
    /// Characters a single path segment must not contain, beyond '\', ':' and the control
    /// characters already rejected for the whole name — these are the extra characters Windows
    /// itself refuses in one file or folder name.
    /// </summary>
    private static readonly char[] SegmentInvalidChars = ['"', '<', '>', '|', '*', '?'];

    /// <summary>DOS device names Windows reserves, whatever extension follows them.</summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly Func<Stream> _open;

    private ContainerEntrySource(string name, Func<Stream> open)
    {
        if (!IsAcceptableName(name))
        {
            throw new CryptoException(ErrorCode.Corrupt, "A container entry name must be a plain relative name.");
        }

        Name = name;
        _open = open;
    }

    /// <summary>
    /// The rules an item name has to follow: a plain relative name, sub folders separated by
    /// a forward slash only. The reader applies the very same rules to the names a foreign
    /// producer declares, so nothing can ever be written outside the chosen folder.
    /// </summary>
    public static bool IsAcceptableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Contains('\\', StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains(':', StringComparison.Ordinal)
            || name.StartsWith('/')
            || name.EndsWith('/')
            || Path.IsPathRooted(name))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        // Each '/'-separated segment also has to be legal as an actual Windows file or folder
        // name on its own: no wildcard or quoting characters, no trailing dot or space, and not
        // one of the reserved DOS device names — otherwise ContainerReader.ExtractTo hands the
        // name straight to FileStream and a hostile manifest turns into a raw IOException
        // instead of a CryptoException.
        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0
                || segment.IndexOfAny(SegmentInvalidChars) >= 0
                || segment.EndsWith('.')
                || segment.EndsWith(' '))
            {
                return false;
            }

            var dot = segment.IndexOf('.', StringComparison.Ordinal);
            var stem = dot >= 0 ? segment[..dot] : segment;
            if (ReservedDeviceNames.Contains(stem))
            {
                return false;
            }
        }

        return true;
    }

    public string Name { get; }

    public static ContainerEntrySource FromBytes(string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var copy = (byte[])content.Clone();
        return new ContainerEntrySource(name, () => new MemoryStream(copy, writable: false));
    }

    public static ContainerEntrySource FromText(string name, string content) =>
        FromBytes(name, System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty));

    public static ContainerEntrySource FromFile(string name, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        return new ContainerEntrySource(name, () => new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Stream OpenRead() => _open();
}
