using Bunit;
using Wakeel.Design.Components;
using Wakeel.Design.Text;

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
    /// not flip the whole field to LTR just because it starts with a Latin/numeric token, and the
    /// control must never inject bidi isolate characters into a value the user is actively editing
    /// (isolation is for read-only display text via Bidi.Wrap, not for a live input's own value).
    /// The mixed line below is taken from docs/design/bidi-test.html's "الرقم الرسمي" case.
    /// </summary>
    [Fact]
    public void TextControl_PassesMixedArabicLatinValueThrough_Unchanged()
    {
        var cut = Render<WInput>(p => p
            .Add(x => x.Label, "الرقم المرجعي")
            .Add(x => x.Value, "20260912/12046"));

        var input = cut.Find("input");
        Assert.Equal("20260912/12046", input.GetAttribute("value"));
        Assert.DoesNotContain(Wakeel.Design.Bidi.Bidi.Lri, input.GetAttribute("value")!);
    }

    /// <summary>
    /// The plaintext bidi behaviour asserted above depends entirely on the CSS contract shipped in
    /// WInput.razor.css; this locks that contract in place independently of any Blazor-side test.
    /// </summary>
    [Fact]
    public void FieldControlCss_DeclaresUnicodeBidiPlaintext()
    {
        var cssPath = FindRepoFile("src/Wakeel.Design/Components/WInput.razor.css");
        var css = File.ReadAllText(cssPath);

        var ruleStart = css.IndexOf(".w-field-control {", StringComparison.Ordinal);
        Assert.True(ruleStart >= 0, "Expected a .w-field-control rule in WInput.razor.css.");
        var ruleEnd = css.IndexOf('}', ruleStart);
        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("unicode-bidi: plaintext", rule);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' by walking up from {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// ARCHITECTURE.md §12: the date control must present dd/MM/yyyy with a calendar icon rather than
    /// the native &lt;input type="date"&gt; and its locale-dependent placeholder.
    /// </summary>
    [Fact]
    public void DateType_RendersMaskedTextInput_WithArabicPlaceholder()
    {
        var cut = Render<WInput>(p => p.Add(x => x.Type, WInputType.Date));

        var input = cut.Find("input");
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("numeric", input.GetAttribute("inputmode"));
        Assert.Equal("10", input.GetAttribute("maxlength"));
        Assert.Equal(Ar.Fields.DatePlaceholder, input.GetAttribute("placeholder"));
        Assert.NotNull(cut.Find(".w-field-adornment--static .w-icon"));
    }

    [Fact]
    public void DateType_InsertsSeparators_AsDigitsAreTyped()
    {
        string? value = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Value, string.Empty)
            .Add(x => x.ValueChanged, (string? v) => value = v));

        cut.Find("input").Input("1");
        Assert.Equal("1", value);

        cut.Find("input").Input("12");
        Assert.Equal("12", value);

        cut.Find("input").Input("120");
        Assert.Equal("12/0", value);

        cut.Find("input").Input("12092026");
        Assert.Equal("12/09/2026", value);
    }

    [Fact]
    public void DateType_NormalizesArabicIndicDigits_AndDropsOtherCharacters()
    {
        string? value = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Value, string.Empty)
            .Add(x => x.ValueChanged, (string? v) => value = v));

        cut.Find("input").Input("١٢٠٩٢٠٢٦");
        Assert.Equal("12/09/2026", value);

        cut.Find("input").Input("12a09b2026");
        Assert.Equal("12/09/2026", value);
    }

    [Fact]
    public void DateType_UsesCallerPlaceholder_WhenSupplied()
    {
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Placeholder, "dd/mm/yyyy"));

        Assert.Equal("dd/mm/yyyy", cut.Find("input").GetAttribute("placeholder"));
    }
}
