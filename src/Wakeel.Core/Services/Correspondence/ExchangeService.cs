using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Crypto;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Services.Correspondence;

/// <summary>
/// One file carried inside a <c>.wakeel-msg</c> beside the message itself. The bytes come from
/// the caller (the vault lives in B3-2), so Core never reaches into the vault from here.
/// </summary>
/// <param name="Name">The name the file takes inside the package; also its name on import.</param>
/// <param name="Mime">Its media type, as recorded in the vault.</param>
/// <param name="Content">Its bytes, exactly as stored.</param>
public sealed record ExchangeAttachment(string Name, string Mime, byte[] Content);

/// <summary>What to export, and who signs it.</summary>
public sealed record ExchangeExportRequest
{
    /// <summary>The approved correspondence to send.</summary>
    public required Guid CorrespondenceId { get; init; }

    /// <summary>Where the <c>.wakeel-msg</c> file is written. A directory gets a generated name.</summary>
    public required string OutputPath { get; init; }

    /// <summary>This device's certificate, which travels in the manifest.</summary>
    public required DeviceCertificate Producer { get; init; }

    /// <summary>This device's keys. Held by the caller, never stored by Core.</summary>
    public required DeviceIdentity Signer { get; init; }

    /// <summary>
    /// The recipient's X25519 public key, when the caller already has it. Left unset, it is read
    /// from the recipient org unit (the structure) or from the party profile.
    /// </summary>
    public byte[]? RecipientPublicKey { get; init; }

    /// <summary>The documents to carry with the letter.</summary>
    public IReadOnlyList<ExchangeAttachment> Attachments { get; init; } = [];

    /// <summary>Clock used for the manifest's <c>createdAt</c>; the system clock by default.</summary>
    public TimeProvider? Time { get; init; }
}

/// <summary>What the export produced.</summary>
/// <param name="Path">The written file.</param>
/// <param name="FileName">Its file name, as shown in the exchange log.</param>
/// <param name="RecipientNameAr">Who it was sealed to.</param>
/// <param name="LogEntryId">The <c>exchange_log</c> row.</param>
public sealed record ExchangeExportResult(string Path, string FileName, string? RecipientNameAr, Guid LogEntryId);

/// <summary>What a <c>.wakeel-msg</c> says before anything is registered (W93).</summary>
/// <param name="SignatureOk">Whether the signature and, for our own organisation, the chain held.</param>
/// <param name="SignatureAr">
/// What was actually proven: «التوقيع سليم» for our own organisation (chain checked against the
/// pinned organisation key), «التوقيع متماسك، لكن الجهة المرسِلة غير موثّقة لدينا» for another
/// organisation, «التوقيع غير سليم؛ لا تسجّل هذا الملف» when it did not hold at all.
/// </param>
/// <param name="FromAr">«من هيئة … — مكتب …».</param>
/// <param name="IsInternal">True when the sender belongs to this same organisation (AGREEMENT item 49).</param>
/// <param name="KindAr">«وارد داخلي» or «وارد خارجي».</param>
/// <param name="Subject">The letter's subject.</param>
/// <param name="SenderNumber">The sender's own official number; it becomes our «رقم الجهة».</param>
/// <param name="SenderDate">The date that number was issued.</param>
/// <param name="BodyText">The letter's text, when it carried one.</param>
/// <param name="AttachmentNames">The files inside the package.</param>
/// <param name="AlreadyImportedAr">Set when this letter is already registered here.</param>
public sealed record ExchangeImportPreview(
    bool SignatureOk,
    string SignatureAr,
    string FromAr,
    bool IsInternal,
    string KindAr,
    string Subject,
    string? SenderNumber,
    DateTime? SenderDate,
    string? BodyText,
    IReadOnlyList<string> AttachmentNames,
    string? AlreadyImportedAr);

