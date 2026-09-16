using Bunit;
using Wakeel.Design.Components;

namespace Wakeel.UI.Tests.Components;

public class BadgesAndChipsTests : WakeelTestContext
{
    [Theory]
    [InlineData(WChipVariant.Info, "w-chip--info")]
    [InlineData(WChipVariant.Warning, "w-chip--warning")]
    [InlineData(WChipVariant.Danger, "w-chip--danger")]
    public void WChip_Renders_VariantClass_AndDot(WChipVariant variant, string expectedClass)
    {
        var cut = Render<WChip>(p => p
            .Add(x => x.Variant, variant)
            .Add(x => x.Label, "قيد المتابعة"));

        Assert.Contains(expectedClass, cut.Find("span.w-chip").ClassList);
        Assert.Single(cut.FindAll(".w-chip-dot"));
    }

    [Theory]
    [InlineData(WBadgeKind.Default, "w-badge--default")]
    [InlineData(WBadgeKind.Tab, "w-badge--tab")]
    [InlineData(WBadgeKind.Group, "w-badge--group")]
    public void WBadge_Renders_KindClass_AndCount(WBadgeKind kind, string expectedClass)
    {
        var cut = Render<WBadge>(p => p.Add(x => x.Count, 5).Add(x => x.Kind, kind));

        Assert.Contains(expectedClass, cut.Find("bdi").ClassList);
        Assert.Contains("5", cut.Markup);
    }

    [Fact]
    public void WAvatar_ShowsInitial_WhenNoPhoto()
    {
        var cut = Render<WAvatar>(p => p.Add(x => x.Name, "أحمد الخطيب"));

        Assert.Contains("أ", cut.Markup);
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void WAvatar_RendersImage_WhenPhotoUrlSet()
    {
        var cut = Render<WAvatar>(p => p
            .Add(x => x.Name, "أحمد الخطيب")
            .Add(x => x.PhotoUrl, "/photo.jpg"));

        Assert.Single(cut.FindAll("img"));
    }

    [Theory]
    [InlineData(WConfidentiality.Public, "عام")]
    [InlineData(WConfidentiality.Private, "خاص")]
    [InlineData(WConfidentiality.Secret, "سري")]
    [InlineData(WConfidentiality.TopSecret, "سري للغاية")]
    public void WConfidentialityRow_Renders_ArabicLabel(WConfidentiality level, string expectedLabel)
    {
        var cut = Render<WConfidentialityRow>(p => p.Add(x => x.Level, level));

        Assert.Contains(expectedLabel, cut.Markup);
    }

    [Fact]
    public void Kbd_RendersTextInsideBdi()
    {
        var cut = Render<Kbd>(p => p.Add(x => x.Text, "Ctrl+F"));

        Assert.Equal("Ctrl+F", cut.Find("bdi").TextContent);
    }

    [Fact]
    public void WIcon_DegradesToEmptyGlyph_ForUnknownIconName()
    {
        // A missing icon name must never tear down the render tree in a shipped build (AGREEMENT
        // item 15: no unlocalized technical errors in the UI) — it renders an empty <svg> instead of
        // throwing. Debug.Fail still records the mistake (see WIcon.razor), which is why this test
        // must run outside DEBUG-assert-sensitive hosts; here it is enough that rendering succeeds.
        var cut = Render<WIcon>(p => p.Add(x => x.Name, "not-a-real-icon"));

        var svg = cut.Find("svg");
        Assert.Empty(svg.Children);
    }

    [Fact]
    public void WIcon_MirrorsDirectionalIcon_ByDefault()
    {
        var cut = Render<WIcon>(p => p.Add(x => x.Name, "chevron-left"));

        Assert.Contains("w-icon--mirror", cut.Find("svg").ClassList);
    }

    [Fact]
    public void WIcon_DoesNotMirror_NonDirectionalIcon()
    {
        var cut = Render<WIcon>(p => p.Add(x => x.Name, "search"));

        Assert.DoesNotContain("w-icon--mirror", cut.Find("svg").ClassList);
    }
}
