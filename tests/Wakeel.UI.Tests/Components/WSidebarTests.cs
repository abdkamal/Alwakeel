using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.UI.Components;
using Wakeel.UI.Services;

namespace Wakeel.UI.Tests.Components;

/// <summary>WSidebar: fixed nav tree, selection, badges, sync footer, and — most importantly — that a
/// group's collapsed/expanded state is persisted through IUiStateStore so it survives a remount
/// (e.g. navigating away and back), per the B0-UI spec's "sidebar collapse persistence" requirement.</summary>
public class WSidebarTests : WakeelTestContext
{
    [Fact]
    public void Renders_OrgAndOfficeName()
    {
        var cut = Render<WSidebar>();

        Assert.Contains("هيئة تنمية المناطق الريفية", cut.Markup);
        Assert.Contains("مكتب مدير دائرة التخطيط", cut.Markup);
    }

    [Fact]
    public void Renders_AllDefaultGroupsAndItems()
    {
        var cut = Render<WSidebar>();

        Assert.Contains("مركز الانتباه", cut.Markup);
        Assert.Contains("المراسلات", cut.Markup);
        Assert.Contains("الوثائق", cut.Markup);
        Assert.Contains("المالية", cut.Markup);
    }

    [Fact]
    public void SelectedKey_MarksMatchingItem_AsSelected()
    {
        var cut = Render<WSidebar>(p => p.Add(x => x.SelectedKey, WSidebar.Keys.Attention));

        var selected = cut.Find(".w-sidebar-item--selected");
        Assert.Contains("مركز الانتباه", selected.TextContent);
    }

    [Fact]
    public void ClickingItem_RaisesOnNavigate_WithItsKey()
    {
        string? navigatedKey = null;
        var cut = Render<WSidebar>(p => p.Add(x => x.OnNavigate, (string k) => navigatedKey = k));

        cut.FindAll(".w-sidebar-item").First(e => e.TextContent.Contains("المراسلات")).Click();

        Assert.Equal(WSidebar.Keys.Correspondence, navigatedKey);
    }

    [Fact]
    public void Badges_ShowCount_ForMatchingKey_AndHideWhenAbsent()
    {
        var cut = Render<WSidebar>(p => p.Add(x => x.Badges, new Dictionary<string, int>
        {
            [WSidebar.Keys.Attention] = 5,
        }));

        var attentionItem = cut.FindAll(".w-sidebar-item").First(e => e.TextContent.Contains("مركز الانتباه"));
        Assert.Contains("5", attentionItem.TextContent);

        var correspondenceItem = cut.FindAll(".w-sidebar-item").First(e => e.TextContent.Contains("المراسلات"));
        Assert.Empty(correspondenceItem.QuerySelectorAll(".w-badge--default"));
    }

    [Fact]
    public void RefreshButton_RaisesOnRefreshSync()
    {
        var refreshed = false;
        var cut = Render<WSidebar>(p => p.Add(x => x.OnRefreshSync, () => refreshed = true));

        cut.Find(".w-sidebar-sync-refresh").Click();

        Assert.True(refreshed);
    }

    [Fact]
    public void CollapsingAGroup_HidesItsItems()
    {
        var cut = Render<WSidebar>();

        Assert.Contains("مركز الانتباه", cut.Markup);

        cut.FindAll(".w-sidebar-group-header")[0].Click();

        Assert.DoesNotContain("مركز الانتباه", cut.Markup);
    }

    [Fact]
    public void CollapsedState_PersistsThroughUiStateStore_AcrossARemount()
    {
        var store = new InMemoryUiStateStore();
        Services.AddSingleton<IUiStateStore>(store);

        var first = Render<WSidebar>();
        first.FindAll(".w-sidebar-group-header")[0].Click();
        Assert.DoesNotContain("مركز الانتباه", first.Markup);

        // Simulate leaving and returning to the shell: a brand-new WSidebar instance backed by the
        // same store must come back already collapsed, proving persistence rather than in-memory state.
        var second = Render<WSidebar>();

        Assert.DoesNotContain("مركز الانتباه", second.Markup);
    }

    [Fact]
    public void ExpandingAPreviouslyCollapsedGroup_PersistsTheExpandedState()
    {
        var store = new InMemoryUiStateStore();
        Services.AddSingleton<IUiStateStore>(store);
        store.SetBool("sidebar.group." + WSidebar.Keys.GroupDailyWork, true);

        var first = Render<WSidebar>();
        Assert.DoesNotContain("مركز الانتباه", first.Markup);

        first.FindAll(".w-sidebar-group-header")[0].Click();
        Assert.Contains("مركز الانتباه", first.Markup);

        var second = Render<WSidebar>();
        Assert.Contains("مركز الانتباه", second.Markup);
    }
}
