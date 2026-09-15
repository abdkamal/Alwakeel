using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Wakeel.Core.Conventions;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;

namespace Wakeel.Core.Data;

/// <summary>
/// EF Core context over the الوكيل SQLCipher database. Table and column names are snake_case
/// (applied by a naming convention pass in <see cref="OnModelCreating"/>, not by
/// <c>EFCore.NamingConventions</c>, which is not a pinned package); enums are stored as their
/// snake_case text; every <see cref="DateTime"/> is stored as ISO-8601 UTC text; money columns
/// are <see cref="long"/> agorot; ids are <see cref="Guid"/> generated with
/// <see cref="Guid.CreateVersion7()"/>. Schema itself is created by <see cref="SchemaMigrator"/>
/// from embedded SQL scripts, not by EF migrations — this context is only used to query and
/// write rows against that schema, so no foreign-key/navigation graph is configured here; the
/// referential integrity lives in the SQL schema.
/// </summary>
public sealed class WakeelDb : DbContext
{
    private readonly IClock _clock;
    private readonly Action<DataIntegrityEvent>? _dataIntegrityHandler;
    private readonly ILogger<WakeelDb>? _logger;

    /// <summary>
    /// Local device id (installation.device_id), resolved lazily and cached for the life of this
    /// context. Only a non-empty result is ever cached: before <c>.wakeel-setup</c> has been
    /// consumed there is no installation row yet, and caching the empty answer would leave every
    /// synced row written later on this context with an empty <c>origin_device</c>.
    /// </summary>
    private string? _localDeviceId;

    /// <summary>Nesting depth of the <see cref="SuppressAuditStamps"/> scopes currently open.</summary>
    private int _auditStampSuppressions;

    /// <param name="options">Context options; the الوكيل host binds these to an already-open keyed SQLCipher connection.</param>
    /// <param name="clock">Clock used for the automatic <see cref="SyncedEntity"/> stamps.</param>
    /// <param name="logger">
    /// Optional log sink. When supplied, this context forwards <see cref="DataIntegrityLog"/>
    /// events (e.g. a corrupt timestamp found in a NOT NULL text column) as warnings, so the
    /// health center can surface them later.
    /// </param>
    public WakeelDb(DbContextOptions<WakeelDb> options, IClock clock, ILogger<WakeelDb>? logger = null)
        : base(options)
    {
        _clock = clock;
        _logger = logger;
        if (logger is not null)
        {
            _dataIntegrityHandler = ForwardDataIntegrityEvent;
            DataIntegrityLog.Reported += _dataIntegrityHandler;
        }
    }

    // §1 — identity, directory, devices, account, settings, numbering, audit, notifications, clock, health.
    public DbSet<Installation> Installation => Set<Installation>();

    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Account> Account => Set<Account>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<OfficialNumber> OfficialNumbers => Set<OfficialNumber>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<ClockCheck> ClockChecks => Set<ClockCheck>();

    public DbSet<HealthSnapshot> HealthSnapshots => Set<HealthSnapshot>();

    // §2 — parties.
    public DbSet<Party> Parties => Set<Party>();

    public DbSet<PartyName> PartyNames => Set<PartyName>();

    // §3 — correspondence and documents.
    public DbSet<Correspondence> Correspondence => Set<Correspondence>();

    public DbSet<CorrespondenceDocument> CorrespondenceDocuments => Set<CorrespondenceDocument>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();

    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();

    public DbSet<Referral> Referrals => Set<Referral>();

    public DbSet<Followup> Followups => Set<Followup>();

    public DbSet<Correction> Corrections => Set<Correction>();

    public DbSet<DuplicateReview> DuplicateReviews => Set<DuplicateReview>();

    public DbSet<Template> Templates => Set<Template>();

    public DbSet<ExchangeLogEntry> ExchangeLog => Set<ExchangeLogEntry>();

    // §4 — follow-up and tasks.
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Decision> Decisions => Set<Decision>();

