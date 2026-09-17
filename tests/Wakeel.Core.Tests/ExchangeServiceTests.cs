using System.Text;
using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Crypto;

namespace Wakeel.Core.Tests;

/// <summary>
/// AGREEMENT items 22 and 49 / B3-1 «ExchangeService»: an approved letter leaves one activated
/// installation as a signed, sealed <c>.wakeel-msg</c> and arrives at a second one as an incoming
/// letter with its own incoming number — internal when both belong to the same organisation,
/// external otherwise.
/// </summary>
public sealed class ExchangeServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly ExchangePki _pki = ExchangePki.Create(Now.AddYears(-1));
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        _pki.Dispose();
        foreach (var folder in _folders)
        {
            TestHelpers.DeleteRootQuietly(folder);
        }
    }

    [Fact]
    public async Task An_approved_letter_round_trips_between_two_offices_of_the_same_organisation()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.SenderOrg, _pki.RecipientDevice, deviceNo: 2);

        // The recipient office is a unit of the shared structure, carrying its own public key —
        // this is the «الهيكلية» half of "the recipient's key from the structure or the profile".
        var recipientUnitId = sender.AddUnit("مكتب دائرة الشؤون المالية", "F1", _pki.RecipientDevice.AgreementPublicKey);
        recipient.AddUnit("مكتب المدير", sender.Installation.OfficeCode, _pki.SenderDevice.AgreementPublicKey, sender.Installation.OfficeUnitId);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(
            subject: "بشأن اعتماد مخصصات الصيانة",
            unitId: recipientUnitId,
            partyName: null));

        var folder = NewFolder();
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = folder,
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                Attachments = [new ExchangeAttachment("المرفق.txt", "text/plain", Encoding.UTF8.GetBytes("جدول المخصصات"))],
                Time = new FixedClock(Now),
            },
            Now);

        Assert.True(File.Exists(export.Path));
        Assert.EndsWith(".wakeel-msg", export.FileName, StringComparison.Ordinal);

        var preview = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);

        Assert.True(preview.SignatureOk);
        Assert.Equal(CoreAr.CorrExchangeSignatureOk, preview.SignatureAr);
        Assert.True(preview.IsInternal);
        Assert.Equal(CoreAr.CorrExchangeInternal, preview.KindAr);
        Assert.Equal("بشأن اعتماد مخصصات الصيانة", preview.Subject);
        Assert.Equal(approved.OfficialNumber, preview.SenderNumber);
        Assert.Contains("المرفق.txt", preview.AttachmentNames);
        Assert.Null(preview.AlreadyImportedAr);

        // Nothing was registered by the preview.
        Assert.Empty(recipient.Db.Correspondence);

        var imported = await recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now);

        Assert.Equal(InOutDirection.In, imported.Registration.View.Direction);
        Assert.Equal(CorrespondenceStatus.New, imported.Registration.View.Status);
        Assert.Equal(CounterpartyKind.Internal, imported.Registration.View.CounterpartyKind);

        // The receiving office numbers it with ITS own incoming sequence; the sender's number
        // becomes «رقم الجهة».
        Assert.Equal("20260916/22001", imported.Registration.OfficialNumber);
        Assert.Equal(approved.OfficialNumber, imported.Registration.View.ExternalNumber);
        Assert.Equal(sender.Installation.OfficeUnitId, imported.Registration.View.UnitId);

        var attachment = Assert.Single(imported.Attachments);
        Assert.Equal("المرفق.txt", attachment.Name);
        Assert.Equal("جدول المخصصات", Encoding.UTF8.GetString(attachment.Content));

        // Both sides logged the exchange.
        var sent = Assert.Single(await sender.Exchange.ListLogAsync(Now));
        Assert.Equal(InOutDirection.Out, sent.Direction);
        Assert.True(sent.SignatureOk);

        var received = Assert.Single(await recipient.Exchange.ListLogAsync(Now));
        Assert.Equal(InOutDirection.In, received.Direction);
        Assert.Equal(imported.Registration.View.Id, received.EntityId);
    }

    [Fact]
    public async Task A_letter_from_another_organisation_arrives_as_an_external_incoming()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.OtherOrg, _pki.RecipientDevice, deviceNo: 2, certificateIssuer: _pki.OtherOrg);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(subject: "بشأن التنسيق المشترك"));

        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        var imported = await recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now);

        // AGREEMENT item 22: the chain cannot be checked across organisations, so the letter is
        // registered as an ordinary external incoming and the preview says so plainly.
        Assert.False(imported.Preview.IsInternal);
        Assert.Equal(CoreAr.CorrExchangeExternal, imported.Preview.KindAr);
        Assert.Equal(CounterpartyKind.External, imported.Registration.View.CounterpartyKind);
        Assert.Null(imported.Registration.View.UnitId);
        Assert.Equal(sender.Installation.OfficeName, imported.Registration.View.PartyNameAr);
        Assert.NotNull(imported.Registration.OfficialNumber);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_is_recognised_and_then_refused_as_a_duplicate()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.SenderOrg, _pki.RecipientDevice, deviceNo: 2);

        // The internal configuration of AGREEMENT item 49: the recipient's structure knows the
        // sending office, so the registration resolves its unit and the numbering step replaces
        // the stored party name with the LOCAL name of that unit. Recognising the same file again
        // therefore cannot rely on the name the package carries.
        recipient.AddUnit("مكتب المدير العام", sender.Installation.OfficeCode, _pki.SenderDevice.AgreementPublicKey, sender.Installation.OfficeUnitId);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(subject: "بشأن التزويد"));
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        var first = await recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now);

        // The stored name is the local one, not the sender's own office name — which is exactly
        // why the second read has to match on the resolved unit.
        Assert.Equal(sender.Installation.OfficeUnitId, first.Registration.View.UnitId);
        Assert.Equal("مكتب المدير العام", first.Registration.View.PartyNameAr);
        Assert.NotEqual(sender.Installation.OfficeName, first.Registration.View.PartyNameAr);

        var second = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);
        Assert.Equal(CoreAr.CorrExchangeAlreadyImported, second.AlreadyImportedAr);

        // And importing it anyway is refused, so one letter never consumes two incoming numbers.
        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now));
        Assert.Equal(CoreAr.CorrExchangeAlreadyImported, error.MessageAr);
        Assert.Equal(1, recipient.Sequence(InOutDirection.In));
        Assert.Single(recipient.Db.Correspondence);
    }

    [Fact]
    public async Task A_letter_that_was_not_approved_is_not_exported()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        var draft = await sender.Correspondence.CreateDraftAsync(sender.OutgoingInput(), Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            sender.Exchange.ExportAsync(
                new ExchangeExportRequest
                {
                    CorrespondenceId = draft.Id,
                    OutputPath = NewFolder(),
                    Producer = _pki.SenderCertificate,
                    Signer = _pki.SenderDevice,
                    RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                    Time = new FixedClock(Now),
                },
                Now));

        Assert.Equal(CoreAr.CorrExchangeNotApproved, error.MessageAr);
    }

    [Fact]
    public async Task A_cancelled_letter_keeps_its_number_but_is_never_sent()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        var approved = await sender.ApproveOutgoingAsync();
        await sender.Correspondence.CancelAsync(approved.Id, "سُحب الكتاب قبل إرساله", Now);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            sender.Exchange.ExportAsync(
                new ExchangeExportRequest
                {
                    CorrespondenceId = approved.Id,
                    OutputPath = NewFolder(),
                    Producer = _pki.SenderCertificate,
                    Signer = _pki.SenderDevice,
                    RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                    Time = new FixedClock(Now),
                },
                Now));

        // The receiving office has no way of telling a withdrawn letter from a live one.
        Assert.Equal(CoreAr.CorrExchangeCancelled, error.MessageAr);
        Assert.Empty(sender.Db.ExchangeLog.Where(e => e.Direction == InOutDirection.Out));
    }

    [Fact]
    public async Task A_package_from_another_organisation_does_not_claim_the_sender_was_verified()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.OtherOrg, _pki.RecipientDevice, deviceNo: 2, certificateIssuer: _pki.OtherOrg);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(subject: "بشأن التنسيق المشترك"));
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        var preview = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);

        // AGREEMENT item 22: there is no shared root across organisations, so the signature holds
        // against a certificate nobody here can vouch for. The wording must say exactly that.
        Assert.True(preview.SignatureOk);
        Assert.False(preview.IsInternal);
        Assert.Equal(CoreAr.CorrExchangeSignatureExternal, preview.SignatureAr);
        Assert.NotEqual(CoreAr.CorrExchangeSignatureOk, preview.SignatureAr);
    }

    [Fact]
    public async Task An_import_that_is_refused_leaves_no_draft_behind()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.OtherOrg, _pki.RecipientDevice, deviceNo: 2, certificateIssuer: _pki.OtherOrg);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(subject: "بشأن التزويد بالبيانات"));
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        // The clerk had already registered the paper copy by hand, with the same number and the
        // same body — typed with a tatweel, so it is the same party only after normalisation and
        // the import's own "already imported" check (an exact comparison) does not see it. The
        // registration that follows the import is refused all the same.
        var byHand = await recipient.RegisterIncomingAsync(recipient.IncomingInput(
            subject: "بشأن التزويد بالبيانات",
            externalNumber: approved.OfficialNumber!,
            partyName: "مكتــب رقم 1"));

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now));
        Assert.Equal(CoreAr.CorrDuplicateExact, error.MessageAr);

        // The draft the import created before it tried to register must be gone: an unnumbered
        // incoming nobody can place would otherwise sit in the list forever.
        var stored = Assert.Single(recipient.Db.Correspondence);
        Assert.Equal(byHand.Id, stored.Id);
        Assert.Equal(1, recipient.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task A_recipient_with_no_public_key_is_refused_with_a_plain_sentence()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);

        // A directory party that has no key on file yet.
        var partyId = sender.AddParty("شركة المقاولات");
        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(partyId: partyId, partyName: null));

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            sender.Exchange.ExportAsync(
                new ExchangeExportRequest
                {
                    CorrespondenceId = approved.Id,
                    OutputPath = NewFolder(),
                    Producer = _pki.SenderCertificate,
                    Signer = _pki.SenderDevice,
                    Time = new FixedClock(Now),
                },
                Now));

        Assert.Equal(CoreAr.CorrExchangeNoRecipientKey, error.MessageAr);
    }

    [Fact]
    public async Task The_key_is_taken_from_the_party_profile_when_the_recipient_is_external()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.OtherOrg, _pki.RecipientDevice, deviceNo: 2, certificateIssuer: _pki.OtherOrg);

        var partyId = sender.AddParty("هيئة أخرى", _pki.RecipientDevice.AgreementPublicKey);
        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(partyId: partyId, partyName: null));

        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                Time = new FixedClock(Now),
            },
            Now);

        var preview = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);
        Assert.True(preview.SignatureOk);
    }

    [Fact]
    public async Task A_file_addressed_to_another_device_cannot_be_opened_and_is_never_registered()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.SenderOrg, _pki.RecipientDevice, deviceNo: 2);

        var approved = await sender.ApproveOutgoingAsync();
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,

                // Sealed to somebody else entirely.
                RecipientPublicKey = _pki.OtherOrg.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        var preview = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);
        Assert.False(preview.SignatureOk);
        Assert.Equal(CoreAr.CorrExchangeSignatureBad, preview.SignatureAr);

        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now));
        Assert.Equal(CoreAr.CorrExchangeSignatureBad, error.MessageAr);
        Assert.Empty(recipient.Db.Correspondence);
        Assert.Equal(0, recipient.Sequence(InOutDirection.In));
    }

    [Fact]
    public async Task A_tampered_file_is_refused()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.SenderOrg, _pki.RecipientDevice, deviceNo: 2);

        var approved = await sender.ApproveOutgoingAsync();
        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                Time = new FixedClock(Now),
            },
            Now);

        // Flip a byte in the middle of the package, where the encrypted payload lives.
        var bytes = await File.ReadAllBytesAsync(export.Path);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(export.Path, bytes);

        var preview = await recipient.Exchange.PreviewImportAsync(export.Path, _pki.RecipientDevice, Now);
        Assert.False(preview.SignatureOk);
        Assert.Empty(recipient.Db.Correspondence);
    }

    /// <summary>
    /// AGREEMENT item 7: «للمستلم فقط» is the one flag that restricts where a letter may go, so
    /// the container is sealed to the key the structure or the party profile records for the
    /// addressee itself. A key handed in by the caller — perfectly valid, but belonging to
    /// somebody else — must not open that door.
    /// </summary>
    [Fact]
    public async Task A_recipient_only_letter_is_never_sealed_to_a_key_the_caller_supplies()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);

        // The addressee is in the structure, but this office has no key on file for it.
        var recipientUnitId = sender.AddUnit("مكتب دائرة الشؤون المالية", "F1");
        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(
            subject: "بشأن ترتيبات خاصة",
            unitId: recipientUnitId,
            partyName: null));
        await sender.Correspondence.SetRecipientOnlyAsync(approved.Id, true, Now);

        var folder = NewFolder();
        var error = await Assert.ThrowsAsync<CorrespondenceRefusedException>(() =>
            sender.Exchange.ExportAsync(
                new ExchangeExportRequest
                {
                    CorrespondenceId = approved.Id,
                    OutputPath = folder,
                    Producer = _pki.SenderCertificate,
                    Signer = _pki.SenderDevice,

                    // A real key, and one the caller holds — but not the addressee's.
                    RecipientPublicKey = _pki.RecipientDevice.AgreementPublicKey,
                    Time = new FixedClock(Now),
                },
                Now));

        Assert.Equal(CoreAr.CorrExchangeNoRecipientKey, error.MessageAr);
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>
    /// The restriction travels with the letter: the receiving office is told the sender marked it
    /// «للمستلم فقط», instead of registering an ordinary incoming it may pass on.
    /// </summary>
    [Fact]
    public async Task A_recipient_only_letter_arrives_still_marked_for_the_recipient_only()
    {
        using var sender = NewOffice(_pki.SenderOrg, _pki.SenderDevice, deviceNo: 1);
        using var recipient = NewOffice(_pki.SenderOrg, _pki.RecipientDevice, deviceNo: 2);

        var recipientUnitId = sender.AddUnit("مكتب دائرة الشؤون المالية", "F1", _pki.RecipientDevice.AgreementPublicKey);
        recipient.AddUnit("مكتب المدير", sender.Installation.OfficeCode, _pki.SenderDevice.AgreementPublicKey, sender.Installation.OfficeUnitId);

        var approved = await sender.ApproveOutgoingAsync(sender.OutgoingInput(
            subject: "بشأن ترتيبات خاصة",
            unitId: recipientUnitId,
            partyName: null));
        await sender.Correspondence.SetRecipientOnlyAsync(approved.Id, true, Now);

        var export = await sender.Exchange.ExportAsync(
            new ExchangeExportRequest
            {
                CorrespondenceId = approved.Id,
                OutputPath = NewFolder(),
                Producer = _pki.SenderCertificate,
                Signer = _pki.SenderDevice,
                Time = new FixedClock(Now),
            },
            Now);

        var imported = await recipient.Exchange.ImportAsync(export.Path, _pki.RecipientDevice, Now);

        Assert.True(imported.Registration.View.RecipientOnly);
        Assert.Equal(CoreAr.CorrRecipientOnly, imported.Registration.View.RecipientOnlyAr);
        Assert.True(recipient.Row(imported.Registration.View.Id).RecipientOnly);
    }

    /// <summary>
    /// A temporary, activated installation whose identity matches one of the test certificates,
    /// so the container reader can check the chain the way a real office would.
    /// </summary>
    private CorrespondenceWorld NewOffice(
        DeviceIdentity org,
        DeviceIdentity device,
        int deviceNo,
        DeviceIdentity? certificateIssuer = null)
    {
        var world = new CorrespondenceWorld(Now);
        var installation = world.Db.Installation.First();
        installation.OrgId = _pki.OrgIdOf(certificateIssuer ?? org);
        installation.OrgName = "الهيئة التجريبية";
        installation.OrgEd25519Pub = (certificateIssuer ?? org).SigningPublicKey;
        installation.OrgX25519Pub = (certificateIssuer ?? org).AgreementPublicKey;
        installation.OfficeName = $"مكتب رقم {deviceNo}";
        installation.OfficeCode = $"O{deviceNo}";
        installation.DeviceNo = deviceNo;
        installation.EmployeeNo = 2;
        world.Db.SaveChanges();
        return world;
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wakeel-core-tests", "exchange", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>A clock pinned to one instant, for the container's own date stamps and checks.</summary>
    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    /// <summary>Two organisations and two device identities, each with a certificate of its own.</summary>
    private sealed class ExchangePki : IDisposable
    {
        private readonly Dictionary<DeviceIdentity, Guid> _orgIds = [];

        private ExchangePki(DateTimeOffset issuedAt)
        {
            SenderOrg = DeviceIdentity.Generate();
            OtherOrg = DeviceIdentity.Generate();
            SenderDevice = DeviceIdentity.Generate();
            RecipientDevice = DeviceIdentity.Generate();
            _orgIds[SenderOrg] = Guid.CreateVersion7();
            _orgIds[OtherOrg] = Guid.CreateVersion7();

            SenderCertificate = DeviceCertificate.Issue(
                new DeviceCertificateBody(
                    _orgIds[SenderOrg].ToString(),
                    Guid.CreateVersion7().ToString(),
                    Guid.CreateVersion7().ToString(),
                    1,
                    2,
                    "secretary",
                    Wakeel.Crypto.DeviceKind.Pc,
                    SenderDevice.SigningPublicKeyText,
                    SenderDevice.AgreementPublicKeyText,
                    issuedAt,
                    _orgIds[SenderOrg].ToString()),
                SenderOrg);
        }

        public DeviceIdentity SenderOrg { get; }

        public DeviceIdentity OtherOrg { get; }

        public DeviceIdentity SenderDevice { get; }

        public DeviceIdentity RecipientDevice { get; }

        public DeviceCertificate SenderCertificate { get; }

        public static ExchangePki Create(DateTime issuedAt) => new(new DateTimeOffset(issuedAt));

        public Guid OrgIdOf(DeviceIdentity org) => _orgIds[org];

        public void Dispose()
        {
            SenderOrg.Dispose();
            OtherOrg.Dispose();
            SenderDevice.Dispose();
            RecipientDevice.Dispose();
        }
    }
}
