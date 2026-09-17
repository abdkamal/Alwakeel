using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wakeel.Admin.UI.Services.Organisation;

namespace Wakeel.Admin.Services;

/// <summary>
/// The Windows half of <see cref="IAdminImageSquareCrop"/>: cuts the organisation logo a person
/// chose on A04 down to the largest square it holds and scales that square to a sensible size.
/// </summary>
/// <remarks>
/// Decoding and re-encoding a picture is this computer's job, so this hands the file to the imaging
/// stack the window already carries rather than adding a library for it. The picture is read once,
/// without colour management and without being cached, and what comes back out is a PNG: the logo
/// is drawn on letterheads and on screen at several sizes, and a PNG keeps its edges clean and its
/// transparency where a photograph's format would not.
/// </remarks>
public sealed class AdminWindowsImageSquareCrop : IAdminImageSquareCrop
{
    /// <summary>
    /// Largest picture that is decoded at all. A logo is a small drawing; anything far beyond a big
    /// photograph is not one, and must not be allowed to make the window think for a minute.
    /// </summary>
    private const int MaxPixels = 40_000_000;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool TryCropSquare(ReadOnlySpan<byte> file, double focus, int maxSide, out byte[] png)
    {
        png = [];

        if (file.IsEmpty || maxSide <= 0)
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

            BitmapSource picture = decoder.Frames[0];
            if (picture.PixelWidth <= 0
                || picture.PixelHeight <= 0
                || (long)picture.PixelWidth * picture.PixelHeight > MaxPixels)
            {
                return false;
            }

            var side = Math.Min(picture.PixelWidth, picture.PixelHeight);

            // The square slides along whichever side is longer; on the short side there is nothing
            // to choose. Where the person put the slider decides how much is left before it.
            var where = Math.Clamp(double.IsNaN(focus) ? 0.5 : focus, 0, 1);
            var x = (int)Math.Round((picture.PixelWidth - side) * where);
            var y = (int)Math.Round((picture.PixelHeight - side) * where);
            x = Math.Clamp(x, 0, picture.PixelWidth - side);
            y = Math.Clamp(y, 0, picture.PixelHeight - side);

            BitmapSource square = new CroppedBitmap(picture, new Int32Rect(x, y, side, side));

            if (side > maxSide)
            {
                var scale = (double)maxSide / side;
                square = new TransformedBitmap(square, new ScaleTransform(scale, scale));
            }

            square.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(square));

            using var output = new MemoryStream();
            encoder.Save(output);

            var bytes = output.ToArray();
            if (bytes.Length == 0)
            {
                return false;
            }

            png = bytes;
            return true;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException
                                              or ArgumentException or OverflowException
                                              or OutOfMemoryException or IOException
                                              or InvalidOperationException)
        {
            // Anything this computer cannot read or re-draw simply means no square was cut; the
            // screen then keeps the picture exactly as it was chosen and says so.
            return false;
        }
    }
}
