namespace Wakeel.E2E.Support;

/// <summary>Locates the repository root (identified by <c>Wakeel.slnx</c>) from the test assembly's
/// build output folder, so paths to <c>src/Wakeel.Desktop</c>, <c>design/exports</c> and this
/// project's own <c>artifacts</c> folder can be resolved without hardcoding a Configuration/TFM
/// depth that would break if either changes.</summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Find);

    public static string Path => Cached.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Wakeel.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate the repository root (Wakeel.slnx) above {AppContext.BaseDirectory}.");
    }
}
