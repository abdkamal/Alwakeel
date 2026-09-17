using Microsoft.AspNetCore.Components;

namespace Wakeel.UI.Services;

/// <summary>
/// Lets a routed page publish its title, subtitle, header actions, and active sidebar key up to
/// Layout/MainLayout.razor's fixed page-header row (Actions on the left, Titles on the right, per
/// DESIGN-GUIDE.md's "بنية الشاشة القياسية"). A <see cref="LayoutComponentBase"/> only ever
/// receives <c>@Body</c> from the page it wraps, so pages and the layout share this scoped service
/// instead to move that data upward.
/// </summary>
public sealed class PageHeaderState
{
    /// <summary>Current page title, or null while no page has published one yet (e.g. before first render).</summary>
    public string? Title { get; private set; }

    /// <summary>Muted subtitle line under the title.</summary>
    public string? Sub { get; private set; }

    /// <summary>Header action buttons/content, rendered on the opposite side from the titles.</summary>
    public RenderFragment? Actions { get; private set; }

    /// <summary>The sidebar item key (see WSidebar.Keys) the current page corresponds to, or null to leave the previous selection.</summary>
    public string? NavKey { get; private set; }

    /// <summary>Raised whenever any published value changes, so MainLayout can re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whoever published what is on screen now. Routing initialises the incoming page BEFORE it
    /// disposes the outgoing one, so an unconditional Clear() in the outgoing page's Dispose wipes
    /// the header the new page has just set and the title row disappears for the rest of that
    /// screen's life. Remembering the owner is what makes that clear a no-op.
    /// </summary>
    private object? _owner;

    /// <summary>Called by a page (typically in OnInitialized/OnParametersSet) to publish its header content.</summary>
    /// <param name="owner">
    /// The publishing component, so its own <see cref="Clear(object)"/> can tell whether the header
    /// still belongs to it. Optional; a caller that passes nothing keeps the previous behaviour.
    /// </param>
    public void Set(
        string? title,
        string? sub = null,
        RenderFragment? actions = null,
        string? navKey = null,
        object? owner = null)
    {
        Title = title;
        Sub = sub;
        Actions = actions;
        NavKey = navKey;
        _owner = owner;
        Changed?.Invoke();
    }

    /// <summary>Resets to the empty state (no title row rendered).</summary>
    public void Clear() => Set(null);

    /// <summary>
    /// Resets the header only while it is still the one <paramref name="owner"/> published. A page
    /// that has already been replaced clears nothing, so the screen the person is looking at keeps
    /// its title.
    /// </summary>
    public void Clear(object owner)
    {
        if (ReferenceEquals(_owner, owner))
        {
            Set(null);
        }
    }
}
