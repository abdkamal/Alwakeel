export const meta = {
  name: 'alwakeel-b2-daily-shell',
  description: 'B2: daily shell — attention/badge/notification/reminder/clock/health/quick-capture services (Opus), then screens W08–W12, W91, W92, W94 (Opus), then the walkthrough + E2E screenshots (Sonnet); every package reviewed, fixed and re-verified by Opus',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }],
}
const REPO = 'D:/AI/Administration2'
const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents you MUST read first: docs/build/packages/B2-daily-shell.md (your specification), docs/build/ARCHITECTURE.md (§9–§12), docs/build/DATA-MODEL.md (§1, §5), docs/build/BUILD-PLAN.md (acceptance criteria), docs/AGREEMENT.md (items 15, 20, 21, 23, 26, 32, 41, 50, 52, 56), docs/design/DESIGN-GUIDE.md and docs/design/SCREENS.md; reference screenshots are PNGs under design/exports/light and design/exports/dark — VIEW every screen you build (light and dark) and match it. Foundation (read the public API before designing): src/Wakeel.Core (WakeelDb, DbSession, services, ClockCheckService, ArabicText), src/Wakeel.Design (components, Ar partial strings, bidi, theme/toast), src/Wakeel.UI (shell, first-run/login from B1, account services), src/Wakeel.Desktop (host, flags --remote-debugging-port/--window-size/--start-url), tests/Wakeel.E2E (Playwright-over-CDP harness). Never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, stop and report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/, or any path outside your allowed paths. Never run git commit/checkout/stash/reset. Build ONLY with the exact commands given ('dotnet test --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build'); on a transient file lock from a concurrent build, wait 60 s and retry. A package that touches src/Wakeel.UI, src/Wakeel.Design or src/Wakeel.Desktop is not done until the host actually runs: launch src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.exe with --remote-debugging-port=9333 (and --start-url for the screen), confirm http://127.0.0.1:9333/json/version answers, screenshot it (Playwright over CDP or PowerShell), look at it, read C:\\ProgramData\\Wakeel\\logs for errors, kill the process. All user-facing text is Arabic with no technical terms or error codes (item 15), every icon-only button has a tooltip, full RTL with bdi/isolate on Latin/numeric tokens (item 55), Arabic strings only in Wakeel.Design/Text/Ar.*.cs partial files, no mention of servers, ports, internet or PostgreSQL anywhere. Identifiers, comments and XML docs in English. Work until the package is complete and its build/tests are green; do not stop early.`

