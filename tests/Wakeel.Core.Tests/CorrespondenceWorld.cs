using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using CorrespondenceRow = Wakeel.Core.Data.Entities.Correspondence;

namespace Wakeel.Core.Tests;

/// <summary>
/// A temporary installation with the B3-1 correspondence services wired over it: one database,
/// one controllable clock, and a seam for the fakes a test needs (a throwing duplicate detector,
/// a derived-document builder). Every world writes to its own temporary folder — never to the
/// real C:\ProgramData\Wakeel.
/// </summary>
internal sealed class CorrespondenceWorld : IDisposable
{
    private readonly string _root;

    public CorrespondenceWorld(DateTime? now = null, IDuplicateDetector? duplicateDetector = null, IDerivedDocumentBuilder? derivedDocuments = null)
    {
        Clock = new TestClock { UtcNow = now ?? new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc) };
        Session = TestHelpers.OpenNewSession(out _root, out _, Clock);
        Paths = WakeelPaths.ForRoot(_root);
        Paths.EnsureDirectories();

        Installation = TestHelpers.SeedInstallation(
            Db,
            activatedAt: Clock.UtcNow.AddYears(-1),
            buildDate: Clock.UtcNow.AddYears(-1));

        Settings = new SettingsService(Db, Clock);
        Ids = new IdGenerator();
        Audit = new AuditService(Db, Clock);
        ClockCheck = new ClockCheckService(Db);
        Numbers = new OfficialNumberService(Db, ClockCheck);
        Notifications = new NotificationService(Db, Settings, Ids, Clock);
        Attention = new AttentionService(Db, Settings);
        Badges = new BadgeService(Db, Attention);

        Duplicates = duplicateDetector ?? new DuplicateDetector(Db);
        Correspondence = new CorrespondenceService(Db, Numbers, ClockCheck, Duplicates, Audit);
        Referrals = new ReferralService(Db, Audit, derivedDocuments);
        FollowUps = new FollowUpService(Db, Audit, Notifications);
        Corrections = new CorrectionService(Db, Audit);
        Exchange = new ExchangeService(Db, Correspondence, Audit);

