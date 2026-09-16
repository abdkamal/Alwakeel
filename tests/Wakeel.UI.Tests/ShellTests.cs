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

    /// <summary>
    /// verify-design-split.json (B1-POLISH-DESIGN review) low finding: the «الكل» tab's count badge
    /// must track <see cref="Ar.AttentionCenter.TodayActionCount"/> rather than a literal that can
    /// drift from the section header's own count when the sample data changes.
    /// </summary>
    [Fact]
    public void W08AttentionCenter_AllTabBadge_MatchesTodayActionCount()
    {
        var cut = Render<W08AttentionCenter>();

        var firstTabBadge = cut.FindAll(".w-tab")[0].QuerySelector(".w-badge");

        Assert.NotNull(firstTabBadge);
        Assert.Equal(Ar.AttentionCenter.TodayActionCount.ToString(), firstTabBadge!.TextContent);
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

    /// <summary>
    /// Regression for the B0-closeout review's expenses-table finding: header and body column counts
    /// must agree (5 each: الموظف/البيان/التاريخ/المبلغ plus a visually-hidden actions label), and
    /// dates must be isolated in &lt;bdi&gt; per AGREEMENT item 55.
    /// </summary>
    [Fact]
    public void W08AttentionCenter_ExpensesTable_HasFiveMatchingHeaderAndBodyColumns()
    {
        var cut = Render<W08AttentionCenter>();

        var table = cut.FindAll(".w-table").Last(t => t.QuerySelector(".w08-expense-meta") is not null);

        var headerCells = table.QuerySelectorAll("thead th");
        Assert.Equal(5, headerCells.Length);
        Assert.Equal("الموظف", headerCells[0].TextContent.Trim());
        Assert.Equal("البيان", headerCells[1].TextContent.Trim());
        Assert.Equal("التاريخ", headerCells[2].TextContent.Trim());
        Assert.Equal("المبلغ", headerCells[3].TextContent.Trim());
        Assert.Contains("إجراءات الموافقة", headerCells[4].QuerySelector(".w-visually-hidden")!.TextContent);

        var bodyRows = table.QuerySelectorAll("tbody tr");
        Assert.Equal(2, bodyRows.Length);
        foreach (var row in bodyRows)
        {
            Assert.Equal(5, row.QuerySelectorAll("td").Length);
        }

        var firstRowDate = bodyRows[0].QuerySelectorAll("td")[2];
        Assert.Equal("11/09/2026", firstRowDate.QuerySelector("bdi")!.TextContent);

        var secondRowDate = bodyRows[1].QuerySelectorAll("td")[2];
        Assert.Equal("10/09/2026", secondRowDate.QuerySelector("bdi")!.TextContent);
    }
}
