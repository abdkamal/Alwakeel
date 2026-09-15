using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class CardsTests : WakeelTestContext
{
    [Fact]
    public void WCard_Neutral_RendersTitleAndChildContent()
    {
        var cut = Render<WCard>(p => p
            .Add(x => x.Title, "بطاقة قياسية")
            .AddChildContent("<p>محتوى حر</p>"));

        Assert.Contains("بطاقة قياسية", cut.Markup);
        Assert.Contains("محتوى حر", cut.Markup);
    }

    [Theory]
    [InlineData(WSemanticVariant.Info, "w-card--info")]
    [InlineData(WSemanticVariant.Warning, "w-card--warning")]
    [InlineData(WSemanticVariant.Danger, "w-card--danger")]
    [InlineData(WSemanticVariant.Success, "w-card--success")]
    public void WCard_Semantic_RendersVariantClass_AndIcon(WSemanticVariant variant, string expectedClass)
    {
        var cut = Render<WCard>(p => p
            .Add(x => x.Variant, variant)
            .Add(x => x.Icon, "info")
            .Add(x => x.Title, "عنوان")
            .Add(x => x.Desc, "وصف"));

        Assert.Contains(expectedClass, cut.Find(".w-card").ClassList);
        Assert.Contains("وصف", cut.Markup);
    }

    [Fact]
    public void WKpiCard_Renders_LabelValueAndSub()
    {
        var cut = Render<WKpiCard>(p => p
            .Add(x => x.Label, "متأخر")
            .Add(x => x.Icon, "clock")
            .Add(x => x.Value, "12")
            .Add(x => x.Sub, "تجاوز موعد الاستحقاق"));

        Assert.Contains("متأخر", cut.Markup);
        Assert.Contains("12", cut.Markup);
        Assert.Contains("تجاوز موعد الاستحقاق", cut.Markup);
    }

    [Fact]
    public void WStateCard_ActionButton_RaisesOnAction()
    {
        var clicked = false;
        var cut = Render<WStateCard>(p => p
            .Add(x => x.Icon, "folder-open")
            .Add(x => x.Title, "لا توجد بيانات بعد")
            .Add(x => x.ActionLabel, "إضافة جديد")
            .Add(x => x.OnAction, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void WStateCard_HidesActionButton_WhenNoActionLabel()
    {
        var cut = Render<WStateCard>(p => p
            .Add(x => x.Icon, "folder-open")
            .Add(x => x.Title, "لا توجد بيانات بعد"));

        Assert.Empty(cut.FindAll("button"));
    }
}
