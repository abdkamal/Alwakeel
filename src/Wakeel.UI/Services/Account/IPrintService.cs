namespace Wakeel.UI.Services.Account;

/// <summary>How saving the recovery sheet as a file ended.</summary>
public enum PrintOutcome
{
    /// <summary>The file was written where the person asked for it.</summary>
    Saved,

    /// <summary>The person closed the save dialog without choosing anywhere.</summary>
    Cancelled,

    /// <summary>The host cannot print or save at all (a test host, for instance).</summary>
    Unavailable,

    /// <summary>Somewhere between the two: a folder that refused to be written to, a full disk.</summary>
    Failed,
}

/// <summary>What a save produced.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Path">Where the file landed, when one was written.</param>
public readonly record struct PrintResult(PrintOutcome Outcome, string? Path = null)
{
    public bool Saved => Outcome == PrintOutcome.Saved;
}

/// <summary>
/// Printing and saving what is on screen. The recovery sheet (W04 and W07) is the only thing in this
/// package that uses it, and it is printed from the page itself — the print stylesheet hides
/// everything except the sheet — so no second rendering of it can ever drift from the one the person
/// just read (ARCHITECTURE.md §9: internal sheets are produced from HTML, without Word).
/// </summary>
public interface IPrintService
{
    /// <summary>Whether this host can print at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens the machine's print dialog for the current page.</summary>
    Task<PrintOutcome> PrintAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks where to put a PDF of the current page, and writes it there.</summary>
    Task<PrintResult> SaveAsPdfAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}

/// <summary>The printer for hosts that have none: nothing is printed and the screen says so.</summary>
public sealed class NoPrintService : IPrintService
{
    public bool IsAvailable => false;

    public Task<PrintOutcome> PrintAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PrintOutcome.Unavailable);

    public Task<PrintResult> SaveAsPdfAsync(string suggestedFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrintResult(PrintOutcome.Unavailable));
}
