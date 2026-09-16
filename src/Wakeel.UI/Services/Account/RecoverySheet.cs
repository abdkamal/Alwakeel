using QRCoder;
using Wakeel.Crypto;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The sheet W04 prints once and never shows again: the recovery code in the grouped form it is
/// read out in, the same code as a QR picture, and who it belongs to.
/// </summary>
/// <remarks>
/// The code itself is the secret. It lives in <see cref="RecoverySheet"/> only while the screen that
/// shows it is open, and the activation service drops the whole sheet the moment the person presses
/// «ابدأ العمل» — which is exactly what makes «تُعرض مرة واحدة فقط» true rather than a slogan.
/// </remarks>
public sealed record RecoverySheet
{
    /// <summary>The printed form: five groups separated by dashes.</summary>
    public required string CodeDisplay { get; init; }

    /// <summary>The same code as a QR picture, ready to put in an <c>img</c> element.</summary>
    public required string QrDataUrl { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required string OrgName { get; init; }

    public required string OfficeName { get; init; }

    public required string EmployeeName { get; init; }

    public required int DeviceNo { get; init; }

    /// <summary>The organisation logo as a data URL, when the setup file carried one.</summary>
    public string? LogoDataUrl { get; init; }
}

/// <summary>Draws a recovery code as a QR picture, and reads one back from a picture.</summary>
public static class RecoveryQr
{
    /// <summary>Pixels per QR module; twelve keeps a printed sheet readable by a phone camera.</summary>
    private const int PixelsPerModule = 12;

    /// <summary>The code as a PNG data URL.</summary>
    public static string ToDataUrl(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return "data:image/png;base64," + Convert.ToBase64String(ToPng(code));
    }

    /// <summary>The code as PNG bytes.</summary>
    public static byte[] ToPng(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        // The grouped form is what is printed underneath, so the picture and the text say exactly
        // the same thing and a person can check one against the other.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code.Display, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }
}
