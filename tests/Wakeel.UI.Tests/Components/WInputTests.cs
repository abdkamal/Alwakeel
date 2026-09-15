using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class WInputTests : WakeelTestContext
{
    [Fact]
    public void Renders_Label_AndRequiredStar()
    {
        var cut = Render<WInput>(p => p
            .Add(x => x.Label, "اسم المراسل")
            .Add(x => x.Required, true));

        Assert.Contains("اسم المراسل", cut.Markup);
        Assert.Contains("w-field-required", cut.Markup);
    }

    [Fact]
    public void Input_RaisesValueChanged()
    {
        string? newValue = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Value, string.Empty)
            .Add(x => x.ValueChanged, (string? v) => newValue = v));

        cut.Find("input").Input("أحمد الخطيب");

        Assert.Equal("أحمد الخطيب", newValue);
    }

    [Fact]
    public void Error_ShowsArabicMessage_InsteadOfHint()
    {
        var cut = Render<WInput>(p => p
            .Add(x => x.Hint, "نص مساعد")
            .Add(x => x.Error, "هذا الحقل مطلوب"));

        Assert.Contains("هذا الحقل مطلوب", cut.Markup);
        Assert.DoesNotContain("نص مساعد", cut.Markup);
        Assert.Contains("w-field--error", cut.Find(".w-field").ClassList);
    }

    [Fact]
    public void PasswordType_TogglesVisibility_OnEyeButtonClick()
    {
        var cut = Render<WInput>(p => p.Add(x => x.Type, WInputType.Password));

        Assert.Equal("password", cut.Find("input").GetAttribute("type"));

        cut.Find(".w-field-adornment").Click();

        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
    }

    [Fact]
    public void TextareaType_RendersTextareaElement()
    {
        var cut = Render<WInput>(p => p.Add(x => x.Type, WInputType.Textarea));

        Assert.Single(cut.FindAll("textarea"));
    }

    /// <summary>
    /// AGREEMENT item 55: free text a user types (a reference number, a file name, an amount) must
    /// not flip the whole field to LTR just because it starts with a Latin/numeric token — the
    /// control needs `unicode-bidi: plaintext` so the field follows its own first strong character.
    /// The mixed line below is taken from docs/design/bidi-test.html's "الرقم الرسمي" case.
    /// </summary>
    [Fact]
    public void TextControl_HasPlaintextClass_ForMixedArabicLatinValue()
    {
        var cut = Render<WInput>(p => p
            .Add(x => x.Label, "الرقم المرجعي")
            .Add(x => x.Value, "20260912/12046"));

        Assert.Contains("w-field-control", cut.Find("input").ClassList);
    }
}
