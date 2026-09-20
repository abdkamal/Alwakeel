using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Services.Documents;

/// <summary>
/// The letter package's narrow window onto the vault (<see cref="ILetterDocumentStore"/>). B3-1b
/// was finished before the vault existed and was left with its contract unimplemented; this is
/// the implementation, so <c>DerivedDocumentBuilder</c> can be registered and the referral print
/// copy of AGREEMENT item 31 is actually produced.
/// </summary>
/// <remarks>
/// Everything it does goes through <see cref="IDocumentService"/> rather than straight to the
/// vault, so a generated letter is a document like any other: it has a record, a size, a name,
/// an OCR state and an audit line, and the documents list shows it beside the scan it was made
/// from. A read that finds a damaged or unreachable file answers <c>null</c> — the letter package
/// treats a missing original as "no original", which is the case it already handles by producing
/// a referral sheet that stands on its own.
/// </remarks>
/// <param name="documents">The document service the vault is reached through.</param>
public sealed class LetterDocumentStore(IDocumentService documents) : ILetterDocumentStore
{
    private readonly IDocumentService _documents = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <inheritdoc />
    public async Task<byte[]?> ReadAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var read = await _documents.ReadContentAsync(documentId, cancellationToken).ConfigureAwait(false);
        return read.IsOk ? read.Content : null;
    }

    /// <inheritdoc />
    public async Task<Guid> SaveAsync(
        byte[] content,
        string fileName,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var result = await _documents
            .SaveDerivedAsync(content, fileName, mediaType, derivedFromId: null, link: null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsStored)
        {
            // The caller is in the middle of recording a referral and has nothing to put in its
            // document column; there is no partial answer to give it.
            throw new VaultUnavailableException(result.MessageAr);
        }

        return result.DocumentId;
    }
}