/// <summary>What the import registered.</summary>
/// <param name="Registration">The incoming registration, with its own official number.</param>
/// <param name="Preview">What the file said.</param>
/// <param name="Attachments">The files the package carried, for the caller to store in the vault.</param>
/// <param name="LogEntryId">The <c>exchange_log</c> row.</param>
public sealed record ExchangeImportResult(
    IncomingRegistrationResult Registration,
    ExchangeImportPreview Preview,
    IReadOnlyList<ExchangeAttachment> Attachments,
    Guid LogEntryId);

/// <summary>One exchange-log row, ready for W93.</summary>
/// <param name="Id">The log row.</param>
/// <param name="Direction">Sent or received.</param>
/// <param name="DirectionAr">«صادر» / «وارد».</param>
/// <param name="FileName">The file's name.</param>
/// <param name="OtherAr">The other organisation and office.</param>
/// <param name="EntityId">The correspondence the row refers to.</param>
/// <param name="SignatureOk">Whether the signature held.</param>
/// <param name="SignatureAr">The signature wording.</param>
/// <param name="At">When it happened.</param>
/// <param name="AtAr">That instant as «أمس 16:40».</param>
public sealed record ExchangeLogView(
    Guid Id,
    InOutDirection Direction,
    string DirectionAr,
    string FileName,
    string? OtherAr,
    Guid? EntityId,
    bool SignatureOk,
    string SignatureAr,
    DateTime At,
    string AtAr);

/// <summary>
/// Exchange of one approved correspondence as a signed, sealed <c>.wakeel-msg</c> file
/// (AGREEMENT items 22 and 49): export to the recipient's public key taken from the structure or
/// from the party profile, and import with a signature/sender preview followed by registration as
/// an incoming letter with automatic incoming numbering — internal when the sender belongs to
/// this organisation, external otherwise.
/// </summary>
public interface IExchangeService
{
    /// <summary>Writes the approved correspondence as a <c>.wakeel-msg</c> sealed to its recipient.</summary>
    Task<ExchangeExportResult> ExportAsync(ExchangeExportRequest request, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a <c>.wakeel-msg</c> and says what it is, without writing anything but the answer.
    /// Never throws for a bad signature: the screen has to be able to show «التوقيع غير سليم».
    /// </summary>
    Task<ExchangeImportPreview> PreviewImportAsync(
        string path,
        DeviceIdentity recipient,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the letter inside a <c>.wakeel-msg</c> as an incoming item, with its own
    /// incoming official number. Refuses a file whose signature does not hold.
    /// </summary>
    Task<ExchangeImportResult> ImportAsync(
        string path,
        DeviceIdentity recipient,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>The exchange log (W93), newest first.</summary>
    Task<IReadOnlyList<ExchangeLogView>> ListLogAsync(DateTime now, int limit = 200, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IExchangeService"/>
public sealed class ExchangeService(
    WakeelDb db,
    ICorrespondenceService correspondence,
    IAuditService audit) : IExchangeService
{
    /// <summary>The name the letter itself takes inside the package.</summary>
    public const string MessageEntryName = "message.json";

    /// <summary>The folder attachments live in inside the package.</summary>
    public const string AttachmentPrefix = "files/";

    public const string AuditActionExported = "correspondence.exported";
    public const string AuditActionImported = "correspondence.imported";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ExchangeExportResult> ExportAsync(ExchangeExportRequest request, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await db.Correspondence.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CorrespondenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        // Only an approved letter leaves the office: an unapproved one has no number, and a
        // recipient would have nothing to register it by.
        if (row.OfficialNumber is null || row.ApprovedAt is null)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrExchangeNotApproved);
        }

        // A withdrawn letter keeps its number (AGREEMENT item 19) but must never be delivered:
        // the receiving office has no way of telling a cancelled letter from a live one and
        // would register it as an ordinary incoming.
        if (row.Status == CorrespondenceStatus.Cancelled)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrExchangeCancelled);
        }

        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        // «للمستلم فقط» (AGREEMENT item 7) is the one flag that restricts where a letter may go,
        // so a caller-supplied key is ignored for it: the container must be sealed to the key the
        // structure or the party profile records for the addressee itself, and when the office
        // has no such key on file the export is refused rather than sealed to whoever asked.
        var recipientKey = row.RecipientOnly
            ? await ResolveRecipientKeyAsync(row, cancellationToken).ConfigureAwait(false)
            : request.RecipientPublicKey ?? await ResolveRecipientKeyAsync(row, cancellationToken).ConfigureAwait(false);
        if (recipientKey is not { Length: DeviceIdentity.PublicKeySize })
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrExchangeNoRecipientKey);
        }

