using System.Text;

namespace Wakeel.Core.Conventions;

/// <summary>
/// Converts between PascalCase (C# identifiers) and snake_case (SQL identifiers and stored enum text).
/// The two directions are inverses of each other for the identifier shapes used in this codebase
/// (each "word" starts with exactly one capital letter, no digits, no consecutive capitals).
/// </summary>
internal static class SnakeCaseText
{
    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder(value.Length);
        foreach (var part in parts)
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part.AsSpan(1));
            }
        }

        return builder.ToString();
    }
}
