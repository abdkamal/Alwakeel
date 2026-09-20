# B2-WALKTHROUGH progress (package key: b2-walkthrough)

Scope: «القبول» of docs/build/packages/B2-daily-shell.md — the Walkthrough scenario and the E2E
screenshot tests for W08-W12/W91/W92/W94. Allowed paths: tests/Wakeel.Walkthrough.Tests,
tests/Wakeel.E2E, this file.

## Plan (steps)

1. tests/Wakeel.Walkthrough.Tests: DailyShellWalkthroughTests.cs — attention/badge counts,
   meeting reminder at the exact minute (default + per-meeting override), clock guard block/
   release, quick capture save/undo.
2. tests/Wakeel.E2E: extend GalleryAndAttentionCenterTests.cs (or a new file) with screenshot
   diff tests for W08, W09, W10 (panel open), W11 (skewed-clock seed), W12, W91, W92, W94 at
   1366x768 light and dark, recording diff percentages.
3. Full build/test chain green; host sanity check.

## Done

- [x] Step 1: tests/Wakeel.Walkthrough.Tests/FirstRunWorld.cs — added a `Shell` accessor
      (`Wakeel.UI.Services.Shell.ShellServices`, resolved from the same DI container the rest of
      the first run runs through) so the scenario can drive the real B2 Core services exactly as
      the shell does, instead of re-registering them. No existing member changed.
- [x] tests/Wakeel.Walkthrough.Tests/DailyShellWalkthroughTests.cs — four facts:
      - `Attention_counts_and_badges_match_one_record_seeded_in_each_bucket`: one task each in
        late/near/stale, two pending phone expenses on a seeded phone device; asserts
        AttentionService.GetCountsAsync (1/1/1/2, total 5), BadgeService.GetTabCountsAsync (same
        four numbers) and BadgeService.RefreshAsync (Attention=5, Tasks=3, Finance=2,
        Groups[daily-work]=5 — the union, not the 3+2+5=10 a naive sum would give, per AGREEMENT
        item 26).
      - `A_meeting_reminder_fires_in_the_bell_at_its_exact_minute_at_the_default_and_at_an_override`:
        two meetings, one with `ReminderMinutes = null` (settings default, 15 min) and one with an
        explicit override (40 min) — AGREEMENT item 56. Seeds a fresh Backup row first so the
        scheduler's own backup reminder (which would otherwise fire on every pass, since this
        installation never backs up) is not noise in the "nothing due yet" assertion.
        `ReminderScheduler.RunOnceAsync` driven directly at three instants: one minute before the
        earlier fire time (Created=0), at the default's exact fire minute (Created=1, notification
        present, override's not), the same minute again (Created=0, Skipped=1 — the idempotency
        key, not just "a row already exists"), then at the override's own exact fire minute
        (Created=1, both notifications now in the panel). `RunOnceAsync` is dispatcher-free by
        design (its own XML doc: "a caller invoking it directly is already on its own context"),
        so no `BackgroundPassDispatcher`/ticker was needed to drive it deterministically.
      - `The_clock_guard_blocks_numbering_the_instant_the_clock_is_set_back_and_releases_it_after_correction`:
        drives `ClockGuard.CheckAsync` directly (not the 10-minute timer) at three instants — an Ok
        baseline, a time turned back more than `ClockCheckService.BackwardTolerance` (5 min) past
        the previous check (Bad, `NumberingBlocked` true, banner `Visible` true,
        `EnsureNumberingAllowed()` throws), then corrected forward again (Ok, blocked false, banner
        hidden, throws no more).
      - `Quick_capture_saves_immediately_and_its_undo_removes_it_only_inside_the_undo_window`:
        `QuickCaptureService.CaptureTaskAsync` writes the row immediately (read back before undo);
        `UndoAsync` soft-deletes it (invisible to the same query afterwards, DATA-MODEL.md §0); the
        same token cannot undo twice; a second capture whose token is used after
        `IQuickCaptureService.UndoWindow` (2 min, advanced via the shared `MovableTime`) is
        refused and the record is left exactly as it was made.
- [x] Fixed one real bug while writing the tests: `WorkTaskStatus`/`TaskPriority`/`InstallationRole`/
      `DeviceKind`/`PhoneExpenseStatus`/`MeetingStatus`/`ClockVerdict` live in `Wakeel.Core.Data`,
      not `Wakeel.Core.Data.Entities` (a `using` guess, not a defect in Core) — added the missing
      `using Wakeel.Core.Data;`.
- [x] Build/test chain so far: `dotnet build src/Wakeel.Desktop` 0/0 ->
      `dotnet build tests/Wakeel.Walkthrough.Tests --no-dependencies` 0/0 ->
      `dotnet test tests/Wakeel.Walkthrough.Tests --no-build` — 24/24 passed (20 pre-existing +
      4 new), ~1m2s.
