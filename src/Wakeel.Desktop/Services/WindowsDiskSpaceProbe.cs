using System.IO;
using Wakeel.Core.Services;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IDiskSpaceProbe"/>: free and total room on the volume that holds
/// the installation — the "المساحة (المتاح/الإجمالي)" card of the health center (W12).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DriveInfo.AvailableFreeSpace"/> is what is reported, not <c>TotalFreeSpace</c>: on a
/// volume with a disk quota the two differ, and what matters to the user is how much الوكيل can
/// actually write, not how much the volume holds in total.
/// </para>
/// <para>
/// The path is resolved to its root before the volume is looked up, so a path that does not exist
/// yet (a fresh installation folder being created) still reports the volume it will live on. A
/// path on a volume that cannot be inspected — a disconnected network location, a drive that has
/// gone away — reports <see cref="DiskSpaceInfo.Unknown"/>, and the card says the space could not
/// be measured rather than claiming zero bytes free.
/// </para>
/// </remarks>
public sealed class WindowsDiskSpaceProbe : IDiskSpaceProbe
{
    public Task<DiskSpaceInfo> MeasureAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Measure(path));
    }

    private static DiskSpaceInfo Measure(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DiskSpaceInfo.Unknown;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return DiskSpaceInfo.Unknown;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return DiskSpaceInfo.Unknown;
            }

            return new DiskSpaceInfo(true, drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception)
        {
            // An unreadable or vanished volume is "could not measure", never "zero free".
            return DiskSpaceInfo.Unknown;
        }
    }
}
