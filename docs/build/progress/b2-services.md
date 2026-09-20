# B2-SERVICES progress (package key: b2-services)

Scope: the «الخدمات (Wakeel.Core.Services)» section of docs/build/packages/B2-daily-shell.md.
Allowed paths: src/Wakeel.Core, tests/Wakeel.Core.Tests, src/Wakeel.Desktop/Services (new probes),
src/Wakeel.Desktop/App.xaml.cs (DI lines only), and this file.

- [x] src/Wakeel.Core/Services/CoreAr.cs — Core's own Arabic sentences in one file (health cards,
      reminder titles/bodies, quick-capture confirmations, clock banner, plural/size/number helpers
      with western digits). Rationale for not living in Wakeel.Design/Text/Ar.*.cs documented in the file.
- [x] src/Wakeel.Core/Services/ArabicRelativeTime.cs — «الآن» / «قبل 10 دقائق» / «أمس 16:40» /
      «خلال …» / «غدًا HH:mm» / «dd/MM/yyyy HH:mm», InvariantCulture digits, explicit TimeZoneInfo.
- [x] src/Wakeel.Core/Services/AttentionService.cs — four mutually-exclusive buckets from the
      settings thresholds across correspondence/tasks/commitments/cases/decisions + phone expenses,
      «يحتاج إجراءً اليوم» ordered by priority, today's meetings, last sync, backup state.
      dotnet build src/Wakeel.Core green (0 warnings, 0 errors).