        // The real per-minute pass, so a test can assert that a follow-up reminder rings through
        // the scheduler the product runs and not only through the service in isolation.
        Reminders = new ReminderScheduler(Db, Notifications, Settings, Badges, FollowUps);
    }

    public TestClock Clock { get; }

    public DbSession Session { get; }

    public WakeelDb Db => Session.Db;

    public WakeelPaths Paths { get; }

    public string Root => _root;

    public Installation Installation { get; }

    public ISettingsService Settings { get; }

    public IIdGenerator Ids { get; }

    public IAuditService Audit { get; }

    public IClockCheckService ClockCheck { get; }

    public IOfficialNumberService Numbers { get; }

    public INotificationService Notifications { get; }

    public IAttentionService Attention { get; }

    public IBadgeService Badges { get; }

    public IDuplicateDetector Duplicates { get; }

    public ICorrespondenceService Correspondence { get; }

    public IReferralService Referrals { get; }

    public IFollowUpService FollowUps { get; }

    public ICorrectionService Corrections { get; }

    public IExchangeService Exchange { get; }

    /// <summary>The whole-product reminder pass, which now includes the follow-up reminders.</summary>
    public IReminderScheduler Reminders { get; }

    public DateTime Now => Clock.UtcNow;

    /// <summary>Adds an external party and returns its id.</summary>
    public Guid AddParty(string name, byte[]? x25519Pub = null)
    {
        var party = new Party { Name = name, Kind = PartyKind.Ministry, X25519Pub = x25519Pub };
        Db.Parties.Add(party);
        Db.SaveChanges();
        return party.Id;
    }

    /// <summary>Adds an org unit (the internal counterparty of AGREEMENT item 49) and returns its id.</summary>
    public Guid AddUnit(string name, string? officeCode = null, byte[]? x25519Pub = null, Guid? id = null)
    {
        var unit = new OrgUnit
        {
            Id = id ?? Guid.CreateVersion7(),
            Level = OrgUnitLevel.Unit,
            Name = name,
            OfficeCode = officeCode,
            X25519Pub = x25519Pub,
        };
        Db.OrgUnits.Add(unit);
        Db.SaveChanges();
        return unit.Id;
    }

    /// <summary>
    /// Adds a vault document row and returns its id. <c>referrals.derived_document_id</c> is a
    /// foreign key into <c>documents</c> and this database enforces them, so a test that links a
    /// document cannot invent an id.
    /// </summary>
    public Guid AddDocument(string originalName = "الأصل.pdf", string mime = "application/pdf")
    {
        var document = new Document
        {
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant(),
            Size = 1024,
            Mime = mime,
            OriginalName = originalName,
            Source = DocumentSource.Import,
            PageCount = 1,
            OcrStatus = OcrStatus.Pending,
        };
        Db.Documents.Add(document);
        Db.SaveChanges();
        return document.Id;
    }

    /// <summary>The template row an outgoing draft needs before its checklist is complete.</summary>
    public Guid AddLetterTemplate(string name = "قالب المراسلة")
    {
        var template = new Template { Name = name, Kind = TemplateKind.Letter, IsDefault = true };
        Db.Templates.Add(template);
        Db.SaveChanges();
        return template.Id;
    }

    /// <summary>An incoming draft with every field the registration needs.</summary>
    public CorrespondenceDraftInput IncomingInput(
        string subject = "بشأن اعتماد الموازنة",
        string externalNumber = "2026/145",
        Guid? partyId = null,
        Guid? unitId = null,
        string? partyName = "وزارة المالية") => new()
        {
            Direction = InOutDirection.In,
            Subject = subject,
            ExternalNumber = externalNumber,
            ExternalDate = Now.AddDays(-1),
            PartyId = partyId,
            UnitId = unitId,
            CounterpartyKind = unitId is null ? CounterpartyKind.External : CounterpartyKind.Internal,
            PartyNameSnapshot = partyName,
            Confidentiality = Confidentiality.Public,
        };

    /// <summary>An outgoing draft that satisfies every readiness row.</summary>
    public CorrespondenceDraftInput OutgoingInput(
        string subject = "بشأن تزويدنا بالبيانات",
        Guid? partyId = null,
        Guid? unitId = null,
        Guid? templateId = null,
        string? partyName = "بلدية المدينة") => new()
        {
            Direction = InOutDirection.Out,
            Subject = subject,
            PartyId = partyId,
            UnitId = unitId,
            CounterpartyKind = unitId is null ? CounterpartyKind.External : CounterpartyKind.Internal,
            PartyNameSnapshot = partyName,
            TemplateId = templateId ?? AddLetterTemplate(),
            BodyText = "نرجو التكرم بتزويدنا بالبيانات المطلوبة.",
            Confidentiality = Confidentiality.Public,
        };

    /// <summary>Registers a ready incoming letter and returns it.</summary>
    public async Task<CorrespondenceView> RegisterIncomingAsync(CorrespondenceDraftInput? input = null)
    {
        var draft = await Correspondence.CreateDraftAsync(input ?? IncomingInput(), Now);
        var result = await Correspondence.RegisterIncomingAsync(draft.Id, Now);
        return result.View;
    }

    /// <summary>Approves a ready outgoing letter and returns it.</summary>
    public async Task<CorrespondenceView> ApproveOutgoingAsync(CorrespondenceDraftInput? input = null)
    {
        var draft = await Correspondence.CreateDraftAsync(input ?? OutgoingInput(), Now);
        var result = await Correspondence.ApproveOutgoingAsync(draft.Id, Now);
        return result.View;
    }

    /// <summary>Reads a row straight from the database, bypassing every service.</summary>
    public CorrespondenceRow Row(Guid id) =>
        Db.Correspondence.AsNoTracking().Single(c => c.Id == id);

    /// <summary>The stored sequence for one numbering kind, or 0 when no row exists yet.</summary>
    public int Sequence(InOutDirection kind) =>
        Db.OfficialNumbers.AsNoTracking()
            .Where(n => n.Kind == kind)
            .Select(n => (int?)n.LastSeq)
            .Max() ?? 0;

    public void Dispose()
    {
        Session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }
}
