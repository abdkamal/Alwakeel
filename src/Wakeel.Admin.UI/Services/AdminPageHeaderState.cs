using Microsoft.AspNetCore.Components;

namespace Wakeel.Admin.UI.Services;

/// <summary>
/// What the shell's page header shows for whichever screen is open: its title, the line under it,
/// the buttons on the far side, and which tab is lit.
/// </summary>
/// <remarks>
/// The header is drawn once, by the layout, rather than by each screen — the design puts it in a
/// fixed place with fixed spacing, and a screen that drew its own would drift. Each screen sets
/// this in <c>OnInitialized</c> and the layout redraws.
/// </remarks>
public sealed class AdminPageHeaderState
{
    /// <summary>Raised when a screen changes what the header should say.</summary>
    public event Action? Changed;

    /// <summary>The screen's title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The line under the title.</summary>
    public string? Sub { get; private set; }

    /// <summary>The buttons on the far side of the header.</summary>
    public RenderFragment? Actions { get; private set; }

    /// <summary>Which tab of the strip is lit, or null on a screen that belongs to none.</summary>
    public string? ActiveTab { get; private set; }

    /// <summary>Sets everything the header shows for the screen that is opening.</summary>
    public void Set(string title, string? sub = null, string? activeTab = null, RenderFragment? actions = null)
    {
        Title = title;
        Sub = sub;
        ActiveTab = activeTab;
        Actions = actions;
        Changed?.Invoke();
    }

    /// <summary>Clears the header, for a screen that draws its own (A01 and A02).</summary>
    public void Clear() => Set(string.Empty);
}
