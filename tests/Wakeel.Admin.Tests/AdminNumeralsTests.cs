using System.Reflection;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// The design guide rules that figures are Western digits (0–9) everywhere, and AGREEMENT item 20
/// requires it for dates. The tool once drifted to Arabic-Indic figures in its prose while الوكيل
/// kept Western ones; these facts keep the two programs counting in one voice.
/// </summary>
public sealed class AdminNumeralsTests
{
    private static bool HasArabicIndic(string text) => text.Any(c => c is >= '\u0660' and <= '\u0669');

    [Fact]
    public void NoStringConstantOfTheToolUsesArabicIndicFigures()
    {
        var offenders = new List<string>();
        var pending = new Stack<Type>();
        pending.Push(typeof(AdminAr));
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                pending.Push(nested);
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (field is { IsLiteral: true } && field.GetRawConstantValue() is string value && HasArabicIndic(value))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Arabic-Indic figures in: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(120)]
    public void CountedSentencesUseWesternDigits(int count)
    {
        Assert.False(HasArabicIndic(AdminAr.Dashboard.RevokedDevices(count)));
        Assert.False(HasArabicIndic(AdminAr.Dashboard.OfOffices(count)));
        Assert.False(HasArabicIndic(AdminAr.Counting.Devices(count)));
    }
}
