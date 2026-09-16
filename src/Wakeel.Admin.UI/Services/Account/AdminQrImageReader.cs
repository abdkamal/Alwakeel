using ZXing;
using ZXing.Common;

namespace Wakeel.Admin.UI.Services.Account;

/// <summary>
/// Turns a picture file the person chose into plain pixels. Decoding a PNG or a JPEG is this
/// computer's job, not this project's, so the host supplies it: the Windows host hands the file to
/// the imaging stack the window already carries, and a test hands over pixels it drew itself.
/// </summary>
public interface IAdminImagePixels
{
    /// <summary>
    /// Decodes a picture file into twenty four bit RGB pixels, row by row from the top left.
    /// Returns false for anything this computer cannot read as a picture.
    /// </summary>
    bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height);
}

/// <summary>A decoder for hosts that have none; every picture is simply unreadable.</summary>
public sealed class NoAdminImagePixels : IAdminImagePixels
{
    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height)
    {
        rgb24 = [];
        width = 0;
        height = 0;
        return false;
    }
}

/// <summary>
/// A02's recovery dialog: reads the recovery code back out of a picture of the printed sheet, for
/// the person who would rather photograph the sheet than copy twenty one characters by hand.
/// </summary>
public sealed class AdminQrImageReader
{
    private readonly IAdminImagePixels _pixels;

    public AdminQrImageReader(IAdminImagePixels pixels)
    {
        _pixels = pixels;
    }

    /// <summary>Whether this host can read pictures at all, so the dialog can say so instead of failing.</summary>
    public bool IsAvailable => _pixels is not NoAdminImagePixels;

    /// <summary>The text of the first code in the picture, or null when there is none to find.</summary>
    public string? TryRead(ReadOnlySpan<byte> imageFile)
    {
        if (!_pixels.TryDecode(imageFile, out var rgb, out var width, out var height)
            || width <= 0
            || height <= 0
            || rgb.Length < (long)width * height * 3)
        {
            return null;
        }

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],

                // A photograph of a printed sheet is never as clean as a rendered picture: uneven
                // light, a slight angle, a phone's compression. The extra passes cost a moment once
                // and save a person from typing twenty one characters by hand.
                TryHarder = true,
                TryInverted = true,
            },
        };

        var source = new RGBLuminanceSource(rgb, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
        return reader.Decode(source)?.Text;
    }
}
