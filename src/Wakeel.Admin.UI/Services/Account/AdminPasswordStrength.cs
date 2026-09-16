using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.UI.Services.Account;

/// <summary>The four rules A01 prints under the password field, each with its own tick.</summary>
/// <param name="Length">At least twelve characters.</param>
/// <param name="Case">At least one upper case and one lower case letter.</param>
/// <param name="Digit">At least one digit.</param>
/// <param name="Symbol">At least one character that is none of the above.</param>
public readonly record struct AdminPasswordRules(bool Length, bool Case, bool Digit, bool Symbol)
{
    /// <summary>How many of the four rules are satisfied.</summary>
    public int Satisfied => (Length ? 1 : 0) + (Case ? 1 : 0) + (Digit ? 1 : 0) + (Symbol ? 1 : 0);
}

/// <summary>How strong the administrator password is, on the meter A01 draws.</summary>
public enum AdminPasswordLevel
{
    /// <summary>Shorter than the minimum; the meter is empty and the account cannot be created.</summary>
    TooShort,

    Weak,
    Fair,
    Good,
    Strong,
}

/// <summary>
/// Scores the administrator password for the meter on A01 and the recovery dialog on A02.
/// </summary>
/// <remarks>
/// The same shape as the meter الوكيل shows its own users, with one difference: this password is
/// the one that opens the organisation's keys and every setup file that will ever be exported, so
/// the floor is twelve characters rather than eight. The scoring is local and offline — it counts
/// the printed rules and the length and nothing else — because the tool never reaches a network.
/// </remarks>
public static class AdminPasswordStrength
{
    /// <summary>The shortest administrator password the tool accepts.</summary>
    public const int MinimumLength = 12;

    /// <summary>Number of segments the meter draws.</summary>
    public const int Segments = 4;

    /// <summary>Which of the four printed rules the password satisfies.</summary>
    public static AdminPasswordRules Rules(string? password)
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

        return new AdminPasswordRules(text.Length >= MinimumLength, upper && lower, digit, symbol);
    }

    /// <summary>The level the meter shows.</summary>
    public static AdminPasswordLevel Level(string? password)
    {
        var text = password ?? string.Empty;
        if (text.Length < MinimumLength)
        {
            return AdminPasswordLevel.TooShort;
        }

        // Length carries real weight on its own: a long spoken phrase is a better secret than a
        // short word decorated with a digit and a punctuation mark.
        var score = Rules(text).Satisfied + (text.Length >= 16 ? 1 : 0) + (text.Length >= 20 ? 1 : 0);

        return score switch
        {
            <= 2 => AdminPasswordLevel.Weak,
            3 => AdminPasswordLevel.Fair,
            4 => AdminPasswordLevel.Good,
            _ => AdminPasswordLevel.Strong,
        };
    }

    /// <summary>How many of the meter's segments are filled.</summary>
    public static int FilledSegments(string? password) => Level(password) switch
    {
        AdminPasswordLevel.TooShort => 0,
        AdminPasswordLevel.Weak => 1,
        AdminPasswordLevel.Fair => 2,
        AdminPasswordLevel.Good => 3,
        _ => Segments,
    };

    /// <summary>
    /// Whether a password may be used to create the account or replace the current one. The floor is
    /// «مقبولة», not merely «not too short»: this one password is what stands between anybody at this
    /// desk and the organisation's signing keys, so a long run of plain letters — which the meter
    /// rates «ضعيفة» — is refused rather than merely frowned at.
    /// </summary>
    public static bool IsAcceptable(string? password) => Level(password) >= AdminPasswordLevel.Fair;

    /// <summary>The Arabic label of a level, as printed next to the meter.</summary>
    public static string LabelOf(AdminPasswordLevel level) => level switch
    {
        AdminPasswordLevel.TooShort => AdminAr.Account.Strength.TooShort,
        AdminPasswordLevel.Weak => AdminAr.Account.Strength.Weak,
        AdminPasswordLevel.Fair => AdminAr.Account.Strength.Fair,
        AdminPasswordLevel.Good => AdminAr.Account.Strength.Good,
        _ => AdminAr.Account.Strength.Strong,
    };

    /// <summary>The whole label the meter prints.</summary>
    public static string Describe(string? password) => LabelOf(Level(password));
}