        var message = new ExchangeMessage
        {
            FormatVersion = ExchangeMessage.CurrentFormatVersion,
            SenderOrgId = installation.OrgId,
            SenderOrgName = installation.OrgName,
            SenderOfficeId = installation.OfficeId,
            SenderOfficeName = installation.OfficeName,
            SenderOfficeUnitId = installation.OfficeUnitId,
            SenderOfficeCode = installation.OfficeCode,
            SenderEmployeeName = installation.EmployeeName,
            SourceCorrespondenceId = row.Id,
            OfficialNumber = row.OfficialNumber,
            NumberIssuedAt = row.NumberIssuedAt,
            Subject = row.Subject,
            Type = row.Type,
            Confidentiality = row.Confidentiality.ToString(),
            RecipientOnly = row.RecipientOnly,
            BodyText = row.BodyText,
            Cc = row.Cc,
            RecipientNameAr = row.PartyNameSnapshot,
            Attachments = [.. request.Attachments.Select(a => new ExchangeAttachmentInfo(AttachmentPrefix + a.Name, a.Mime, a.Content.LongLength))],
        };

        var entries = new List<ContainerEntrySource>
        {
            ContainerEntrySource.FromBytes(MessageEntryName, JsonSerializer.SerializeToUtf8Bytes(message, Json)),
        };
        entries.AddRange(request.Attachments.Select(a => ContainerEntrySource.FromBytes(AttachmentPrefix + a.Name, a.Content)));

        var path = ResolveOutputPath(request.OutputPath, row.OfficialNumber);
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Msg,
            Producer = request.Producer,
            Signer = request.Signer,

