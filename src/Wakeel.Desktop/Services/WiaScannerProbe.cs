using System.Runtime.InteropServices;
using Wakeel.Core.Services;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IScannerProbe"/>: answers whether a scanner is attached — the
/// "الماسح (WIA متاح؟)" card of the health center (W12).
/// </summary>
/// <remarks>
/// <para>
/// Windows exposes scanners through WIA, whose device manager is a COM object. It is created by
/// ProgID and driven through late binding (<c>dynamic</c>), exactly as ARCHITECTURE.md §9 does for
/// Word: no interop assembly is added, so nothing here is tied to one Windows SDK version, and a
/// machine whose WIA service is stopped simply reports no scanner instead of failing to load a
/// type.
/// </para>
/// <para>
/// Only scanners are counted. WIA's device list also contains cameras and video devices, and a
/// webcam is not something the office can scan a letter with, so devices are filtered on WIA's
/// scanner device type. A device whose type cannot be read is counted anyway rather than dropped:
/// under-reporting a scanner that is really there would tell the user to plug in hardware they are
/// already looking at.
/// </para>
/// <para>
/// The enumeration runs on a pool thread and every COM object it touches is released explicitly.
/// WIA holds a device handle while a DeviceInfo lives, and leaking one would leave the scanner
/// busy for the rest of the session — the health center refreshes often enough for that to matter.
/// </para>
/// </remarks>
public sealed class WiaScannerProbe : IScannerProbe
{
    /// <summary>WIA's device manager, the entry point to the device list.</summary>
    private const string DeviceManagerProgId = "WIA.DeviceManager";

    /// <summary>WIA's <c>WiaDeviceType.ScannerDeviceType</c>.</summary>
    private const int ScannerDeviceType = 1;

    public Task<ScannerInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(ScannerInfo.None);
        }

        // COM enumeration blocks; keep it off whichever thread asked (in the app, the UI thread).
        return Task.Run(Detect, cancellationToken);
    }

    private static ScannerInfo Detect()
    {
        object? manager = null;
        object? deviceInfos = null;
        try
        {
            var type = Type.GetTypeFromProgID(DeviceManagerProgId);
            if (type is null)
            {
                return ScannerInfo.None;
            }

            manager = Activator.CreateInstance(type);
            if (manager is null)
            {
                return ScannerInfo.None;
            }

            dynamic dynamicManager = manager;
            deviceInfos = dynamicManager.DeviceInfos;
            if (deviceInfos is null)
            {
                return ScannerInfo.None;
            }

            dynamic dynamicInfos = deviceInfos;
            int total = dynamicInfos.Count;
            var scanners = 0;

            // WIA's collections are 1-based.
            for (var i = 1; i <= total; i++)
            {
                object? info = null;
                try
                {
                    info = dynamicInfos[i];
                    if (info is null)
                    {
                        continue;
                    }

                    dynamic dynamicInfo = info;
                    try
                    {
                        int deviceType = dynamicInfo.Type;
                        if (deviceType == ScannerDeviceType)
                        {
                            scanners++;
                        }
                    }
                    catch (Exception)
                    {
                        // Type unreadable on this driver: count it rather than hide a real scanner.
                        scanners++;
                    }
                }
                catch (Exception)
                {
                    // One unreadable device must not hide the rest of the list.
                }
                finally
                {
                    Release(info);
                }
            }

            return scanners > 0 ? new ScannerInfo(true, scanners) : ScannerInfo.None;
        }
        catch (Exception)
        {
            // No WIA, service stopped, or a driver that refuses to enumerate: for the user this is
            // simply "no scanner is ready", which is what the card says.
            return ScannerInfo.None;
        }
        finally
        {
            Release(deviceInfos);
            Release(manager);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception)
        {
            // Releasing is best effort; the runtime reclaims the wrapper either way.
        }
    }
}