const PACKAGES = [
  {
    key: 'services', title: 'B2-SERVICES (Wakeel.Core: attention, badges, notifications, reminders, clock guard, health, quick capture)', model: 'opus',
    paths: 'src/Wakeel.Core, tests/Wakeel.Core.Tests, src/Wakeel.Desktop/Services (only new Windows probe implementations: Word presence/version, WIA scanner, disk space, WebView2/runtime version) and src/Wakeel.Desktop/App.xaml.cs (DI registration lines only)',
    build: 'dotnet build src/Wakeel.Core && dotnet test tests/Wakeel.Core.Tests && dotnet build src/Wakeel.Desktop',
    security: 'no secrets in notifications/audit, health report contains no key material or full paths of the vault content, background timers cannot run work on the UI thread, clock guard cannot be bypassed silently',
    extraCheck: 'the service interfaces are directly consumable by the B2 screens (view models with Arabic-ready values: counts, relative times via ArabicText, entity references for navigation) and the queries stay under 200 ms on a 10k-row seeded database (measure and report)',
    spec: `Build section «الخدمات (Wakeel.Core.Services)» of docs/build/packages/B2-daily-shell.md completely: AttentionService (four indicators from settings thresholds across correspondence, tasks, commitments, cases, decisions; «يحتاج إجراءً اليوم» list ordered by priority; today's meetings; last sync; backup state), BadgeService (sidebar item and group counts without double-counting the same record, bell count, inner tab counts, change event), NotificationService (create/read/dismiss on the notifications table, grouping today/yesterday/older, mark-all-read, Arabic relative time «قبل 10 دقائق» / «أمس 16:40», optional sound flag from settings), ReminderScheduler (a minute tick via an abstraction so tests drive it: meeting reminders at meeting time − reminder_minutes or the settings default (item 56), task/commitment due dates, financial-cycle reminders (item 52: N days before, the last day, then daily until the report is issued), backup reminder; idempotent via a unique key per event), ClockGuard (runs ClockCheckService at startup, every 10 minutes and on import; raises the clock banner state and blocks numbering until corrected; «تجاهل مؤقتًا» hides for the session only), HealthService (checks listed in the spec through small probe interfaces implemented in Wakeel.Desktop/Services for Word, WIA scanner, disk space and runtime; database integrity via PRAGMA integrity_check, vault folder/count/corrupt-file check, models folder state, clock, last backup, last sync per device, version; each card = status ok/warning/fault + Arabic message + action id; a summary «كل شيء سليم» / «هناك N مشكلة تحتاج تدخلًا»; exportable Arabic text report), QuickCaptureService (task/note/report-note/expense/appointment from short fields with immediate save and undo token). Seed helper for tests that creates 10k mixed rows with overdue/near/stale/pending items; measure AttentionService and BadgeService under 200 ms on it and report the timings. Tests: thresholds on crafted data, badge counts without double counting, reminder idempotency and the item-56 lead time (default + per-meeting override), relative time formatting incl. mixed digits, clock guard blocking numbering, health checks on a crafted environment (fake probes), quick capture + undo. Register everything in the Core DI extension and the desktop host.`,
  },
  {
    key: 'screens', title: 'B2-SCREENS (W08–W12, W91, W92, W94)', model: 'opus',
    paths: 'src/Wakeel.UI, src/Wakeel.Design (Text/Ar.Shell.cs new partial file; new components only if the spec needs them, e.g. WNotificationPanel, WBanner, WHealthCard), tests/Wakeel.UI.Tests, src/Wakeel.Desktop (only: opening ms-settings:dateandtime, saving the health report file, Ctrl+N global shortcut wiring)',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'no MarkupString with user data, no technical text in any state card or banner, health report saved only where the user chooses',
    extraCheck: 'each of the eight screens matches its PNG (light and dark) pixel-for-pixel in layout, texts and states; empty/loaded/error states exist; every icon-only button has a tooltip; the host was launched on each route and screenshots viewed',
    spec: `Build section «الشاشات (Wakeel.UI/Pages)» of docs/build/packages/B2-daily-shell.md completely on top of the B2 services: W08 attention center with live data (replace the B0 placeholder; KPI cards open W09 on the right tab; monthly-report banner from ReadinessService if it exists else 0; «يحتاج إجراءً اليوم» mixed rows with type chip, subject/number, party, assignee, semantic due colour, local search and badge tabs; phone expenses pending confirmation with direct confirm/reject (item 50); side column: today's meetings, last sync, backup state; «إدخال سريع» and «تحديث» buttons), W09 overdue/near/stale/pending tabs with badges, local search, table and «معالجة» opening the record, W10 notification panel (380 px dropdown from the bell, tabs all/unread N, items with type icon and relative time, click opens the record, mark all read, settings link to W86), W11 clock banner (Banner.Clock above content with the exact texts, «تصحيح الساعة» opens ms-settings:dateandtime through the desktop host, «تجاهل مؤقتًا» with tooltip), W12 health center (status card grid from HealthService, «فحص شامل الآن», «تصدير تقرير الصحة» saving an Arabic text file, filter tabs, local search), W91 standard states reference page (every WStateCard with final Ar.States texts), W92 standard dialogs reference page, W94 quick capture dialog on Ctrl+N (sections task/note/report-note/expense/appointment, validation, immediate save, toast with «تراجع»). bUnit tests for the eight screens and their states (empty/loaded/error) with mixed Arabic/English content; navigation from KPI cards/rows/notifications asserted. Launch the host on each route (--start-url) in light and dark, compare with the PNGs, and report the fidelity per screen.`,
  },
  {
    key: 'walkthrough', title: 'B2-WALKTHROUGH (service scenario + E2E screenshots)', model: 'sonnet',
    paths: 'tests/Wakeel.Walkthrough.Tests, tests/Wakeel.E2E',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.Walkthrough.Tests && dotnet test tests/Wakeel.Walkthrough.Tests --no-build && dotnet build tests/Wakeel.E2E --no-dependencies && dotnet test tests/Wakeel.E2E --no-build',
    security: 'tests use temporary folders only and never touch C:\\ProgramData\\Wakeel of the real installation',
    extraCheck: 'the walkthrough asserts real counts and timings through the public services, and the E2E screenshots of W08–W12 are produced against the design PNGs with recorded diff percentages',
    spec: `Build section «القبول» of docs/build/packages/B2-daily-shell.md: (1) tests/Wakeel.Walkthrough.Tests scenario on a temporary activated installation (reuse the B1 walkthrough helpers): create overdue, near-due and stale records plus pending phone expenses → AttentionService/BadgeService return the exact counts → a meeting with a reminder fires the notification in the bell at the right minute (drive the scheduler clock; check the item-56 default and a per-meeting override) → the clock guard blocks numbering when the clock is set back and releases after correction → quick capture saves and undo removes. (2) tests/Wakeel.E2E: extend the harness with screenshot tests for W08, W09, W10 (panel open), W11 (banner shown via a test hook or seeded state), W12, W91, W92, W94 at 1366x768 light and dark, comparing with design/exports/{light,dark}/W/*.png and recording diff percentages in the test output and tests/Wakeel.E2E/artifacts/; use a temporary ProgramData folder passed to the host (add a --data-folder flag in src/Wakeel.Desktop only if it does not exist yet — coordinate by reading App.xaml.cs first) seeded through the walkthrough helpers. Report the diff percentages per screen in notes.`,
  },
]

