using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.Design.Components;
using Wakeel.Design.Text;
using Wakeel.UI.Layout;
using Wakeel.UI.Pages;
using Wakeel.UI.Services;

namespace Wakeel.UI.Tests;

/// <summary>Layout/MainLayout.razor and the two B0 pages (Gallery, W08AttentionCenter) render as a whole shell.</summary>
public class ShellTests : WakeelTestContext
{
    [Fact]
    public void MainLayout_RendersSidebarAndTopBar_AroundBody()
    {
        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(b => b.AddContent(0, "محتوى الصفحة"))));

        Assert.Single(cut.FindComponents<WSidebar>());
        Assert.Single(cut.FindComponents<WTopBar>());
        Assert.Contains("محتوى الصفحة", cut.Markup);
    }

    [Fact]
    public void MainLayout_HidesPageHeaderRow_UntilAPagePublishesATitle()
    {
        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(b => b.AddContent(0, "محتوى"))));

        Assert.Empty(cut.FindAll(".w-page-header"));
    }

    [Fact]
    public void MainLayout_ShowsPageHeader_WhenPageHeaderStatePublishesATitle()
    {
        var headerState = new PageHeaderState();
        Services.AddSingleton(headerState);
        headerState.Set("مركز الانتباه", "ما يحتاج انتباهك اليوم");

        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(b => b.AddContent(0, "محتوى"))));

        Assert.Contains("مركز الانتباه", cut.Find(".w-page-header-title").TextContent);
        Assert.Contains("ما يحتاج انتباهك اليوم", cut.Markup);
    }

    [Fact]
    public void Gallery_Renders_UnderMainLayout_WithoutThrowing()
    {
        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            (RenderFragment)(b =>
            {
                b.OpenComponent<Gallery>(0);
                b.CloseComponent();
            })));

        Assert.Contains("معرض المكوّنات", cut.Markup);
    }

    [Fact]
    public void Gallery_ThemeToggle_ChangesDataThemeViaJsInterop()
    {
        var cut = Render<Gallery>();

        cut.FindAll(".w-segmented-item")[1].Click(); // "داكن"

        var invocation = Assert.Single(JSInterop.Invocations, i => i.Identifier == "wakeelUi.setTheme");
        Assert.Equal("dark", invocation.Arguments[0]);
    }

    /// <summary>
    /// W08 is live now: it reads IAttentionService, so with no session open it draws the
    /// closed-session state and nothing of the B0 fixed sample. The loaded screen is covered
    /// against real rows in <c>Shell/AttentionScreenTests</c>; what this pins is that the sample
    /// data — the four hard-coded KPI numbers, the three action rows and the two expense rows —
    /// is gone for good.
    /// </summary>
    [Fact]
    public void W08AttentionCenter_NoLongerDrawsTheB0SampleDataset()
    {
        var cut = Render<W08AttentionCenter>();

        Assert.Contains(Ar.Shell.SessionClosedTitle, cut.Markup);
        Assert.DoesNotContain(Ar.AttentionCenter.ActionRow1Subject, cut.Markup);
        Assert.DoesNotContain(Ar.AttentionCenter.ActionRow1Assignee, cut.Markup);
        Assert.DoesNotContain(Ar.AttentionCenter.Expense1Employee, cut.Markup);
        Assert.Empty(cut.FindAll(".w-table"));
        Assert.Empty(cut.FindAll(".w-kpi"));
    }

    [Fact]
    public void W08AttentionCenter_PublishesTitle_ToPageHeaderState()
    {
        var headerState = new PageHeaderState();
        Services.AddSingleton(headerState);

        Render<W08AttentionCenter>();

        Assert.Equal("مركز الانتباه", headerState.Title);
        Assert.Equal(WSidebar.Keys.Attention, headerState.NavKey);
    }
}
