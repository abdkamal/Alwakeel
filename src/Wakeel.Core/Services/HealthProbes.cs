namespace Wakeel.Core.Services;

// The health center has to report on things that live outside the database and outside Core:
// whether Word is installed, whether a scanner is attached, how much room is left on the disk,
// and whether the display components the application renders through are present. Each is a tiny
// interface here and a Windows implementation in Wakeel.Desktop/Services, so Core stays free of
// Windows-only APIs and every check can be driven from a test with a fake.

/// <summary>Whether Word is installed, and which version (ARCHITECTURE.md §9, AGREEMENT item 10).</summary>
/// <param name="Installed">True when Word is available for editing and PDF generation.</param>
/// <param name="Version">Version text as reported by Word, when known.</param>
public sealed record WordInfo(bool Installed, string? Version)
{
    /// <summary>Word is not installed.</summary>
    public static WordInfo Missing { get; } = new(false, null);
}

/// <summary>Whether a scanner is attached.</summary>
/// <param name="Available">True when at least one scanner answered.</param>
/// <param name="DeviceCount">How many scanners answered.</param>
public sealed record ScannerInfo(bool Available, int DeviceCount)
{
    /// <summary>No scanner answered.</summary>
    public static ScannerInfo None { get; } = new(false, 0);
}

/// <summary>Free and total room on the volume holding the installation.</summary>
/// <param name="Measured">False when the volume could not be inspected at all.</param>
/// <param name="FreeBytes">Bytes available.</param>
/// <param name="TotalBytes">Bytes in total.</param>
public sealed record DiskSpaceInfo(bool Measured, long FreeBytes, long TotalBytes)
{
    /// <summary>The volume could not be inspected.</summary>
    public static DiskSpaceInfo Unknown { get; } = new(false, 0, 0);
}

/// <summary>Whether the display components the application renders through are present, and their version.</summary>
/// <param name="Installed">True when the components are present.</param>
/// <param name="Version">Version text, when known.</param>
public sealed record RuntimeInfo(bool Installed, string? Version)
{
    /// <summary>The components are missing.</summary>
    public static RuntimeInfo Missing { get; } = new(false, null);
}

/// <summary>Detects Word.</summary>
public interface IWordProbe
{
    Task<WordInfo> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Detects attached scanners.</summary>
public interface IScannerProbe
{
    Task<ScannerInfo> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Measures free space on the volume that holds a given path.</summary>
public interface IDiskSpaceProbe
{
    Task<DiskSpaceInfo> MeasureAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Detects the display components the application renders through.</summary>
public interface IRuntimeProbe
{
    Task<RuntimeInfo> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The answer Core falls back to when no platform probe is registered — every probe reports
/// "not available" rather than throwing, so the health center degrades to a warning card instead
/// of failing to render. The desktop host replaces each of these with a real Windows probe.
/// </summary>
public sealed class UnavailableProbes : IWordProbe, IScannerProbe, IDiskSpaceProbe, IRuntimeProbe
{
    Task<WordInfo> IWordProbe.DetectAsync(CancellationToken cancellationToken) => Task.FromResult(WordInfo.Missing);

    Task<ScannerInfo> IScannerProbe.DetectAsync(CancellationToken cancellationToken) => Task.FromResult(ScannerInfo.None);

    Task<DiskSpaceInfo> IDiskSpaceProbe.MeasureAsync(string path, CancellationToken cancellationToken) => Task.FromResult(DiskSpaceInfo.Unknown);

    Task<RuntimeInfo> IRuntimeProbe.DetectAsync(CancellationToken cancellationToken) => Task.FromResult(RuntimeInfo.Missing);
}
