using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Wakeel.Design.Components;
using Wakeel.Design.Text;

namespace Wakeel.UI.Tests.Components;

/// <summary>
/// Covers the shared hierarchy component: the row anatomy, expansion, selection, search (with the
/// ancestors of a match kept and the match highlighted), the keyboard, and the bidi isolation of a
/// mixed Arabic/Latin name (AGREEMENT item 55).
/// </summary>
public class WTreeTests : WakeelTestContext
{
    /// <summary>One node of the fixture hierarchy: هيئة → دائرة → قسم → وحدة.</summary>
    private sealed record OrgNode(string Key, string Name, string Level)
    {
        public string? Head { get; init; }
        public IReadOnlyList<OrgNode> Children { get; init; } = Array.Empty<OrgNode>();
    }

    private static readonly IReadOnlyList<OrgNode> Roots = new List<OrgNode>
    {
        new("authority", "هيئة تنمية المناطق الريفية", "هيئة")
        {
            Head = "د. سامي العبد الله",
            Children = new List<OrgNode>
            {
                new("planning", "دائرة التخطيط", "دائرة")
                {
                    Head = "أ. ليلى الحسن",
                    Children = new List<OrgNode>
                    {
                        new("strategic", "قسم التخطيط الاستراتيجي", "قسم")
                        {
                            Head = "أحمد الخطيب",
                            Children = new List<OrgNode>
                            {
                                new("studies", "وحدة الدراسات", "وحدة"),
                                new("archive", "وحدة الأرشيف Archive", "وحدة"),
                            },
                        },
                    },
                },
                new("finance", "الدائرة المالية", "دائرة") { Head = "رانيا القاسم" },
            },
        },
    };

    private static IRenderedComponent<WTree<OrgNode>> RenderOrgTree(
        BunitContext context,
        bool expandAll = true,
        string? search = null,
        Action<Bunit.ComponentParameterCollectionBuilder<WTree<OrgNode>>>? extra = null) =>
        context.Render<WTree<OrgNode>>(p =>
        {
            p.Add(x => x.Items, Roots);
            p.Add(x => x.ChildrenOf, node => node.Children);
            p.Add(x => x.KeyOf, node => node.Key);
            p.Add(x => x.RowOf, node => new WTreeRow(node.Name)
            {
                Icon = "folder-open",
                LevelLabel = node.Level,
                Meta = node.Head,
            });
            p.Add(x => x.DefaultExpandAll, expandAll);
            p.Add(x => x.Search, search);
            extra?.Invoke(p);
        });

    [Fact]
    public void Renders_Tree_TreeItem_And_Group_Roles()
    {
        var cut = RenderOrgTree(this);

        Assert.Single(cut.FindAll("[role=tree]"));
        Assert.Equal(6, cut.FindAll("[role=treeitem]").Count);
        Assert.NotEmpty(cut.FindAll("[role=group]"));
        Assert.Equal("1", cut.FindAll("[role=treeitem]")[0].GetAttribute("aria-level"));
        Assert.Contains("هيئة تنمية المناطق الريفية", cut.Markup);
    }

