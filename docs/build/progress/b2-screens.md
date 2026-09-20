# B2-SCREENS progress (package key: b2-screens)

Scope: the «الشاشات (Wakeel.UI/Pages)» section of docs/build/packages/B2-daily-shell.md
(W08, W09, W10, W11, W12, W91, W92, W94) + the shell wiring of ClockGuard/ReminderScheduler/BadgeService
+ the WButton tooltip fix + the three low findings of docs/build/reviews/B2/verify-b2-carryover.json.
Allowed paths: src/Wakeel.UI, src/Wakeel.Design (Ar.Shell.cs, WButton tooltip, new components),
tests/Wakeel.UI.Tests, src/Wakeel.Desktop (ms-settings, health report save, Ctrl+N, DI lines), this file.

## Plan (steps)

1. Foundation: WakeelDb registration + ShellServices session scope (Wakeel.UI).
2. Ar.Shell.cs (all new Arabic text for W08-W12/W91/W92/W94).
3. WButton tooltip for every variant + bUnit test.
4. Shell wiring in MainLayout (ClockGuard/ReminderScheduler start/stop, badge subscription, bell, Ctrl+N).
5. W08, W09, W10, W11, W12, W91, W92, W94.
6. bUnit tests, carryover findings, host run + screenshots.

## Done

- [x] src/Wakeel.UI/Services/Shell/ShellServices.cs — one IServiceScope per OPEN session (created on
      sign-in/unlock, disposed on lock/sign-out, because AccountSession.Db is a NEW WakeelDb after
      every lock); typed accessors for the B2 Core services; IsOpen for the screens' closed state.
- [x] src/Wakeel.UI/Services/Shell/PhoneExpenseReview.cs — pending rows with the employee name,
      «تأكيد» (books a double-entry expense transaction into the open cycle, same shape as
      QuickCaptureService) and «رفض» (AGREEMENT item 50). Lives in UI because Core's finance
      service is B5's; documented in the interface remarks.
- [x] src/Wakeel.UI/Services/Account/AccountServiceCollectionExtensions.cs — WakeelDb Scoped from
      AccountSession.Db, ShellServices, IPhoneExpenseReview.
- [x] src/Wakeel.Design/Text/Ar.Shell.cs — all Arabic text of W08-W12/W91/W92/W94.
- [x] src/Wakeel.Design/Components/WButton.razor — Tooltip now wraps EVERY variant (was Icon only);
      aria-label still only on the icon-only variant, which has no visible label.
- [x] dotnet build src/Wakeel.Desktop green (0 Warning, 0 Error).
- [x] Step 4 shell wiring in MainLayout (ClockGuard/ReminderScheduler start on sign-in/unlock via
      ComponentBase.InvokeAsync as BackgroundPassDispatcher and stop on lock/sign-out, badge
      subscription + bell count, W10/W94 hosting, Ctrl+N) — verified present and green.
