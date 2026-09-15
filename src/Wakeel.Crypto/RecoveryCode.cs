using System.Numerics;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// The printed recovery secret: one hundred random bits rendered as twenty Crockford
/// base32 characters plus one check character.
/// It is shown grouped as <c>XXXX-XXXX-XXXX-XXXX-XXXXC</c>: five groups, the check
/// character riding at the end of the last group.
/// </summary>
public sealed class RecoveryCode
{
    /// <summary>Number of data characters (20 characters × 5 bits = 100 bits).</summary>
    public const int DataCharacters = 20;

    /// <summary>Data characters plus the check character.</summary>
    public const int TotalCharacters = DataCharacters + 1;

    /// <summary>Length of the packed secret, big endian, top four bits always zero.</summary>
    public const int KeyMaterialSize = 13;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string CheckAlphabet = Alphabet + "*~$=U";
    private const string Separators = "- \t\r\n_.";

    private RecoveryCode(byte[] keyMaterial, string raw)
    {
        KeyMaterial = keyMaterial;
        Raw = raw;
    }

    /// <summary>The one hundred bit secret, packed big endian into thirteen bytes.</summary>
    public byte[] KeyMaterial { get; }

    /// <summary>Twenty one upper case characters with no separators.</summary>
    public string Raw { get; }

    /// <summary>The grouped form printed on the recovery sheet.</summary>
    public string Display => Group(Raw);

    public static RecoveryCode Generate()
    {
        var material = RandomBytes.Next(KeyMaterialSize);
        material[0] &= 0x0F;
        return FromKeyMaterial(material);
    }

    public static RecoveryCode FromKeyMaterial(ReadOnlySpan<byte> keyMaterial)
    {
        if (keyMaterial.Length != KeyMaterialSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The recovery secret must be thirteen bytes.");
        }

        if ((keyMaterial[0] & 0xF0) != 0)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The recovery secret carries more than one hundred bits.");
        }

        var value = new BigInteger(keyMaterial, isUnsigned: true, isBigEndian: true);
        var builder = new StringBuilder(TotalCharacters);
        for (var position = DataCharacters - 1; position >= 0; position--)
        {
            var shifted = value >> (position * 5);
            builder.Append(Alphabet[(int)(shifted & 31)]);
        }

        builder.Append(CheckCharacter(value));
        return new RecoveryCode(keyMaterial.ToArray(), builder.ToString());
    }

    /// <summary>
    /// Folds the human variations away: upper case, letter O becomes zero, letters I and L
    /// become one, and every separator is dropped.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            if (Separators.Contains(character, StringComparison.Ordinal))
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            upper = upper switch
            {
                'O' => '0',
                'I' => '1',
                'L' => '1',
                _ => upper,
            };

            builder.Append(upper);
        }

        return builder.ToString();
    }

    public static bool TryParse(string? input, out RecoveryCode? code)
    {
        code = null;
        var normalized = Normalize(input);
        if (normalized.Length != TotalCharacters)
        {
            return false;
        }

        BigInteger value = BigInteger.Zero;
        for (var index = 0; index < DataCharacters; index++)
        {
            var digit = Alphabet.IndexOf(normalized[index], StringComparison.Ordinal);
            if (digit < 0)
            {
                return false;
            }

            value = (value << 5) + digit;
        }

        if (normalized[DataCharacters] != CheckCharacter(value))
        {
            return false;
        }

        var material = ToKeyMaterial(value);
        code = new RecoveryCode(material, normalized);
        return true;
    }

    public static RecoveryCode Parse(string? input)
    {
        if (!TryParse(input, out var code) || code is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The recovery code is not complete or its check character does not match.");
        }

        return code;
    }

    /// <summary>Inserts the dashes used on the printed sheet.</summary>
    public static string Group(string raw)
    {
        if (raw is null || raw.Length != TotalCharacters)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The recovery code has an unexpected length.");
        }

        return string.Join(
            '-',
            raw[..4],
            raw[4..8],
            raw[8..12],
            raw[12..16],
            raw[16..]);
    }

    private static char CheckCharacter(BigInteger value) => CheckAlphabet[(int)(value % 37)];

    private static byte[] ToKeyMaterial(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var material = new byte[KeyMaterialSize];
        raw.CopyTo(material, KeyMaterialSize - raw.Length);
        return material;
    }
}
