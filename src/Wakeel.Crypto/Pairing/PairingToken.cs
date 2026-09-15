using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// The one time secret behind a pairing session. The computer shows it inside the QR image
/// and derives the six digit fallback code from it.
/// </summary>
public sealed class PairingToken
{
    public const int TokenSize = 32;

    public PairingToken(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != TokenSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The pairing secret must be thirty two bytes.");
        }

        Value = (byte[])value.Clone();
    }

    public byte[] Value { get; }

    public string Text => Base64Url.Encode(Value);

    /// <summary>The six digit code a person can type instead of scanning.</summary>
    public string ShortCode => PairingCodes.ShortCode(Value);

    public static PairingToken Generate() => new(RandomBytes.Next(TokenSize));

    public static PairingToken FromText(string text) => new(Base64Url.Decode(text));
}

/// <summary>Derivations shared by both sides of a pairing session.</summary>
public static class PairingCodes
{
    public const int Digits = 6;

    private const string ShortCodeLabel = "code";
    private const int Modulus = 1_000_000;

    /// <summary>
    /// Six digits derived as HMAC-SHA256 of the literal "code" under the pairing token,
    /// read as one big unsigned number and reduced modulo one million.
    /// </summary>
    public static string ShortCode(ReadOnlySpan<byte> token)
    {
        var mac = Hmac(token, ShortCodeLabel);
        var value = new BigInteger(mac, isUnsigned: true, isBigEndian: true);
        var digits = (int)(value % Modulus);
        return digits.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    /// <summary>HMAC-SHA256 of a label under the pairing token.</summary>
    public static byte[] Hmac(ReadOnlySpan<byte> token, string label) =>
        HMACSHA256.HashData(token, Encoding.UTF8.GetBytes(label ?? string.Empty));

    /// <summary>The proof a phone puts in its pairing request to show it saw the token.</summary>
    public static string TokenProof(ReadOnlySpan<byte> token, string phoneDeviceId) =>
        Base64Url.Encode(Hmac(token, "pairing-request|" + phoneDeviceId));
}