- [x] Step 5 screens W08/W09/W10/W11/W12/W91/W92/W94 present and building.
- [x] Design consistency pass 1: WKpiCard gained OnClick/Tooltip/Class (the clickable KPI is now the
      component's own button instead of a raw <button> wrapper in W08); WSegmented gained Icons +
      Stacked so W94's five kinds are a design component rather than a hand-rolled tablist.
- [x] Design consistency pass 2 (W10): new Wakeel.Design components WNotificationPanel (380px panel
      chrome: head + count badge, tabs slot, scrolling list, footer) and WNotificationItem (dot,
      kind + relative time, title, body, icon square, semantic tint). W10 no longer hand-rolls the
      panel or its rows; Ar.Notifications.KindLabel added.
- [x] Design consistency pass 3 (W92): WDialog gained Notice (the dark consequence strip) and
      Inline (the surface drawn in the page). W92's five reference cards ARE real WDialog surfaces
      now; the extra «triggers» strip the preview does not have was removed.
- [x] W08 anatomy fixes against the preview: main column moved to the start (side column now on the
      end side as drawn), tabs + local search moved onto the section-header row, meetings are WKvRow
      (hour at the end), sync/backup cards are WKvRow + WChip instead of one sentence, KPI numbers
      carry their semantic colour (WKpiCard.Variant), «عرض كل المصروفات» and «التقويم» links added,
      monthly-report route corrected from /w75 (sync center) to /w70 (monthly draft).
- [x] tests/Wakeel.UI.Tests/Shell/ShellScreenContext.cs — an activated installation with an OPEN
      session, so the screens render against the real B2 services; seeding helpers for tasks,
      meetings and pending phone expenses (SuppressAuditStamps for the stale case).
- [x] tests/Wakeel.UI.Tests/Shell/AttentionScreenTests.cs — 17 tests over W08 and W09 (closed,
      empty, loaded; KPI colours and navigation to the W09 tab; row -> record; tabs; local search;
      confirm/reject of a phone expense). GREEN.
- [x] BUG FOUND AND FIXED by those tests: PhoneExpenseReview.ConfirmAsync stamped the expense
      before saving its transaction, so the UPDATE and the INSERT it depends on went to SQLite in
      one batch and «تأكيد» failed on a foreign key every time. The expense is now stamped in a
      third save after the transaction and its ledger lines.
- [x] Carry-over finding 1 closed: W07RecoveryDialog's confirm label follows the SHEET
      (NewSheetDone / Ar.Buttons.Close / Submit), and the recovered-without-session-and-without-sheet
      path now renders only the Warning card plus the closing button instead of the whole form over
      a Danger card.
- [x] tests/Wakeel.UI.Tests/Shell/ShellSurfaceTests.cs — 21 tests over W10, W11 and W94.
- [x] tests/Wakeel.UI.Tests/Shell/HealthAndReferenceScreenTests.cs — 15 tests over W12, W91, W92.
- [x] tests/Wakeel.UI.Tests/ShellTests.cs — the four B0 fixed-sample W08 tests replaced by one that
      pins the sample dataset is gone; the expenses-column regression moved to live data in
      AttentionScreenTests.
- [x] Carry-over finding 2 closed: two bUnit tests in FirstRunGuardTests drive W07 to
      RecoveryOutcome.RecoveredNotSignedIn with and without a new sheet (by removing the
      installation identity before the attempt, which is what LoginService.OpenSessionAsync
      refuses to open a session over) and assert the sentence, the closing label, the Warning tone
      and that pressing the button closes exactly once without re-submitting.
- [x] dotnet build src/Wakeel.Desktop + dotnet build/test tests/Wakeel.UI.Tests: 289/289 green.

## Host run (2026-09-17, Wakeel.Desktop.exe, port 9333, 1366x768, temporary data folder)

The data folder is activated by tests/Wakeel.UI.Tests/Shell/HostDataFolderSeed.cs (gated on
WAKEEL_HOST_ROOT), which also seeds a day's work: 1 overdue, 2 near-due, 1 stale, 2 meetings today,
2 phone expenses pending and 4 notifications. Signed in over CDP, screenshotted light and dark,
compared each with design/exports/preview/light/W/*.jpg, killed the process; the host log holds only
«Wakeel desktop host starting up» — no error, no secret, nothing about servers/ports/databases.

Fixes the comparison produced: column order of W08 (main column to the start), tabs+search onto the
section-header row, four tabs instead of seven, meetings as WKvRow with the hour at the end,
sync/backup as WKvRow + WChip, semantic colour on the KPI numbers, the notification panel moved
inside the content area (it lay over the sidebar), the W12 summary from a full card to a strip,
W09's primary header button renamed «معالجة التالي», W94's dialog widened to 560 so its two-up rows
stop overflowing, and PageHeaderState made owner-aware (routing initialises the incoming page before
disposing the outgoing one, so the outgoing Clear() was wiping the new screen's title).
Also: a failing background pass no longer takes the window down (MainLayout's dispatcher catches).
- [x] tests/Wakeel.UI.Tests/Components/DailyShellComponentTests.cs — 14 tests over the design-system
      pieces this package added (WKpiCard clickable/variant, WSegmented stacked + icons,
      WNotificationPanel slots and scrim, WNotificationItem read/unread/tinted, WDialog Inline,
      Notice, MaxWidthPx and the close cross's name + tooltip).
- [x] FINAL: dotnet build src/Wakeel.Desktop (0/0), dotnet build tests/Wakeel.UI.Tests (0/0),
      dotnet test tests/Wakeel.UI.Tests — 308/308 passed.

## Review pass 2 (2026-09-17) — applying docs/build/reviews/B2 findings

- [x] HIGH 1 (host crash): ShellServices gained a one-at-a-time gate
      (RunExclusiveAsync / RunExclusiveAsync<T> / TryRunExclusive) over the session's single
      WakeelDb. MainLayout routes the BackgroundPassDispatcher body, RefreshBadgesAsync, UndoAsync
      and the sign-out through it; LockService.UnlockAsync writes its audit lines inside it and
      LockNow writes its own only if the database is free this instant. HandleShellChanged no
      longer starts anything: it sets _needsStart and the start happens in OnAfterRenderAsync.
- [x] HIGH 2+4 (invisible «تراجع» + hand-rolled toast): UndoToast moved out of Wakeel.UI into
      src/Wakeel.Design/Components/WUndoToast.razor(.css) with the `::deep .x` space form; gallery
      entry added. Generated bundles verified to contain no `] (` sequence.
- [x] HIGH 3 (dark-theme dialog notice): .w-dialog-notice colour is var(--w-bg), so the strip
      inverts with the theme instead of being white on near-white.
- [x] MEDIUM 5: W91StandardStates.razor.css `::deep(...)` -> `::deep ...`; WToastHost.razor.css
      carried the same pre-existing bug and was fixed in the same pass.
- [x] dotnet build src/Wakeel.Desktop: 0 Warning(s), 0 Error(s).
- [x] MEDIUM 6: WTopBar gained a BellPanel slot inside `.w-topbar-bell` (already position:relative)
      and WNotificationPanel is now `position:absolute; inset-inline-start:0; inset-block-start:
      calc(100% + 8px)` — it hangs from the bell and knows nothing about the sidebar.
- [x] MEDIUM 7: W10's footer got its own AllNotificationsRoute ("/notifications") and OpenAllAsync,
      plus its own tooltip; the gear keeps SettingsRoute ("/w86").
- [x] MEDIUM 8: MainLayout starts a TimeProvider timer at result.UndoableUntil; the strip and
      «تراجع» disappear together when the window closes. ClearUndo/Dispose dispose the timer.
- [x] MEDIUM 9: W94's task form gained the export's «ملاحظة (اختياري)» textarea, and
      «تاريخ الاستحقاق» is now Required with the +7-day default pre-filled (so the star is honest).
      The note is written through the new ITaskNotes (src/Wakeel.UI/Services/Shell/TaskNotes.cs),
      because IQuickCaptureService.CaptureTaskAsync takes no note — raised in open_issues.
- [x] LOW 10: WDialog gained ConfirmTooltip/CancelTooltip (defaulting to the labels); W92 passes its
      per-dialog tooltips, and the unused DialogEntry.Trigger plus the five orphaned
      Ar.StandardDialogs.Open* constants are gone.
- [x] LOW 11: W09 pages its table through WTable's own pager (PageSize 20, Page/PageCount/
      PageChanged, footer range), resets to page 1 on tab/search change, and its columns wrap so
      seven of them fit at 1366 without a horizontal scrollbar.
- [x] LOW 12: .w08-filters no longer wraps (nowrap + min-width:0, the search shrinks first); the
      expenses table gets the same column relaxation; the KPI icon square moved into WKpiCard's own
      CSS with a tint per variant.
- [x] LOW 13: WStateCard gained an optional Variant that tints .w-state-icon; W91 sets it per entry
      (danger for the database, warning for the file/vault/failure/package/record/Word states,
      success for the save) and gained the export's «تحديث» header action.
- [x] LOW 14: Ar.Overdue.LateByDays now takes the count and uses the oblique dual «متأخر يومين»;
      the standalone «يومان» of CoreAr.DaysPhrase is untouched (it is right in W09's own column).
- [x] LOW 15: a _hadSession flag — after a session has been open the badges fall back to nothing,
      not to the fixed sample numbers.
- [x] LOW 16: W08's rows are no longer clickable; the last cell is a real icon WButton with a
      tooltip, the same interaction W09 teaches.
- [x] Root-cause sweep: every page DB call (W08 load + confirm/reject, W09 load, W10 panel/mark,
      W12 check, W94 save/undo) now goes through the gate, not only the two the review named.

## Review pass 3 (2026-09-17) — verification run and the design comparison it produced

All sixteen findings of review1-b2-screens.json were already closed by pass 2 and were re-checked in
the code and in the running host (the invisible «تراجع» now measures white on the green strip, the
phantom scroll is gone, the notice strip inverts with the theme). What follows is what the
side-by-side comparison of every screen with its export added.

- [x] Host run on a fresh activated data folder (hostroot2), seeded through HostDataFolderSeed with
      the new opt-in WAKEEL_HOST_SKEW_CLOCK, signed in over CDP, every screen photographed light and
      dark at 1366x768, each compared with design/exports/preview/{light,dark}/W. Process killed;
      the log holds one «[INF] Wakeel desktop host starting up» and nothing else.
- [x] WTooltip.razor.css: the resting bubble left the layout. `visibility:hidden` still counts
      towards the scrollable overflow of every scrolling ancestor, so EVERY table carried a
      permanent horizontal scrollbar and every header a 21px strip of stray scroll (measured: W08's
      table scrollWidth 779 vs clientWidth 702, .w-page-header 1089 vs 1068; with the bubbles hidden
      both collapsed to the client width). Now display:none at rest, display:block on
      hover/focus-within, the fade kept through allow-discrete + @starting-style. Re-measured in the
      host afterwards: 692/692 and 1058/1058, and W09's seven columns 1077/1077.
- [x] WHealthCard: the footer's timestamp now comes FIRST, so «آخر فحص» sits on every tile's start
      edge whether or not the tile has an action, as the export draws it. It used to jump to the
      opposite edge on tiles with a button, so W12's timestamps zig-zagged across the grid.
- [x] WNotificationItem: the kind's icon square moved to the row's START edge and the unread dot to
      its END edge — the export's order; the row was built mirrored.
- [x] WSelect.razor.css: the caret moved from inset-inline-start to inset-inline-end. It was sitting
      underneath the right-aligned Arabic value (invisible, overlapping) while the 34px of padding
      the control reserves on the other side stayed empty.
- [x] WStateCard: density follows the export (16px padding, 6px gap, 40px icon circle, no extra icon
      margin), so W91's thirteen reference states all fit 1366x768 as they do in the export; the
      third row used to be cut off.
- [x] W92: the export's two rows instead of a uniform two-up grid — six tracks, the three short
      dialogs spanning two each and the two field/version dialogs spanning three each, with
      «تم تحديث هذا السجل» moved into third place so the reading order matches. All five surfaces
      now fit the window.
- [x] tests/Wakeel.UI.Tests/Shell/HostDataFolderSeed.cs: opt-in SkewClock writes a ClockCheck dated
      two days ahead, which is what a clock turned back looks like to the clock check. It is the
      only way to photograph W11 in the host without touching the machine clock; W11 then rendered
      with its three exact texts and both buttons.
- [x] Four new bUnit tests pin the four component fixes (notification row order, health footer
      order, the resting tooltip's display, the select caret's edge).
- [x] FINAL: dotnet build src/Wakeel.Desktop (0/0), dotnet build tests/Wakeel.UI.Tests (0/0),
      dotnet test tests/Wakeel.UI.Tests — 325/325 passed.

## Round 3 — verify-b2-screens.json (2 medium, 10 low, 3 notes)

- [x] Findings 1 and 2 were already closed by the attempt the network cut off, verified by reading
      the files: W94QuickCapture.razor.css carries `.w94 > ::deep .w-field { width: 100% }` and
      WTabItem.razor.css draws the active tab as the filled primary pill with the count on a
      translucent ground (ruling 1 and 2).
- [x] Finding 7 / ruling 5 — MainLayout.razor: `<W11ClockBanner />` moved from above `.w-page-header`
      to below it, still above `.w-page-content`, which is where the export draws it.
- [x] Finding 9 / ruling 10 — Ar.Health.ExportFileName(stamp) added to Ar.Shell.cs; W12 no longer
      builds the suggested file name from an inline Arabic literal.
- [x] Finding 10 / ruling 10 — Ar.QuickCapture.AppointmentTimePlaceholder («ساعة:دقيقة») replaces
      the literal `HH:mm` on W94's time field; the hh:mm parse is unchanged.
- [x] Note 2 / ruling 8 — IQuickCaptureService.CaptureTaskAsync gained an OPTIONAL `noteAr`
      parameter before the cancellation token (additive: every existing call still compiles). The
      note is written into TaskItem.Description in the same save, W94 passes it there, and the
      package-local ITaskNotes/TaskNotes and its DI line are deleted. New Core test file
      tests/Wakeel.Core.Tests/QuickCaptureTaskNoteTests.cs: five facts, including that one «تراجع»
      soft-deletes the task with its note. dotnet test --filter QuickCapture: 19/19 passed.
- [x] Finding 4 / ruling 6 — WBanner gained a TrailingContent slot (between the sentence and the
      buttons, flex:none) and W08 moved WProgressBar from BodyContent into it, so the monthly banner
      is one row: sentence, readiness bar with its figure, «فتح التقرير الشهري». The figure stays 0
      with «لم تُحسب بنود الجاهزية بعد» until ReadinessService lands in B5.
- [x] Finding 5 / ruling 6 — the «آخر مزامنة» WCard now carries a WSectionHeader with an icon
      WButton («إعادة قراءة حالة المزامنة») that calls ReloadAsync.
- [x] Finding 6 / ruling 4 — W10 renders one continuous list: the day groups still decide the ORDER
      (today, yesterday, older) but the WSectionHeader day heads and .w10-group are gone; the panel
      keeps a flat _rows list. The W10 bUnit test now asserts three rows and zero WSectionHeader.
- [x] Finding 8 — W91: row gap 16→10 and the sheet's own block gap 16→12 (column gap untouched), so
      all thirteen cards clear the 768px fold with room to spare.
- [x] Finding 11 / ruling 7 — W08, W09 and W12 subscribe to ShellServices.Changed and re-read on the
      next render (never inside the event, which arrives while the unlock is still writing),
      unsubscribing in Dispose.
- [x] Findings 3 and 12 / ruling 3 — W09's second control strip under the tabs: WSelect «تصفية» over
      the types actually present in the open tab, WToggle «الأكثر تأخرًا أولًا», and the live summary
      «أطول تأخر: N · N سجلات تجاوزت أسبوعين» counted from the visible rows. «تصدير القائمة» in the
      header saves a tab-separated Arabic text file through the same IFileSaveService the health
      report uses, with every cell formula-escaped and tabs/newlines flattened. The footer now reads
      «عرض 1–8 من 12 سجلًا متأخرًا» (a counted noun per tab), and the delay cell carries the
      bucket's semantic dot through a new shared .w-dot/.w-dot--* utility in the design system's
      app.css rather than a page-local ornament.
- [x] W09's strip re-cut against the export after the first host photo: the export draws «تصفية» and
      «الأكثر تأخرًا أولًا» as compact PILLS on one line with the search, not a labelled 260px select
      and a switch. WMenu gained a first-class named trigger (Label/Icon/Tooltip → a
      .w-menu-trigger--pill styled like a secondary button) in the design system, W09 uses it for the
      filter, and the sort is a WButton that fills in (Primary) while it is on. Gallery entry and two
      bUnit tests added for the named menu.
- [x] HOST RUN (build at 08:41/08:47, three freshly seeded folders, 1366x768, CDP device metrics).
      W08: banner one row, sync card refresh present, tab pill. W09: header «تصدير القائمة», the
      three-control strip on one line (search 717-1106, pill 615-705, sort 465-603, summary at the
      end), no overlap, no horizontal overflow (1134/1134), footer «عرض 1–1 من سجل واحد متأخر», red
      dot on the delay cell; the filter menu opens with «كل الأنواع»/«مهمة» and the sort pill fills
      in when pressed. W10: five rows, ZERO section headers. W11 (skewed-clock folder): banner at
      y=151, between the page header (y=78) and the page content (y=255) — it no longer pushes the
      title down. W12: eleven cards, no overflow. W91: .w-page 712/712 with all thirteen cards (row
      gap 8px was needed; 10px still left 2px of scroll). W94: all three single fields 520px = the
      dialog's full content width, and the time placeholder reads «ساعة:دقيقة». Every process
      killed; all three logs hold only «Wakeel desktop host starting up».
- [x] FINAL: dotnet build src/Wakeel.Desktop 0/0, dotnet build tests/Wakeel.UI.Tests 0/0,
      dotnet test tests/Wakeel.UI.Tests — 339/339 passed. Core: 19/19 on --filter QuickCapture.
- [x] Ar.Notifications.GroupToday/GroupYesterday/GroupOlder deleted — dead user-facing text once the
      panel stopped drawing day headings. Re-verified: Desktop 0/0, UI tests 339/339.

## Round 4 — verify2-b2-screens.json (1 medium, 4 low)

- [x] MEDIUM 1 (WHealthCard head mirrored): .w-health-head now emits the title first and the chip
      second, and inside the title the icon leads the text — icon+title on the START edge, the
      status chip on the END edge, as the export draws all eleven tiles. New bUnit fact
      WHealthCard_LeadsWithTheIsolatedTitleAndHangsTheStatusChipOffTheEndEdge.
- [x] LOW 2 (unisolated tile title): the title is a <bdi class="w-health-title-text">, so Latin
      component names («Word 2019») keep their order inside the Arabic head (item 55). Asserted in
      the same fact.
- [x] LOW 3 (W08 counts did not add up): new NeedsActionRows drops AttentionEntityKind.PhoneExpense
      from the «يحتاج إجراء اليوم» list — no sub-tab could reach those rows and the card below draws
      every one of them in full with «تأكيد»/«رفض» (item 50). The section-header count, the tab
      counts and the visible rows all read the same source now. The W08 tab test was rewritten to
      the fixed behaviour (all 3 = tasks 3 + correspondence 0 + commitments 0, three open buttons).
- [x] LOW 4 (W09 kind glyph): W08AttentionCenter.IconFor(AttentionEntityKind) added beside ChipFor
      (mail / check-square / flag / landmark / file-text / wallet — the registry has no scales, so a
      case takes the court building), and W09's «النوع» cell draws that WIcon before the WChip.
- [x] LOW 5 (extra header action): «تحديث» removed from W09's header, so it carries the export's two
      actions; RefreshAsync went with it (the list re-reads on unlock and after each export).
- [x] dotnet build src/Wakeel.Desktop 0/0, tests/Wakeel.UI.Tests 0/0, dotnet test — 340/340 passed.
- [x] HOST RUN (round 4, one launch on a freshly seeded temporary folder, 1366x768 device metrics,
      port 9333). W08: the tab strip reads «الكل 4 · مراسلات 0 · مهام 4 · التزامات 0» — the counts
      add up and the two phone expenses live only in their own card. W09: the header carries exactly
      «معالجة التالي» and «تصدير القائمة», and the «النوع» cell draws the kind glyph on the row's
      start edge with the chip beside it, as the export does. W12: measured on the first tile —
      card 761..1106 (start = 1106), title 979..1090 on the START edge, chip 778..838 on the END
      edge, the exact inverse of what the review measured; photographed dark and light beside
      design/exports/preview/{dark,light}/W/W12 — same anatomy on all eleven tiles, «Word» isolated.
      The host log holds only «Wakeel desktop host starting up»; the process was killed, none remain.