const BUILD_SCHEMA = { type: 'object', properties: {
  status: { type: 'string', enum: ['done', 'partial'] },
  files_changed: { type: 'array', items: { type: 'string' } },
  tests_total: { type: 'number' }, tests_passed: { type: 'number' },
  build_ok: { type: 'boolean' },
  open_issues: { type: 'array', items: { type: 'string' } },
  notes: { type: 'string' } }, required: ['status', 'files_changed', 'tests_total', 'tests_passed', 'build_ok', 'open_issues', 'notes'] }
const REVIEW_SCHEMA = { type: 'object', properties: {
  verdict: { type: 'string', enum: ['accept', 'fix'] },
  build_ok: { type: 'boolean' }, tests_ok: { type: 'boolean' },
  findings: { type: 'array', items: { type: 'object', properties: {
    severity: { type: 'string', enum: ['high', 'medium', 'low'] },
    file: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } },
    required: ['severity', 'file', 'issue', 'fix'] } },
  missing_spec_items: { type: 'array', items: { type: 'string' } },
  notes: { type: 'string' } }, required: ['verdict', 'build_ok', 'tests_ok', 'findings', 'missing_spec_items', 'notes'] }

function builderPrompt(p) {
  return `You are the builder of work package ${p.title} of الوكيل v0.21. ${COMMON}\nAllowed paths (create/edit only here): ${p.paths}.\nBuild/test command: ${p.build}\n\nSPECIFICATION:\n${p.spec}\n\nWhen done, return: status, files_changed, tests_total, tests_passed, build_ok, open_issues (anything you could not do, with the reason), notes (design decisions, measurements, screen-by-screen fidelity).`
}
function reviewPrompt(p, build, round) {
  return `You are the Opus reviewer (round ${round}) of work package ${p.title} of الوكيل v0.21. ${COMMON}\nThe builder reported: ${JSON.stringify(build)}.\nRead the specification below and the governing docs, then read every file under ${p.paths} that the package touches, VIEW the reference PNGs of every screen involved, and run exactly: ${p.build} (read the full output). Check: (1) every spec item and screen is implemented (list missing ones), (2) correctness bugs and edge cases, (3) security: ${p.security}, (4) ${p.extraCheck}, (5) tests assert real behaviour (not tautologies), warnings-as-errors clean, (6) Arabic-only user text without technical terms, tooltips on icon-only buttons, RTL/bidi compliance. Do NOT modify any file. Return verdict ('accept' only when build+tests pass and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), missing_spec_items, notes.\n\nSPECIFICATION:\n${p.spec}`
}
function fixPrompt(p, review) {
  return `You are the builder of work package ${p.title} of الوكيل v0.21 returning to apply review findings. ${COMMON}\nAllowed paths: ${p.paths}. Build/test command: ${p.build}\nReview result to address (fix every high and medium finding and every missing spec item; fix low ones too unless genuinely out of scope): ${JSON.stringify(review)}\n\nOriginal specification for reference:\n${p.spec}\n\nAfter fixing, run the build/test command until green. Return status, files_changed, tests_total, tests_passed, build_ok, open_issues, notes (what you changed per finding).`
}
async function chain(p) {
  const build = await agent(builderPrompt(p), { label: `build:${p.key}`, phase: 'Build', schema: BUILD_SCHEMA, model: p.model, effort: 'high' })
  if (!build) return { key: p.key, failed: 'build' }
  const review1 = await agent(reviewPrompt(p, build, 1), { label: `review1:${p.key}`, phase: 'Review', schema: REVIEW_SCHEMA, model: 'opus', effort: 'high' })
  const s = { key: p.key, build, review1 }
  if (review1 && review1.verdict === 'fix') {
    s.fix = await agent(fixPrompt(p, review1), { label: `fix:${p.key}`, phase: 'Fix', schema: BUILD_SCHEMA, model: p.model, effort: 'high' })
    s.verify = await agent(reviewPrompt(p, s.fix, 2), { label: `verify:${p.key}`, phase: 'Verify', schema: REVIEW_SCHEMA, model: 'opus', effort: 'medium' })
    if (s.verify && s.verify.verdict === 'fix') {
      s.fix2 = await agent(fixPrompt(p, s.verify), { label: `fix2:${p.key}`, phase: 'Fix2', schema: BUILD_SCHEMA, model: p.model, effort: 'high' })
      s.verify2 = await agent(reviewPrompt(p, s.fix2, 3), { label: `verify2:${p.key}`, phase: 'Verify2', schema: REVIEW_SCHEMA, model: 'opus', effort: 'medium' })
    }
  }
  return s
}
log('B2: services → screens → walkthrough/E2E')
const results = []
for (const p of PACKAGES) results.push(await chain(p))
return results.filter(Boolean)
