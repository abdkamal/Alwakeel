using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wakeel.Reports.Letters;

/// <summary>What came of asking Word to do something.</summary>
public enum WordOutcome
{
    /// <summary>Word did it.</summary>
    Done = 0,

    /// <summary>Word is not installed on this machine.</summary>
    Unavailable = 1,

    /// <summary>Word is installed but the attempt did not finish.</summary>
    Failed = 2,
}

/// <summary>
/// One conversation with the copy of Word installed on this machine, held through late-bound COM
/// (ARCHITECTURE §9, AGREEMENT item 10).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is compiled against a Word type library. Word's object model has changed shape
/// between releases, and a build tied to one of them would refuse to run beside another; a
/// late-bound call asks the installed copy, whatever it is, by name. The cost is that a
/// misspelled member is found at run time instead of at compile time, which is why the surface is
/// this small — open a file, export a PDF, wait for a window to close — and why every path ends
/// in Word being told to quit.
/// </para>
/// <para>
/// Word is released in a <c>finally</c>, and the document is always closed without saving before
/// the application quits: a Word left running invisibly holds the office's file open, and the
/// next attempt to write it fails for a reason nobody can see. The wait for the user to finish
/// editing is bounded, so a letter forgotten on a screen overnight does not hold a thread for
/// ever.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WordSession
{
    /// <summary>The identity Word registers itself under.</summary>
    private const string ProgId = "Word.Application";

    /// <summary>Word's own number for "save this as a PDF".</summary>
    private const int ExportFormatPdf = 17;

    /// <summary>Word's own number for "close without saving".</summary>
    private const int DoNotSaveChanges = 0;

    /// <summary>How often the wait looks to see whether the window is still open.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Whether a copy of Word is registered on this machine.</summary>
    public static bool IsAvailable => Type.GetTypeFromProgID(ProgId, throwOnError: false) is not null;

    /// <summary>
    /// Turns a Word document into a PDF. The document is opened invisibly, exported and closed.
    /// </summary>
    /// <param name="documentPath">The <c>.docx</c> to convert.</param>
    /// <param name="pdfPath">Where the PDF goes; an existing file is replaced.</param>
    public static WordOutcome ExportPdf(string documentPath, string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        return Run(documentPath, visible: false, (_, document) =>
        {
            document.ExportAsFixedFormat(pdfPath, ExportFormatPdf);
            return WordOutcome.Done;
        });
    }

    /// <summary>
    /// Opens the document in Word for the user to edit and returns when they close its window, so
    /// the caller can read the file back in and keep what they wrote.
    /// </summary>
    /// <param name="documentPath">The <c>.docx</c> to edit.</param>
    /// <param name="timeout">How long to wait before giving up on the window ever closing.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    public static WordOutcome Edit(string documentPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        return Run(documentPath, visible: true, (application, document) =>
        {
            try
            {
                application.Activate();
            }
            catch (COMException)
            {
                // Word decides for itself whether it may come to the front; it is open either way.
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsOpen(application))
                {
                    // The user closed the window; Word saved what they wrote, or asked them.
                    return WordOutcome.Done;
                }

                Thread.Sleep(PollInterval);
            }

            // Still open when the wait ran out: the file on disk is whatever Word last wrote.
            document.Save();
            return WordOutcome.Done;
        });
    }

    private static bool IsOpen(dynamic application)
    {
        try
        {
            return (int)application.Documents.Count > 0;
        }
        catch (COMException)
        {
            // Word itself has gone; nothing is open.
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
    }

    private static WordOutcome Run(string documentPath, bool visible, Func<dynamic, dynamic, WordOutcome> work)
    {
        var type = Type.GetTypeFromProgID(ProgId, throwOnError: false);
        if (type is null)
        {
            return WordOutcome.Unavailable;
        }

        object? application = null;
        object? document = null;
        try
        {
            application = Activator.CreateInstance(type);
            if (application is null)
            {
                return WordOutcome.Unavailable;
            }

            dynamic word = application;

            // msoAutomationSecurityForceDisable: every macro in whatever is opened from here on
            // stays switched off, whatever this machine's own trust settings say. A letter is
            // written on a template the office supplied and may be one that came from outside the
            // office altogether; nothing stored in it is allowed to run because الوكيل opened it
            // (the B3 security check).
            word.AutomationSecurity = 3;
            word.Visible = visible;
            word.DisplayAlerts = 0;

            document = word.Documents.Open(
                Path.GetFullPath(documentPath),
                ReadOnly: false,
                AddToRecentFiles: false,
                Visible: visible);

            return work(word, document!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is COMException
                                              or InvalidComObjectException
                                              or MissingMemberException
                                              or UnauthorizedAccessException
                                              or IOException
                                              or InvalidOperationException)
        {
            return WordOutcome.Failed;
        }
        finally
        {
            Release(application, document);
        }
    }

    /// <summary>
    /// Closes the document and quits Word, whatever happened, and lets go of both objects — a
    /// copy of Word still running holds the file and the next attempt fails silently.
    /// </summary>
    private static void Release(object? application, object? document)
    {
        if (document is not null)
        {
            try
            {
                ((dynamic)document).Close(DoNotSaveChanges);
            }
            catch (Exception exception) when (exception is COMException or InvalidComObjectException or MissingMemberException)
            {
                // Already closed by the user, or Word is gone.
            }

            SafeRelease(document);
        }

        if (application is not null)
        {
            try
            {
                ((dynamic)application).Quit(DoNotSaveChanges);
            }
            catch (Exception exception) when (exception is COMException or InvalidComObjectException or MissingMemberException)
            {
                // Word had already exited.
            }

            SafeRelease(application);
        }
    }

    private static void SafeRelease(object instance)
    {
        try
        {
            if (Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
        catch (ArgumentException)
        {
            // Not a COM object after all; nothing to release.
        }
    }
}
