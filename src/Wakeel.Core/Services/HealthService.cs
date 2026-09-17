using System.Text;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Stable ids of the health-center cards (W12); used for filtering, tests and the export.</summary>
public static class HealthComponents
{
    public const string Database = "database";
    public const string Vault = "vault";
    public const string Word = "word";
    public const string Scanner = "scanner";
    public const string Models = "models";
    public const string Clock = "clock";
    public const string Space = "space";
    public const string Backup = "backup";
    public const string Sync = "sync";
    public const string Runtime = "runtime";
    public const string Version = "version";
}

/// <summary>
/// Stable ids of the action each card offers. English identifiers, never shown to the user: the
/// UI maps them to an Arabic button label and a route (AGREEMENT item 15).
/// </summary>
public static class HealthActions
{
    public const string RestoreBackup = "restore-backup";
    public const string TakeBackup = "take-backup";
    public const string OpenVault = "open-vault";
    public const string OpenModelsFolder = "open-models-folder";
    public const string FixClock = "fix-clock";
    public const string FreeSpace = "free-space";
    public const string OpenSync = "open-sync";
    public const string InstallWord = "install-word";
    public const string ConnectScanner = "connect-scanner";
    public const string ReinstallRuntime = "reinstall-runtime";
    public const string None = "";
}

/// <summary>One health-center card.</summary>
/// <param name="Component">One of <see cref="HealthComponents"/>.</param>
/// <param name="TitleAr">Card title.</param>
/// <param name="Status">Traffic light: سليم / تحذير / عطل.</param>
/// <param name="MessageAr">One Arabic sentence saying what was found (AGREEMENT item 15: no codes, no jargon).</param>
/// <param name="ActionId">One of <see cref="HealthActions"/>, or empty when nothing can be done.</param>
/// <remarks>
/// A card says what was FOUND, never when the finding was made: the instant of the run is
/// <see cref="HealthReport.CheckedAt"/>, which the health screen draws once as the header and
/// once as the footer of every card («آخر فحص HH:mm»), exactly as the W12 mockup shows it.
/// </remarks>
public sealed record HealthCard(
    string Component,
    string TitleAr,
    HealthStatus Status,
    string MessageAr,
    string ActionId);

/// <summary>The whole health center for one run.</summary>
/// <param name="Cards">Every card, in display order.</param>
/// <param name="Overall">The worst status among the cards.</param>
/// <param name="SummaryAr">«كل شيء سليم» or «هناك N مشكلة تحتاج تدخلًا».</param>
/// <param name="Problems">How many cards are a warning or a fault.</param>
/// <param name="CheckedAt">When the run happened, UTC.</param>
public sealed record HealthReport(
    IReadOnlyList<HealthCard> Cards,
    HealthStatus Overall,
    string SummaryAr,
    int Problems,
    DateTime CheckedAt);

/// <summary>The health center (W12).</summary>
public interface IHealthService
{
    /// <summary>Runs every check and returns the cards, the summary and the worst status.</summary>
    /// <param name="now">The instant to check at.</param>
    /// <param name="persist">
    /// When true, the run is also recorded in <c>health_snapshots</c>. Only cards whose status,
    /// message or action changed since the last stored snapshot are written, so the table stays a
    /// history of what changed rather than a transcript of every quarter-hour re-check.
    /// </param>
    Task<HealthReport> CheckAsync(DateTime now, bool persist = true, CancellationToken cancellationToken = default);

    /// <summary>Renders <paramref name="report"/> as the Arabic text file «تصدير تقرير الصحة» saves.</summary>
    string ExportText(HealthReport report);
}