            // The content key is sealed to the recipient alone; the organisation's recovery copy
            // rides along so the administration tool can still open the file (AGREEMENT item 17).
            Key = ContainerKeySource.SealFor(recipientKey),
            OrgAgreementPublicKey = installation.OrgX25519Pub.Length == DeviceIdentity.PublicKeySize ? installation.OrgX25519Pub : null,
            Entries = entries,
            Time = request.Time ?? TimeProvider.System,
        });

        var fileName = Path.GetFileName(path);
        var log = new ExchangeLogEntry
        {
            Direction = InOutDirection.Out,
            Kind = ExchangeKind.Msg,
            FileName = fileName,
            OtherOrg = null,
            OtherOffice = row.PartyNameSnapshot,
            EntityId = row.Id,
            SignatureOk = true,
            At = now,
        };
        db.ExchangeLog.Add(log);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(AuditActionExported, CoreAr.CorrAuditExported(fileName), row.Id, cancellationToken).ConfigureAwait(false);

        return new ExchangeExportResult(path, fileName, row.PartyNameSnapshot, log.Id);
    }

    public async Task<ExchangeImportPreview> PreviewImportAsync(
        string path,
        DeviceIdentity recipient,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync(path, recipient, now, cancellationToken).ConfigureAwait(false);
        return read.Preview;
    }

    public async Task<ExchangeImportResult> ImportAsync(
        string path,
        DeviceIdentity recipient,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync(path, recipient, now, cancellationToken).ConfigureAwait(false);
        if (!read.Preview.SignatureOk || read.Message is null)
        {
            throw new CorrespondenceRefusedException(CoreAr.CorrExchangeSignatureBad);
        }

        if (read.Preview.AlreadyImportedAr is { } already)
        {
            // Registering the same package twice would consume a second incoming number for one
            // letter. The preview said so; importing anyway is refused rather than silently
            // creating the pair of near-identical rows the duplicate review exists to prevent.
            throw new CorrespondenceRefusedException(already);
        }

        var message = read.Message;
        var unitId = read.SenderUnitId;

        var draft = await correspondence.CreateDraftAsync(
            new CorrespondenceDraftInput
            {
                Direction = InOutDirection.In,
                Subject = message.Subject,
                Type = message.Type,
                Confidentiality = ParseConfidentiality(message.Confidentiality),

                // The sender's «للمستلم فقط» restriction is kept on the registered incoming, so
                // this office is told the letter must not be passed on either.
                RecipientOnly = message.RecipientOnly,
                CounterpartyKind = read.Preview.IsInternal ? CounterpartyKind.Internal : CounterpartyKind.External,
                UnitId = unitId,

                // The sender's own official number is, from here, "the number the other party
                // wrote on the letter" — exactly what external_number means for any incoming.
                ExternalNumber = message.OfficialNumber,
                ExternalDate = message.NumberIssuedAt ?? now,
                PartyNameSnapshot = SenderName(message),
                Cc = message.Cc,
                BodyText = message.BodyText,
            },
            now,
            cancellationToken).ConfigureAwait(false);

        // The draft is already committed by CreateDraftAsync, so a refused registration — an
        // exact duplicate of a letter somebody registered by hand, a clock that is not trusted, a
        // field the check rejects, a number that was issued elsewhere meanwhile — would leave a
        // stray unnumbered incoming in the list with nothing to say where it came from. The two
        // writes cannot share one transaction (the registration opens its own), so the draft is
        // removed again before the refusal reaches the caller.
        IncomingRegistrationResult registration;
        try
        {
            registration = await correspondence.RegisterIncomingAsync(draft.Id, now, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DiscardDraftAsync(draft.Id, now).ConfigureAwait(false);
            throw;
        }

        var log = new ExchangeLogEntry
        {
            Direction = InOutDirection.In,
            Kind = ExchangeKind.Msg,
            FileName = Path.GetFileName(path),
            OtherOrg = message.SenderOrgName,
            OtherOffice = message.SenderOfficeName,
            EntityId = registration.View.Id,
            SignatureOk = true,
            At = now,
        };
        db.ExchangeLog.Add(log);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(AuditActionImported, CoreAr.CorrAuditImported(registration.OfficialNumber), registration.View.Id, cancellationToken)
            .ConfigureAwait(false);

        return new ExchangeImportResult(registration, read.Preview, read.Attachments, log.Id);
    }

    public async Task<IReadOnlyList<ExchangeLogView>> ListLogAsync(DateTime now, int limit = 200, CancellationToken cancellationToken = default)
    {
        var rows = await db.ExchangeLog.AsNoTracking()
            .Where(e => e.Kind == ExchangeKind.Msg)
            .OrderByDescending(e => e.At)
            .Take(Math.Clamp(limit, 1, 2000))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows.Select(e => new ExchangeLogView(
                e.Id,
                e.Direction,
                CorrespondenceAr.Direction(e.Direction),
                e.FileName,
                Other(e),
                e.EntityId,
                e.SignatureOk,
                e.SignatureOk ? CoreAr.CorrExchangeSignatureOk : CoreAr.CorrExchangeSignatureBad,
                e.At,
                ArabicRelativeTime.Describe(e.At, now))),
        ];
    }

    /// <summary>
    /// Opens the file and decides what it is. The container is opened twice on purpose: once
    /// without chain verification, only to learn which organisation produced it (the producer's
    /// own signature over the manifest is checked either way), and — when that organisation is
    /// ours — a second time WITH the chain checked against the organisation key this
    /// installation pinned at activation. A letter from another organisation (AGREEMENT item 22)
    /// has no chain we could check, so its producer signature is all the proof there is, and the
    /// preview says plainly that it came from outside.
    /// </summary>
    private async Task<ReadResult> ReadAsync(string path, DeviceIdentity recipient, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(recipient);

        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new CorrespondenceRefusedException(CoreAr.CorrRefusedNotFound);

        var clock = new FixedUtcTimeProvider(ArabicRelativeTime.ToUtc(now));

        ExchangeMessage? message = null;
        var attachments = new List<ExchangeAttachment>();
        var signatureOk = false;
        var isInternal = false;
        string fromAr;

        try
        {
            string producerOrgId;
            using (var probe = ContainerReader.Open(path, new ContainerOpenOptions
            {
                ExpectedKind = ContainerKind.Msg,
                VerifyCertificateChain = false,
                Time = clock,
            }))
            {
                producerOrgId = probe.Manifest.Producer.Body.OrgId;
            }

            isInternal = string.Equals(producerOrgId, installation.OrgId.ToString(), StringComparison.OrdinalIgnoreCase);

            using var reader = ContainerReader.Open(path, new ContainerOpenOptions
            {
                ExpectedKind = ContainerKind.Msg,
                VerifyCertificateChain = isInternal,
                OrgSigningPublicKey = isInternal ? installation.OrgEd25519Pub : null,
                Time = clock,
            });

            var entries = reader.ReadEntries(ContainerKeySource.ForRecipient(recipient));
            if (!entries.TryGetValue(MessageEntryName, out var messageBytes))
            {
                throw new CryptoException(ErrorCode.Corrupt, "The package carries no message.");
            }

            message = JsonSerializer.Deserialize<ExchangeMessage>(Encoding.UTF8.GetString(messageBytes), Json);
            if (message is null || string.IsNullOrWhiteSpace(message.Subject))
            {
                throw new CryptoException(ErrorCode.Corrupt, "The package's message could not be read.");
            }

            // A message written by a newer build may carry fields this one would silently drop —
            // a recipient, a confidentiality level, an attachment. Refusing to register it is the
            // only honest answer; the container itself has already been proven genuine.
            if (message.FormatVersion > ExchangeMessage.CurrentFormatVersion)
            {
                throw new CryptoException(ErrorCode.UnknownKind, "The package was produced by a newer version of the product.");
            }

            foreach (var (name, bytes) in entries.Where(e => e.Key.StartsWith(AttachmentPrefix, StringComparison.Ordinal)))
            {
                var info = message.Attachments.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
                attachments.Add(new ExchangeAttachment(name[AttachmentPrefix.Length..], info?.Mime ?? "application/octet-stream", bytes));
            }

            signatureOk = true;
            fromAr = CoreAr.CorrExchangeFrom(message.SenderOrgName ?? string.Empty, message.SenderOfficeName ?? string.Empty);
        }
        catch (Exception exception) when (exception is CryptoException or InvalidDataException or JsonException)
        {
            // Every refusal the container raises — a broken signature, a revoked certificate, a
            // file that is not ours, a package not addressed to this device — becomes the same
            // plain answer: this file must not be registered. A file damaged so badly that the
            // archive or the message itself will not parse lands here too, for the same reason.
            // The technical detail stays out of the Arabic (AGREEMENT item 15).
            message = null;
            attachments.Clear();
            signatureOk = false;
            fromAr = string.Empty;
        }

        // The sender's unit is resolved once, here, and reused by the import: for an internal
        // letter the stored party name is the LOCAL unit name (the numbering step snapshots it),
        // so recognising a package that was already registered has to compare units, not names.
        var senderUnitId = signatureOk && isInternal && message is not null
            ? await ResolveSenderUnitAsync(message, cancellationToken).ConfigureAwait(false)
            : null;

        var alreadyImported = signatureOk && message is not null
            && await AlreadyImportedAsync(message, senderUnitId, cancellationToken).ConfigureAwait(false);

        // What the wording may promise depends on what was actually proven. For a package from
        // this organisation the chain was checked against the pinned organisation key, so
        // «التوقيع سليم» is the truth. For another organisation there is no shared root
        // (AGREEMENT item 22): the signature holds against the certificate the file carries, but
        // anybody can mint such a certificate naming any body, so the sentence has to say that
        // the sender itself is not vouched for.
        var signatureAr = signatureOk
            ? isInternal ? CoreAr.CorrExchangeSignatureOk : CoreAr.CorrExchangeSignatureExternal
            : CoreAr.CorrExchangeSignatureBad;

        var preview = new ExchangeImportPreview(
            signatureOk,
            signatureAr,
            fromAr,
            isInternal && signatureOk,
            isInternal && signatureOk ? CoreAr.CorrExchangeInternal : CoreAr.CorrExchangeExternal,
            message?.Subject ?? string.Empty,
            message?.OfficialNumber,
            message?.NumberIssuedAt,
            message?.BodyText,
            [.. attachments.Select(a => a.Name)],
            alreadyImported ? CoreAr.CorrExchangeAlreadyImported : null);

        return new ReadResult(preview, message, attachments, senderUnitId);
    }

    /// <summary>
    /// True when this exact package was already registered here: a live incoming row carries the
    /// sender's own number and names the same sender. For an internal letter (AGREEMENT item 49)
    /// the sender is the resolved org unit, because the numbering step replaces the stored party
    /// name with the LOCAL name of that unit and the name the package carries would never match
    /// again; only when the unit could not be resolved is that name still the thing to compare.
    /// </summary>
    private async Task<bool> AlreadyImportedAsync(ExchangeMessage message, Guid? senderUnitId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.OfficialNumber))
        {
            return false;
        }

        var number = message.OfficialNumber;
        var office = message.SenderOfficeName;
        var rows = db.Correspondence.AsNoTracking()
            .Where(c => c.Direction == InOutDirection.In
                && c.ExternalNumber == number
                && c.Status != CorrespondenceStatus.Cancelled);
        rows = senderUnitId is { } unitId
            ? rows.Where(c => c.UnitId == unitId)
            : rows.Where(c => c.PartyNameSnapshot == office);
        return await rows.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// AGREEMENT item 49: an internal letter's counterparty is picked from the structure. The
    /// sender names its own unit id; an office whose structure was updated separately may know it
    /// by its office code instead, so both are tried before giving up and leaving the unit unset
    /// (the name snapshot still carries who sent it).
    /// </summary>
    private async Task<Guid?> ResolveSenderUnitAsync(ExchangeMessage message, CancellationToken cancellationToken)
    {
        if (message.SenderOfficeUnitId is { } unitId
            && await db.OrgUnits.AsNoTracking().AnyAsync(u => u.Id == unitId, cancellationToken).ConfigureAwait(false))
        {
            return unitId;
        }

        if (string.IsNullOrWhiteSpace(message.SenderOfficeCode))
        {
            return null;
        }

        var code = message.SenderOfficeCode;
        return await db.OrgUnits.AsNoTracking()
            .Where(u => u.OfficeCode == code)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> ResolveRecipientKeyAsync(CorrespondenceRow row, CancellationToken cancellationToken)
    {
        if (row.UnitId is { } unitId)
        {
            var fromStructure = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.X25519Pub)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (fromStructure is { Length: DeviceIdentity.PublicKeySize })
            {
                return fromStructure;
            }
        }

        if (row.PartyId is { } partyId)
        {
            var fromParty = await db.Parties.AsNoTracking().Where(p => p.Id == partyId).Select(p => p.X25519Pub)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (fromParty is { Length: DeviceIdentity.PublicKeySize })
            {
                return fromParty;
            }
        }

        return null;
    }

    private static string? SenderName(ExchangeMessage message) =>
        string.IsNullOrWhiteSpace(message.SenderOfficeName) ? message.SenderOrgName : message.SenderOfficeName;

    private static string? Other(ExchangeLogEntry entry) =>
        string.IsNullOrWhiteSpace(entry.OtherOrg) ? entry.OtherOffice : CoreAr.CorrExchangeFrom(entry.OtherOrg, entry.OtherOffice ?? string.Empty);

    private static Confidentiality ParseConfidentiality(string? value) =>
        Enum.TryParse<Confidentiality>(value, ignoreCase: true, out var parsed) ? parsed : Confidentiality.Public;

    /// <summary>
    /// Turns a directory into a full file path. The name carries the official number with the
    /// characters a file name cannot hold replaced, so two letters never overwrite each other and
    /// the recipient can see what arrived without opening it.
    /// </summary>
    private static string ResolveOutputPath(string outputPath, string officialNumber)
    {
        var extension = ContainerKinds.Extension(ContainerKind.Msg);
        if (Directory.Exists(outputPath))
        {
            var safe = new string([.. officialNumber.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)]);
            return Path.Combine(outputPath, safe + extension);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        return outputPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? outputPath : outputPath + extension;
    }

    /// <summary>
    /// Removes the draft an import created when the registration that follows it refused. The
    /// cleanup deliberately ignores the caller's token — a cancelled import must not leave the
    /// stray draft behind either — and swallows its own failure, because the refusal the caller
    /// is about to see is the one that explains what happened.
    /// </summary>
    private async Task DiscardDraftAsync(Guid draftId, DateTime now)
    {
        try
        {
            await correspondence.DeleteDraftAsync(draftId, now, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CorrespondenceRefusedException or DbUpdateException or InvalidOperationException)
        {
        }
    }

    private async Task LogAsync(string action, string summaryAr, Guid entityId, CancellationToken cancellationToken)
    {
        var actor = await db.Installation.AsNoTracking().Select(i => i.EmployeeName)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        await audit.LogAsync(
            actor,
            action,
            summaryAr,
            CorrespondenceService.AuditEntityType,
            entityId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private sealed record ReadResult(
        ExchangeImportPreview Preview,
        ExchangeMessage? Message,
        IReadOnlyList<ExchangeAttachment> Attachments,
        Guid? SenderUnitId);

    /// <summary>A <see cref="TimeProvider"/> pinned to one instant, so the container's date checks use the caller's clock.</summary>
    private sealed class FixedUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}

/// <summary>One attachment's metadata, as written into <c>message.json</c>.</summary>
/// <param name="Name">Its full name inside the package, including the folder.</param>
/// <param name="Mime">Its media type.</param>
/// <param name="Size">Its plain size in bytes.</param>
public sealed record ExchangeAttachmentInfo(string Name, string Mime, long Size);

/// <summary>
/// The letter as it travels inside a <c>.wakeel-msg</c>. Deliberately a flat, self-describing
/// record: the receiving office may be running a different build, and must be able to register
/// the letter from this alone — no row ids of ours mean anything there, except the sender ids it
/// carries so a reply can be matched up.
/// </summary>
public sealed record ExchangeMessage
{
    /// <summary>The shape this record is in; a newer file is refused rather than misread.</summary>
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public Guid SenderOrgId { get; init; }

    public string? SenderOrgName { get; init; }

    public Guid SenderOfficeId { get; init; }

    public string? SenderOfficeName { get; init; }

    public Guid? SenderOfficeUnitId { get; init; }

    public string? SenderOfficeCode { get; init; }

    public string? SenderEmployeeName { get; init; }

    /// <summary>The correspondence id at the sender, so a reply can point back at it.</summary>
    public Guid SourceCorrespondenceId { get; init; }

    public string? OfficialNumber { get; init; }

    public DateTime? NumberIssuedAt { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string? Type { get; init; }

    public string? Confidentiality { get; init; }

    /// <summary>
    /// «للمستلم فقط» as the sender set it (AGREEMENT item 7). The restriction has to travel with
    /// the letter, otherwise the receiving office registers an ordinary incoming and may pass it
    /// on. An older reader that does not know the field simply ignores it, so the format version
    /// stays at <see cref="CurrentFormatVersion"/>.
    /// </summary>
    public bool RecipientOnly { get; init; }

    public string? BodyText { get; init; }

    public string? Cc { get; init; }

    /// <summary>Who the sender addressed it to, as the sender wrote it.</summary>
    public string? RecipientNameAr { get; init; }

    public IReadOnlyList<ExchangeAttachmentInfo> Attachments { get; init; } = [];
}