- [x] src/Wakeel.Core/Services/BadgeService.cs — sidebar item + group badges (group = UNION of its
      items' record sets, so a record in «مركز الانتباه» and «المراسلات» counts once, بند 26), bell,
      inner-tab counts (W09 buckets / W10 all+unread / finance by phone-expense status), Changed event
      + Invalidate(). Reads through one AttentionService pass (IAttentionService.GetAllAsync).
- [x] src/Wakeel.Core/Services/NotificationService.cs — create/read/dismiss, today/yesterday/older
      grouping, mark-all-read, Arabic relative time, sound flag from settings; uniqueKey stored in
      notifications.source for idempotency.
- [x] src/Wakeel.Core/Services/ReminderScheduler.cs — IMinuteTicker abstraction (+ TimeProviderMinuteTicker),
      RunOnceAsync with per-event unique keys; meetings/appointments (بند 56 default + per-record override),
      task/commitment due dates, financial-cycle reminders (بند 52: N days before, end day, then daily),
      backup reminder.
- [x] src/Wakeel.Core/Services/ClockGuard.cs — startup + 10-minute + import checks, banner state,
      numbering blocked on any non-Ok verdict, «تجاهل مؤقتًا» session-only.
- [x] src/Wakeel.Core/Services/HealthProbes.cs + HealthService.cs — 11 cards, probe interfaces,
      PRAGMA integrity_check, vault sample check, summary and Arabic text export.
- [x] src/Wakeel.Core/Services/QuickCaptureService.cs — task/note/report-note/expense/appointment,
      immediate save, single-use expiring undo token that soft-deletes.
- [x] src/Wakeel.Core/Services/ServiceCollectionExtensions.cs — all of the above registered in AddWakeelCore.
      dotnet build src/Wakeel.Core green (0/0).

## Tests

- [x] tests/Wakeel.Core.Tests/DailyShellWorld.cs — temp installation + every B2 service + fake probes
      + ManualMinuteTicker + AddWithStamps (SuppressAuditStamps, so crafted updated_at survives) +
      EnsureDevice (phone_expenses.phone_device_id is a NOT NULL FK into devices).
- [x] tests/Wakeel.Core.Tests/DailyShellSeed.cs — deterministic 10k mixed-row seed (10% late,
      10% near, 10% stale per table; 3/5 phone expenses pending) returning the exact expected counts.
- [x] AttentionServiceTests (12), BadgeServiceTests (11), ArabicRelativeTimeTests (20),
      NotificationServiceTests (10), ReminderSchedulerTests (18), ClockGuardTests (11),
      HealthServiceTests (17), QuickCaptureServiceTests (14), DailyShellPerformanceTests (2),
      ServiceRegistrationTests extended (24).
- [x] Measured on 10,000 rows (median of 5 warm runs): attention snapshot 51 ms, badge refresh 54 ms,
      attention counts 43 ms — budget 200 ms. Seeding itself 1516 ms.

## Fixes made while testing

- ClockGuard.Apply compared whole ClockBannerState records, so StateChanged fired on every
  ten-minute re-check (only the timestamp had changed). Now compares only what the user can see or do.
- QuickCaptureService.CaptureExpenseAsync wrote the transaction and its ledger lines in one
  SaveChanges; WakeelDb configures no navigations, so EF ordered the ledger inserts first and the
  FK failed. Now two saves inside one database transaction (a caller's own transaction is respected).

## Desktop probes

- [x] src/Wakeel.Desktop/Services/WindowsWordProbe.cs (registry ProgID, mapped to an Office year),
      WiaScannerProbe.cs (late-bound WIA COM, scanner device type only, COM objects released),
      WindowsDiskSpaceProbe.cs (AvailableFreeSpace on the installation's own volume),
      WindowsRuntimeProbe.cs (display components' available-version query).
- [x] src/Wakeel.Desktop/App.xaml.cs — the four probes registered BEFORE AddWakeelCore so its TryAdd
      leaves them in place. dotnet build src/Wakeel.Desktop green.

## Final verification (complete)

- Exact chain: `dotnet build src/Wakeel.Core` (0 warnings / 0 errors) -> `dotnet test tests/Wakeel.Core.Tests`
  (304/304 passed, 22 s) -> `dotnet build src/Wakeel.Desktop` (0/0).
- Host check: Wakeel.Desktop.exe launched on port 9333 with an empty temp --data-folder;
  /json/version answered (Edg/153.0.4234.32), /json/list reported /first-run. CDP JPEG screenshot
  LOOKED AT: W02 renders intact in dark RTL — «مرحبًا بك في التشغيل الأول», the three-step indicator
  with step 2 active, the «ملف الإعداد» card with the drop zone, «كلمة مرور الحزمة» with the reveal
  eye, «فحص الحزمة», the signed/encrypted-package notice, footer "الوكيل v0.21 — 16/09/2026 20:24".
  No server/port/internet/database wording anywhere. Host log held exactly one line
  ("Wakeel desktop host starting up"), no errors. Process killed; 0 Wakeel.Desktop.exe remaining.

PACKAGE COMPLETE.

## Review round 2 — findings applied (2026-09-16)

- [x] HIGH (background timers on the shared session): new src/Wakeel.Core/Services/BackgroundPass.cs
      defines `BackgroundPassDispatcher` (Func<Task> -> Task, the shape of ComponentBase.InvokeAsync)
      and `BackgroundPass.Inline`. IClockGuard.StartAsync and IReminderScheduler.Start now REQUIRE a
      dispatcher and run every pass — the startup check included — through it; the contract is on
      both interfaces and in both classes' remarks. Tests: dispatcher is used, null is refused, and
      a 20-tick pass runs concurrently with a 20-query screen loop through one gate without throwing.
- [x] MEDIUM (singleton ticker refuses a second Start): TimeProviderMinuteTicker.Start now rebinds
      (StopCore then re-create) and IMinuteTicker is registered Scoped. New MinuteTickerTests with a
      ManualTimeProvider: Start(a) then Start(b) ticks b only, leaves one live timer, and a throwing
      tick does not stop the next one.
- [x] LOW: IBadgeService.Changed and IClockGuard.StateChanged now document the off-thread hazard.
- [x] LOW: the reminder horizon is read from the data (MAX reminder_minutes over planned meetings
      and appointments) instead of a 1440-minute cap; tests cover a 2880-minute meeting and appointment.
- [x] LOW: AttentionWindow.For takes the zone, derives the day from the LOCAL date and converts the
      cutoffs back to UTC (with a DST-gap guard shared with ReadTodayMeetingsAsync); DaysLate is local
      too. New AttentionWindowTests: the same row classifies identically at 01:00 and 09:00 in UTC+3.
- [x] LOW: health_snapshots — a card is stored only when its status/message/action differs from the
      newest stored one, and snapshots older than HealthService.SnapshotRetention (90 days) are pruned.
      Three tests (no-change run adds nothing, a changed card is recorded, ancient rows are dropped).
- [x] LOW: CoreAr.HealthVaultUnreadable / HealthModelsUnreadable — the catch blocks no longer claim
      a folder is missing when it merely could not be read.
- [x] LOW: AttentionService.ReadSyncStateAsync excludes revoked devices, matching the W12 sync card.
- [x] LOW: Money.Shekels leads with ₪ («₪ 42.50»), as W08's phone-expense rows show; AttentionItem.NumberAr
      now documents the bdi/isolate obligation. New MoneyTests.
- [x] Chain green: dotnet build src/Wakeel.Core (0/0) -> dotnet test tests/Wakeel.Core.Tests
      (323/323) -> dotnet build src/Wakeel.Desktop (0/0).
- [x] Host re-check after the fixes: Wakeel.Desktop.exe on port 9333 with an empty temp --data-folder;
      /json/version answered (Edg/153.0.4234.32), /json/list reported /first-run. CDP JPEG LOOKED AT:
      W02 intact in dark RTL — «مرحبًا بك في التشغيل الأول», three-step indicator with step 2 active,
      «ملف الإعداد» + drop zone, «كلمة مرور الحزمة» with the reveal eye, «فحص الحزمة», the
      signed-package notice, footer «الوكيل v0.21 — 16/09/2026 21:06». Log: one INF startup line,
      no errors. Process killed; 0 Wakeel.Desktop.exe remaining.
- [x] Performance re-measured after the local-day change: [b2-services] rows=10000 seed=1453ms
      attention-snapshot=48ms badges-refresh=52ms attention-counts=41ms budget=200ms.

REVIEW ROUND 2 COMPLETE.

## Review round 3 — findings applied (2026-09-16)

- [x] MEDIUM (AttentionItem.PartyAr always null for commitments and cases, blanking «الجهة» on
      W08 and W09): AttentionService.ReadAllAsync now projects c.PartyId for commitments and
      cases, collects the distinct ids during the six reads and resolves them in ONE extra query
      (FillPartyNamesAsync — db.Parties, AsNoTracking, Contains, into a dictionary), then fills
      PartyAr with `items[i] with { PartyAr = name }`. A row whose party is gone keeps null.
      Decisions have no party column in the data model, so they stay null by necessity.
      Test: ACommitmentAndACase_CarryTheirPartysName_SoTheGihaColumnIsNotBlank (named party on a
      commitment and a case, plus a commitment with no party that must stay null).
      Performance after the extra query: attention-snapshot 47 ms (was 48) — unchanged.
- [x] LOW (notification table scanned every minute, never pruned): NEW migration
      src/Wakeel.Core/Migrations/0002_notification_indexes.sql adds ix_notifications_source
      (source, created_at) and ix_notifications_created_at; 0001 untouched. RunOnceAsync now
      pre-loads only `Source.StartsWith(KeyPrefix) && CreatedAt >= keyFloor`. Added
      ReminderScheduler.SweepAsync: deletes DISMISSED notifications older than KeyRetention only
      (undismissed rows are never touched at any age; Lookback is far shorter than the floor, so
      no pass can still need a swept key). Tests: the sweep keeps an old undismissed row and a
      recent dismissed one and drops only the old dismissed one; a non-reminder source does not
      suppress a reminder. SchemaTests.Open_RecordsSchemaVersionOne became
      Open_RecordsEveryEmbeddedSchemaVersion (derives the expected version from the embedded
      scripts instead of hard-coding 1) plus Open_IndexesTheNotificationSourceAndCreationTime.
- [x] LOW (ClockGuard's in-flight pass survived Stop): ClockGuard now has the same shape as
      TimeProviderMinuteTicker — a CancellationTokenSource created inside the lock in StartAsync
      after StopCore(), its token passed into the interval CheckAsync, a separate
      OperationCanceledException catch, and cancel + dispose in StopCore(). Three tests with the
      ManualTimeProvider and a new RecordingClockCheckService that captures the token it was
      given: Stop cancels the running pass's token, a second Start cancels the first binding's
      token and leaves exactly one live timer, and a cancelled tick does not tear down the timer.
- [x] LOW (privacy: unused EmployeeName projection in the exported health report):
      CheckSyncAsync projects only { Kind, LastSyncAt }. Test TheSyncCard_NeverNamesAnEmployee
      asserts the seeded employee name appears neither on the card nor in ExportText.
- [x] LOW (W12 card statuses): scanner — the mockup (design/exports/preview/light/W/W12, viewed)
      renders a missing scanner as «عطل» with «إعادة الاكتشاف», so CheckScannerAsync now returns
      HealthStatus.Error (action id stays ConnectScanner; the reason is recorded in the method's
      remarks: paper correspondence enters the office through the scanner, so one the program
      cannot see is DOWN, not degraded). Sync — new HealthService.SyncWarningDays = 3 (shorter
      than BackupWarningDays on purpose, documented); the newest LastSyncAt across trusted
      devices older than that returns Warning + HealthActions.OpenSync with CoreAr.HealthSyncOld.
      Tests: a device synced 10 days ago warns, one inside the threshold stays «سليم»; the two
      existing scanner assertions updated to «عطل».
- [x] LOW (health prune committed without the insert): PersistAsync now opens a transaction when
      db.Database.CurrentTransaction is null, SaveChangesAsync FIRST, then the ExecuteDeleteAsync
      prune, then commit, disposing in a finally — the same shape as
      QuickCaptureService.CaptureExpenseAsync, and a caller-owned transaction is respected.
- [x] LOW (backup age still on UTC days): new AttentionService.BackupAgeInDays(lastBackupAt,
      utcNow, zone?) converts both sides to the local zone before taking .Date, and is now the one
      place the age is computed — ReadBackupStateAsync, ReminderScheduler's backupAgeDays and
      HealthService.CheckBackupAsync all call it. Test
      TheBackupAge_IsTheSameEarlyInTheLocalMorningAsItIsLaterTheSameDay compares 01:00 and 09:00
      local on the same local day (7 days, overdue, identical at both instants).
- [ ] LOW (CoreAr.cs vs Ar.*.cs) — NO CODE CHANGE, as the review directed: needs the owner's
      ruling. Reported in open_issues.
- [x] Chain green: dotnet build src/Wakeel.Core (0/0) -> dotnet test tests/Wakeel.Core.Tests
      (334/334) -> dotnet build src/Wakeel.Desktop (0/0).
- [x] Performance re-measured after the extra party query: [b2-services] rows=10000 seed=1446ms
      attention-snapshot=47ms badges-refresh=51ms attention-counts=43ms budget=200ms.
- [x] Host re-check: Wakeel.Desktop.exe on port 9333, --window-size=1366x768, empty temp
      --data-folder. /json/version answered (Edg/153.0.4234.32), /json/list reported /first-run.
      CDP JPEG LOOKED AT: W02 intact in dark RTL — «مرحبًا بك في التشغيل الأول», the three-step
      indicator with step 2 current, «ملف الإعداد» with the drop zone, «كلمة مرور الحزمة» with the
      reveal eye, «فحص الحزمة», the signed/encrypted-package notice, footer «الوكيل — 16/09/2026
      21:43 — v0.21». No server/port/internet/database wording. Log: one INF startup line, no
      errors. Process killed; 0 Wakeel.Desktop.exe remaining.

REVIEW ROUND 3 COMPLETE.

## REVIEW ROUND 4 (verify2-b2-services.json)

- [x] MEDIUM (W08's «الموظف» column blank on every phone-expense row): AttentionService.ReadAllAsync
      now projects e.PhoneDeviceId, collects deviceIds/devicePlaceholders beside the party ones via
      a local RememberDevice(Guid), and a new FillEmployeeNamesAsync mirrors FillPartyNamesAsync —
      ONE extra query over db.Devices { Id, EmployeeName }, then `items[i] with { AssigneeAr = name }`
      for every non-blank name. A device with no name keeps null, exactly as a vanished party does.
      Tests: APendingPhoneExpense_CarriesTheNameOfTheEmployeeWhoseDeviceSentIt (two devices, «محمد
      عوض» / «رنا حمدان», the mockup's own two rows) and
      APendingPhoneExpense_LeavesTheEmployeeBlankWhenTheDeviceHasNoName.
- [x] LOW (health database card «آخر فحص»): CheckDatabaseAsync takes utcNow, reads the newest stored
      HealthSnapshot for HealthComponents.Database and renders the new CoreAr.HealthDatabaseOk(size,
      lastCheckRelative) => «سليمة — الحجم …؛ آخر فحص …»; a first run with nothing stored keeps the
      one-argument form rather than claiming a check that never happened. To keep the change-only
      snapshot rule intact (a self-dating sentence would have written a row every quarter of an hour,
      which ARunThatFindsNothingNew_AddsNoSnapshots exists to forbid), HealthCard gained an optional
      HistoryMessageAr and PersistAsync compares and stores `HistoryMessageAr ?? MessageAr`; the
      database card stores the undated finding. Tests: TheDatabaseCard_AddsWhenItWasLastCheckedOnce
      ARunHasBeenStored asserts «آخر فحص أمس», asserts the stored row does NOT carry the clause and
      that exactly one database snapshot exists; the original test now also asserts the first run
      omits the clause.
- [x] LOW (NotificationPanel.Total/Unread counted the page): GetPanelAsync now takes Total from
      `query.CountAsync` (the filter's real total, so «الكل N» keeps growing past the limit) and
      Unread from a table-wide `DismissedAt == null && ReadAt == null` count, agreeing with
      GetUnreadCountAsync and the tab badges; the per-page unread counter is gone and both XML docs
      say what the figures mean. Tests: ThePanelsTotalCountsEveryRow_NotOnlyThePageItFetched
      (limit 10, 15 rows -> Total 15, Unread 15, page 10, Unread == GetUnreadCountAsync);
      ThePanel_IsPagedSoALongHistoryNeverLoadsWholesale now asserts the PAGE size, not Total.
- [x] LOW (name said backup, maths was generic): AttentionService.BackupAgeInDays renamed to
      LocalDaysSince(instant, utcNow, zone?) with remarks rewritten about ages in general; the four
      call sites updated (ReadBackupStateAsync, ReminderScheduler backupAgeDays, CheckBackupAsync,
      CheckSyncAsync). Pure rename.
- [x] FLAKY WALL-CLOCK ASSERTION (failed once at «200ms, budget 200ms» under parallel test runs):
      DailyShellPerformanceTests keeps its single warm-up, makes Runs = 3 timed runs and judges the
      BEST one against the unchanged 200 ms budget; the new Measurement record carries every timing
      and the failure message prints them all, so a failure says whether the code slowed down or the
      machine was busy. Measured after the change: [b2-services] rows=10000 seed=1237ms
      attention-snapshot=43ms (runs: 45ms, 43ms, 46ms) badges-refresh=42ms (runs: 47ms, 42ms, 48ms)
      attention-counts=40ms (runs: 46ms, 40ms, 42ms) budget=200ms.
- [x] Supervisor rulings recorded, no code: CoreAr.cs ratified (ARCHITECTURE §12); an absent scanner
      stays HealthStatus.Error; SyncWarningDays = 3 stays; the three non-actionable missing items are
      closed; the Windows probes stay without unit tests; IClockGuard.StartAsync /
      IReminderScheduler.Start/Stop wiring belongs to the B2 screens package and
      ClockCheckTrigger.Import to the sync package.
- [x] Chain green: dotnet build src/Wakeel.Core (0/0) -> dotnet test tests/Wakeel.Core.Tests
      (338/338, +4 tests) -> dotnet build src/Wakeel.Desktop (0/0).
- [x] Host re-check: Wakeel.Desktop.exe on port 9333, --window-size=1366x768, fresh empty temp
      --data-folder. /json/version answered (Edg/153.0.4234.32), /json/list reported /first-run.
      CDP JPEG LOOKED AT: W02 intact in dark RTL — «مرحبًا بك في التشغيل الأول», the three-step
      indicator with step 2 current («أنت هنا — الخطوة الحالية»), «ملف الإعداد» with the drop zone
      and the wakeel-setup note, «كلمة مرور الحزمة» with the reveal eye and the one-time warning,
      «فحص الحزمة», the signed/encrypted-package notice, footer «الوكيل — 16/09/2026 22:20 — v0.21».
      Latin/numeric tokens sit correctly inside the Arabic runs; no server/port/internet/database
      wording. Log: one INF startup line, no errors. Process killed; 0 Wakeel.Desktop.exe remaining.

REVIEW ROUND 4 COMPLETE.

## Review round 5 — reverting the database «آخر فحص» clause

- [x] MEDIUM (the database card dated the last CHANGE, not the last check): reverted exactly as
      prescribed. CheckDatabaseAsync lost its utcNow parameter and the health_snapshots lookup and
      returns `CoreAr.HealthDatabaseOk(CoreAr.Size(size))` again; the two-argument
      CoreAr.HealthDatabaseOk overload is gone; HealthCard.HistoryMessageAr, the historyMessage
      local, the Card helper's extra argument and PersistAsync's indirection are gone (PersistAsync
      compares and stores card.MessageAr, and its remarks no longer mention a history sentence).
      NOTE FOR THE B2 SCREENS PACKAGE: the spec's «آخر فحص» fact is served by HealthReport.CheckedAt
      — already on the record HealthService returns — which W12 draws as the per-card footer
      «آخر فحص HH:mm» and the header «آخر فحص شامل … · يتكرر كل 15 دقيقة», identical on all eleven
      cards. If a per-card instant is ever wanted on the record itself, add HealthCard.CheckedAt set
      from the run's utcNow, never from health_snapshots. HealthCard's remarks now say this.
- [x] Tests: TheDatabaseCard_AddsWhenItWasLastCheckedOnceARunHasBeenStored deleted and the
      Assert.DoesNotContain("آخر فحص", …) line removed from
      TheDatabaseCardPassesItsIntegrityCheckAndShowsTheSize, which instead now asserts
      report.CheckedAt is the instant of the run — the fact the screen binds. 338 -> 337 tests.
- [x] Untouched this round (all three confirmed correct by the review): the W08 employee name
      (FillEmployeeNamesAsync), NotificationPanel.Total/Unread, the LocalDaysSince rename and the
      best-of-three performance timing.
- [x] Chain green: dotnet build src/Wakeel.Core (0 Warning, 0 Error) -> dotnet test
      tests/Wakeel.Core.Tests (Passed 337, Failed 0) -> dotnet build src/Wakeel.Desktop (0/0).
- [x] Host re-check: Wakeel.Desktop.exe on port 9333, --window-size=1366x768, fresh empty temp
      --data-folder. /json/version answered (Edg/153.0.4234.32), /json/list reported /first-run.
      CDP JPEG LOOKED AT: W02 intact in dark RTL — «مرحبًا بك في التشغيل الأول», the three-step
      indicator with step 2 current, «ملف الإعداد» with drop zone and wakeel-setup note, «كلمة مرور
      الحزمة» with reveal eye and one-time warning, «فحص الحزمة», the signed/encrypted notice,
      footer «الوكيل — 16/09/2026 22:39 — v0.21». Log: one INF startup line, no errors. Process
      killed; 0 remaining.

REVIEW ROUND 5 COMPLETE.
