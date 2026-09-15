using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class WTopBarTests : WakeelTestContext
{
    [Fact]
    public void Renders_UserNameAndRole()
    {
        var cut = Render<WTopBar>(p => p
            .Add(x => x.UserName, "أحمد الخطيب")
            .Add(x => x.UserRole, "مدير المكتب"));

        Assert.Contains("أحمد الخطيب", cut.Markup);
        Assert.Contains("مدير المكتب", cut.Markup);
    }

    [Fact]
    public void ShowsNotificationBadge_WhenCountAboveZero()
    {
        var cut = Render<WTopBar>(p => p
            .Add(x => x.UserName, "أحمد الخطيب")
            .Add(x => x.NotificationCount, 3));

        Assert.Contains("3", cut.Find(".w-topbar-bell-badge").TextContent);
    }

    [Fact]
    public void HidesNotificationBadge_WhenCountIsZero()
    {
        var cut = Render<WTopBar>(p => p
            .Add(x => x.UserName, "أحمد الخطيب")
            .Add(x => x.NotificationCount, 0));

        Assert.Empty(cut.FindAll(".w-topbar-bell-badge"));
    }

    [Fact]
    public void BellClick_RaisesOnBellClick()
    {
        var clicked = false;
        var cut = Render<WTopBar>(p => p
            .Add(x => x.UserName, "أحمد الخطيب")
            .Add(x => x.OnBellClick, () => clicked = true));

        cut.Find(".w-topbar-bell button").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void UserMenu_SignOutItem_IsDangerStyled()
    {
        var cut = Render<WTopBar>(p => p.Add(x => x.UserName, "أحمد الخطيب"));

        cut.Find(".w-menu-trigger").Click();

        var items = cut.FindAll(".w-menu-item");
        var signOut = items.Last();
        Assert.Contains("w-menu-item--danger", signOut.ClassList);
    }
}