    public DbSet<Commitment> Commitments => Set<Commitment>();

    public DbSet<CommitmentPayment> CommitmentPayments => Set<CommitmentPayment>();

    public DbSet<Obstacle> Obstacles => Set<Obstacle>();

    public DbSet<Need> Needs => Set<Need>();

    public DbSet<Note> Notes => Set<Note>();

    // §5 — meetings and calendar.
    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<MeetingAttendee> MeetingAttendees => Set<MeetingAttendee>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    // §6 — cases.
    public DbSet<Case> Cases => Set<Case>();

    public DbSet<CaseEvent> CaseEvents => Set<CaseEvent>();

    public DbSet<CaseParty> CaseParties => Set<CaseParty>();

    // §7 — employees and payroll.
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();

    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();

    public DbSet<Bonus> Bonuses => Set<Bonus>();

    public DbSet<PayrollImport> PayrollImports => Set<PayrollImport>();

    // §8 — assets and custody.
    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<CustodyMovement> CustodyMovements => Set<CustodyMovement>();

    public DbSet<AssetTransfer> AssetTransfers => Set<AssetTransfer>();

    public DbSet<InventorySession> InventorySessions => Set<InventorySession>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    // §9 — finance.
    public DbSet<FinancialCycle> FinancialCycles => Set<FinancialCycle>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<PhoneExpense> PhoneExpenses => Set<PhoneExpense>();

    public DbSet<CashCount> CashCounts => Set<CashCount>();

    // §10 — reports.
    public DbSet<MonthlyReport> MonthlyReports => Set<MonthlyReport>();

    public DbSet<ReportAddendum> ReportAddenda => Set<ReportAddendum>();

    public DbSet<OtherReport> OtherReports => Set<OtherReport>();

    // §11 — sync, backup and phone.
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    public DbSet<SyncPackage> SyncPackages => Set<SyncPackage>();

    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    public DbSet<PhoneQueueEntry> PhoneQueue => Set<PhoneQueueEntry>();

    public DbSet<PairingSession> PairingSessions => Set<PairingSession>();

    public DbSet<PinnedFile> PinnedFiles => Set<PinnedFile>();

    public DbSet<Backup> Backups => Set<Backup>();

    public DbSet<RestoreLogEntry> RestoreLog => Set<RestoreLogEntry>();

    // §12 — search and models. search_fts is an FTS5 virtual table kept in sync by SQL triggers
    // and is queried with raw SQL; it has no DbSet.
    public DbSet<SearchChunk> SearchChunks => Set<SearchChunk>();

