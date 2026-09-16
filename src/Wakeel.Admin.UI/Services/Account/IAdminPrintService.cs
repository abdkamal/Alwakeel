namespace Wakeel.Admin.UI.Services.Account;

/// <summary>How printing or saving what is on screen ended.</summary>
public enum AdminPrintOutcome
{
    /// <summary>The file was written where the person asked for it.</summary>
    Saved,

    /// <summary>
    /// This computer's print window was put on screen. The engine returns the moment it opens and
    /// never says what the person then chose, so this is the most the tool can honestly report
    /// about printing — it is not a claim that anything came out of a printer.
    /// </summary>
    DialogOpened,

    /// <summary>The person closed the dialog without choosing anything.</summary>
    Cancelled,

    /// <summary>This host cannot print or save at all (a test host, for instance).</summary>
    Unavailable,

    /// <summary>Somewhere in between: a folder that refused to be written to, a full disk.</summary>
    Failed,
}

/// <summary>What a save produced.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Path">Where the file landed, when one was written.</param>
public readonly record struct AdminPrintResult(AdminPrintOutcome Outcome, string? Path = null)
{
    /// <summary>Whether something actually came out of it.</summary>
    public bool Saved => Outcome == AdminPrintOutcome.Saved;
}

/// <summary>
/// Printing and saving the page that is on screen. The recovery sheet of A01 is the only thing in
/// this sub-package that uses it, and it is printed from the page itself — the print stylesheet
/// hides everything except the sheet — so what comes out of the printer is exactly what the person
/// just read and ticked «طبعتها وحفظتها» for (ARCHITECTURE.md §9: internal sheets come from HTML,
/// not from Word).
/// </summary>
public interface IAdminPrintService
{
    /// <summary>Whether this host can print at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens this computer's print window for the current page. The most it can report is
    /// <see cref="AdminPrintOutcome.DialogOpened"/>: what the person chose there is never told back,
    /// so no screen may claim on the strength of this call that the page was printed.
    /// </summary>
    Task<AdminPrintOutcome> PrintAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks where to put a PDF of the current page, and writes it there.</summary>
    Task<AdminPrintResult> SaveAsPdfAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}

/// <summary>The printer for hosts that have none: nothing is printed and the screen says so.</summary>
public sealed class NoAdminPrintService : IAdminPrintService
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<AdminPrintOutcome> PrintAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AdminPrintOutcome.Unavailable);

    /// <inheritdoc />
    public Task<AdminPrintResult> SaveAsPdfAsync(string suggestedFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdminPrintResult(AdminPrintOutcome.Unavailable));
}
