using ZXing;
using ZXing.Common;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// Turns a picture file the person chose into raw pixels. Decoding a PNG or a JPEG is the operating
/// system's job, not this project's, so the host supplies it: the Windows host hands the file to the
/// imaging stack it already has, and a test hands over pixels it drew itself.
/// </summary>
public interface IImagePixels
{
    /// <summary>
    /// Decodes an image file into twenty four bit RGB pixels, row by row from the top left.
    /// Returns false for anything that is not an image this machine can read.
    /// </summary>
    bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height);
}

/// <summary>An image decoder for hosts that have none; every picture is simply unreadable.</summary>
public sealed class NoImagePixels : IImagePixels
{
    public bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height)
    {
        rgb24 = [];
        width = 0;
        height = 0;
        return false;
    }
}

/// <summary>
/// W07: reads the recovery code back out of a picture of the QR printed on the sheet, for the person
/// who would rather photograph the sheet than copy twenty one characters by hand.
/// </summary>
public sealed class QrImageReader
{
    private readonly IImagePixels _pixels;

    public QrImageReader(IImagePixels pixels)
    {
        _pixels = pixels;
    }

    /// <summary>Whether this host can read pictures at all, so the dialog can say so instead of failing.</summary>
    public bool IsAvailable => _pixels is not NoImagePixels;

    /// <summary>The text of the first QR code in the picture, or null when there is none to find.</summary>
    public string? TryRead(ReadOnlySpan<byte> imageFile)
    {
        if (!_pixels.TryDecode(imageFile, out var rgb, out var width, out var height)
            || width <= 0
            || height <= 0
            || rgb.Length < width * height * 3)
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
                // light, a slight angle, a phone's compression. Spending the extra passes here costs
                // a moment once and saves a person from typing the code by hand.
                TryHarder = true,
                TryInverted = true,
            },
        };

        var source = new RGBLuminanceSource(rgb, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
        return reader.Decode(source)?.Text;
    }
}
