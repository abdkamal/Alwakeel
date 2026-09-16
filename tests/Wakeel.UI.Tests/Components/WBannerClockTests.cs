using Bunit;
using Wakeel.Design.Components;

namespace Wakeel.UI.Tests.Components;

public class WBannerClockTests : WakeelTestContext
{
    [Fact]
    public void Renders_WarningMessage()
    {
        var cut = Render<WBannerClock>();

        Assert.Contains("تعذّر التحقق من ساعة الجهاز", cut.Markup);
    }

    [Fact]
    public void ActionButton_RaisesOnAction()
    {
        var clicked = false;
        var cut = Render<WBannerClock>(p => p.Add(x => x.OnAction, () => clicked = true));

        cut.Find("button").Click();

        Assert.True(clicked);
    }
}
