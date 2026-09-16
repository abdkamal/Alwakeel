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

    /// <summary>
    /// Round-1 verify-design-split.json MEDIUM finding: the Date/Password adornment (calendar icon /
    /// eye toggle) is absolutely positioned over the control, so the control needs a reserved
    /// end-padding track wide enough that a right-aligned plaintext value never renders underneath it.
    /// Locks the CSS contract independently of the Blazor-side class-list test below, the same pattern
    /// <see cref="FieldControlCss_DeclaresUnicodeBidiPlaintext"/> already uses.
    /// </summary>
    [Fact]
    public void AdornedFieldCss_ReservesAdornmentTrack()
    {
        var cssPath = FindRepoFile("src/Wakeel.Design/Components/WInput.razor.css");
        var css = File.ReadAllText(cssPath);

        var ruleStart = css.IndexOf(".w-field-control--adorned {", StringComparison.Ordinal);
        Assert.True(ruleStart >= 0, "Expected a .w-field-control--adorned rule in WInput.razor.css.");
        var ruleEnd = css.IndexOf('}', ruleStart);
        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("padding-inline-end: 40px", rule);
    }

    /// <summary>
    /// Only Date and Password carry an overlapping adornment, so only those two types should get the
    /// reserved-track class; Text/Number (and the textarea, which has no wrap/adornment at all) must
    /// not, or the padding contract locked above would be applied where nothing needs it.
    /// </summary>
    [Theory]
    [InlineData(WInputType.Date, true)]
    [InlineData(WInputType.Password, true)]
    [InlineData(WInputType.Text, false)]
    [InlineData(WInputType.Number, false)]
    public void FieldControlClass_IsAdorned_OnlyForDateAndPassword(WInputType type, bool expectAdorned)
    {
        var cut = Render<WInput>(p => p.Add(x => x.Type, type));

        var classList = cut.Find("input").ClassList;

        Assert.Equal(expectAdorned, classList.Contains("w-field-control--adorned"));
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

    /// <summary>
    /// verify-design-split.json finding 1: on an unbound field (Value still null) a first keystroke
    /// that produces no digits used to compare formatted ("") against a null Value and skip the DOM
    /// correction entirely, leaving the rejected character stuck on screen. The DOM is now corrected
    /// unconditionally from OnAfterRenderAsync (see the "second rejected keystroke" test below for why
    /// a comparison-based guard cannot cover every keystroke on an unbound field), so this still fires.
    /// </summary>
    [Fact]
    public void DateType_NullValue_FirstRejectedKeystroke_CorrectsDomViaJsInterop()
    {
        string? value = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.ValueChanged, (string? v) => value = v));

        cut.Find("input").Input("a");

        Assert.Equal(string.Empty, value);

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setInputValue");
        Assert.Equal(string.Empty, invocation.Arguments[1]);
        Assert.Equal(0, invocation.Arguments[2]);
    }

    /// <summary>
    /// A character the mask rejects outright (typed after two digits already accepted) must not
    /// change the emitted value, and the DOM is corrected from OnAfterRenderAsync via
    /// <c>wakeelUi.setInputValue</c> with the caret placed right after the last accepted digit, not
    /// at the end of the rejected text.
    /// </summary>
    [Fact]
    public void DateType_RejectedCharacter_KeepsValue_AndFixesDomWithCaretAfterLastDigit()
    {
        JSInterop.Setup<int>("wakeelUi.getSelectionStart", _ => true).SetResult(3);

        string? value = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Value, "12")
            .Add(x => x.ValueChanged, (string? v) => value = v));

        cut.Find("input").Input("12a");

        Assert.Equal("12", value);

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setInputValue");
        Assert.Equal("12", invocation.Arguments[1]);
        Assert.Equal(2, invocation.Arguments[2]);
    }

    /// <summary>
    /// verify-design-split.json finding 3: inserting a genuinely new digit in the middle of an
    /// already-typed date changes the formatted string, so Blazor's own diff rewrites the `value`
    /// attribute this render — which would bounce the caret to the end of the field. The caret (and
    /// value, written idempotently) are instead re-applied from OnAfterRenderAsync via
    /// wakeelUi.setInputValue, after that render has already happened.
    /// </summary>
    [Fact]
    public void DateType_MiddleInsertion_RestoresCaret_ViaJsInterop_AfterRender()
    {
        JSInterop.Setup<int>("wakeelUi.getSelectionStart", _ => true).SetResult(3);

        string? value = null;
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Value, "12/09")
            .Add(x => x.ValueChanged, (string? v) => value = v));

        // Cursor placed after "125" (index 3) inserting the new "5" between the already-typed "12" and "/09".
        cut.Find("input").Input("125/09");

        Assert.Equal("12/50/9", value);

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setInputValue");
        Assert.Equal("12/50/9", invocation.Arguments[1]);
        Assert.Equal(4, invocation.Arguments[2]);
    }

    /// <summary>
    /// verify-design-split.json (this package's review) finding 2, low: the null-value guard fixed
    /// above only covers the very first keystroke on an unbound field (no @bind-Value/ValueChanged).
    /// Because <see cref="WInput.Value"/> then never changes, Blazor's diff never rewrites the `value`
    /// attribute on any later keystroke either, so a second rejected character used to stay stuck on
    /// screen (typing "1" then "a" left "1a" visible instead of being corrected back to "1"). Writing
    /// <c>_pendingValue</c> unconditionally from OnAfterRenderAsync fixes every keystroke, not just
    /// the first, because it no longer depends on comparing against <see cref="WInput.Value"/> at all.
    /// getSelectionStart is stubbed to the end of the raw text (2, i.e. right after "1a") to simulate
    /// the caret landing where the user actually typed.
    /// </summary>
    [Fact]
    public void DateType_NullValue_SecondRejectedKeystroke_AlsoCorrectsDomViaJsInterop()
    {
        JSInterop.Setup<int>("wakeelUi.getSelectionStart", _ => true).SetResult(2);

        var cut = Render<WInput>(p => p.Add(x => x.Type, WInputType.Date));

        cut.Find("input").Input("1");
        cut.Find("input").Input("1a");

        var invocation = JSInterop.Invocations.Last(i => i.Identifier == "wakeelUi.setInputValue");
        Assert.Equal("1", invocation.Arguments[1]);
        Assert.Equal(1, invocation.Arguments[2]);
    }

    /// <summary>
    /// fix2-design-polish.json low finding 3: <c>_inputSeq</c> (WInput.razor) guards against two
    /// keystrokes' <c>getSelectionStart</c> interop round-trips resolving out of order. The
    /// <c>getSelectionStart</c> setup below is left "planned" (no <c>SetResult</c> yet), so both
    /// <c>Input()</c> calls suspend at their own <c>await Js.InvokeAsync</c> without either completing;
    /// calling <c>SetResult</c> afterwards resolves both pending invocations in the order they were
    /// made, i.e. the older ("1") keystroke's continuation resumes before the newer ("12") one's — the
    /// exact interleaving the guard exists for. It must (a) drop the superseded "1" keystroke's
    /// ValueChanged entirely rather than emit a stale value after "12" already went out, and (b) leave
    /// only the newer keystroke's (value, caret) pair written to the DOM.
    /// </summary>
    [Fact]
    public void DateType_OutOfOrderInteropReplies_SupersededKeystrokeIsDroppedNotEmittedStale()
    {
        var pendingSelectionStart = JSInterop.Setup<int>("wakeelUi.getSelectionStart", _ => true);

        var values = new List<string?>();
        var cut = Render<WInput>(p => p
            .Add(x => x.Type, WInputType.Date)
            .Add(x => x.Value, string.Empty)
            .Add(x => x.ValueChanged, (string? v) => values.Add(v)));

        cut.Find("input").Input("1");
        cut.Find("input").Input("12");

        pendingSelectionStart.SetResult(2);

        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setInputValue"));

        Assert.Equal(new[] { "12" }, values);

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setInputValue");
        Assert.Equal("12", invocation.Arguments[1]);
        Assert.Equal(2, invocation.Arguments[2]);
    }
}
