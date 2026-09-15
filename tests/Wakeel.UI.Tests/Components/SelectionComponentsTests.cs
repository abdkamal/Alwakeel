using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

public class SelectionComponentsTests : WakeelTestContext
{
    [Fact]
    public void WSelect_Renders_AllOptions()
    {
        var cut = Render<WSelect>(p => p.Add(x => x.Options, new List<(string, string)>
        {
            ("planning", "مكتب مدير دائرة التخطيط"),
            ("legal", "الإدارة القانونية"),
        }));

        Assert.Equal(2, cut.FindAll("option").Count);
    }

    [Fact]
    public void WSelect_Change_RaisesValueChanged()
    {
        string? selected = null;
        var cut = Render<WSelect>(p => p
            .Add(x => x.Options, new List<(string, string)> { ("a", "أ"), ("b", "ب") })
            .Add(x => x.ValueChanged, (string? v) => selected = v));

        cut.Find("select").Change("b");

        Assert.Equal("b", selected);
    }

    [Fact]
    public void WCheckbox_Toggle_RaisesValueChanged()
    {
        bool? newValue = null;
        var cut = Render<WCheckbox>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.ValueChanged, (bool v) => newValue = v));

        cut.Find("input").Change(true);

        Assert.True(newValue);
    }

    [Fact]
    public void WRadio_Select_RaisesGroupValue()
    {
        string? selected = null;
        var cut = Render<WRadio>(p => p
            .Add(x => x.GroupName, "g1")
            .Add(x => x.Value, "b")
            .Add(x => x.SelectedValueChanged, (string v) => selected = v));

        cut.Find("input").Change(true);

        Assert.Equal("b", selected);
    }

    [Fact]
    public void WToggle_Switch_RaisesValueChanged()
    {
        bool? newValue = null;
        var cut = Render<WToggle>(p => p
            .Add(x => x.Value, false)
            .Add(x => x.ValueChanged, (bool v) => newValue = v));

        cut.Find("input").Change(true);

        Assert.True(newValue);
    }

    [Fact]
    public void WSegmented_Click_SelectsOption()
    {
        string? selected = null;
        var cut = Render<WSegmented>(p => p
            .Add(x => x.Options, new List<(string, string)> { ("day", "يوم"), ("week", "أسبوع") })
            .Add(x => x.SelectedValueChanged, (string v) => selected = v));

        cut.FindAll("button")[1].Click();

        Assert.Equal("week", selected);
    }

    [Fact]
    public void WSearch_Input_RaisesValueChanged_AndShowsKbdHint()
    {
        string? value = null;
        var cut = Render<WSearch>(p => p
            .Add(x => x.BindGlobalShortcut, false)
            .Add(x => x.ValueChanged, (string? v) => value = v));

        Assert.Contains("Ctrl+F", cut.Markup);

        cut.Find("input").Input("مراسلة");

        Assert.Equal("مراسلة", value);
    }
}