    [Fact]
    public void Collapsed_Tree_Shows_Roots_Only()
    {
        var cut = RenderOrgTree(this, expandAll: false);

        Assert.Single(cut.FindAll("[role=treeitem]"));
        Assert.Equal("false", cut.Find("[role=treeitem]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Caret_Click_Expands_And_Collapses_The_Branch()
    {
        var cut = RenderOrgTree(this, expandAll: false);

        cut.Find("button.w-tree-caret").Click();
        Assert.Equal(3, cut.FindAll("[role=treeitem]").Count);
        Assert.Equal("true", cut.Find("[role=treeitem]").GetAttribute("aria-expanded"));

        cut.Find("button.w-tree-caret").Click();
        Assert.Single(cut.FindAll("[role=treeitem]"));
    }

    [Fact]
    public void ExpandAll_And_CollapseAll_Open_And_Close_Every_Branch()
    {
        var cut = RenderOrgTree(this, expandAll: false);

        cut.InvokeAsync(() => cut.Instance.ExpandAll());
        Assert.Equal(6, cut.FindAll("[role=treeitem]").Count);

        cut.InvokeAsync(() => cut.Instance.CollapseAll());
        Assert.Single(cut.FindAll("[role=treeitem]"));
    }

    [Fact]
    public void Clicking_A_Row_Raises_SelectedChanged_And_Marks_The_Row()
    {
        string? selected = null;
        var cut = RenderOrgTree(this, extra: p => p.Add(x => x.SelectedChanged, key => selected = key));

        cut.FindAll("div.w-tree-row")[1].Click();

        Assert.Equal("planning", selected);
        Assert.Contains("w-tree-row--selected", cut.FindAll("div.w-tree-row")[1].ClassList);
        Assert.Equal("true", cut.FindAll("[role=treeitem]")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Search_Keeps_Ancestors_Of_A_Match_And_Highlights_It()
    {
        var cut = RenderOrgTree(this, expandAll: false, search: "الدراسات");

        var names = cut.FindAll("bdi.w-tree-name").Select(n => n.TextContent).ToList();

        // The matching unit plus every ancestor above it; the unrelated branches are gone.
        Assert.Equal(4, names.Count);
        Assert.Contains(names, n => n.Contains("هيئة تنمية المناطق الريفية", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("دائرة التخطيط", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("وحدة الدراسات", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("الدائرة المالية", StringComparison.Ordinal));

        var mark = cut.Find("mark.w-tree-match");
        Assert.Contains("الدراسات", mark.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_Also_Matches_The_Trailing_Meta_Text()
    {
        var cut = RenderOrgTree(this, expandAll: false, search: "رانيا");

        var names = cut.FindAll("bdi.w-tree-name").Select(n => n.TextContent).ToList();

        Assert.Equal(2, names.Count);
        Assert.Contains(names, n => n.Contains("الدائرة المالية", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_Without_A_Match_Shows_The_Arabic_Empty_Text()
    {
        var cut = RenderOrgTree(this, search: "مكتب البريد");

        Assert.Empty(cut.FindAll("[role=tree]"));
        Assert.Equal(Ar.Tree.NoMatch, cut.Find("p.w-tree-empty").TextContent.Trim());
    }

    [Fact]
    public void Empty_Tree_Shows_The_No_Items_Text()
    {
        var cut = Render<WTree<OrgNode>>(p => p
            .Add(x => x.Items, Array.Empty<OrgNode>())
            .Add(x => x.KeyOf, node => node.Key));

        Assert.Equal(Ar.Tree.Empty, cut.Find("p.w-tree-empty").TextContent.Trim());
    }

    [Fact]
    public void Keyboard_Moves_The_Active_Row_And_Enter_Selects_It()
    {
        string? selected = null;
        var cut = RenderOrgTree(this, extra: p => p.Add(x => x.SelectedChanged, key => selected = key));

        var tree = cut.Find("ul.w-tree");
        tree.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        tree.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        tree.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("strategic", selected);

        cut.Find("ul.w-tree").KeyDown(new KeyboardEventArgs { Key = "Home" });
        cut.Find("ul.w-tree").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("authority", selected);

        cut.Find("ul.w-tree").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("ul.w-tree").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("finance", selected);
    }

    [Fact]
    public void ArrowLeft_Opens_And_ArrowRight_Closes_In_The_RightToLeft_Layout()
    {
        var cut = RenderOrgTree(this, expandAll: false);

        var tree = cut.Find("ul.w-tree");
        tree.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal(3, cut.FindAll("[role=treeitem]").Count);

        cut.Find("ul.w-tree").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Single(cut.FindAll("[role=treeitem]"));
    }

    [Fact]
    public void Mixed_Arabic_And_Latin_Name_Is_Bidi_Isolated()
    {
        var cut = RenderOrgTree(this);

        var archive = cut.FindAll("bdi.w-tree-name").Single(n => n.TextContent.Contains("Archive", StringComparison.Ordinal));

        Assert.Contains('⁦', archive.TextContent);
        Assert.Contains('⁩', archive.TextContent);
    }

    /// <summary>
    /// A search match that falls INSIDE a Latin word must not split it into two isolates: the halves
    /// are reordered against each other under the RTL paragraph direction and «Archive» would read
    /// «iveArch» (AGREEMENT item 55). The highlight widens to the whole token instead.
    /// </summary>
    [Fact]
    public void Partial_Latin_Match_Keeps_The_Whole_Token_In_One_Isolate()
    {
        var cut = RenderOrgTree(this, search: "Arch");

        var archive = cut.FindAll("bdi.w-tree-name").Single(n => n.TextContent.Contains("Archive", StringComparison.Ordinal));

        Assert.Equal(1, archive.TextContent.Count(c => c == '⁦'));
        Assert.Equal(1, archive.TextContent.Count(c => c == '⁩'));
        Assert.Contains("⁦Archive⁩", archive.TextContent, StringComparison.Ordinal);
        Assert.Equal("Archive", cut.Find("mark.w-tree-match").TextContent.Trim('⁦', '⁩', '‏'));
    }

    /// <summary>
    /// With a custom Row and no SearchTextOf the tree has nothing visible to match on, so it must
    /// keep every node instead of silently filtering on the invisible node key.
    /// </summary>
    [Fact]
    public void Custom_Row_Without_Search_Text_Keeps_Every_Node()
    {
        var cut = Render<WTree<OrgNode>>(p =>
        {
            p.Add(x => x.Items, Roots);
            p.Add(x => x.ChildrenOf, node => node.Children);
            p.Add(x => x.KeyOf, node => node.Key);
            p.Add(x => x.Row, node => builder => builder.AddContent(0, node.Name));
            p.Add(x => x.DefaultExpandAll, true);
            p.Add(x => x.Search, "authority");
        });

        Assert.True(cut.FindAll("[role=treeitem]").Count > 1);
    }

    /// <summary>The same custom row filters correctly once the caller says what the operator reads.</summary>
    [Fact]
    public void Custom_Row_Filters_On_Search_Text_Of()
    {
        var cut = Render<WTree<OrgNode>>(p =>
        {
            p.Add(x => x.Items, Roots);
            p.Add(x => x.ChildrenOf, node => node.Children);
            p.Add(x => x.KeyOf, node => node.Key);
            p.Add(x => x.Row, node => builder => builder.AddContent(0, node.Name));
            p.Add(x => x.SearchTextOf, node => node.Name);
            p.Add(x => x.DefaultExpandAll, true);
            p.Add(x => x.Search, "وحدة الدراسات");
        });

        // The match plus its three ancestors, and nothing else.
        Assert.Equal(4, cut.FindAll("[role=treeitem]").Count);
    }

    /// <summary>Items arriving after the first render must still honour DefaultExpandAll.</summary>
    [Fact]
    public void Default_Expand_All_Applies_To_A_Hierarchy_That_Arrives_Late()
    {
        var cut = Render<WTree<OrgNode>>(p =>
        {
            p.Add(x => x.Items, Array.Empty<OrgNode>());
            p.Add(x => x.ChildrenOf, node => node.Children);
            p.Add(x => x.KeyOf, node => node.Key);
            p.Add(x => x.RowOf, node => new WTreeRow(node.Name) { LevelLabel = node.Level });
            p.Add(x => x.DefaultExpandAll, true);
        });

        cut.Render(p => p.Add(x => x.Items, Roots));

        Assert.True(cut.FindAll("[role=treeitem]").Count > Roots.Count);
    }

    /// <summary>
    /// Selecting the row that needs attention must keep both signals: the selection class and the
    /// attention class live on the same row, and the CSS gives the selection tint precedence.
    /// </summary>
    [Fact]
    public void Selected_Attention_Row_Keeps_Both_States()
    {
        var cut = Render<WTree<OrgNode>>(p => p
            .Add(x => x.Items, Roots)
            .Add(x => x.ChildrenOf, node => node.Children)
            .Add(x => x.KeyOf, node => node.Key)
            .Add(x => x.DefaultExpandAll, true)
            .Add(x => x.Selected, "studies")
            .Add(x => x.RowOf, node => new WTreeRow(node.Name)
            {
                LevelLabel = node.Level,
                StateLabel = node.Key == "studies" ? Ar.Tree.Demo.NoHead : null,
                Attention = node.Key == "studies",
            }));

        var row = cut.FindAll("div.w-tree-row--selected").Single();

        Assert.Contains("w-tree-row--attention", row.ClassList);
        Assert.Contains(Ar.Tree.Demo.NoHead, cut.Find("span.w-tree-state--warning").TextContent);
    }

    [Fact]
    public void Level_Chip_Colour_Follows_The_Depth()
    {
        var cut = RenderOrgTree(this);

        var chips = cut.FindAll("span.w-chip").ToList();

        Assert.Contains("w-chip--primary", chips[0].ClassList);
        Assert.Contains("w-chip--info", chips[1].ClassList);
        Assert.Contains("w-chip--success", chips[2].ClassList);
        Assert.Contains("w-chip--neutral", chips[3].ClassList);
    }

    [Fact]
    public void Row_States_Render_The_Attention_Tint_The_Dim_And_The_Trailing_State_Chip()
    {
        var cut = Render<WTree<OrgNode>>(p => p
            .Add(x => x.Items, Roots)
            .Add(x => x.ChildrenOf, node => node.Children)
            .Add(x => x.KeyOf, node => node.Key)
            .Add(x => x.DefaultExpandAll, true)
            .Add(x => x.Movable, true)
            .Add(x => x.RowOf, node => new WTreeRow(node.Name)
            {
                LevelLabel = node.Level,
                StateLabel = node.Key == "studies" ? Ar.Tree.Demo.NoHead : null,
                StateIcon = node.Key == "studies" ? "alert-triangle" : null,
                Attention = node.Key == "studies",
                Dimmed = node.Key == "archive",
                Badge = node.Key == "planning" ? 3 : null,
            }));

        Assert.Single(cut.FindAll("div.w-tree-row--attention"));
        Assert.Single(cut.FindAll("div.w-tree-row--dimmed"));
        Assert.Contains(Ar.Tree.Demo.NoHead, cut.Find("span.w-tree-state--warning").TextContent);
        Assert.Equal(6, cut.FindAll("span.w-tree-handle").Count);
        Assert.Contains("3", cut.Find("bdi.w-badge").TextContent);
    }

    [Fact]
    public void Toolbar_Shows_Title_Count_And_The_Expand_Controls()
    {
        var expanded = 0;
        var collapsed = 0;

        var cut = Render<WTreeToolbar>(p => p
            .Add(x => x.Title, Ar.Tree.Demo.Title)
            .Add(x => x.Count, 11)
            .Add(x => x.BindGlobalShortcut, false)
            .Add(x => x.OnExpandAll, () => expanded++)
            .Add(x => x.OnCollapseAll, () => collapsed++));

        Assert.Contains(Ar.Tree.Demo.Title, cut.Markup);
        Assert.Contains("11", cut.Find("bdi.w-badge").TextContent);

        // The shortcut hint follows the binding: an unbound box must not advertise a shortcut.
        Assert.DoesNotContain(Ar.LocalSearch.KbdHint, cut.Markup, StringComparison.Ordinal);

        var buttons = cut.FindAll("button").Where(b => b.TextContent.Contains("الكل", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, buttons.Count);
        buttons[0].Click();
        buttons[1].Click();

        Assert.Equal(1, expanded);
        Assert.Equal(1, collapsed);
    }

    /// <summary>A toolbar whose box IS bound to the shortcut still shows the hint chip.</summary>
    [Fact]
    public void Toolbar_Shows_The_Shortcut_Hint_When_The_Shortcut_Is_Bound()
    {
        var cut = Render<WTreeToolbar>(p => p
            .Add(x => x.Title, Ar.Tree.Demo.Title)
            .Add(x => x.BindGlobalShortcut, true));

        Assert.Contains(Ar.LocalSearch.KbdHint, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Indentation_Is_Expressed_Through_One_Css_Custom_Property()
    {
        var cut = RenderOrgTree(this);

        var rows = cut.FindAll("div.w-tree-row");

        Assert.Contains("--w-tree-depth: 0", rows[0].GetAttribute("style"));
        Assert.Contains("--w-tree-depth: 1", rows[1].GetAttribute("style"));
        Assert.Contains("--w-tree-depth: 2", rows[2].GetAttribute("style"));
        Assert.Contains("--w-tree-depth: 3", rows[3].GetAttribute("style"));
    }

    /// <summary>
    /// The pane in the design gallery is deliberately not bound to the page-wide shortcut, yet it
    /// still has to show the hint chip the A05 tree pane shows. That is what the opt-in is for.
    /// </summary>
    [Fact]
    public void Toolbar_Shows_The_Shortcut_Hint_When_It_Is_Asked_For_Without_The_Binding()
    {
        var cut = Render<WTreeToolbar>(p => p
            .Add(x => x.Title, Ar.Tree.Demo.Title)
            .Add(x => x.BindGlobalShortcut, false)
            .Add(x => x.ShowShortcutHint, true));

        Assert.Contains(Ar.LocalSearch.KbdHint, cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>A pane title carrying a Latin or numeric token must be isolated too (item 55).</summary>
    [Fact]
    public void Toolbar_Title_Is_Bidi_Isolated()
    {
        var cut = Render<WTreeToolbar>(p => p
            .Add(x => x.Title, "شجرة الهيكل Archive 2026")
            .Add(x => x.BindGlobalShortcut, false));

        var title = cut.Find("bdi.w-tree-toolbar-text");

        // The Latin run and the digits that follow it are one token, so they share a single isolate.
        Assert.Contains("⁦Archive 2026⁩", title.TextContent, StringComparison.Ordinal);
        Assert.Equal(1, title.TextContent.Count(c => c == '⁦'));
    }

    /// <summary>
    /// While a search is active the non-matching children are gone, so a branch that matched on its
    /// own name must lose its caret instead of offering a control that opens nothing.
    /// </summary>
    [Fact]
    public void Search_Leaves_No_Caret_On_A_Branch_Whose_Children_Were_Filtered_Out()
    {
        var cut = RenderOrgTree(this, search: "الاستراتيجي");

        var items = cut.FindAll("[role=treeitem]");

        Assert.Equal(3, items.Count);
        Assert.Null(items[2].GetAttribute("aria-expanded"));
        Assert.Equal(2, cut.FindAll("button.w-tree-caret").Count);
    }

    /// <summary>The guidance line of the A05 pane sits inside the pane, under the last row.</summary>
    [Fact]
    public void Hint_Is_Printed_Under_The_Last_Row()
    {
        var cut = RenderOrgTree(this, extra: p => p.Add(x => x.Hint, Ar.Tree.Demo.Hint));

        var hint = cut.Find("p.w-tree-hint");

        Assert.Contains("مقبض الترتيب", hint.TextContent, StringComparison.Ordinal);
        Assert.True(
            cut.Markup.IndexOf("w-tree-hint", StringComparison.Ordinal)
                > cut.Markup.IndexOf("</ul>", StringComparison.Ordinal),
            "The hint has to follow the last row, not precede the tree.");
    }
}
