using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.UI.Components;
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

    [Fact]
    public void W08AttentionCenter_Renders_FixedSampleKpis()
    {
        var cut = Render<W08AttentionCenter>();

        Assert.Contains("بانتظار تأكيدي", cut.Markup);
        Assert.Contains("متأخر", cut.Markup);
        Assert.Contains("سامر أبو غزالة", cut.Markup);
    }

    [Fact]
    public void W08AttentionCenter_AttentionRow_UsesAttentionRowStyle()
    {
        var cut = Render<W08AttentionCenter>();

        Assert.Contains("w-table-row--attention", cut.Markup);
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
