using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Ocr;

/// <summary>
/// The background reader (<see cref="IOcrQueue"/>, B3-2): documents wait their turn, the one the
/// office is looking at goes first, and an interrupted run picks up where it stopped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the queue lives.</b> The order is in memory, but what is waiting is not: it is the
/// <c>ocr_status</c> column. <see cref="ResumeAsync"/> reads every document still marked as
/// waiting — or marked as being read, which after a restart means the run was cut off — and puts
/// them back in line. That is what makes the queue survive a crash, a sign-out and a power cut
/// without a queue file of its own to get out of step with the database.
/// </para>
/// <para>
/// <b>Order.</b> What the office is waiting on (<see cref="OcrPriority.Interactive"/>) is taken
/// before the background batch, and within one priority the order is the order things were asked
/// for. A document already in line is not queued twice; asking for it again at the higher
/// priority moves it up rather than adding a second entry.
/// </para>
/// <para>
/// <b>Low priority.</b> The drain yields between documents and runs its reading on the thread
/// pool, so the office's typing never waits behind a twenty-page scan. It deliberately does not
/// use a background thread of its own: the host starts the drain from its own idle pass, which is
/// where "when nothing else is happening" is actually known.
/// </para>
/// <para>
/// <b>Changing the model.</b> The reading files in force are stored on each document as
/// <c>ocr_lang</c> — the two languages and a short fingerprint of the files. When the files
/// change (AGREEMENT item 30), <see cref="ReprocessChangedModelAsync"/> marks every document whose
/// text came from the old ones as waiting again, and the ordinary drain reads them.
/// </para>
/// </remarks>
public sealed class OcrQueue : IOcrQueue
{
    private readonly WakeelDb _db;
    private readonly IDocumentService _documents;
    private readonly IOcrEngine _engine;
    private readonly Lock _gate = new();
    private readonly List<Entry> _waiting = [];
    private int _done;
    private int _total;

