using Microsoft.AspNetCore.Components;

namespace Wakeel.Design.Components;

/// <summary>Side the tooltip bubble opens toward relative to its anchor.</summary>
public enum TooltipPlacement
{
    Top,
    Bottom,
}

/// <summary>Visual style of a WButton.</summary>
public enum WButtonVariant
{
    Primary,
    Secondary,
    Danger,
    Text,
    Icon,
    Split,
}

/// <summary>Where a WButton's icon sits relative to its label. Leading renders first in markup, which
/// under the app's permanent RTL layout places it on the right of the label (reading-order-first);
/// Trailing renders after the label, placing it on the left.</summary>
public enum WIconPosition
{
    Leading,
    Trailing,
}

/// <summary>Semantic color/state of a WChip, mirroring the correspondence-status palette in DESIGN-GUIDE.md.</summary>
public enum WChipVariant
{
    Info,
    Primary,
    Warning,
    Success,
    Neutral,
    Danger,
}

/// <summary>Semantic variant of a WCard/WToast.</summary>
public enum WSemanticVariant
{
    Neutral,
    Info,
    Warning,
    Danger,
    Success,
}

/// <summary>Confidentiality level shown by WConfidentialityRow (DATA-MODEL correspondence.confidentiality).</summary>
public enum WConfidentiality
{
    Public,
    Private,
    Secret,
    TopSecret,
}

/// <summary>Where a WBadge is placed, which drives its size (sidebar item, tab, or group header).</summary>
public enum WBadgeKind
{
    Default,
    Tab,
    Group,
}

/// <summary>Input control kind rendered by WInput.</summary>
public enum WInputType
{
    Text,
    Password,
    Number,
    Date,
    Textarea,
}

/// <summary>One crumb in a WBreadcrumb trail.</summary>
/// <param name="Label">Arabic label shown for the crumb.</param>
/// <param name="OnClick">Raised when a non-final crumb is clicked; the final crumb ignores this and renders as plain text.</param>
public readonly record struct WBreadcrumbItem(string Label, EventCallback OnClick = default);

/// <summary>One actionable entry in a WMenu popover.</summary>
/// <param name="Label">Arabic label.</param>
/// <param name="Icon">Lucide icon name shown before the label.</param>
/// <param name="OnClick">Raised when the item is chosen.</param>
/// <param name="Danger">Renders the item in the danger color (e.g. permanent delete).</param>
public readonly record struct WMenuItem(string Label, string Icon, EventCallback OnClick, bool Danger = false);

/// <summary>One navigation entry in the sidebar (Nav.Item in DESIGN-GUIDE.md).</summary>
/// <param name="Key">Stable identifier used for selection comparison and the persisted collapse-state store key.</param>
/// <param name="Label">Arabic label.</param>
/// <param name="Icon">Lucide icon name.</param>
public sealed record WSidebarItem(string Key, string Label, string Icon);

/// <summary>One collapsible group of sidebar items (Nav.Group in DESIGN-GUIDE.md).</summary>
/// <param name="Key">Stable identifier used as the persisted collapse-state store key.</param>
/// <param name="Label">Arabic group title.</param>
/// <param name="Items">Ordered items belonging to this group.</param>
public sealed record WSidebarGroup(string Key, string Label, IReadOnlyList<WSidebarItem> Items);

/// <summary>Which style a WDialog's confirm button uses.</summary>
public enum WDialogKind
{
    /// <summary>Confirm renders as WButtonVariant.Primary.</summary>
    Normal,

    /// <summary>Confirm renders as WButtonVariant.Danger (irreversible actions: permanent delete, etc.).</summary>
    Danger,
}

/// <summary>Live state of the autosave-draft cycle shown by WAutosaveIndicator (ARCHITECTURE.md §10).</summary>
public enum WAutosaveState
{
    Saved,
    Saving,
    Failed,
}

/// <summary>One toast notification queued through IToastService.</summary>
/// <param name="Id">Unique identifier used to remove this toast from the host's active list.</param>
/// <param name="Text">Arabic message text.</param>
/// <param name="Variant">Semantic color/icon.</param>
public sealed record WToastMessage(Guid Id, string Text, WSemanticVariant Variant);

/// <summary>
/// Display data for one row of a <c>WTree</c>, produced by its <c>RowOf</c> delegate. The tree is
/// generic over the caller's own item type; this record is the single place where that item is
/// translated into the row anatomy the design system draws (icon, name, level chip, meta text,
/// state chip, badge), so every hierarchy screen — the organisation structure, unit pickers,
/// document folders — renders with identical spacing and states.
/// </summary>
/// <param name="Name">The node's Arabic name, shown isolated (bidi) and highlighted on a search match.</param>
public sealed record WTreeRow(string Name)
{
    /// <summary>Lucide icon name drawn before the name (e.g. "landmark", "building", "folder-open").</summary>
    public string? Icon { get; init; }

    /// <summary>Level chip label (e.g. «دائرة»). When null the chip is omitted.</summary>
    public string? LevelLabel { get; init; }

    /// <summary>Level chip colour. When null the tree derives it from the node's depth.</summary>
    public WChipVariant? LevelVariant { get; init; }

    /// <summary>Trailing meta text on the far side of the row (e.g. the head's name).</summary>
    public string? Meta { get; init; }

    /// <summary>Trailing state text (e.g. «بلا رئيس معيّن»). When null no state chip is drawn.</summary>
    public string? StateLabel { get; init; }

    /// <summary>Colour of the state chip; warning by default.</summary>
    public WChipVariant StateVariant { get; init; } = WChipVariant.Warning;

    /// <summary>Lucide icon shown inside the state chip (e.g. "alert-triangle").</summary>
    public string? StateIcon { get; init; }

    /// <summary>Optional trailing count badge (e.g. the number of units under this branch).</summary>
    public int? Badge { get; init; }

    /// <summary>Tints the whole row with the attention colour (the state that needs the operator's eye).</summary>
    public bool Attention { get; init; }

    /// <summary>Dims the row (an out-of-service node that is still part of the hierarchy).</summary>
    public bool Dimmed { get; init; }
}
