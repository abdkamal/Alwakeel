using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class WButtonTests : WakeelTestContext
{
    [Fact]
    public void Renders_Label()
    {
        var cut = Render<WButton>(p => p.Add(x => x.Label, "حفظ"));

        Assert.Contains("حفظ", cut.Markup);
    }

    [Theory]
    [InlineData(WButtonVariant.Primary, "w-btn--primary")]
    [InlineData(WButtonVariant.Secondary, "w-btn--secondary")]
    [InlineData(WButtonVariant.Danger, "w-btn--danger")]
    [InlineData(WButtonVariant.Text, "w-btn--text")]
    public void Renders_VariantClass(WButtonVariant variant, string expectedClass)
    {
        var cut = Render<WButton>(p => p
            .Add(x => x.Variant, variant)
            .Add(x => x.Label, "زر"));

        Assert.Contains(expectedClass, cut.Find("button").ClassList);
    }

    [Fact]
    public void IconVariant_RequiresTooltip_RenderedAsAriaLabel()
    {
        var cut = Render<WButton>(p => p
            .Add(x => x.Variant, WButtonVariant.Icon)
            .Add(x => x.Icon, "refresh-cw")
            .Add(x => x.Tooltip, "تحديث"));

        Assert.Equal("تحديث", cut.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Click_InvokesOnClick()
    {
        var clicked = false;
        var cut = Render<WButton>(p => p
            .Add(x => x.Label, "حفظ")
            .Add(x => x.OnClick, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void Disabled_PreventsClick()
    {
        var clicked = false;
        var cut = Render<WButton>(p => p
            .Add(x => x.Label, "حفظ")
            .Add(x => x.Disabled, true)
            .Add(x => x.OnClick, () => clicked = true));

        Assert.True(cut.Find("button").HasAttribute("disabled"));

        // bUnit dispatches the click handler regardless of the rendered `disabled` attribute, so this
        // actually exercises WButton's own IsDisabled guard inside HandleClickAsync rather than
        // relying solely on the browser's native disabled-button behaviour.
        cut.Find("button").Click();

        Assert.False(clicked);
    }

    [Fact]
    public async Task Loading_DoubleClickGuard_RunsHandlerOnlyOnce()
    {
        var runCount = 0;
        var tcs = new TaskCompletionSource();
        var cut = Render<WButton>(p => p
            .Add(x => x.Label, "حفظ")
            .Add(x => x.OnClick, async () =>
            {
                runCount++;
                await tcs.Task;
            }));

        // Click twice in quick succession before the first (still in-flight) handler completes.
        var button = cut.Find("button");
        button.Click();
        button.Click();

        Assert.Equal(1, runCount);

        tcs.SetResult();
        cut.WaitForState(() => !cut.Find("button").HasAttribute("disabled"));

        button.Click();

        Assert.Equal(2, runCount);
    }

    [Fact]
    public void Loading_ShowsWorkingText_InsteadOfLabel()
    {
        var cut = Render<WButton>(p => p
            .Add(x => x.Label, "حفظ")
            .Add(x => x.Loading, true));

        Assert.Contains("جارٍ التنفيذ", cut.Markup);
        Assert.DoesNotContain(">حفظ<", cut.Markup);
    }

    [Fact]
    public void SplitVariant_TogglesMenu_OnCaretClick()
    {
        var cut = Render<WButton>(p => p
            .Add(x => x.Variant, WButtonVariant.Split)
            .Add(x => x.Label, "إجراء")
            .Add(x => x.SplitContent, (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddContent(0, "خيارات"))));

        Assert.Empty(cut.FindAll(".w-btn-split-menu"));

        cut.Find(".w-btn-split-caret").Click();

        Assert.Single(cut.FindAll(".w-btn-split-menu"));
    }
}
