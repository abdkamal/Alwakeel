using System.Runtime.InteropServices;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IScanner"/> (AGREEMENT item 11, B3-2): the device list, the
/// resolution and colour the office chose, a quick preview, and a feeder run that keeps going
/// until the tray is empty.
/// </summary>
/// <remarks>
/// <para>
/// <b>Late binding, as everywhere else.</b> WIA is reached through <c>WIA.DeviceManager</c> by
/// ProgID and driven with <c>dynamic</c>, exactly as ARCHITECTURE §9 does for Word and as
/// <see cref="WiaScannerProbe"/> already does for the health centre's card. No interop assembly is
/// added, so nothing is tied to one Windows SDK version, and a machine whose WIA service is
/// stopped answers «لا ماسح ضوئي» instead of failing to load a type.
/// </para>
/// <para>
/// <b>Nothing here throws at the office.</b> Every failure — no WIA, no device, a driver that
/// refuses a property, an empty feeder, a cable pulled mid-page — becomes a
/// <see cref="ScanState"/>. A scan that stops after three of five sheets returns those three:
/// paper that has already gone through the machine is not fed again willingly.
/// </para>
/// <para>
/// <b>COM objects are released by hand.</b> WIA holds the scanner open while a device object
/// lives; leaking one leaves the hardware busy for the rest of the session, which the office
/// notices as a scanner that has stopped answering.
/// </para>
/// </remarks>
public sealed class WiaScanner : IScanner
{
    /// <summary>WIA's device manager, the entry point to the device list.</summary>
    private const string DeviceManagerProgId = "WIA.DeviceManager";

    /// <summary>WIA's <c>WiaDeviceType.ScannerDeviceType</c>.</summary>
    private const int ScannerDeviceType = 1;

    /// <summary>WIA's PNG format id — lossless, which is what text wants.</summary>
    private const string FormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    // WIA item property ids, from wiadef.h. They are numbers because that is how a late-bound
    // Properties collection is addressed; the names are what they mean.
    private const int PropertyDataType = 4103;
    private const int PropertyHorizontalResolution = 6147;
    private const int PropertyVerticalResolution = 6148;
    private const int PropertyHorizontalStart = 6149;
    private const int PropertyVerticalStart = 6150;
    private const int PropertyHorizontalExtent = 6151;
    private const int PropertyVerticalExtent = 6152;
    private const int PropertyDocumentHandlingSelect = 3088;
    private const int PropertyDocumentHandlingStatus = 3087;

    /// <summary>WIA's data types: colour, greyscale, and the two-tone setting for text.</summary>
    private const int DataTypeThreshold = 0;
    private const int DataTypeGrayscale = 2;
    private const int DataTypeColor = 3;

    /// <summary>WIA's <c>FEEDER</c> bit of Document Handling Select.</summary>
    private const int HandlingFeeder = 1;

    /// <summary>WIA's <c>FLATBED</c> bit of Document Handling Select.</summary>
    private const int HandlingFlatbed = 2;

    /// <summary>WIA's <c>FEED_READY</c> bit of Document Handling Status.</summary>
    private const int StatusFeedReady = 1;

    /// <summary>The most sheets one run will take, so a jammed feeder cannot scan for ever.</summary>
    private const int MaxFeederPages = 200;

    private bool _sawScanner;

    /// <inheritdoc />
    /// <remarks>
    /// Answers from what the last enumeration found, so the property is cheap enough for a screen
    /// to read while it renders. W45 calls <see cref="ListDevicesAsync"/> when it opens, which is
    /// what sets it.
    /// </remarks>
    public bool IsAvailable => _sawScanner;

