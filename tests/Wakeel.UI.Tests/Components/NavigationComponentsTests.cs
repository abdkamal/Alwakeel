using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class NavigationComponentsTests : WakeelTestContext
{
    [Fact]
    public void WTabItem_Click_RaisesOnClick()
    {
        var clicked = false;
        var cut = Render<WTabItem>(p => p
            .Add(x => x.Label, "الكل")
            .Add(x => x.OnClick, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void WTabItem_ShowsCountBadge_WhenSet()
    {
        var cut = Render<WTabItem>(p => p
            .Add(x => x.Label, "مراسلات")
            .Add(x => x.Count, 4));

        Assert.Contains("4", cut.Markup);
    }

    [Fact]
    public void WTabItem_Active_HasActiveClass()
    {
        var cut = Render<WTabItem>(p => p
            .Add(x => x.Label, "الكل")
            .Add(x => x.Active, true));

        Assert.Contains("w-tab--active", cut.Find("button").ClassList);
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-selected"));
    }

    [Fact]
    public void WStepper_MarksStepsBeforeCurrent_AsDone()
    {
        var cut = Render<WStepper>(p => p
            .Add(x => x.Steps, new List<string> { "البيانات", "المراجعة", "الاعتماد" })
            .Add(x => x.CurrentStep, 2));

        var items = cut.FindAll(".w-stepper-item");
        Assert.Contains("w-stepper-item--done", items[0].ClassList);
        Assert.Contains("w-stepper-item--active", items[1].ClassList);
    }

    [Fact]
    public void WPager_Click_RaisesPageChanged()
    {
        int? newPage = null;
        var cut = Render<WPager>(p => p
            .Add(x => x.Page, 1)
            .Add(x => x.PageCount, 5)
            .Add(x => x.PageChanged, (int v) => newPage = v));

        cut.FindAll(".w-pager-page")[1].Click();

        Assert.Equal(2, newPage);
    }

    [Fact]
    public void WPager_FirstPage_DisablesPreviousButton()
    {
        var cut = Render<WPager>(p => p.Add(x => x.Page, 1).Add(x => x.PageCount, 3));

        Assert.True(cut.FindAll(".w-pager-nav")[0].HasAttribute("disabled"));
    }

    [Fact]
    public void WBreadcrumb_LastItem_HasCurrentPageStyle_AndIsNotAButton()
    {
        var cut = Render<WBreadcrumb>(p => p.Add(x => x.Items, new List<WBreadcrumbItem>
        {
            new("المراسلات"),
            new("طلب تزويد بيانات"),
        }));

        Assert.Contains("طلب تزويد بيانات", cut.Markup);
        Assert.Single(cut.FindAll("button"));
    }

    [Fact]
    public void WProgressBar_ClampsPercent_AboveOneHundred()
    {
        var cut = Render<WProgressBar>(p => p.Add(x => x.Percent, 150));

        Assert.Equal("100", cut.Find(".w-progress-track").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void WSectionHeader_ActionButton_RaisesOnAction()
    {
        var clicked = false;
        var cut = Render<WSectionHeader>(p => p
            .Add(x => x.Title, "قسم")
            .Add(x => x.ActionLabel, "عرض الكل")
            .Add(x => x.OnAction, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void WSectionHeader_Count_RendersBadgeBeforeTitle()
    {
        var cut = Render<WSectionHeader>(p => p
            .Add(x => x.Title, "قسم")
            .Add(x => x.Count, 6));

        var badge = cut.Find(".w-badge");
        Assert.Equal("6", badge.TextContent);
    }

    [Fact]
    public void WKvRow_Renders_KeyAndBdiWrappedValue()
    {
        var cut = Render<WKvRow>(p => p
            .Add(x => x.Key, "الرقم المرجعي")
            .Add(x => x.Value, "20260912/12046"));

        Assert.Contains("الرقم المرجعي", cut.Markup);
        Assert.Equal("20260912/12046", cut.Find("bdi.w-kv-value").TextContent);
    }

    [Theory]
    [InlineData(WAutosaveState.Saved, "تم الحفظ تلقائيًا")]
    [InlineData(WAutosaveState.Saving, "جارٍ الحفظ التلقائي")]
    [InlineData(WAutosaveState.Failed, "تعذّر الحفظ التلقائي")]
    public void WAutosaveIndicator_ShowsStateText(WAutosaveState state, string expectedText)
    {
        var cut = Render<WAutosaveIndicator>(p => p.Add(x => x.State, state));

        Assert.Contains(expectedText, cut.Markup);
    }
}
