using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>Why one stored item looks like the one being registered.</summary>
public enum DuplicateMatchKind
{
    /// <summary>Same party number AND same party: refused outright.</summary>
    Exact,

    /// <summary>Same party number, a different party: suspected, shown for review.</summary>
    SameNumberOtherParty,

    /// <summary>Normalised subjects overlap at or above the Jaccard threshold: suspected.</summary>
    SimilarSubject,
}

/// <summary>One stored item that resembles the candidate, ready to show in the review (W14).</summary>
/// <param name="CorrespondenceId">The stored item.</param>
/// <param name="Kind">Why it matched.</param>
/// <param name="Score">1.0 for an exact match; the Jaccard score for a subject match.</param>
/// <param name="ReasonAr">The Arabic sentence explaining the match.</param>
/// <param name="Subject">The stored item's subject.</param>
/// <param name="PartyNameAr">The stored item's party name snapshot.</param>
/// <param name="ExternalNumber">The stored item's party number.</param>
/// <param name="ExternalDate">The stored item's party date.</param>
/// <param name="OfficialNumber">The stored item's own official number, when it has one.</param>
/// <param name="ReviewId">The <c>duplicate_reviews</c> row written for a suspected match.</param>
public sealed record DuplicateMatch(
    Guid CorrespondenceId,
    DuplicateMatchKind Kind,
    double Score,
    string ReasonAr,
    string Subject,
    string? PartyNameAr,
    string? ExternalNumber,
    DateTime? ExternalDate,
    string? OfficialNumber,
    Guid? ReviewId = null);

/// <summary>The verdict of one scan.</summary>
/// <param name="Exact">The blocking match, when there is one.</param>
/// <param name="Suspected">Matches worth a human look; never blocking.</param>
public sealed record DuplicateScan(DuplicateMatch? Exact, IReadOnlyList<DuplicateMatch> Suspected)
{
    /// <summary>Nothing resembled the candidate.</summary>
    public static DuplicateScan Clean { get; } = new(null, []);

    /// <summary>True when registration must be refused (AGREEMENT item 14: exact match is prevented).</summary>
    public bool IsBlocked => Exact is not null;
}

/// <summary>What is being checked, before it exists as a row.</summary>
/// <param name="Direction">Incoming or outgoing; only items of the same direction are compared.</param>
/// <param name="Subject">The candidate's subject, as typed.</param>
/// <param name="ExternalNumber">The number the other party wrote on the letter.</param>
/// <param name="PartyId">External party, when the counterparty is external.</param>
/// <param name="UnitId">Internal unit, when the counterparty is internal.</param>
/// <param name="PartyNameSnapshot">
/// The counterparty typed by hand, used when it is not in the directory yet — the ordinary path
/// for a body the office deals with for the first time. Without it the exact-match check could
/// never fire for such a letter.
/// </param>
/// <param name="ExcludeId">The candidate's own id, when it already exists as a draft.</param>
public sealed record DuplicateCandidate(
    InOutDirection Direction,
    string Subject,
    string? ExternalNumber,
    Guid? PartyId,
    Guid? UnitId,
    string? PartyNameSnapshot = null,
    Guid? ExcludeId = null);

/// <summary>
/// Finds items that resemble a correspondence being registered (AGREEMENT item 14, B3-1
/// «DuplicateDetector»): an exact match on party number + party is refused, everything else is
/// surfaced as a review the user can dismiss with «ليست مكررة».
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>
    /// Looks for matches without writing anything — the check the register screen runs while the
    /// user is still typing.
    /// </summary>
    Task<DuplicateScan> ScanAsync(DuplicateCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a pending <c>duplicate_reviews</c> row for each suspected match of
    /// <paramref name="correspondenceId"/> and returns the matches with their review ids filled
    /// in. Called by the registration once the row exists; it does not save.
    /// </summary>
    Task<IReadOnlyList<DuplicateMatch>> RecordSuspectedAsync(
        Guid correspondenceId,
        IReadOnlyList<DuplicateMatch> suspected,
        CancellationToken cancellationToken = default);

    /// <summary>The pending reviews of one item, newest first.</summary>
    Task<IReadOnlyList<DuplicateReviewView>> GetReviewsAsync(Guid correspondenceId, CancellationToken cancellationToken = default);

    /// <summary>«ليست مكررة»: settles one review without touching either item.</summary>
    Task<bool> MarkNotDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default);

    /// <summary>«مكررة»: settles one review as a confirmed duplicate.</summary>
    Task<bool> MarkDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default);
}

