using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wakeel.Admin.UI.Services.Account;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminImagePixels"/>: decodes the picture a person chose on A02's
/// recovery dialog — a photograph or a scan of the printed organisation sheet — into plain pixels
/// for the code reader.
/// </summary>
/// <remarks>
/// Decoding pictures is this computer's job, so this hands the file to the imaging stack the window
/// already carries rather than adding a library for it. The picture is decoded once, without colour
/// management and without being cached, because it is looked at once and then forgotten.
/// </remarks>
public sealed class AdminWindowsImagePixels : IAdminImagePixels
{
    /// <summary>
    /// Largest picture that is decoded at all. A phone photograph of one sheet of paper is a few
    /// megabytes; anything far beyond that is not a recovery sheet and must not be allowed to make
    /// the window think for a minute.
    /// </summary>
    private const int MaxPixels = 40_000_000;

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height)
    {
        rgb24 = [];
        width = 0;
        height = 0;

        if (file.IsEmpty)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(file.ToArray(), writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return false;
            }

            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0
                || frame.PixelHeight <= 0
                || (long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
            {
                return false;
            }

            // Whatever arrived — a JPEG, a PNG with transparency, a greyscale scan — becomes the one
            // arrangement the code reader understands: three bytes a pixel, row by row.
            var converted = new FormatConvertedBitmap(
                frame, PixelFormats.Rgb24, destinationPalette: null, alphaThreshold: 0);
            var stride = converted.PixelWidth * 3;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            rgb24 = pixels;
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            return true;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException
                                              or ArgumentException or OverflowException
                                              or OutOfMemoryException or IOException)
        {
            // Anything this computer cannot read is simply "no code found"; the dialog then asks the
            // person to type the twenty one characters instead.
            return false;
        }
    }
}