    /// <inheritdoc />
    public Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _sawScanner = false;
            return Task.FromResult<IReadOnlyList<ScannerDevice>>([]);
        }

        // COM enumeration blocks; keep it off whichever thread asked (in the app, the UI thread).
        return Task.Run<IReadOnlyList<ScannerDevice>>(ListDevices, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ScanResult> PreviewAsync(ScanSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // A preview is one sheet at a low resolution off the glass: fast enough to look at before
        // committing a 600 dpi colour run of the wrong side of the page.
        var preview = settings with { Dpi = ScanSettings.PreviewDpi, MultiPage = false };
        return ScanAsync(preview, pageScanned: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ScanResult> ScanAsync(
        ScanSettings settings,
        IProgress<int>? pageScanned = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!OperatingSystem.IsWindows())
        {
            _sawScanner = false;
            return Task.FromResult(ScanResult.NoScanner);
        }

        return Task.Run(() => Scan(settings, pageScanned, cancellationToken), cancellationToken);
    }

    private List<ScannerDevice> ListDevices()
    {
        var devices = new List<ScannerDevice>();
        object? manager = null;
        object? deviceInfos = null;
        try
        {
            manager = CreateDeviceManager();
            if (manager is null)
            {
                _sawScanner = false;
                return devices;
            }

            dynamic dynamicManager = manager;
            deviceInfos = dynamicManager.DeviceInfos;
            if (deviceInfos is null)
            {
                _sawScanner = false;
                return devices;
            }

            dynamic dynamicInfos = deviceInfos;
            int total = dynamicInfos.Count;

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
                    if (!IsScanner(dynamicInfo))
                    {
                        continue;
                    }

                    string id = dynamicInfo.DeviceID;
                    devices.Add(new ScannerDevice(id, ReadName(dynamicInfo, id)));
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
        }
        catch (Exception)
        {
            // No WIA, service stopped, or a driver that refuses to enumerate.
        }
        finally
        {
            Release(deviceInfos);
            Release(manager);
        }

        _sawScanner = devices.Count > 0;
        return devices;
    }

    private ScanResult Scan(ScanSettings settings, IProgress<int>? pageScanned, CancellationToken cancellationToken)
    {
        object? manager = null;
        object? deviceInfos = null;
        object? deviceInfo = null;
        object? device = null;
        var pages = new List<ScannedPage>();

        try
        {
            manager = CreateDeviceManager();
            if (manager is null)
            {
                _sawScanner = false;
                return ScanResult.NoScanner;
            }

            dynamic dynamicManager = manager;
            deviceInfos = dynamicManager.DeviceInfos;
            if (deviceInfos is null)
            {
                _sawScanner = false;
                return ScanResult.NoScanner;
            }

            deviceInfo = FindDevice(deviceInfos, settings.DeviceId);
            if (deviceInfo is null)
            {
                _sawScanner = false;
                return ScanResult.NoScanner;
            }

            _sawScanner = true;
            dynamic dynamicInfo = deviceInfo;
            device = dynamicInfo.Connect();
            if (device is null)
            {
                return ScanResult.Failed;
            }

            dynamic dynamicDevice = device;
            var useFeeder = settings.MultiPage && TrySelectFeeder(dynamicDevice);
            var limit = useFeeder ? MaxFeederPages : 1;

            for (var page = 0; page < limit; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (useFeeder && page > 0 && !HasMorePaper(dynamicDevice))
                {
                    break;
                }

                var image = TransferOnePage(dynamicDevice, settings);
                if (image is null)
                {
                    // An empty feeder after at least one sheet is a finished run, not a failure.
                    break;
                }

                pages.Add(new ScannedPage(image, DocumentMediaTypes.Png));
                pageScanned?.Report(pages.Count);
            }

            if (pages.Count == 0)
            {
                return ScanResult.Failed;
            }

            return new ScanResult(ScanState.Ok, pages);
        }
        catch (OperationCanceledException)
        {
            // Paper that has already gone through the machine is kept: feeding it again is the
            // office's problem, not a tidy empty result.
            return new ScanResult(ScanState.Cancelled, pages);
        }
        catch (Exception)
        {
            return pages.Count > 0 ? new ScanResult(ScanState.Cancelled, pages) : ScanResult.Failed;
        }
        finally
        {
            Release(device);
            Release(deviceInfo);
            Release(deviceInfos);
            Release(manager);
        }
    }

    /// <summary>Scans one sheet and returns its PNG bytes, or <c>null</c> when there is no paper.</summary>
    private static byte[]? TransferOnePage(dynamic device, ScanSettings settings)
    {
        object? items = null;
        object? item = null;
        object? image = null;
        object? fileData = null;
        try
        {
            items = device.Items;
            if (items is null)
            {
                return null;
            }

            dynamic dynamicItems = items;
            if (dynamicItems.Count < 1)
            {
                return null;
            }

            item = dynamicItems[1];
            if (item is null)
            {
                return null;
            }

            dynamic dynamicItem = item;
            ApplySettings(dynamicItem, settings);

            image = dynamicItem.Transfer(FormatPng);
            if (image is null)
            {
                return null;
            }

            dynamic dynamicImage = image;
            fileData = dynamicImage.FileData;
            if (fileData is null)
            {
                return null;
            }

            dynamic dynamicData = fileData;
            return (byte[])dynamicData.get_BinaryData();
        }
        catch (COMException)
        {
            // WIA_ERROR_PAPER_EMPTY and its neighbours all mean the same thing to the office: the
            // machine has no more paper to give.
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Release(fileData);
            Release(image);
            Release(item);
            Release(items);
        }
    }

    /// <summary>
    /// Writes the resolution and the colour the office chose, and re-frames the scan area to the
    /// new resolution. A driver that refuses one property is left with its own value for that one
    /// rather than losing the whole scan.
    /// </summary>
    private static void ApplySettings(dynamic item, ScanSettings settings)
    {
        var dpi = settings.Dpi > 0 ? settings.Dpi : 300;
        var before = ReadProperty(item, PropertyHorizontalResolution) ?? dpi;
        var width = ReadProperty(item, PropertyHorizontalExtent);
        var height = ReadProperty(item, PropertyVerticalExtent);

        SetProperty(item, PropertyHorizontalResolution, dpi);
        SetProperty(item, PropertyVerticalResolution, dpi);

        // The extent is measured in pixels, so changing the resolution without rescaling it would
        // scan a corner of the sheet at 600 dpi and call it a page.
        if (before > 0 && width is > 0 && height is > 0)
        {
            SetProperty(item, PropertyHorizontalStart, 0);
            SetProperty(item, PropertyVerticalStart, 0);
            SetProperty(item, PropertyHorizontalExtent, (int)(width.Value * (double)dpi / before));
            SetProperty(item, PropertyVerticalExtent, (int)(height.Value * (double)dpi / before));
        }

        SetProperty(item, PropertyDataType, settings.Color switch
        {
            ScanColorMode.Gray => DataTypeGrayscale,
            ScanColorMode.BlackAndWhite => DataTypeThreshold,
            _ => DataTypeColor,
        });
    }

    /// <summary>Asks the device to take its pages from the feeder; false when it has none.</summary>
    private static bool TrySelectFeeder(dynamic device)
    {
        try
        {
            var handling = ReadProperty(device, PropertyDocumentHandlingSelect);
            if (handling is null)
            {
                return false;
            }

            SetProperty(device, PropertyDocumentHandlingSelect, HandlingFeeder);
            var now = ReadProperty(device, PropertyDocumentHandlingSelect);
            if (now is not null && (now.Value & HandlingFeeder) == HandlingFeeder)
            {
                return true;
            }

            // Put the flatbed back so the single-sheet scan that follows is not left half-set.
            SetProperty(device, PropertyDocumentHandlingSelect, HandlingFlatbed);
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether the feeder still has a sheet in it.</summary>
    private static bool HasMorePaper(dynamic device)
    {
        var status = ReadProperty(device, PropertyDocumentHandlingStatus);

        // A driver that does not report the tray is asked for another page anyway; an empty
        // feeder then ends the run through the transfer instead.
        return status is null || (status.Value & StatusFeedReady) == StatusFeedReady;
    }

    private static object? FindDevice(object deviceInfos, string? deviceId)
    {
        dynamic dynamicInfos = deviceInfos;
        int total = dynamicInfos.Count;
        for (var i = 1; i <= total; i++)
        {
            object? info = null;

            // Everything enumerated on the way past — the camera, the unreadable device, the
            // scanner that is not the one asked for — has to be let go of here. A device object
            // still held leaves the hardware busy for the rest of the session, and the two ways
            // out of the loop body that used to skip the release made that the ordinary case on
            // any machine with more than one imaging device.
            var keep = false;
            try
            {
                info = dynamicInfos[i];
                if (info is null)
                {
                    continue;
                }

                dynamic dynamicInfo = info;
                if (!IsScanner(dynamicInfo))
                {
                    continue;
                }

                if (deviceId is null)
                {
                    keep = true;
                    return info;
                }

                string id = dynamicInfo.DeviceID;
                if (string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    keep = true;
                    return info;
                }
            }
            catch (Exception)
            {
                // One unreadable device must not hide the one being looked for.
            }
            finally
            {
                if (!keep)
                {
                    Release(info);
                }
            }
        }

        return null;
    }

    private static bool IsScanner(dynamic deviceInfo)
    {
        try
        {
            int type = deviceInfo.Type;
            return type == ScannerDeviceType;
        }
        catch (Exception)
        {
            // Type unreadable on this driver: count it rather than hide a real scanner.
            return true;
        }
    }

    /// <summary>The name to show, falling back to the device's own id when it has no friendly name.</summary>
    private static string ReadName(dynamic deviceInfo, string id)
    {
        // Both wrappers are released in a finally rather than on each way out: this runs once per
        // enumerated device every time the office opens the scanning screen, and a driver whose
        // Name is not a string throws in the middle of it.
        object? properties = null;
        object? nameProperty = null;
        try
        {
            properties = deviceInfo.Properties;
            if (properties is null)
            {
                return id;
            }

            dynamic dynamicProperties = properties;
            nameProperty = dynamicProperties["Name"];
            if (nameProperty is null)
            {
                return id;
            }

            dynamic dynamicName = nameProperty;
            var name = dynamicName.get_Value() as string;
            return string.IsNullOrWhiteSpace(name) ? id : name;
        }
        catch (Exception)
        {
            // A driver with no readable name: the id is still something to choose between.
            return id;
        }
        finally
        {
            Release(nameProperty);
            Release(properties);
        }
    }

    private static int? ReadProperty(dynamic target, int propertyId)
    {
        object? properties = null;
        object? property = null;
        try
        {
            properties = target.Properties;
            if (properties is null)
            {
                return null;
            }

            dynamic dynamicProperties = properties;
            property = dynamicProperties[propertyId];
            if (property is null)
            {
                return null;
            }

            dynamic dynamicProperty = property;
            return (int)dynamicProperty.get_Value();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Release(property);
            Release(properties);
        }
    }

    private static void SetProperty(dynamic target, int propertyId, int value)
    {
        object? properties = null;
        object? property = null;
        try
        {
            properties = target.Properties;
            if (properties is null)
            {
                return;
            }

            dynamic dynamicProperties = properties;
            property = dynamicProperties[propertyId];
            if (property is null)
            {
                return;
            }

            dynamic dynamicProperty = property;
            dynamicProperty.set_Value(value);
        }
        catch (Exception)
        {
            // A property this driver does not allow. Its own value stands.
        }
        finally
        {
            Release(property);
            Release(properties);
        }
    }

    private static object? CreateDeviceManager()
    {
        var type = Type.GetTypeFromProgID(DeviceManagerProgId);
        return type is null ? null : Activator.CreateInstance(type);
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