/// <summary>One duplicate review, ready for the W14 card.</summary>
/// <param name="ReviewId">The review row.</param>
/// <param name="CorrespondenceId">The item being reviewed.</param>
/// <param name="SimilarId">The item it resembles.</param>
/// <param name="Score">The similarity that raised it.</param>
/// <param name="Verdict">Pending, «ليست مكررة» or «مكررة».</param>
/// <param name="VerdictAr">The verdict's Arabic wording.</param>
/// <param name="SimilarSubject">The other item's subject.</param>
/// <param name="SimilarNumber">The other item's official or party number.</param>
/// <param name="SimilarPartyNameAr">The other item's party name snapshot.</param>
public sealed record DuplicateReviewView(
    Guid ReviewId,
    Guid CorrespondenceId,
    Guid SimilarId,
    double Score,
    DuplicateVerdict Verdict,
    string VerdictAr,
    string SimilarSubject,
    string? SimilarNumber,
    string? SimilarPartyNameAr);

/// <inheritdoc cref="IDuplicateDetector"/>
public sealed class DuplicateDetector(WakeelDb db) : IDuplicateDetector
{
    /// <summary>
    /// B3-1: «الموضوع المطبَّع متشابه ≥ 0.8 بمقياس Jaccard على الكلمات». The comparison runs on
    /// <see cref="ArabicText.Normalize"/>d words, so diacritics, tatweel and the ة/ه and ى/ي
    /// spellings never decide whether two subjects are the same letter.
    /// </summary>
    public const double SubjectThreshold = 0.8;

    /// <summary>
    /// How many earlier items of the same direction the subject comparison looks back over. The
    /// exact/same-number checks are indexed queries and see everything; the subject check is a
    /// word-set comparison in memory, and an office that has registered tens of thousands of
    /// letters must not pay for all of them on every registration. Newest first, because a
    /// duplicate of a letter from three years ago is not what item 14 is about.
    /// </summary>
    public const int SubjectScanLimit = 2000;

    public async Task<DuplicateScan> ScanAsync(DuplicateCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var hasNumber = !string.IsNullOrWhiteSpace(candidate.ExternalNumber);
        var number = candidate.ExternalNumber?.Trim();

        // Same party number: one indexed read covers both the blocking case (same party) and the
        // suspected case (another party).
        var numbered = hasNumber
            ? await BaseQuery(candidate)
                .Where(c => c.ExternalNumber != null && c.ExternalNumber == number)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

        DuplicateMatch? exact = null;
        var suspected = new List<DuplicateMatch>();
        var seen = new HashSet<Guid>();

        foreach (var row in numbered)
        {
            if (SameParty(row, candidate))
            {
                exact ??= Match(row, DuplicateMatchKind.Exact, 1.0, CoreAr.CorrDuplicateExact);
                seen.Add(row.Id);
                continue;
            }

            if (seen.Add(row.Id))
            {
                suspected.Add(Match(row, DuplicateMatchKind.SameNumberOtherParty, 1.0, CoreAr.CorrDuplicateSameNumberOtherParty));
            }
        }

        if (exact is not null)
        {
            // The registration is refused anyway; nothing is gained by also listing near misses.
            return new DuplicateScan(exact, []);
        }

        var candidateWords = Words(candidate.Subject);
        if (candidateWords.Count > 0)
        {
            var recent = await BaseQuery(candidate)
                .OrderByDescending(c => c.CreatedAt)
                .Take(SubjectScanLimit)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var row in recent)
            {
                if (seen.Contains(row.Id))
                {
                    continue;
                }

                var score = Jaccard(candidateWords, Words(row.Subject));
                if (score >= SubjectThreshold)
                {
                    seen.Add(row.Id);
                    suspected.Add(Match(row, DuplicateMatchKind.SimilarSubject, score, CoreAr.CorrDuplicateSimilarSubject));
                }
            }
        }