    /// <summary>Creates the queue.</summary>
    /// <param name="db">The open database, where what is waiting is recorded.</param>
    /// <param name="documents">The document service the pages are written through.</param>
    /// <param name="engine">The reader.</param>
    public OcrQueue(WakeelDb db, IDocumentService documents, IOcrEngine engine)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(engine);
        _db = db;
        _documents = documents;
        _engine = engine;
    }

    /// <inheritdoc />
    public event Action<OcrQueueProgress>? ProgressChanged;

    /// <inheritdoc />
    public OcrQueueProgress Progress { get; private set; } = new(0, 0, Guid.Empty, 0, 0);

    /// <summary>What is still in line, in the order it will be read — what the ordering test asserts.</summary>
    public IReadOnlyList<Guid> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. Ordered().Select(e => e.DocumentId)];
            }
        }
    }

    /// <summary>The value written to <c>ocr_lang</c> for text this engine produced.</summary>
    public string OcrLang => _engine.ModelFingerprint is { Length: > 0 } fingerprint
        ? DocumentService.OcrLanguages + "/" + fingerprint
        : DocumentService.OcrLanguages;

    /// <inheritdoc />
    public void Enqueue(Guid documentId, OcrPriority priority = OcrPriority.Background) =>
        TryEnqueue(documentId, priority);

    /// <summary>
    /// Puts a document in line and says whether it was added. Resuming a restore with a few
    /// thousand waiting documents asks this once per document, so it must not have to project and
    /// sort the whole waiting list to tell the caller what it just did.
    /// </summary>
    /// <returns><c>true</c> when the document was not in line before.</returns>
    private bool TryEnqueue(Guid documentId, OcrPriority priority)
    {
        if (documentId == Guid.Empty)
        {
            return false;
        }

        lock (_gate)
        {
            var existing = _waiting.FindIndex(e => e.DocumentId == documentId);
            if (existing >= 0)
            {
                // Already in line. Asking again from a screen the office is looking at moves it
                // up; asking again in the background leaves it where it is.
                if (priority > _waiting[existing].Priority)
                {
                    _waiting[existing] = _waiting[existing] with { Priority = priority };
                }

                return false;
            }

            _waiting.Add(new Entry(documentId, priority, _total));
            _total++;
        }

        Report(Guid.Empty, 0, 0);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> ResumeAsync(CancellationToken cancellationToken = default)
    {
        // "Running" on the way in means a run that was cut off: nothing is reading it now, because
        // a queue only exists inside one open session.
        var interrupted = await _db.Documents
            .Where(d => d.OcrStatus == OcrStatus.Running)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var document in interrupted)
        {
            document.OcrStatus = OcrStatus.Pending;
        }

        if (interrupted.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var waiting = await _db.Documents
            .AsNoTracking()
            .Where(d => d.OcrStatus == OcrStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = 0;
        foreach (var id in waiting)
        {
            if (TryEnqueue(id, OcrPriority.Background))
            {
                added++;
            }
        }

        return added;
    }

    /// <inheritdoc />
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var finished = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            Entry entry;
            lock (_gate)
            {
                var next = Ordered().FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                _waiting.Remove(next);
                entry = next;
            }

            var result = await ReadCoreAsync(entry.DocumentId, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _done++;
            }

            if (result.State == OcrState.Ok)
            {
                finished++;
            }

            Report(Guid.Empty, 0, 0);

            // Low priority: the next document waits for whatever else wants the thread.
            await Task.Yield();
        }

        return finished;
    }

    /// <inheritdoc />
    public async Task<OcrDocumentResult> ReadNowAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var queued = _waiting.FindIndex(e => e.DocumentId == documentId);
            if (queued >= 0)
            {
                _waiting.RemoveAt(queued);
            }
            else
            {
                _total++;
            }
        }

        var result = await ReadCoreAsync(documentId, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _done++;
        }

        Report(Guid.Empty, 0, 0);
        return result;
    }

    /// <inheritdoc />
    public async Task<int> ReprocessChangedModelAsync(CancellationToken cancellationToken = default)
    {
        if (!_engine.IsAvailable)
        {
            // Without reading files there is nothing better to produce, and marking documents as
            // waiting would leave the list saying «بانتظار قراءة النص» for ever.
            return 0;
        }

        var current = OcrLang;
        var stale = await _db.Documents
            .Where(d => d.OcrStatus == OcrStatus.Done && d.OcrLang != current)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var document in stale)
        {
            document.OcrStatus = OcrStatus.Pending;
        }

        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var document in stale)
        {
            Enqueue(document.Id);
        }

        return stale.Count;
    }

    private async Task<OcrDocumentResult> ReadCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return OcrDocumentResult.Failed;
        }

        if (!DocumentMediaTypes.IsReadable(document.Mime))
        {
            await _documents.SetOcrStatusAsync(documentId, OcrStatus.Unsupported, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return OcrDocumentResult.Unsupported;
        }

        if (!_engine.IsAvailable)
        {
            // Left waiting on purpose: the files may be dropped into the models folder later
            // (AGREEMENT item 30) and the document is then read without anybody asking again.
            return OcrDocumentResult.ModelMissing;
        }

        var read = await _documents.ReadContentAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (!read.IsOk || read.Content is null)
        {
            // Unreachable or damaged bytes. The record and whatever text it already has stay as
            // they are — the «الملف تالف — الأصل محفوظ» card says exactly that.
            await _documents.SetOcrStatusAsync(documentId, OcrStatus.Failed, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return OcrDocumentResult.Failed;
        }

        await _documents.SetOcrStatusAsync(documentId, OcrStatus.Running, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var progress = new Progress<OcrPageProgress>(p => Report(documentId, p.Page, p.Pages));
        var result = await _engine
            .ReadAsync(read.Content, document.Mime, progress, cancellationToken)
            .ConfigureAwait(false);

        if (result.Pages.Count > 0)
        {
            // A cut-off run keeps the pages it did manage to read, so the work is not thrown away
            // — but it must not be filed as read. Saving and then putting the document back in
            // line is done with no cancellation of its own: the token that stopped the reading is
            // already cancelled, and losing the record of where the reading stopped is exactly the
            // thing that would leave a twenty-page scan half-indexed for ever.
            var write = result.State == OcrState.Cancelled ? CancellationToken.None : cancellationToken;
            await _documents.SavePagesAsync(documentId, result.Pages, OcrLang, write).ConfigureAwait(false);

            if (result.State == OcrState.Cancelled)
            {
                await _documents
                    .SetOcrStatusAsync(documentId, OcrStatus.Pending, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            await _documents
                .SetOcrStatusAsync(
                    documentId,
                    result.State switch
                    {
                        OcrState.Unsupported => OcrStatus.Unsupported,
                        OcrState.Cancelled or OcrState.ModelMissing => OcrStatus.Pending,
                        _ => OcrStatus.Failed,
                    },
                    cancellationToken: result.State == OcrState.Cancelled ? CancellationToken.None : cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The waiting list in the order it will be read: what is being waited on, then first asked for.</summary>
    private IEnumerable<Entry> Ordered() =>
        _waiting.OrderByDescending(e => e.Priority).ThenBy(e => e.Sequence);

    private void Report(Guid documentId, int page, int pages)
    {
        // Read as a pair under the same lock the two are counted under. Reading one document now
        // for a screen while the background batch drains is the design, not an edge case, and a
        // count taken between the two increments would say «اكتملت قراءة نصوص المستندات» early.
        int done, total;
        lock (_gate)
        {
            done = _done;
            total = _total;
        }

        var progress = new OcrQueueProgress(done, total, documentId, page, pages);
        Progress = progress;
        ProgressChanged?.Invoke(progress);
    }

    /// <summary>One document waiting, with what decides its place.</summary>
    /// <param name="DocumentId">The document.</param>
    /// <param name="Priority">Whether somebody is waiting on it.</param>
    /// <param name="Sequence">When it was asked for, so equal priorities keep their order.</param>
    private sealed record Entry(Guid DocumentId, OcrPriority Priority, int Sequence);
}
