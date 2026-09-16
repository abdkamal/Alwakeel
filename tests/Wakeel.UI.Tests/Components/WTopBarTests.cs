using Bunit;
using Wakeel.Design.Components;

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

    /// <summary>
    /// Regression for the smoke-test overflow (docs/build/reviews/B0-closeout/smoke-shell-dark.png):
    /// the name wrapped to two lines and collided with the bell badge at 1366 width. Each of name and
    /// role must render as ONE element holding the whole string (not split across several elements the
    /// way a wrapped multi-line layout would need) so WTopBar.razor.css's single-line/ellipsis rules
    /// (white-space: nowrap + text-overflow: ellipsis on a fixed-width .w-topbar-user-text) apply to
    /// the entire name and the entire role, each on its own line.
    /// </summary>
    [Fact]
    public void UserNameAndRole_RenderAsSingleLineElements()
    {
        var cut = Render<WTopBar>(p => p
            .Add(x => x.UserName, "أحمد الخطيب")
            .Add(x => x.UserRole, "مدير المكتب"));

        var name = Assert.Single(cut.FindAll(".w-topbar-user-name"));
        var role = Assert.Single(cut.FindAll(".w-topbar-user-role"));
        Assert.Equal("أحمد الخطيب", name.TextContent);
        Assert.Equal("مدير المكتب", role.TextContent);
        Assert.Same(name.ParentElement, role.ParentElement);
        Assert.Contains("w-topbar-user-text", name.ParentElement!.ClassList);
        Assert.Equal("BDI", name.TagName);
    }
}