        return suspected.Count == 0
            ? DuplicateScan.Clean
            : new DuplicateScan(null, [.. suspected.OrderByDescending(m => m.Score)]);
    }

    public async Task<IReadOnlyList<DuplicateMatch>> RecordSuspectedAsync(
        Guid correspondenceId,
        IReadOnlyList<DuplicateMatch> suspected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suspected);
        if (suspected.Count == 0)
        {
            return [];
        }

        // A review may already exist for this pair (the same incoming scanned twice, or a second
        // registration attempt after a rolled-back one); re-using it keeps the W14 list free of
        // repeated cards for one pair.
        var similarIds = suspected.Select(m => m.CorrespondenceId).ToList();
        var existing = await db.DuplicateReviews
            .Where(r => r.CorrespondenceId == correspondenceId && similarIds.Contains(r.SimilarId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var recorded = new List<DuplicateMatch>(suspected.Count);
        foreach (var match in suspected)
        {
            var review = existing.FirstOrDefault(r => r.SimilarId == match.CorrespondenceId);
            if (review is null)
            {
                review = new DuplicateReview
                {
                    CorrespondenceId = correspondenceId,
                    SimilarId = match.CorrespondenceId,
                    Score = match.Score,
                    Verdict = DuplicateVerdict.Pending,
                };
                db.DuplicateReviews.Add(review);
            }
            else
            {
                review.Score = match.Score;
            }

            recorded.Add(match with { ReviewId = review.Id });
        }

        return recorded;
    }

    public async Task<IReadOnlyList<DuplicateReviewView>> GetReviewsAsync(Guid correspondenceId, CancellationToken cancellationToken = default)
    {
        var reviews = await db.DuplicateReviews.AsNoTracking()
            .Where(r => r.CorrespondenceId == correspondenceId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (reviews.Count == 0)
        {
            return [];
        }

        var similarIds = reviews.Select(r => r.SimilarId).Distinct().ToList();
        var similar = await db.Correspondence.AsNoTracking()
            .Where(c => similarIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Subject, c.OfficialNumber, c.ExternalNumber, c.PartyNameSnapshot })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. reviews.Select(r =>
            {
                var other = similar.FirstOrDefault(s => s.Id == r.SimilarId);
                return new DuplicateReviewView(
                    r.Id,
                    r.CorrespondenceId,
                    r.SimilarId,
                    r.Score,
                    r.Verdict,
                    CorrespondenceAr.Verdict(r.Verdict),
                    other?.Subject ?? string.Empty,
                    other?.OfficialNumber ?? other?.ExternalNumber,
                    other?.PartyNameSnapshot);
            }),
        ];
    }

    public Task<bool> MarkNotDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default) =>
        SetVerdictAsync(reviewId, DuplicateVerdict.NotDuplicate, cancellationToken);

    public Task<bool> MarkDuplicateAsync(Guid reviewId, CancellationToken cancellationToken = default) =>
        SetVerdictAsync(reviewId, DuplicateVerdict.Duplicate, cancellationToken);

    /// <summary>
    /// Jaccard similarity of two word sets: |A ∩ B| / |A ∪ B|. Two empty subjects are not
    /// "identical", they are simply unknown, so the score is 0 rather than 1 — otherwise every
    /// blank draft would suspect every other blank draft.
    /// </summary>
    public static double Jaccard(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var union = new HashSet<string>(left, StringComparer.Ordinal);
        var intersection = 0;
        foreach (var word in new HashSet<string>(right, StringComparer.Ordinal))
        {
            if (!union.Add(word))
            {
                intersection++;
            }
        }

        return union.Count == 0 ? 0 : (double)intersection / union.Count;
    }

    /// <summary>
    /// Similarity of two subjects as the detector measures it: Arabic-normalised, split on
    /// whitespace and punctuation, compared as sets of words. Exposed so a screen can show the
    /// same number the threshold was decided on.
    /// </summary>
    public static double SubjectSimilarity(string? left, string? right) => Jaccard(Words(left), Words(right));

    /// <summary>The normalised word set of a subject: the unit the Jaccard score is measured in.</summary>
    public static IReadOnlyCollection<string> Words(string? subject)
    {
        var normalized = ArabicText.Normalize(subject);
        if (normalized.Length == 0)
        {
            return [];
        }

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in normalized.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length > 0)
            {
                words.Add(token);
            }
        }

        return words;
    }

    /// <summary>
    /// What separates two words of a subject. Arabic office subjects are written with the same
    /// punctuation as the rest of the letter, and «بشأن: الموازنة» must produce the same words as
    /// «بشأن الموازنة» or the score would depend on a colon.
    /// </summary>
    private static readonly char[] Separators =
    [
        ' ', '\t', '\r', '\n', ' ', '‏', '‎',
        '.', ',', '،', ';', '؛', ':', '/', '\\', '|', '-', '_', '(', ')', '[', ']', '{', '}',
        '"', '\'', '«', '»', '?', '؟', '!', '*', '+', '#', '&', '@',
    ];

    private async Task<bool> SetVerdictAsync(Guid reviewId, DuplicateVerdict verdict, CancellationToken cancellationToken)
    {
        var review = await db.DuplicateReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return false;
        }

        review.Verdict = verdict;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The population any scan compares against: items of the same direction that are not the
    /// candidate itself and are not cancelled. A cancelled item's number stays consumed but the
    /// letter it stood for was withdrawn, so registering the same letter again is legitimate and
    /// must not be blocked. Drafts are included on purpose: two half-typed registrations of the
    /// same incoming letter are exactly what item 14 is meant to catch.
    /// </summary>
    private IQueryable<CorrespondenceRow> BaseQuery(DuplicateCandidate candidate)
    {
        var query = db.Correspondence.AsNoTracking()
            .Where(c => c.Direction == candidate.Direction && c.Status != CorrespondenceStatus.Cancelled);
        return candidate.ExcludeId is { } exclude ? query.Where(c => c.Id != exclude) : query;
    }

    /// <summary>
    /// Whether the stored row carries the same counterparty as the candidate. A directory party
    /// or an org unit decides it by id. When the candidate names its counterparty by hand — no
    /// party, no unit, which is how a body that is not in the directory yet is registered — the
    /// normalised names decide instead, otherwise the same letter typed twice from the same body
    /// would never be recognised as the exact duplicate item 14 refuses, and would be reported
    /// with the wrong reason («الرقم نفسه مسجَّل لجهة أخرى») on top of consuming a second number.
    /// </summary>
    private static bool SameParty(CorrespondenceRow row, DuplicateCandidate candidate)
    {
        if ((candidate.PartyId is { } party && row.PartyId == party)
            || (candidate.UnitId is { } unit && row.UnitId == unit))
        {
            return true;
        }

        if (candidate.PartyId is not null || candidate.UnitId is not null)
        {
            return false;
        }

        var left = ArabicText.Normalize(candidate.PartyNameSnapshot);
        return left.Length > 0 && string.Equals(left, ArabicText.Normalize(row.PartyNameSnapshot), StringComparison.Ordinal);
    }

    private static DuplicateMatch Match(CorrespondenceRow row, DuplicateMatchKind kind, double score, string reasonAr) =>
        new(row.Id, kind, score, reasonAr, row.Subject, row.PartyNameSnapshot, row.ExternalNumber, row.ExternalDate, row.OfficialNumber);
}
