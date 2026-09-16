namespace Wakeel.Admin.UI.Services.Organisation;

/// <summary>Why a picture a person chose for the organisation logo could not be used.</summary>
public enum AdminLogoRefusal
{
    /// <summary>Nothing is wrong with it.</summary>
    None,

    /// <summary>Bigger than the two megabytes a logo is allowed to be.</summary>
    TooLarge,

    /// <summary>Not a PNG and not a JPEG, whatever the file name says.</summary>
    WrongKind,

    /// <summary>The right kind of file, but its contents make no sense as a picture.</summary>
    Unreadable,

    /// <summary>So small that it would be a smudge next to the organisation's name.</summary>
    TooSmall,
}

/// <summary>A picture that has been looked at and accepted, ready to be stored.</summary>
/// <param name="Bytes">The picture itself, square-cropped when this computer could do it.</param>
/// <param name="Mime">Its kind, as stored beside it.</param>
/// <param name="Width">How wide the stored picture is, in points of the picture.</param>
/// <param name="Height">How tall it is.</param>
/// <param name="WasCropped">Whether the tool managed to cut it to a square.</param>
public sealed record AdminLogoImage(byte[] Bytes, string Mime, int Width, int Height, bool WasCropped)
{
    /// <summary>The picture as a data address a browser can draw without a round trip to a file.</summary>
    public string ToDataUrl() => $"data:{Mime};base64,{Convert.ToBase64String(Bytes)}";
}

/// <summary>
/// Cutting a picture down to a square. Decoding and re-encoding a PNG or a JPEG is this computer's
/// job, not this project's, so the host supplies it; a host that cannot do it says so and the logo
/// is stored exactly as the person chose it, with the screen saying the square was left to them.
/// </summary>
public interface IAdminImageSquareCrop
{
    /// <summary>Whether this computer can cut a picture at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Cuts the largest square out of <paramref name="file"/> and scales it down to at most
    /// <paramref name="maxSide"/> points a side, as a PNG.
    /// </summary>
    /// <param name="file">The picture the person chose.</param>
    /// <param name="focus">
    /// Where along the long side the square is taken from: 0 is the start of the picture, 1 the end,
    /// 0.5 the middle. A person moves this with a slider while watching the preview.
    /// </param>
    /// <param name="maxSide">The largest side the stored picture may have.</param>
    /// <param name="png">The cut picture, when it worked.</param>
    /// <returns>Whether anything came out of it.</returns>
    bool TryCropSquare(ReadOnlySpan<byte> file, double focus, int maxSide, out byte[] png);
}

/// <summary>A cutter for hosts that have none: the picture is kept whole.</summary>
public sealed class NoAdminImageSquareCrop : IAdminImageSquareCrop
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public bool TryCropSquare(ReadOnlySpan<byte> file, double focus, int maxSide, out byte[] png)
    {
        png = [];
        return false;
    }
}

/// <summary>
/// Looks at a picture a person chose for the organisation logo (A04) before anything is stored:
/// the kind of file, how big it is, and how big the picture inside it is.
/// </summary>
/// <remarks>
/// The width and height are read straight out of the file's own header rather than by decoding the
/// whole picture, so a two megabyte photograph is measured without ever being unpacked into memory.
/// A PNG says so in its first chunk; a JPEG says so in whichever frame header comes first.
/// </remarks>
public static class AdminLogoReader
{
    /// <summary>The largest a logo file may be (AGREEMENT: two megabytes).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>The smallest picture worth keeping; anything under this is a smudge.</summary>
    public const int MinSide = 48;

    /// <summary>The side the stored square is scaled down to.</summary>
    public const int StoredSide = 512;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The kind of picture, or null for anything that is neither a PNG nor a JPEG.</summary>
    public static string? MimeOf(ReadOnlySpan<byte> file)
    {
        if (file.Length >= 8 && file[..8].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        return file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF
            ? "image/jpeg"
            : null;
    }

    /// <summary>
    /// Decides whether a picture may be used, and cuts it to a square when this computer can.
    /// </summary>
    /// <param name="file">The bytes the person chose.</param>
    /// <param name="cropper">This computer's cutter.</param>
    /// <param name="focus">Where along the long side to take the square from, 0 to 1.</param>
    /// <param name="image">The accepted picture, when it was accepted.</param>
    /// <returns><see cref="AdminLogoRefusal.None"/> when it was accepted.</returns>
    public static AdminLogoRefusal Accept(
        ReadOnlySpan<byte> file,
        IAdminImageSquareCrop cropper,
        double focus,
        out AdminLogoImage? image)
    {
        ArgumentNullException.ThrowIfNull(cropper);
        image = null;

        if (file.Length > MaxBytes)
        {
            return AdminLogoRefusal.TooLarge;
        }

        var mime = MimeOf(file);
        if (mime is null)
        {
            return AdminLogoRefusal.WrongKind;
        }

        if (!TryMeasure(file, out var width, out var height))
        {
            return AdminLogoRefusal.Unreadable;
        }

        if (width < MinSide || height < MinSide)
        {
            return AdminLogoRefusal.TooSmall;
        }

        if (cropper.IsAvailable
            && cropper.TryCropSquare(file, focus, StoredSide, out var png)
            && png.Length > 0
            && TryMeasure(png, out var side, out _))
        {
            image = new AdminLogoImage(png, "image/png", side, side, WasCropped: true);
            return AdminLogoRefusal.None;
        }

        image = new AdminLogoImage(file.ToArray(), mime, width, height, WasCropped: false);
        return AdminLogoRefusal.None;
    }

    /// <summary>How big the picture inside the file is, read from its header alone.</summary>
    public static bool TryMeasure(ReadOnlySpan<byte> file, out int width, out int height)
    {
        width = 0;
        height = 0;

        var mime = MimeOf(file);
        return mime switch
        {
            "image/png" => TryMeasurePng(file, out width, out height),
            "image/jpeg" => TryMeasureJpeg(file, out width, out height),
            _ => false,
        };
    }

    private static bool TryMeasurePng(ReadOnlySpan<byte> file, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Signature (8) + length (4) + "IHDR" (4) + width (4) + height (4).
        if (file.Length < 24 || file[12] != (byte)'I' || file[13] != (byte)'H' || file[14] != (byte)'D' || file[15] != (byte)'R')
        {
            return false;
        }

        width = ReadBigEndianInt32(file[16..20]);
        height = ReadBigEndianInt32(file[20..24]);
        return width > 0 && height > 0;
    }

    private static bool TryMeasureJpeg(ReadOnlySpan<byte> file, out int width, out int height)
    {
        width = 0;
        height = 0;

        var at = 2;
        while (at + 3 < file.Length)
        {
            if (file[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = file[at + 1];
            at += 2;

            // Padding and the standalone markers carry no length of their own.
            if (marker is 0xFF or 0x01 or (>= 0xD0 and <= 0xD9))
            {
                continue;
            }

            if (at + 1 >= file.Length)
            {
                return false;
            }

            var length = (file[at] << 8) | file[at + 1];
            if (length < 2 || at + length > file.Length)
            {
                return false;
            }

            // Every frame header but the four that are not frames at all states the size first.
            var isFrame = marker is (>= 0xC0 and <= 0xCF) and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame && at + 7 < file.Length)
            {
                height = (file[at + 3] << 8) | file[at + 4];
                width = (file[at + 5] << 8) | file[at + 6];
                return width > 0 && height > 0;
            }

            at += length;
        }

        return false;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> four) =>
        (four[0] << 24) | (four[1] << 16) | (four[2] << 8) | four[3];
}
