namespace Wakeel.Crypto;

/// <summary>One logical file handed to the container writer.</summary>
public sealed class ContainerEntrySource
{
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
