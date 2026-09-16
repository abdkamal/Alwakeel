namespace Wakeel.E2E.Support;

/// <summary>Resolves the path to the built Wakeel.Desktop host exe, per the package's fixed build
/// command (<c>dotnet build src/Wakeel.Desktop</c>, Debug configuration).</summary>
internal static class WakeelDesktopLocator
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string ExePath => Cached.Value;

    private static string Resolve()
    {
        var exePath = Path.Combine(
            RepoRoot.Path, "src", "Wakeel.Desktop", "bin", "Debug",
            "net10.0-windows10.0.19041.0", "Wakeel.Desktop.exe");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                "Wakeel.Desktop.exe was not found. Build it first: dotnet build src/Wakeel.Desktop",
                exePath);
        }

        return exePath;
    }
}