/// <inheritdoc cref="IHealthService"/>
/// <remarks>
/// <para>
/// <b>A check never throws.</b> Each card is produced inside its own try/catch: a probe that
/// fails, a folder that cannot be enumerated or a database that cannot answer becomes a card
/// saying so, because a health center that crashes when something is wrong is useless precisely
/// when it is needed. Nothing technical reaches the card — no exception text, no path, no code
/// (AGREEMENT item 15) — the details belong in the host log.
/// </para>
/// <para>
/// <b>The vault check samples.</b> Verifying every stored document against the index would read
/// the whole vault on every run. The check instead verifies the newest
/// <see cref="VaultSampleSize"/> indexed documents (existence and size against
/// <c>documents.size</c>) plus the total file count of the folder. A corruption older than the
/// sample is therefore found by the full verification B6 owns, not here; this card is the daily
/// smoke test, and it says nothing stronger than it checked.
/// </para>
/// </remarks>
public sealed class HealthService(
    WakeelDb db,
    WakeelPaths paths,
    IClockGuard clockGuard,
    IWordProbe word,
    IScannerProbe scanner,
    IDiskSpaceProbe disk,
    IRuntimeProbe runtime) : IHealthService
{
    /// <summary>Free space below this raises a warning (spec: تحذير تحت 2GB).</summary>
    public const long LowDiskWarningBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Days since the last backup before the backup card warns.</summary>
    public const int BackupWarningDays = 7;

    /// <summary>
    /// Days since the newest sync of any trusted device before the «آخر مزامنة» card warns.
    /// </summary>
    /// <remarks>
    /// Shorter than <see cref="BackupWarningDays"/> on purpose: a backup is a weekly habit, but
    /// two devices that have not met for three days are already holding work the other cannot
    /// see. Without a threshold the card answered «سليم» for a device that last synced weeks ago,
    /// which is exactly the situation the card exists to reveal.
    /// </remarks>
    public const int SyncWarningDays = 3;

    /// <summary>How many indexed documents the vault card verifies per run.</summary>
    public const int VaultSampleSize = 200;

    /// <summary>How long a stored health snapshot is kept before it is dropped.</summary>
    public static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(90);

    public async Task<HealthReport> CheckAsync(DateTime now, bool persist = true, CancellationToken cancellationToken = default)
    {
        var utcNow = ArabicRelativeTime.ToUtc(now);
        var cards = new List<HealthCard>
        {
            await CheckDatabaseAsync(cancellationToken).ConfigureAwait(false),
            await CheckVaultAsync(cancellationToken).ConfigureAwait(false),
            await CheckWordAsync(cancellationToken).ConfigureAwait(false),
            await CheckScannerAsync(cancellationToken).ConfigureAwait(false),
            await CheckModelsAsync(cancellationToken).ConfigureAwait(false),
            CheckClock(),
            await CheckSpaceAsync(cancellationToken).ConfigureAwait(false),
            await CheckBackupAsync(utcNow, cancellationToken).ConfigureAwait(false),
            await CheckSyncAsync(utcNow, cancellationToken).ConfigureAwait(false),
            await CheckRuntimeAsync(cancellationToken).ConfigureAwait(false),
            await CheckVersionAsync(cancellationToken).ConfigureAwait(false),
        };

        var problems = cards.Count(c => c.Status != HealthStatus.Ok);
        var overall = cards.Any(c => c.Status == HealthStatus.Error)
            ? HealthStatus.Error
            : problems > 0 ? HealthStatus.Warning : HealthStatus.Ok;

        if (persist)
        {
            await PersistAsync(cards, utcNow, cancellationToken).ConfigureAwait(false);
        }

        return new HealthReport(cards, overall, CoreAr.HealthProblems(problems), problems, utcNow);
    }

    public string ExportText(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder();
        builder.AppendLine(CoreAr.HealthReportHeading);
        builder.AppendLine(CoreAr.HealthReportGeneratedAt(ArabicRelativeTime.DateTimeText(report.CheckedAt)));
        builder.AppendLine();
        builder.AppendLine(report.SummaryAr);
        builder.AppendLine();
        foreach (var card in report.Cards)
        {
            builder.AppendLine($"- {card.TitleAr} [{StatusAr(card.Status)}]: {card.MessageAr}");
        }

        return builder.ToString();
    }

    /// <summary>Arabic name of a status, for the exported report.</summary>
    public static string StatusAr(HealthStatus status) => status switch
    {
        HealthStatus.Ok => CoreAr.HealthReportStatusOk,
        HealthStatus.Warning => CoreAr.HealthReportStatusWarning,
        _ => CoreAr.HealthReportStatusError,
    };

    /// <summary>
    /// Writes this run's cards to <c>health_snapshots</c>, but only the ones that say something
    /// new, and drops snapshots older than <see cref="SnapshotRetention"/>.
    /// </summary>
    /// <remarks>
    /// W12 re-checks every fifteen minutes, so writing all eleven cards every time would add
    /// about a thousand rows a day, forever, to a database the office backs up and syncs — for a
    /// history that repeats the same eleven answers. A snapshot is worth keeping when it records
    /// a CHANGE: the run that first found the disk low, and the run that found it fixed. So a
    /// card is stored only when its status, message or action differs from the newest stored
    /// snapshot for that component, which leaves the history readable as a list of events.
    /// </remarks>
    private async Task PersistAsync(IReadOnlyList<HealthCard> cards, DateTime utcNow, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var card in cards)
        {
            var component = card.Component;
            var action = card.ActionId.Length == 0 ? null : card.ActionId;
            var newest = await db.HealthSnapshots.AsNoTracking()
                .Where(s => s.Component == component)
                .OrderByDescending(s => s.CheckedAt)
                .Select(s => new { s.Status, s.MessageAr, s.Action })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (newest is not null
                && newest.Status == card.Status
                && string.Equals(newest.MessageAr, card.MessageAr, StringComparison.Ordinal)
                && string.Equals(newest.Action, action, StringComparison.Ordinal))
            {
                continue;
            }

            db.HealthSnapshots.Add(new HealthSnapshot
            {
                Component = component,
                Status = card.Status,
                MessageAr = card.MessageAr,
                Action = action,
                CheckedAt = utcNow,
            });
            added++;
        }

        if (added == 0)
        {
            return;
        }

        // The prune and the insert are one change to the history. ExecuteDeleteAsync runs
        // immediately and outside the change tracker, so without a transaction a failing save
        // would leave the old rows already gone and the new ones never written: the history would
        // lose entries without gaining the ones that justified dropping them. The insert goes
        // first so the prune can only ever commit alongside it. A caller that already opened a
        // transaction keeps ownership of it.
        var floor = utcNow - SnapshotRetention;
        var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await db.HealthSnapshots.Where(s => s.CheckedAt < floor).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Cards.
    // -------------------------------------------------------------------------------------------

    private async Task<HealthCard> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();

            var size = FileLength(paths.DbPath);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Card(HealthComponents.Database, CoreAr.HealthTitleDatabase, HealthStatus.Error, CoreAr.HealthDatabaseDamaged, HealthActions.RestoreBackup);
            }

            // The card states the finding only — integrity and size. The spec's «آخر فحص» is the
            // instant of THIS run, which HealthReport.CheckedAt already carries and the health
            // screen draws as the footer of every card; a sentence that dated itself from the
            // stored history would report the last CHANGE instead, and on a settled installation
            // that is months old while the check itself just ran.
            return Card(HealthComponents.Database, CoreAr.HealthTitleDatabase, HealthStatus.Ok, CoreAr.HealthDatabaseOk(CoreAr.Size(size)), HealthActions.None);
        }
        catch (Exception)
        {
            return Card(HealthComponents.Database, CoreAr.HealthTitleDatabase, HealthStatus.Error, CoreAr.HealthDatabaseUnavailable, HealthActions.RestoreBackup);
        }
    }

    private async Task<HealthCard> CheckVaultAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(paths.VaultDir))
            {
                return Card(HealthComponents.Vault, CoreAr.HealthTitleVault, HealthStatus.Error, CoreAr.HealthVaultMissing, HealthActions.OpenVault);
            }

            var fileCount = Directory.EnumerateFiles(paths.VaultDir, "*.bin", SearchOption.AllDirectories).Count();

            var sample = await db.Documents.AsNoTracking()
                .OrderByDescending(d => d.CreatedAt)
                .Take(VaultSampleSize)
                .Select(d => new { d.Sha256, d.Size })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var corrupt = 0;
            foreach (var document in sample)
            {
                if (string.IsNullOrWhiteSpace(document.Sha256) || document.Sha256.Length < 2)
                {
                    corrupt++;
                    continue;
                }

                var path = paths.VaultFilePath(document.Sha256);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != document.Size)
                {
                    corrupt++;
                }
            }

            return corrupt == 0
                ? Card(HealthComponents.Vault, CoreAr.HealthTitleVault, HealthStatus.Ok, CoreAr.HealthVaultOk(fileCount), HealthActions.None)
                : Card(HealthComponents.Vault, CoreAr.HealthTitleVault, HealthStatus.Error, CoreAr.HealthVaultCorrupt(corrupt), HealthActions.RestoreBackup);
        }
        catch (Exception)
        {
            // Not «غير موجود»: a permissions refusal or an I/O failure on a folder that is plainly
            // there would tell the user something untrue about their own installation.
            return Card(HealthComponents.Vault, CoreAr.HealthTitleVault, HealthStatus.Warning, CoreAr.HealthVaultUnreadable, HealthActions.OpenVault);
        }
    }

    private async Task<HealthCard> CheckWordAsync(CancellationToken cancellationToken)
    {
        WordInfo info;
        try
        {
            info = await word.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            info = WordInfo.Missing;
        }

        if (!info.Installed)
        {
            // A warning, not a fault: without Word the internal editor still works and printing
            // goes through the application itself (ARCHITECTURE.md §9).
            return Card(HealthComponents.Word, CoreAr.HealthTitleWord, HealthStatus.Warning, CoreAr.HealthWordMissing, HealthActions.InstallWord);
        }

        var message = info.Version is { Length: > 0 } version ? CoreAr.HealthWordOk(version) : CoreAr.HealthWordOkNoVersion;
        return Card(HealthComponents.Word, CoreAr.HealthTitleWord, HealthStatus.Ok, message, HealthActions.None);
    }

    private async Task<HealthCard> CheckScannerAsync(CancellationToken cancellationToken)
    {
        ScannerInfo info;
        try
        {
            info = await scanner.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            info = ScannerInfo.None;
        }

        // Severity follows the W12 mockup, which renders a missing scanner as «عطل» with
        // «إعادة الاكتشاف» — not as a warning. That is the right reading of the card: scanning is
        // how paper correspondence enters the office at all, so a scanner the program cannot see
        // is a component that is DOWN, not one that is merely degraded. The action id stays
        // ConnectScanner (the screen renders it «إعادة الاكتشاف»), because the remedy is the same
        // whether the cable is out or the driver is gone: look for it again.
        return info.Available
            ? Card(HealthComponents.Scanner, CoreAr.HealthTitleScanner, HealthStatus.Ok, CoreAr.HealthScannerOk, HealthActions.None)
            : Card(HealthComponents.Scanner, CoreAr.HealthTitleScanner, HealthStatus.Error, CoreAr.HealthScannerMissing, HealthActions.ConnectScanner);
    }

    private async Task<HealthCard> CheckModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(paths.ModelsDir))
            {
                return Card(HealthComponents.Models, CoreAr.HealthTitleModels, HealthStatus.Warning, CoreAr.HealthModelsFolderMissing, HealthActions.OpenModelsFolder);
            }

            var models = await db.Models.AsNoTracking()
                .Select(m => new { m.Name, m.Status })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var active = models.FirstOrDefault(m => m.Status == ModelStatus.Active);
            if (active is not null)
            {
                return Card(HealthComponents.Models, CoreAr.HealthTitleModels, HealthStatus.Ok, CoreAr.HealthModelsOk(active.Name), HealthActions.None);
            }

            if (models.Any(m => m.Status == ModelStatus.Preparing))
            {
                return Card(HealthComponents.Models, CoreAr.HealthTitleModels, HealthStatus.Warning, CoreAr.HealthModelsPreparing, HealthActions.None);
            }

            // No model at all is a warning, never a fault: text search works without one
            // (AGREEMENT item 30), so the office is not blocked.
            return Card(HealthComponents.Models, CoreAr.HealthTitleModels, HealthStatus.Warning, CoreAr.HealthModelsNone, HealthActions.OpenModelsFolder);
        }
        catch (Exception)
        {
            // Same rule as the vault card: «تعذّر الفحص» is the truth here, «غير موجود» is not.
            return Card(HealthComponents.Models, CoreAr.HealthTitleModels, HealthStatus.Warning, CoreAr.HealthModelsUnreadable, HealthActions.OpenModelsFolder);
        }
    }

    private HealthCard CheckClock()
    {
        var state = clockGuard.State;
        return state.Verdict switch
        {
            ClockVerdict.Ok => Card(HealthComponents.Clock, CoreAr.HealthTitleClock, HealthStatus.Ok, CoreAr.HealthClockOk, HealthActions.None),
            ClockVerdict.Suspect => Card(HealthComponents.Clock, CoreAr.HealthTitleClock, HealthStatus.Warning, CoreAr.HealthClockSuspect, HealthActions.FixClock),
            _ => Card(HealthComponents.Clock, CoreAr.HealthTitleClock, HealthStatus.Error, CoreAr.HealthClockBad, HealthActions.FixClock),
        };
    }

    private async Task<HealthCard> CheckSpaceAsync(CancellationToken cancellationToken)
    {
        DiskSpaceInfo info;
        try
        {
            info = await disk.MeasureAsync(paths.Root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            info = DiskSpaceInfo.Unknown;
        }

        if (!info.Measured)
        {
            return Card(HealthComponents.Space, CoreAr.HealthTitleSpace, HealthStatus.Warning, CoreAr.HealthSpaceUnknown, HealthActions.None);
        }

        var free = CoreAr.Size(info.FreeBytes);
        var total = CoreAr.Size(info.TotalBytes);
        return info.FreeBytes < LowDiskWarningBytes
            ? Card(HealthComponents.Space, CoreAr.HealthTitleSpace, HealthStatus.Warning, CoreAr.HealthSpaceLow(free, total), HealthActions.FreeSpace)
            : Card(HealthComponents.Space, CoreAr.HealthTitleSpace, HealthStatus.Ok, CoreAr.HealthSpaceOk(free, total), HealthActions.None);
    }

    private async Task<HealthCard> CheckBackupAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var last = await db.Backups.AsNoTracking()
            .Select(b => (DateTime?)b.At)
            .MaxAsync(cancellationToken).ConfigureAwait(false);
        if (last is null)
        {
            return Card(HealthComponents.Backup, CoreAr.HealthTitleBackup, HealthStatus.Warning, CoreAr.HealthBackupNever, HealthActions.TakeBackup);
        }

        var relative = ArabicRelativeTime.Describe(last.Value, utcNow);
        // Local days, like W08's «مضى N أيام»: the two surfaces must not disagree about the age
        // of the same backup during the hours between local midnight and the zone's offset.
        var days = AttentionService.LocalDaysSince(last.Value, utcNow);
        return days >= BackupWarningDays
            ? Card(HealthComponents.Backup, CoreAr.HealthTitleBackup, HealthStatus.Warning, CoreAr.HealthBackupOld(relative), HealthActions.TakeBackup)
            : Card(HealthComponents.Backup, CoreAr.HealthTitleBackup, HealthStatus.Ok, CoreAr.HealthBackupOk(relative), HealthActions.None);
    }

    private async Task<HealthCard> CheckSyncAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.RevokedAt == null)
            // Only the kind and the timestamp: the card names a KIND of device, never a person.
            // The health report is a file the user hands to whoever helps them, and an employee's
            // name has no business in it.
            .Select(d => new { d.Kind, d.LastSyncAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var synced = devices.Where(d => d.LastSyncAt is not null).ToList();
        if (synced.Count == 0)
        {
            return Card(HealthComponents.Sync, CoreAr.HealthTitleSync, HealthStatus.Warning, CoreAr.HealthSyncNever, HealthActions.OpenSync);
        }

        // One line per device kind, newest per kind — "آخر مزامنة (حاسوب/هاتف)" in the spec.
        var parts = new List<string>();
        foreach (var kind in new[] { DeviceKind.Pc, DeviceKind.Phone })
        {
            var newest = synced.Where(d => d.Kind == kind).Select(d => d.LastSyncAt!.Value).DefaultIfEmpty().Max();
            if (newest == default)
            {
                continue;
            }

            var label = kind == DeviceKind.Pc ? CoreAr.HealthDevicePc : CoreAr.HealthDevicePhone;
            parts.Add(CoreAr.HealthSyncDevice(label, ArabicRelativeTime.Describe(newest, utcNow)));
        }

        // «سليم» only while the office's devices are actually in step. Answering «سليم» for a
        // device that last synced weeks ago is the one failure this card exists to catch, so the
        // newest sync across every trusted device is measured against SyncWarningDays — in the
        // user's own days, like every other age the shell shows.
        var newestOverall = synced.Max(d => d.LastSyncAt!.Value);
        var summary = string.Join("، ", parts);
        return AttentionService.LocalDaysSince(newestOverall, utcNow) >= SyncWarningDays
            ? Card(HealthComponents.Sync, CoreAr.HealthTitleSync, HealthStatus.Warning, CoreAr.HealthSyncOld(summary), HealthActions.OpenSync)
            : Card(HealthComponents.Sync, CoreAr.HealthTitleSync, HealthStatus.Ok, CoreAr.HealthSyncOk(summary), HealthActions.None);
    }

    private async Task<HealthCard> CheckRuntimeAsync(CancellationToken cancellationToken)
    {
        RuntimeInfo info;
        try
        {
            info = await runtime.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            info = RuntimeInfo.Missing;
        }

        return info.Installed
            ? Card(HealthComponents.Runtime, CoreAr.HealthTitleRuntime, HealthStatus.Ok, CoreAr.HealthRuntimeOk, HealthActions.None)
            : Card(HealthComponents.Runtime, CoreAr.HealthTitleRuntime, HealthStatus.Error, CoreAr.HealthRuntimeMissing, HealthActions.ReinstallRuntime);
    }

    private async Task<HealthCard> CheckVersionAsync(CancellationToken cancellationToken)
    {
        var installation = await db.Installation.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (installation is null)
        {
            return Card(HealthComponents.Version, CoreAr.HealthTitleVersion, HealthStatus.Warning, CoreAr.HealthVersionUnknown, HealthActions.None);
        }

        var build = ArabicRelativeTime.Date(ArabicRelativeTime.ToUtc(installation.BuildDate).Date);
        return Card(HealthComponents.Version, CoreAr.HealthTitleVersion, HealthStatus.Ok, CoreAr.HealthVersionOk(installation.AppVersion, build), HealthActions.None);
    }

    private static HealthCard Card(string component, string title, HealthStatus status, string message, string action) =>
        new(component, title, status, message, action);

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