    public DbSet<SearchModel> Models => Set<SearchModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTablesAndKeys(modelBuilder);
        ApplyNamingAndValueConventions(modelBuilder);
        ApplySoftDeleteFilters(modelBuilder);
    }

    /// <inheritdoc/>
    /// <remarks>Maintains the <see cref="SyncedEntity"/> audit columns first — see <see cref="ApplySyncedEntityStamps"/>.</remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySyncedEntityStamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc/>
    /// <remarks>Maintains the <see cref="SyncedEntity"/> audit columns first — see <see cref="ApplySyncedEntityStampsAsync"/>.</remarks>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ApplySyncedEntityStampsAsync(cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        DetachDataIntegrityHandler();
        base.Dispose();
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        DetachDataIntegrityHandler();
        return base.DisposeAsync();
    }

    /// <summary>
    /// Opens a scope in which the automatic <see cref="SyncedEntity"/> stamps are NOT applied:
    /// inside it, an Added or Modified synced row keeps the <c>CreatedAt</c>, <c>UpdatedAt</c>,
    /// <c>RowVersion</c>, <c>BaseVersion</c> and <c>OriginDevice</c> values exactly as the caller
    /// set them. This is the sync-import path (ARCHITECTURE.md §6, DATA-MODEL.md §0): an incoming
    /// row must be written with the originating device's own audit columns, or the
    /// <c>base_version</c>/<c>row_version</c> conflict detection loses its meaning. Outside the
    /// scope the stamps always apply. Scopes may be nested; the stamps resume when the outermost
    /// one is disposed. What the scope does NOT suspend is the DATA-MODEL.md §0 rule that an
    /// official row is never physically deleted — a Deleted entry still becomes a soft delete.
    /// </summary>
    /// <returns>A scope; dispose it to restore the automatic stamps.</returns>
    public IDisposable SuppressAuditStamps()
    {
        _auditStampSuppressions++;
        return new AuditStampSuppression(this);
    }

    /// <summary>
    /// Marks an official row as deleted the only way DATA-MODEL.md §0 allows: logically, by
    /// setting <c>DeletedAt</c> (plus the usual <c>UpdatedAt</c>/<c>RowVersion</c> stamps on
    /// save), so the row stays present for the other devices and for the audit trail. An entity
    /// that has not been inserted yet (state Added) is simply dropped from the change tracker,
    /// since no row exists to hide. <see cref="DbSet{TEntity}.Remove(TEntity)"/> is equivalent —
    /// <see cref="SaveChanges()"/> converts it into exactly this — but this method states the
    /// intent at the call site.
    /// </summary>
    public void SoftDelete<T>(T entity)
        where T : SyncedEntity
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entry = Entry(entity);
        if (entry.State == EntityState.Added)
        {
            entry.State = EntityState.Detached;
            return;
        }

        entity.DeletedAt ??= _clock.UtcNow;
        entry.State = EntityState.Modified;
    }

    /// <summary>
    /// Maintains the <see cref="SyncedEntity"/> audit columns automatically (DATA-MODEL.md §0),
    /// so a caller that forgets to set them by hand (as every service before this fix did) no
    /// longer writes an unattributed <c>0001-01-01</c> row that the ARCHITECTURE.md §6
    /// date-range sync export would miss. A newly Added row gets <c>CreatedAt</c>/<c>UpdatedAt</c>
    /// stamped with <see cref="IClock.UtcNow"/>, <c>RowVersion</c> set to 1, and
    /// <c>OriginDevice</c> filled from the local installation's device id — but only when the
    /// caller has not already set one. A Modified row gets <c>UpdatedAt</c> re-stamped and
    /// <c>RowVersion</c> incremented, which is also what the <c>change_log</c> AFTER UPDATE
    /// trigger's <c>at</c> column reads from. A Deleted row is turned into a soft delete instead
    /// of being removed. Inside a <see cref="SuppressAuditStamps"/> scope the Added/Modified
    /// stamps are skipped entirely and the caller's values are written as they are.
    /// </summary>
    private void ApplySyncedEntityStamps()
    {
        var entries = ChangeTracker.Entries<SyncedEntity>().ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var suppressed = _auditStampSuppressions > 0;
        var now = _clock.UtcNow;
        var deviceId = suppressed ? string.Empty : ResolveLocalDeviceId();
        foreach (var entry in entries)
        {
            StampEntry(entry, now, deviceId, suppressed);
        }
    }

    /// <summary>Async counterpart of <see cref="ApplySyncedEntityStamps"/>.</summary>
    private async Task ApplySyncedEntityStampsAsync(CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<SyncedEntity>().ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var suppressed = _auditStampSuppressions > 0;
        var now = _clock.UtcNow;
        var deviceId = suppressed
            ? string.Empty
            : await ResolveLocalDeviceIdAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            StampEntry(entry, now, deviceId, suppressed);
        }
    }

    private static void StampEntry(EntityEntry<SyncedEntity> entry, DateTime now, string localDeviceId, bool suppressStamps)
    {
        switch (entry.State)
        {
            case EntityState.Deleted:
                // DATA-MODEL.md §0 / ARCHITECTURE.md §12: an official row is never physically
                // deleted. Turning the delete into an update also keeps it visible to sync — the
                // schema has AFTER INSERT/UPDATE change_log triggers only, so a physical delete
                // would leave no trace and the row would be resurrected by the next import.
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt ??= now;
                if (!suppressStamps)
                {
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.RowVersion += 1;
                }

                break;
            case EntityState.Added:
                if (suppressStamps)
                {
                    break;
                }

                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.RowVersion = 1;
                if (string.IsNullOrEmpty(entry.Entity.OriginDevice))
                {
                    entry.Entity.OriginDevice = localDeviceId;
                }

                break;
            case EntityState.Modified:
                if (suppressStamps)
                {
                    break;
                }

                entry.Entity.UpdatedAt = now;
                entry.Entity.RowVersion += 1;
                break;
        }
    }

    private string ResolveLocalDeviceId()
    {
        if (_localDeviceId is not null)
        {
            return _localDeviceId;
        }

        var deviceId = Set<Installation>().AsNoTracking().Select(i => i.DeviceId).FirstOrDefault();
        if (deviceId == Guid.Empty)
        {
            // Not set up yet: answer empty for now, but do NOT cache it — the installation row
            // arrives later in the same context's lifetime and every later row must be attributed.
            return string.Empty;
        }

        return _localDeviceId = FormatDeviceId(deviceId);
    }

    private async Task<string> ResolveLocalDeviceIdAsync(CancellationToken cancellationToken)
    {
        if (_localDeviceId is not null)
        {
            return _localDeviceId;
        }

        var deviceId = await Set<Installation>().AsNoTracking().Select(i => i.DeviceId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (deviceId == Guid.Empty)
        {
            return string.Empty;
        }

        return _localDeviceId = FormatDeviceId(deviceId);
    }

    private void DetachDataIntegrityHandler()
    {
        if (_dataIntegrityHandler is not null)
        {
            DataIntegrityLog.Reported -= _dataIntegrityHandler;
        }
    }

    private void ForwardDataIntegrityEvent(DataIntegrityEvent integrityEvent)
        => _logger?.LogWarning(
            "Unreadable stored value in {Table}.{Column}; the text was kept in the database and read as the earliest representable date. Stored text: {StoredText}",
            integrityEvent.Table,
            integrityEvent.Column,
            integrityEvent.Text);

    /// <summary>Scope handle returned by <see cref="SuppressAuditStamps"/>; idempotent on double dispose.</summary>
    private sealed class AuditStampSuppression(WakeelDb db) : IDisposable
    {
        private WakeelDb? _db = db;

        public void Dispose()
        {
            var target = Interlocked.Exchange(ref _db, null);
            if (target is not null)
            {
                target._auditStampSuppressions--;
            }
        }
    }

    /// <summary>
    /// Formats a <see cref="Guid"/> the same way EF's default Sqlite Guid-to-TEXT mapping writes
    /// <c>installation.device_id</c> itself (upper-case, hyphenated) so that a value stamped into a
    /// row's <c>origin_device</c> column in C# compares equal, byte-for-byte, to the value the
    /// change_log triggers copy straight out of <c>installation.device_id</c> in raw SQL.
    /// </summary>
    private static string FormatDeviceId(Guid deviceId) => deviceId.ToString().ToUpperInvariant();

    private static void ConfigureTablesAndKeys(ModelBuilder modelBuilder)
    {
        // §1
        modelBuilder.Entity<Installation>().ToTable("installation");
        modelBuilder.Entity<OrgUnit>().ToTable("org_units");
        modelBuilder.Entity<Device>().ToTable("devices");
        modelBuilder.Entity<Account>().ToTable("account");
        modelBuilder.Entity<Setting>().ToTable("settings").HasKey(e => e.Key);
        modelBuilder.Entity<OfficialNumber>().ToTable("official_numbers").HasKey(e => new { e.Kind, e.Year });

        // ARCHITECTURE.md §5 (decision of 2026-09-16): last_seq is an optimistic-concurrency
        // token, so an UPDATE from a context holding a stale value affects no row and throws
        // DbUpdateConcurrencyException instead of silently reissuing a number another context
        // already issued. OfficialNumberService reloads and retries on that exception.
        modelBuilder.Entity<OfficialNumber>().Property(e => e.LastSeq).IsConcurrencyToken();
        modelBuilder.Entity<AuditLogEntry>().ToTable("audit_log");
        modelBuilder.Entity<Notification>().ToTable("notifications");
        modelBuilder.Entity<ClockCheck>().ToTable("clock_checks");
        modelBuilder.Entity<HealthSnapshot>().ToTable("health_snapshots");

        // §2
        modelBuilder.Entity<Party>().ToTable("parties");
        modelBuilder.Entity<PartyName>().ToTable("party_names");

        // §3
        modelBuilder.Entity<Correspondence>().ToTable("correspondence");
        modelBuilder.Entity<CorrespondenceDocument>().ToTable("correspondence_documents");
        modelBuilder.Entity<Document>().ToTable("documents");
        // Synced official tables since the DATA-MODEL.md §3 decision of 2026-09-16: their id is
        // the primary key and (document_id, page_no) / (document_id, entity_type, entity_id) are
        // unfiltered unique indexes in the SQL schema.
        modelBuilder.Entity<DocumentPage>().ToTable("document_pages");
        modelBuilder.Entity<DocumentLink>().ToTable("document_links");
        modelBuilder.Entity<Referral>().ToTable("referrals");
        modelBuilder.Entity<Followup>().ToTable("followups");
        modelBuilder.Entity<Correction>().ToTable("corrections");
        modelBuilder.Entity<DuplicateReview>().ToTable("duplicate_reviews");
        modelBuilder.Entity<Template>().ToTable("templates");
        modelBuilder.Entity<ExchangeLogEntry>().ToTable("exchange_log");

        // §4
        modelBuilder.Entity<TaskItem>().ToTable("tasks");
        modelBuilder.Entity<Decision>().ToTable("decisions");
        modelBuilder.Entity<Commitment>().ToTable("commitments");
        modelBuilder.Entity<CommitmentPayment>().ToTable("commitment_payments");
        modelBuilder.Entity<Obstacle>().ToTable("obstacles");
        modelBuilder.Entity<Need>().ToTable("needs");
        modelBuilder.Entity<Note>().ToTable("notes");

        // §5
        modelBuilder.Entity<Meeting>().ToTable("meetings");
        modelBuilder.Entity<MeetingAttendee>().ToTable("meeting_attendees");
        modelBuilder.Entity<Appointment>().ToTable("appointments");

        // §6
        modelBuilder.Entity<Case>().ToTable("cases");
        modelBuilder.Entity<CaseEvent>().ToTable("case_events");
        modelBuilder.Entity<CaseParty>().ToTable("case_parties");

        // §7
        modelBuilder.Entity<Employee>().ToTable("employees");
        modelBuilder.Entity<SalaryComponent>().ToTable("salary_components");
        modelBuilder.Entity<PayrollRun>().ToTable("payroll_runs");
        modelBuilder.Entity<PayrollLine>().ToTable("payroll_lines");
        modelBuilder.Entity<Bonus>().ToTable("bonuses");
        modelBuilder.Entity<PayrollImport>().ToTable("payroll_imports");

        // §8
        modelBuilder.Entity<Asset>().ToTable("assets");
        modelBuilder.Entity<CustodyMovement>().ToTable("custody_movements");
        modelBuilder.Entity<AssetTransfer>().ToTable("asset_transfers");
        modelBuilder.Entity<InventorySession>().ToTable("inventory_sessions");
        modelBuilder.Entity<InventoryItem>().ToTable("inventory_items");

        // §9
        modelBuilder.Entity<FinancialCycle>().ToTable("financial_cycles");
        modelBuilder.Entity<Category>().ToTable("categories");
        modelBuilder.Entity<Transaction>().ToTable("transactions");
        modelBuilder.Entity<LedgerEntry>().ToTable("ledger_entries");
        modelBuilder.Entity<PhoneExpense>().ToTable("phone_expenses");
        modelBuilder.Entity<CashCount>().ToTable("cash_counts");

        // §10
        modelBuilder.Entity<MonthlyReport>().ToTable("monthly_reports");
        modelBuilder.Entity<ReportAddendum>().ToTable("report_addenda");
        modelBuilder.Entity<OtherReport>().ToTable("other_reports");

        // §11
        modelBuilder.Entity<ChangeLogEntry>().ToTable("change_log").HasKey(e => e.Seq);
        modelBuilder.Entity<SyncPackage>().ToTable("sync_packages");
        modelBuilder.Entity<SyncConflict>().ToTable("sync_conflicts");
        modelBuilder.Entity<PhoneQueueEntry>().ToTable("phone_queue");
        modelBuilder.Entity<PairingSession>().ToTable("pairing_sessions");
        modelBuilder.Entity<PinnedFile>().ToTable("pinned_files").HasKey(e => e.DocumentId);
        modelBuilder.Entity<Backup>().ToTable("backups");
        modelBuilder.Entity<RestoreLogEntry>().ToTable("restore_log");

        // §12
        modelBuilder.Entity<SearchChunk>().ToTable("search_chunks");
        modelBuilder.Entity<SearchModel>().ToTable("models");
    }

    /// <summary>
    /// Naming-convention pass: every column takes the snake_case form of its C# property name
    /// (e.g. <c>OrgId</c> → <c>org_id</c>); every enum property is stored as snake_case text;
    /// every <see cref="DateTime"/> property is stored as ISO-8601 UTC text; every single-Guid
    /// primary key is app-generated (<see cref="Guid.CreateVersion7()"/>), never DB-generated.
    /// </summary>
    private static void ApplyNamingAndValueConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName() ?? entityType.ClrType.Name;
            foreach (var property in entityType.GetProperties())
            {
                var columnName = SnakeCaseText.ToSnakeCase(property.Name);
                property.SetColumnName(columnName);

                var propertyType = property.ClrType;
                var underlying = Nullable.GetUnderlyingType(propertyType);
                var isNullable = underlying != null;
                var effectiveType = underlying ?? propertyType;

                if (effectiveType == typeof(ChangeOperation))
                {
                    property.SetValueConverter(new ChangeOperationConverter());
                }
                else if (effectiveType.IsEnum)
                {
                    var converterType = isNullable
                        ? typeof(NullableEnumSnakeCaseConverter<>).MakeGenericType(effectiveType)
                        : typeof(EnumSnakeCaseConverter<>).MakeGenericType(effectiveType);
                    property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
                }
                else if (effectiveType == typeof(DateTime))
                {
                    // The table/column pair travels with the converter so an unreadable stored
                    // timestamp can be reported to DataIntegrityLog by name instead of silently
                    // becoming year 1 (health center, W12).
                    property.SetValueConverter(isNullable
                        ? new NullableDateTimeUtcConverter(tableName, columnName)
                        : new DateTimeUtcConverter(tableName, columnName));
                }
            }

            var pk = entityType.FindPrimaryKey();
            if (pk is { Properties.Count: 1 } && pk.Properties[0].ClrType == typeof(Guid))
            {
                pk.Properties[0].ValueGenerated = ValueGenerated.Never;
            }
        }
    }

    /// <summary>Applies the <c>deleted_at IS NULL</c> global query filter to every synced (SyncedEntity) table.</summary>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(SyncedEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var deletedAt = Expression.Property(parameter, nameof(SyncedEntity.DeletedAt));
            var isNull = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTime?)));
            var lambda = Expression.Lambda(isNull, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
