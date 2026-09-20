# design-verify progress (B1-POLISH-DESIGN, final verification round)

Addressing verify-design-polish.json (round 3): re-accept blocked only by an external DI gap
(finding 1, not this package's files); two low findings in this package's own files.

- [done] Low finding 2 (WInput.razor): widened `OnDateInputAsync`'s `catch (JSException)` to
  `catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException)`,
  matching the same widening `OnAfterRenderAsync` already had, so a getSelectionStart interop call that
  races window/renderer teardown no longer throws out of a Blazor event handler.
- [done] Low finding 3 (WInputTests.cs): added
  `DateType_OutOfOrderInteropReplies_SupersededKeystrokeIsDroppedNotEmittedStale` — leaves
  `wakeelUi.getSelectionStart` planned (no `SetResult` yet), fires two `Input()` calls so both suspend
  on their own pending invocation, then resolves the shared handler so the older ("1") keystroke's
  continuation resumes before the newer ("12") one's (bUnit/TCS FIFO order) — exactly the interleaving
  `_inputSeq` (WInput.razor) exists for. Asserts the superseded keystroke's ValueChanged is dropped
  entirely (`values == ["12"]`, not `["1","12"]`) and only the newer (value, caret) pair reaches
  `wakeelUi.setInputValue`. Needed `cut.WaitForAssertion(...)` after `SetResult` because the interop
  continuation resumes via bUnit's renderer dispatcher, not synchronously on the calling thread.
- [done] Full mandated chain green: `dotnet build src/Wakeel.Design --no-dependencies` (0/0),
  `dotnet build src/Wakeel.UI --no-dependencies` (0/0), `dotnet build tests/Wakeel.UI.Tests --no-dependencies`
  (0/0), `dotnet test tests/Wakeel.UI.Tests --no-build` -> **188/188 passed, 0 failed**. Finding 1's
  4 previously-failing MainLayout/Gallery tests are now green too: the concurrent B1 first-run package
  fixed the missing `AccountSession` registration in `tests/Wakeel.UI.Tests/WakeelTestContext.cs` while
  this round was running (confirmed via a transient `AddWakeelCore`/`AddWakeelAccount` CS1061 mid-edit,
  then a clean build one retry later) — no file of mine touches that fix.
- [blocked-external, retried per protocol] Live host re-check of W08 (`--start-url=/w08`): confirmed
  finding 1 is not yet fixed in `src/Wakeel.Desktop/App.xaml.cs` itself (only `WakeelTestContext.cs` was
  fixed so far) — the host still crashes at startup landing on `/first-run` -> `W02SetupFile`
  (`System.InvalidOperationException: no registered service of type 'System.TimeProvider'`, logged at
  `C:\ProgramData\Wakeel\logs\wakeel-20260916_002.log` 14:41:00). Retried the host launch 4x at 60s
  intervals rebuilding `src/Wakeel.Desktop --no-dependencies` each time (only `--no-dependencies` builds
  used, per the build-command rule); one attempt happened to land on `/first-run` without crashing
  (no route to `/w08` reachable without an activated installation — `MainWindow.DefaultStartPath`
  forces `/first-run` and `--start-url` cannot bypass the app's own auth gate), so the masked-date field
  still could not be re-verified live in this round either. All Wakeel.Desktop.exe processes I launched
  were killed; 0 remain. W08's own scope (the «الكل» tab badge) stays covered by bUnit
  (`W08AttentionCenter_AllTabBadge_MatchesTodayActionCount`, green) and the design preview jpg
  comparison already recorded in the prior verification round.
- Files touched this round (all within allowed paths): `src/Wakeel.Design/Components/WInput.razor`,
  `tests/Wakeel.UI.Tests/Components/WInputTests.cs`. No csproj/Directory.Build.props/
  Directory.Packages.props/Wakeel.slnx/docs (other than this file) edited; no NuGet package added; no
  git commit/checkout/stash/reset/clean run.
