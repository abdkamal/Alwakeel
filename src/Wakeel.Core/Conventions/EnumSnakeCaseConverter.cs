using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Wakeel.Core.Conventions;

/// <summary>Stores an enum as its snake_case text form (e.g. <c>InProgress</c> ↔ <c>in_progress</c>).</summary>
internal sealed class EnumSnakeCaseConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public EnumSnakeCaseConverter()
        : base(
            v => SnakeCaseText.ToSnakeCase(v.ToString()),
            v => Enum.Parse<TEnum>(SnakeCaseText.ToPascalCase(v)))
    {
    }
}

/// <summary>Stores a nullable enum as its snake_case text form, or <c>NULL</c>.</summary>
internal sealed class NullableEnumSnakeCaseConverter<TEnum> : ValueConverter<TEnum?, string?>
    where TEnum : struct, Enum
{
    public NullableEnumSnakeCaseConverter()
        : base(
            v => v == null ? null : SnakeCaseText.ToSnakeCase(v.Value.ToString()),
            v => v == null ? null : Enum.Parse<TEnum>(SnakeCaseText.ToPascalCase(v)))
    {
    }
}
