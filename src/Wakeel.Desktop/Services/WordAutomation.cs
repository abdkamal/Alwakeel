using System.IO;
using System.Runtime.Versioning;
using CorePaths = Wakeel.Core.Data.WakeelPaths;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Desktop.Services;

/// <inheritdoc cref="IWordAutomation"/>
/// <remarks>
/// <para>
/// The Windows half of "open the letter in Word" (AGREEMENT item 10, ARCHITECTURE §9). الوكيل
/// never hands Word a file out of the vault: it writes the generated letter into the
/// installation's own staging folder — the one place meant for content that is briefly in the
/// clear — opens that, waits for the window to close, reads it back, and deletes it. The vault's
/// copy is replaced only afterwards and only by the caller.
/// </para>
/// <para>
/// The wait happens on a thread of its own, because it is a person deciding when it ends and the
/// window must stay alive meanwhile. Word not being installed is not a failure: it is a state the
/// screens show as «Word غير متوفر» beside the internal editor, which can write the whole letter
/// on its own.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WordAutomation : IWordAutomation
{
    /// <summary>How long a letter may stay open in Word before الوكيل stops waiting for it.</summary>
    private static readonly TimeSpan EditTimeout = TimeSpan.FromHours(4);

    private readonly CorePaths _paths;

    /// <summary>Creates the service.</summary>
    /// <param name="paths">The installation layout, for the staging folder.</param>
    public WordAutomation(CorePaths paths) =>
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <inheritdoc />
    public bool IsAvailable => WordSession.IsAvailable;

    /// <inheritdoc />
    public async Task<WordEditResult> EditAsync(
        byte[] docx,
        string fileNameHint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docx);
        if (!WordSession.IsAvailable)
        {
            return WordEditResult.Unavailable;
        }

        var path = StagingPath(fileNameHint);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, docx, cancellationToken).ConfigureAwait(false);

            var outcome = await Task.Run(
                () => WordSession.Edit(path, EditTimeout, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (outcome != WordOutcome.Done)
            {
                return outcome == WordOutcome.Unavailable ? WordEditResult.Unavailable : WordEditResult.Failed;
            }

            var edited = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return new WordEditResult(WordEditState.Imported, edited);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WordEditResult.Failed;
        }
        finally
        {
            Discard(path);
        }
    }

    /// <inheritdoc />
    public async Task<WordEditState> EditFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!WordSession.IsAvailable)
        {
            return WordEditState.WordUnavailable;
        }

        if (!File.Exists(path))
        {
            return WordEditState.Failed;
        }

        // The office's own template is edited where it lies: nothing is copied to staging and
        // nothing is read back, because Word saving over the file is the whole point.
        var outcome = await Task.Run(
            () => WordSession.Edit(path, EditTimeout, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            WordOutcome.Done => WordEditState.Imported,
            WordOutcome.Unavailable => WordEditState.WordUnavailable,
            _ => WordEditState.Failed,
        };
    }

    /// <summary>
    /// The letter as a PDF through Word, or <c>null</c> when Word is not there or refused.
    /// </summary>
    /// <param name="docx">The generated letter.</param>
    /// <param name="fileNameHint">What to call the temporary file.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<byte[]?> ToPdfAsync(
        byte[] docx,
        string fileNameHint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docx);
        if (!WordSession.IsAvailable)
        {
            return null;
        }

        var documentPath = StagingPath(fileNameHint);
        var pdfPath = Path.ChangeExtension(documentPath, ".pdf");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
            await File.WriteAllBytesAsync(documentPath, docx, cancellationToken).ConfigureAwait(false);

            var outcome = await Task.Run(
                () => WordSession.ExportPdf(documentPath, pdfPath),
                cancellationToken).ConfigureAwait(false);

            return outcome == WordOutcome.Done && File.Exists(pdfPath)
                ? await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            Discard(documentPath);
            Discard(pdfPath);
        }
    }

    private string StagingPath(string fileNameHint)
    {
        var name = Safe(fileNameHint);
        return Path.Combine(_paths.StagingDir, $"{Guid.NewGuid():N}-{name}");
    }

    /// <summary>
    /// A file name Word will accept and Windows will store: the caller's hint is a subject line,
    /// which may carry a slash or a colon.
    /// </summary>
    private static string Safe(string hint)
    {
        var name = string.IsNullOrWhiteSpace(hint) ? "letter.docx" : hint.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        if (!name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            name += ".docx";
        }

        return name.Length > 80 ? name[^80..] : name;
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Staging is cleared on the next start; a file Word still holds is not worth an error.
        }
    }
}
