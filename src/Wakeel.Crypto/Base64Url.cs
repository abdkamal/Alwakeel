namespace Wakeel.Crypto;

/// <summary>
/// Unpadded base64url (RFC 4648 §5) used for every binary value that travels inside JSON,
/// QR payloads or file names.
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        if (!TryDecode(value, out var bytes))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The value is not valid base64url text.");
        }

        return bytes;
    }

    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null)
        {
            return false;
        }

        if (value.Length == 0)
        {
            return true;
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 0:
                break;
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
            default:
                return false;
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
