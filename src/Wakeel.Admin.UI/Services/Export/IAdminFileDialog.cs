namespace Wakeel.Admin.UI.Services.Export;

/// <summary>What came back from a file chooser.</summary>
/// <param name="Chosen">Whether a place was chosen at all.</param>
/// <param name="Path">Where, when one was.</param>
public readonly record struct AdminFileChoice(bool Chosen, string? Path)
{
    /// <summary>Nothing was chosen.</summary>
    public static AdminFileChoice None { get; } = new(false, null);
}

/// <summary>
/// The two questions the screens have to ask the window around them about files: where to put a copy
/// of something the tool has made, and which file on this computer to open.
/// </summary>
/// <remarks>
/// The tool always writes its own copy into its own folder first, so a chooser that is refused or
/// dismissed never costs anything: the file is already safe and the person is only deciding where a
/// second copy goes. A host with no windowing at all — the test host — answers «nothing was chosen»
/// to both, and every screen carries on saying so in words.
/// </remarks>
public interface IAdminFileDialog
{
    /// <summary>Whether this host can put a chooser on the screen at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Asks where to put a copy of a file the tool has made.</summary>
    /// <param name="suggestedFileName">The name to offer.</param>
    /// <param name="filterLabel">What the kind of file is called, in Arabic.</param>
    /// <param name="extension">Its extension, with the dot.</param>
    AdminFileChoice AskWhereToSave(string suggestedFileName, string filterLabel, string extension);

    /// <summary>Asks which file to open.</summary>
    /// <param name="filterLabel">What the kind of file is called, in Arabic.</param>
    /// <param name="extensions">The extensions to offer, each with its dot.</param>
    AdminFileChoice AskWhichFile(string filterLabel, IReadOnlyList<string> extensions);

    /// <summary>Asks which folder to open.</summary>
    /// <param name="prompt">The one line the chooser shows, in Arabic.</param>
    AdminFileChoice AskWhichFolder(string prompt);

    /// <summary>Shows a folder of this computer in its own file window.</summary>
    /// <param name="path">A file inside the folder, or the folder itself.</param>
    void Reveal(string path);
}

/// <summary>The chooser for hosts that have no windows: nothing is chosen and the screen says so.</summary>
public sealed class NoAdminFileDialog : IAdminFileDialog
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public AdminFileChoice AskWhereToSave(string suggestedFileName, string filterLabel, string extension) =>
        AdminFileChoice.None;

    /// <inheritdoc />
    public AdminFileChoice AskWhichFile(string filterLabel, IReadOnlyList<string> extensions) =>
        AdminFileChoice.None;

    /// <inheritdoc />
    public AdminFileChoice AskWhichFolder(string prompt) => AdminFileChoice.None;

    /// <inheritdoc />
    public void Reveal(string path)
    {
        // Nothing to show: a test host has no file window.
    }
}
