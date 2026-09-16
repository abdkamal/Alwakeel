using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// The password that locks a setup file. The administration tool shows it exactly once, on
/// the last step of the export, and the person carries it to the other machine by hand, so it
/// has to be short enough to copy without a mistake and strong enough that the file itself can
/// travel on any medium: sixteen characters of the unambiguous alphabet, eighty random bits.
/// </summary>
public static class PackagePassword
{
    /// <summary>Number of characters, as the export wizard shows them.</summary>
    public const int Length = 16;

    /// <summary>
    /// Crockford base32: no letter that can be read as a digit and no digit that can be read
    /// as a letter, the same alphabet the recovery sheet uses.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const string Separators = "- \t\r\n_.";

    /// <summary>Characters per group in the printed form.</summary>
    private const int GroupSize = 4;

    /// <summary>A fresh package password, taken from the product's one source of randomness.</summary>
    public static string New()
    {
        var builder = new StringBuilder(Length);
        for (var index = 0; index < Length; index++)
        {
            builder.Append(Alphabet[RandomBytes.NextInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>The grouped form the export wizard shows: <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    public static string Display(string? password)
    {
        var normalized = Normalize(password);
        if (normalized.Length != Length)
        {
            throw new CryptoException(ErrorCode.Corrupt, "A package password is sixteen characters long.");
        }

        var groups = new string[Length / GroupSize];
        for (var index = 0; index < groups.Length; index++)
        {
            groups[index] = normalized.Substring(index * GroupSize, GroupSize);
        }

        return string.Join('-', groups);
    }

    /// <summary>
    /// Folds away every difference a person can introduce while copying: lower case, the dashes
    /// and spaces of the printed form, and the three look alike letters. Both the writer and the
    /// reader derive the key from this form, so a password typed in any of those ways opens the
    /// file it was issued for, and no other.
    /// </summary>
    public static string Normalize(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(password.Length);
        foreach (var character in password)
        {
            if (Separators.Contains(character, StringComparison.Ordinal))
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            builder.Append(upper switch
            {
                'O' => '0',
                'I' => '1',
                'L' => '1',
                _ => upper,
            });
        }

        return builder.ToString();
    }

    /// <summary>Whether a typed password can be one this product issued.</summary>
    public static bool IsWellFormed(string? password)
    {
        var normalized = Normalize(password);
        if (normalized.Length != Length)
        {
            return false;
        }

        foreach (var character in normalized)
        {
            if (!Alphabet.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The exact characters the key derivation sees. The writer insists on a password this
    /// product issued, so the strength of every setup file is a property of the format and not
    /// of whoever operated the export wizard.
    /// </summary>
    internal static string Require(string? password)
    {
        if (!IsWellFormed(password))
        {
            throw new CryptoException(
                ErrorCode.Corrupt,
                "A setup file is locked with one of the sixteen character passwords this product issues.");
        }

        return Normalize(password);
    }
}
