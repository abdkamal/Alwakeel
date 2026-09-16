using System.Globalization;
using System.Security.Cryptography;
using QRCoder;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services.Account;

/// <summary>
/// The organisation recovery sheet A01 shows once and never again: the code in the grouped form it
/// is read out in, the same code as a picture, and whose organisation it belongs to.
/// </summary>
/// <remarks>
/// The code itself is the secret. It exists in this object only while the screen that prints it is
/// open; pressing «ابدأ» drops the whole sheet, which is what makes «تُعرض مرة واحدة فقط» a fact
/// rather than a promise. Nothing writes it to the database, the key file or the log.
/// </remarks>
public sealed record AdminRecoverySheet
{
    /// <summary>The printed form: five groups separated by dashes.</summary>
    public required string CodeDisplay { get; init; }

    /// <summary>The same code as a picture, ready to put in an <c>img</c> element.</summary>
    public required string QrDataUrl { get; init; }

    /// <summary>When the sheet was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>The organisation the sheet belongs to.</summary>
    public required string OrgName { get; init; }

    /// <summary>The administrator whose account it recovers.</summary>
    public required string AdminName { get; init; }

    /// <summary>
    /// The label printed on the sheet so a person can say which piece of paper they are holding
    /// without reading the code out. It is drawn at random and has nothing to do with the code, so
    /// naming it in the operations log gives nothing away.
    /// </summary>
    public required string SheetNumber { get; init; }
}

/// <summary>Draws the label a recovery sheet carries: the day it was issued and a serial.</summary>
public static class AdminSheetNumber
{
    /// <summary>A fresh label for a sheet issued at <paramref name="issuedAt"/>.</summary>
    public static string Next(DateTimeOffset issuedAt)
    {
        // Five digits drawn at random: enough to tell two sheets apart, and carrying nothing that
        // could help anybody guess the code on either of them.
        var serial = RandomNumberGenerator.GetInt32(10000, 100000);
        var day = issuedAt.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"{day}/{serial.ToString(CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Draws a recovery code as a picture.</summary>
public static class AdminRecoveryQr
{
    /// <summary>Pixels per module; twelve keeps a printed sheet readable by a phone camera.</summary>
    private const int PixelsPerModule = 12;

    /// <summary>The code as a PNG data URL.</summary>
    public static string ToDataUrl(RecoveryCode code) =>
        "data:image/png;base64," + Convert.ToBase64String(ToPng(code));

    /// <summary>The code as PNG bytes.</summary>
    public static byte[] ToPng(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        // The grouped form is what is printed underneath the picture, so the two say exactly the
        // same thing and a person can check one against the other.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code.Display, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }
}
