using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>The four rules W04 and W07 show under the password field, each with its own tick.</summary>
/// <param name="Length">At least eight characters.</param>
/// <param name="Case">At least one upper case and one lower case letter.</param>
/// <param name="Digit">At least one digit.</param>
/// <param name="Symbol">At least one character that is none of the above.</param>
public readonly record struct PasswordRules(bool Length, bool Case, bool Digit, bool Symbol)
{
    /// <summary>How many of the four rules are satisfied.</summary>
    public int Satisfied => (Length ? 1 : 0) + (Case ? 1 : 0) + (Digit ? 1 : 0) + (Symbol ? 1 : 0);
}

/// <summary>
/// How strong an account password is, on the five-step scale the meter on W04 draws. The scale is
/// deliberately simple and local: it counts the four printed rules and the length, and it never
/// reaches a network, a dictionary or a list of leaked passwords, because الوكيل works offline.
/// </summary>
public enum PasswordStrengthLevel
{
    /// <summary>Shorter than the minimum; the meter is empty and the account cannot be created.</summary>
    TooShort,

    Weak,
    Fair,
    Good,
    Strong,
}

/// <summary>Scores an account password for the strength meter on W04 and the recovery dialog on W07.</summary>
public static class PasswordStrength
{
    /// <summary>The shortest password the product accepts.</summary>
    public const int MinimumLength = 8;

    /// <summary>Number of filled segments the meter draws for each level.</summary>
    public const int Segments = 4;

    /// <summary>Which of the four printed rules the password satisfies.</summary>
    public static PasswordRules Rules(string? password)
    {
        var text = password ?? string.Empty;
        var upper = false;
        var lower = false;
        var digit = false;
        var symbol = false;

        foreach (var character in text)
        {
            if (char.IsUpper(character))
            {
                upper = true;
            }
            else if (char.IsLower(character))
            {
                lower = true;
            }
            else if (char.IsDigit(character))
            {
                digit = true;
            }
            else
            {
                symbol = true;
            }
        }

        return new PasswordRules(text.Length >= MinimumLength, upper && lower, digit, symbol);
    }

    /// <summary>The level the meter shows.</summary>
    public static PasswordStrengthLevel Level(string? password)
    {
        var text = password ?? string.Empty;
        if (text.Length < MinimumLength)
        {
            return PasswordStrengthLevel.TooShort;
        }

        var satisfied = Rules(text).Satisfied;

        // Length carries real weight on its own: a long passphrase of nothing but letters is a
        // better secret than a short one decorated with a digit and a punctuation mark.
        var score = satisfied + (text.Length >= 12 ? 1 : 0) + (text.Length >= 16 ? 1 : 0);

        return score switch
        {
            <= 2 => PasswordStrengthLevel.Weak,
            3 => PasswordStrengthLevel.Fair,
            4 => PasswordStrengthLevel.Good,
            _ => PasswordStrengthLevel.Strong,
        };
    }

    /// <summary>How many of the meter's four segments are filled.</summary>
    public static int FilledSegments(string? password) => Level(password) switch
    {
        PasswordStrengthLevel.TooShort => 0,
        PasswordStrengthLevel.Weak => 1,
        PasswordStrengthLevel.Fair => 2,
        PasswordStrengthLevel.Good => 3,
        _ => Segments,
    };

    /// <summary>Whether a password may be used to create or reset the account.</summary>
    public static bool IsAcceptable(string? password) =>
        Level(password) > PasswordStrengthLevel.TooShort;

    /// <summary>The Arabic label of a level, as printed next to the meter.</summary>
    public static string LabelOf(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.TooShort => Ar.FirstRun.Strength.TooShort,
        PasswordStrengthLevel.Weak => Ar.FirstRun.Strength.Weak,
        PasswordStrengthLevel.Fair => Ar.FirstRun.Strength.Fair,
        PasswordStrengthLevel.Good => Ar.FirstRun.Strength.Good,
        _ => Ar.FirstRun.Strength.Strong,
    };

    /// <summary>The whole line the meter prints: «قوية — 14 حرفًا».</summary>
    public static string Describe(string? password)
    {
        var text = password ?? string.Empty;
        var label = LabelOf(Level(text));
        return text.Length == 0 ? label : Ar.FirstRun.Strength.WithLength(label, text.Length);
    }
}
