export const meta = {
  name: 'alwakeel-b1-admin-firstrun',
  description: 'B1: the administration tool (A01–A12) and the first run / login / lock / recovery screens (W02–W07) built in parallel by Opus, each reviewed, fixed and re-verified by Opus',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }],
}
const REPO = 'D:/AI/Administration2'
const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents you MUST read first: docs/build/packages/B1-admin-firstrun.md (your specification), docs/build/ARCHITECTURE.md (§2–§5, §9, §10, §12 and the .wakeel-setup format paragraph in §4), docs/build/DATA-MODEL.md (§1, §13), docs/build/BUILD-PLAN.md (acceptance criteria), docs/AGREEMENT.md (items referenced by the spec), docs/design/DESIGN-GUIDE.md and docs/design/SCREENS.md; the reference screenshots are PNGs under design/exports/light and design/exports/dark — VIEW every screen you build (light and dark) and match it. Foundation you build on (read the public API before designing): src/Wakeel.Crypto (keys, wraps, certificates, containers, Setup/SetupPackageWriter+Reader, pairing), src/Wakeel.Core (WakeelDb, DbSession, SchemaMigrator, services), src/Wakeel.Design (components, Ar strings, bidi, theme/toast services), src/Wakeel.UI (app shell). Never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, stop and report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/, or any path outside your allowed paths (csproj files inside your allowed paths may gain ProjectReferences only). Never run git commit/checkout/stash/reset. Another agent edits other projects in the same working tree concurrently: build ONLY with the exact commands given, and when a build fails on a transient file lock from the other agent's build, wait a minute and retry. All user-facing text is Arabic with no technical terms or error codes (item 15), every icon-only button has a tooltip, full RTL with bdi/isolate on Latin/numeric tokens (item 55), no mention of servers, ports, internet or PostgreSQL anywhere. Identifiers, comments and XML docs in English. Passwords, keys and seeds must never reach logs, the database in clear text, or UI state longer than needed. Work until the package is complete and its build/tests are green; do not stop early.`

