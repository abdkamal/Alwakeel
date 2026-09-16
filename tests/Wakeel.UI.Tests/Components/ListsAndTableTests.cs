using Bunit;
using Wakeel.Design.Components;

namespace Wakeel.UI.Tests.Components;

public class ListsAndTableTests : WakeelTestContext
{
    [Fact]
    public void WTable_Renders_HeaderRowsAndFooterText()
    {
        var cut = Render<WTable>(p => p
            .Add(x => x.HeaderRow, (Microsoft.AspNetCore.Components.RenderFragment)(b =>
            {
                b.OpenElement(0, "th");
                b.AddContent(1, "الرقم");
                b.CloseElement();
            }))
            .Add(x => x.FooterText, "عرض 1–3 من 3")
            .AddChildContent("<tr><td>20260911/12001</td></tr>"));

        Assert.Contains("الرقم", cut.Markup);
        Assert.Contains("20260911/12001", cut.Markup);
        Assert.Contains("عرض 1–3 من 3", cut.Markup);
    }

    [Fact]
    public void WTable_AttentionRow_HasAttentionClass()
    {
        var cut = Render<WTable>(p => p
            .AddChildContent("<tr class=\"w-table-row--attention\"><td>متأخر</td></tr>"));

        Assert.Contains("w-table-row--attention", cut.Markup);
    }

    [Fact]
    public void WTable_IsEmpty_RendersEmptyStateInsteadOfTable()
    {
        var cut = Render<WTable>(p => p
            .Add(x => x.IsEmpty, true)
            .Add(x => x.EmptyState, (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddContent(0, "لا توجد نتائج"))));

        Assert.Contains("لا توجد نتائج", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void WTable_PageCountAboveOne_RendersPager()
    {
        var cut = Render<WTable>(p => p.Add(x => x.Page, 1).Add(x => x.PageCount, 3));

        Assert.Single(cut.FindAll(".w-pager"));
    }

    [Fact]
    public void WDocumentRow_Click_RaisesOnClick_AndShowsFileNameInBdi()
    {
        var clicked = false;
        var cut = Render<WDocumentRow>(p => p
            .Add(x => x.FileName, "عقد_2026.pdf")
            .Add(x => x.OnClick, () => clicked = true));

        Assert.Equal("عقد_2026.pdf", cut.Find("bdi.w-doc-row-name").TextContent);

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void WTimelineItem_Renders_TitleDescAndMeta()
    {
        var cut = Render<WTimelineItem>(p => p
            .Add(x => x.Title, "أُنشئت المراسلة")
            .Add(x => x.Desc, "إلى إدارة المشاريع")
            .Add(x => x.Meta, "أحمد الخطيب · 12/09/2026 10:24"));

        Assert.Contains("أُنشئت المراسلة", cut.Markup);
        Assert.Contains("إلى إدارة المشاريع", cut.Markup);
        Assert.Contains("10:24", cut.Markup);
    }

    /// <summary>
    /// AGREEMENT item 55 / docs/design/bidi-test.html "W04 · اسم الحساب": a meta line mixing Arabic
    /// text with a device id must land inside a &lt;bdi&gt; (unicode-bidi: isolate) so the id stays
    /// put regardless of what comes after it.
    /// </summary>
    [Fact]
    public void WTimelineItem_Meta_RendersInsideBdi_ForMixedArabicLatinLine()
    {
        const string meta = "الحساب a.khatib · الموظف أحمد الخطيب · الجهاز 1 — PLN-PC-01";
        var cut = Render<WTimelineItem>(p => p
            .Add(x => x.Title, "تسجيل الحساب")
            .Add(x => x.Meta, meta));

        Assert.Equal(meta, cut.Find("bdi.w-bdi").TextContent);
    }

    /// <summary>
    /// AGREEMENT item 55 / docs/design/bidi-test.html "W02 · التشغيل الأول": a file name mixing a
    /// device id, dot-separated extension, and hyphens must land inside a &lt;bdi&gt; so it is not
    /// pulled apart by the surrounding RTL paragraph.
    /// </summary>
    [Fact]
    public void WDocumentRow_FileName_RendersInsideBdi_ForMixedArabicLatinFileName()
    {
        var cut = Render<WDocumentRow>(p => p.Add(x => x.FileName, "PLN-PC-01.wakeel-setup"));

        Assert.Equal("PLN-PC-01.wakeel-setup", cut.Find("bdi.w-doc-row-name").TextContent);
    }

    [Fact]
    public void WMenu_Click_OpensPopover_AndSelectingItem_InvokesCallbackAndCloses()
    {
        var selected = false;
        var cut = Render<WMenu>(p => p.Add(x => x.Items, new List<WMenuItem>
        {
            new("عرض", "eye", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => selected = true)),
        }));

        cut.Find(".w-menu-trigger").Click();
        Assert.Single(cut.FindAll(".w-menu-popover"));

        cut.Find(".w-menu-item").Click();

        Assert.True(selected);
        Assert.Empty(cut.FindAll(".w-menu-popover"));
    }

    [Fact]
    public void WMenu_DangerItem_HasDangerClass()
    {
        var cut = Render<WMenu>(p => p.Add(x => x.Items, new List<WMenuItem>
        {
            new("حذف نهائيًا", "trash-2", default, Danger: true),
        }));

        cut.Find(".w-menu-trigger").Click();

        Assert.Contains("w-menu-item--danger", cut.Find(".w-menu-item").ClassList);
    }

    [Fact]
    public void WMenu_DefaultTrigger_ExposesAccessibleName()
    {
        var cut = Render<WMenu>(p => p.Add(x => x.Items, new List<WMenuItem>
        {
            new("عرض", "eye", default),
        }));

        Assert.Equal(Wakeel.Design.Text.Ar.Menu.MoreActions, cut.Find(".w-menu-trigger").GetAttribute("aria-label"));
        Assert.Contains(Wakeel.Design.Text.Ar.Menu.MoreActions, cut.Find(".w-tooltip-bubble").TextContent);
    }

    [Fact]
    public void WCalendarDay_Click_RaisesOnClick_WhenNotOutsideMonth()
    {
        var clicked = false;
        var cut = Render<WCalendarDay>(p => p
            .Add(x => x.DayNumber, 12)
            .Add(x => x.OnClick, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void WCalendarDay_OutsideMonth_IsDisabled()
    {
        var cut = Render<WCalendarDay>(p => p
            .Add(x => x.DayNumber, 31)
            .Add(x => x.IsOutsideMonth, true));

        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void WCalendarDay_ShowsUpToTwoEventLabels()
    {
        var cut = Render<WCalendarDay>(p => p
            .Add(x => x.DayNumber, 14)
            .Add(x => x.Events, new List<string> { "تسليم تقرير", "متابعة" }));

        Assert.Equal(2, cut.FindAll(".w-cal-day-event").Count);
    }
}