const PACKAGES = [
  {
    key: 'admin', title: 'B1-A (مدير نظام الوكيل: A01–A12)', model: 'opus',
    paths: 'src/Wakeel.Admin.UI, src/Wakeel.Admin, tests/Wakeel.Admin.Tests',
    build: 'dotnet build src/Wakeel.Admin.UI --no-dependencies && dotnet build src/Wakeel.Admin --no-dependencies && dotnet build tests/Wakeel.Admin.Tests --no-dependencies && dotnet test tests/Wakeel.Admin.Tests --no-build',
    security: 'admin password and recovery code handling (Argon2id wraps via Wakeel.Crypto, no clear-text secrets in admin.db, logs or UI state), org key generation and storage, device certificate issuance and revocation list signing, setup package password shown once, DPAPI use, file dialogs and paths',
    extraCheck: 'every screen A01–A12 matches its PNG (light and dark) with the admin-tool shell (dark top bar + top tabs, no sidebar), all texts Arabic via the AdminAr class, and the exported .wakeel-setup opens with Wakeel.Crypto SetupPackageReader with every check Ok',
    spec: `Build section «B1-A» of docs/build/packages/B1-admin-firstrun.md completely (A01–A12) in src/Wakeel.Admin.UI (Blazor pages/components/services) and src/Wakeel.Admin (WPF + BlazorWebView host, like Wakeel.Desktop but titled «مدير نظام الوكيل», 1366x768, DPAPI IPlatformProtector, file open/save dialogs, print-to-PDF through CoreWebView2.PrintToPdfAsync, ProgramData folder C:\\ProgramData\\WakeelAdmin\\ with admin.db, keys, exports, logs). Wakeel.Admin.UI references Wakeel.Design (components + Ar), Wakeel.Core (reuse DbConnectionFactory/SchemaMigrator/SQLCipher helpers; add the admin schema of DATA-MODEL §13 as its own migration set inside Wakeel.Admin.UI) and Wakeel.Crypto. Admin-tool Arabic strings live in src/Wakeel.Admin.UI/Text/AdminAr.cs (static class AdminAr, grouped like Ar). Deliverables: the 12 screens with their states, services (AdminDb, OrgService, StructureService, DeviceService, KeyService, SetupExportService using Wakeel.Crypto SetupPackageWriter with the 16-char package password, MaintenanceService (open a Wakeel backup/ProgramData with AdminWrap, open a .wakeel-msg), DistributionService (A10), AuditService (A11 with CSV export)), lock-out after 5 failed logins (30 s doubling), recovery by code or QR image (ZXing already pinned), provisional guide PDF at docs/guide/دليل-الوكيل.pdf if the file is absent generate a one-page placeholder PDF at build time into the exports (do not write under docs/). Tests in tests/Wakeel.Admin.Tests (bUnit for screens/states, service tests on a temp admin.db): account creation + recovery sheet, login lock-out, structure rules (4 fixed levels, no duplicates per level, no delete with active devices), device numbering 1–9 unique per office, certificate issuance + revocation list, setup export produces a file that SetupPackageReader opens with all checks Ok and the seed is removed from admin.db after the first export, distribution regenerates files for affected offices, audit log has no secrets. Report the screen-by-screen fidelity in notes.`,
  },
  {
    key: 'firstrun', title: 'B1-W (التشغيل الأول والدخول: W02–W07)', model: 'opus',
    paths: 'src/Wakeel.UI, src/Wakeel.Desktop, src/Wakeel.Core (additions only: services/entities the activation needs), src/Wakeel.Design/Text/Ar.FirstRun.cs (new partial file only), tests/Wakeel.Walkthrough.Tests, tests/Wakeel.Core.Tests (additions only), tests/Wakeel.UI.Tests (additions only)',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.Walkthrough.Tests && dotnet test tests/Wakeel.Walkthrough.Tests --no-build && dotnet test tests/Wakeel.Core.Tests --no-build && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'account password → PasswordWrap/RecoveryWrap/MachineWrap (DPAPI)/AdminWrap creation exactly as ARCHITECTURE §3, DbKey/VaultKey never written in clear, setup package verified before anything is trusted (org key pinned on activation), lock-out counters, recovery code entry, temp setup file deletion, no secrets in logs or UI state',
    extraCheck: 'W02–W07 match their PNGs (light and dark), the first-run window has no sidebar and shows the approved brand, the walkthrough test drives the whole path on a temporary ProgramData folder, and the desktop host boots into W02 when no installation exists and into W05 otherwise',
    spec: `Build section «B1-W» of docs/build/packages/B1-admin-firstrun.md completely (W02–W07). Placement: Wakeel.UI gains a ProjectReference to Wakeel.Crypto; the account services live in src/Wakeel.UI/Services/Account (SetupInspectionService wrapping SetupPackageReader for W02–W03, ActivationService creating the database from the setup content — installation, org_units, devices, settings, pinned org key — and the InstallationKeyFile with the three wraps + AdminWrap, LoginService with attempt counter and temporary lock-out, LockService with the idle timer from settings (default 10 min) and the MachineWrap unlock, RecoveryService re-creating PasswordWrap from the recovery code with an optional new recovery sheet); Wakeel.Core gets only what is generic (e.g. an InstallationService/SettingsService if missing). Wakeel.Desktop supplies DpapiPlatformProtector (IPlatformProtector), ProgramData paths (C:\\ProgramData\\Wakeel\\{data,vault,keys,models,packages,backups,logs}, created with user write access), PrintToPdf via CoreWebView2 for the recovery sheet, drag-and-drop of a .wakeel-setup file onto W02, and the route decision at startup (no installation → /first-run, else → /login; the gallery stays reachable at /gallery). Screens: W02 (setup file + package password + «فحص الحزمة» + «أين أجد الملف؟» tooltip), W03 (live check list from SetupCheckResult, org card with logo, «تفعيل هذه النسخة», all error states listed in the spec), W04 (account password with strength meter + confirm, recovery sheet shown once with code + QR (QRCoder), print/PDF, «طبعتها وحفظتها» checkbox gating «ابدأ العمل»), W05 (login with org logo/name/office, employee name/avatar, password show/hide, «نسيت كلمة المرور؟ استخدم ورقة الاسترداد», clock status line from ClockCheckService), W06 (lock overlay over the current screen after idle, password, «متابعة», drafts-saved note), W07 (wrong password state + attempt counter + temporary lock, recovery dialog with manual code or QR image via ZXing, new password, new PasswordWrap, optional new recovery sheet). Arabic strings in src/Wakeel.Design/Text/Ar.FirstRun.cs as partial class Ar. Tests: tests/Wakeel.Walkthrough.Tests drives the full path on a temporary folder: a setup package produced with Wakeel.Crypto SetupPackageWriter (standing in for the admin tool, which is built in parallel) → inspection shows every check Ok → activation creates the database and key file → login succeeds/fails/locks out → lock and unlock → recovery with the code sets a new password and the old one stops working; each step asserts database rows, files on disk and audit_log entries, and that no secret appears in the log file or the database in clear text. bUnit tests for the six screens and their states (including the mixed Arabic/English lines). Report the screen-by-screen fidelity in notes.`,
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
  return `You are the builder of work package ${p.title} of الوكيل v0.21. ${COMMON}\nAllowed paths (create/edit only here): ${p.paths}.\nBuild/test command: ${p.build}\n\nSPECIFICATION:\n${p.spec}\n\nWhen done, return: status, files_changed, tests_total, tests_passed, build_ok, open_issues (anything you could not do, with the reason), notes (design decisions worth knowing, screen-by-screen fidelity).`
}
function reviewPrompt(p, build, round) {
  return `You are the Opus reviewer (round ${round}) of work package ${p.title} of الوكيل v0.21. ${COMMON}\nThe builder reported: ${JSON.stringify(build)}.\nRead the specification below and the governing docs, then read every file under ${p.paths} that the package touches, VIEW the reference PNGs of every screen, and run exactly: ${p.build} (read the full output). Check: (1) every spec item and screen is implemented (list missing ones), (2) correctness bugs and edge cases, (3) security: ${p.security}, (4) ${p.extraCheck}, (5) tests assert real behaviour (not tautologies), warnings-as-errors clean, (6) Arabic-only user text without technical terms, tooltips on icon-only buttons, RTL/bidi compliance. Do NOT modify any file. Return verdict ('accept' only when build+tests pass and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), missing_spec_items, notes.\n\nSPECIFICATION:\n${p.spec}`
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
log('B1: admin tool ∥ first run')
const results = await parallel(PACKAGES.map(p => () => chain(p)))
return results.filter(Boolean)
