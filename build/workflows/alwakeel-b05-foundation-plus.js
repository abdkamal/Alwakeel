export const meta = {
  name: 'alwakeel-b05-foundation-plus',
  description: 'B0.5: split the design system into Wakeel.Design (shared with the admin tool), implement the .wakeel-setup format in Wakeel.Crypto, polish the Core low findings, then prototype the Playwright-over-CDP E2E harness; each package reviewed by Opus',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }],
}
const REPO = 'D:/AI/Administration2'
const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents you MUST read first: docs/build/ARCHITECTURE.md, docs/build/DATA-MODEL.md, docs/build/BUILD-PLAN.md, docs/AGREEMENT.md (items referenced), docs/design/DESIGN-GUIDE.md. Never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, stop and report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/, or any path outside your allowed paths. Never run git commit/checkout/stash/reset — leave changes in the working tree. Other agents may edit other projects in the same working tree concurrently: build ONLY with the exact commands given ('dotnet test --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build'); when a build fails on a transient file lock caused by another agent's concurrent build, wait 60 seconds and retry. A package that touches src/Wakeel.Desktop or src/Wakeel.UI or src/Wakeel.Design is not done until the built host actually runs: launch src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.exe with --remote-debugging-port=9333, confirm http://127.0.0.1:9333/json/version answers, take a screenshot (Playwright over CDP, or PowerShell CopyFromScreen) and look at it, read C:\\ProgramData\\Wakeel\\logs\\wakeel-<date>.log for errors, then kill the process — a build that compiles but does not render is a failure. All user-facing text is Arabic with no technical terms or error codes; identifiers, comments and XML docs in English. Work until the package is complete and its build/tests are green; do not stop early.`

const PACKAGES = [
  {
    key: 'design-split', title: 'B0.5-DESIGN-SPLIT (Wakeel.Design)', model: 'sonnet',
    paths: 'src/Wakeel.Design, src/Wakeel.UI, src/Wakeel.Admin.UI (only Wakeel.Admin.UI.csproj + _Imports.razor), src/Wakeel.Desktop (only wwwroot/index.html, App.xaml.cs DI registration and the csproj if needed), tests/Wakeel.UI.Tests',
    build: 'dotnet build Wakeel.slnx && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'no MarkupString fed by user data, no unsafe JS interop, nothing secret in UI state',
    extraCheck: 'the moved static web assets resolve at runtime (Wakeel.Desktop.staticwebassets.runtime.json lists _content/Wakeel.Design/...), the scoped CSS bundle still includes every component style, RTL/bidi rules untouched, Wakeel.Admin.UI can reference the components without pulling Wakeel.Sync/Ocr/Search/Reports, and the desktop host was actually launched and rendered (screenshot viewed, log clean)',
    spec: `Goal: the design system must be shared by the Wakeel desktop app AND the separate administration tool (Wakeel.Admin.UI), which must not depend on Wakeel.Sync/Ocr/Search/Reports (heavy native packages). An empty razor class library src/Wakeel.Design (already registered in Wakeel.slnx, package ref Microsoft.AspNetCore.Components.Web) exists for this.
1. Move into src/Wakeel.Design (root namespace Wakeel.Design): every design-system component under src/Wakeel.UI/Components (all W* .razor + .razor.css + code-behind, WIcon + IconRegistry.g.cs and the vendored icons), src/Wakeel.UI/Bidi, the pure UI services (ThemeService, ToastService, the UI-state store interface and its default implementation, tooltip/dialog helpers) and src/Wakeel.UI/wwwroot (css/tokens.css, css/app.css, js/app.js, fonts, icons). Keep in src/Wakeel.UI only what is application-specific: Layout (MainLayout, the app shell composition), Pages, Routes.razor, navigation/page-header state, sample data.
2. Arabic strings: ALL of them live in Wakeel.Design as the single partial static class Wakeel.Design.Text.Ar split over partial files by area (Text/Ar.Common.cs for shared groups such as Fields/Buttons/Dialogs/States/Menu/Pager/Breadcrumb/Nav/Toast/TopBar, Text/Ar.App.cs for the desktop-app groups such as AttentionCenter/Gallery/Sample/sidebar menu items). Future packages add their own partial file (e.g. Ar.FirstRun.cs). Wakeel.UI must contain no Arabic string class of its own after the move; every reference compiles as Ar.X.Y with one using.
3. Update namespaces/usings (_Imports.razor in Wakeel.UI and Wakeel.Admin.UI import Wakeel.Design, Wakeel.Design.Components, Wakeel.Design.Text, Wakeel.Design.Bidi), the ProjectReferences (Wakeel.UI -> Wakeel.Design; Wakeel.Admin.UI -> Wakeel.Design in addition to Core+Crypto), Wakeel.Desktop/wwwroot/index.html (_content/Wakeel.Design/... for tokens.css, app.css, app.js, fonts), and DI registration (a Wakeel.Design AddWakeelDesign() extension for the UI services, called from Wakeel.Desktop App.xaml.cs).
4. tests/Wakeel.UI.Tests: fix usings/namespaces so every existing test still passes (136+); the WakeelTestContext helper registers the Design services.
5. Result: 'dotnet build Wakeel.slnx' green with 0 warnings; src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.staticwebassets.runtime.json lists the _content/Wakeel.Design assets; Wakeel.Desktop.styles.css still contains the component styles. Report the final folder layout in notes.
6. While the files move, also apply the six low findings of docs/build/reviews/B0-closeout/verify-ui.json (read it): WSectionHeader count badge rendered after the title so it sits to the LEFT of the title in RTL as the W08 PNG shows; W08 second action row's الاستحقاق («اليوم 16:00») in the warning colour and the «يحتاج إجراء اليوم» header carrying the «9» count badge with الموضوع cells as in the PNG; ShellTests asserting the expenses table has the five header cells (الموظف | البيان | التاريخ | المبلغ | action) with matching body cells; remove the dead Ar.AttentionCenter.PendingExpensesCountBadge or use it for the badge; BidiWrapTests asserting the exact W04 reference line («الجهاز 1» as in docs/design/bidi-test.html).
7. Defects seen when the supervisor launched the host (screenshot docs/build/reviews/B0-closeout/smoke-shell-dark.png, compare with design/exports/dark/W/W08 — مركز الانتباه.png top bar): the top-bar user block (avatar + name + role) overflows — the name wraps to two lines and the «3» badge/bell overlap it at 1366 width; lay it out as the PNG shows (avatar with name and role on one line each, fixed widths, ellipsis on long names) and add a bUnit assertion that the top bar renders the sample user name and role in single-line elements. The native date input shows the browser placeholder «yyyy-mm-dd»: WInput type=date must present dd/MM/yyyy per ARCHITECTURE §12 (a masked text input with a calendar icon is acceptable), with Western digits.
8. Launch the host as COMMON requires (light and dark: toggle the theme in the gallery), view both screenshots, and describe what you saw in notes.
Rules: otherwise a pure move/rename, no new components; keep file names so history stays readable.`,
  },
  {
    key: 'setup-format', title: 'B0.5-SETUP-FORMAT (Wakeel.Crypto/Setup)', model: 'opus',
    paths: 'src/Wakeel.Crypto, tests/Wakeel.Crypto.Tests',
    build: 'dotnet build src/Wakeel.Crypto && dotnet test tests/Wakeel.Crypto.Tests',
    security: 'misuse of primitives, signature over the wrong bytes, trust-on-first-use pinning done right, secrets (deviceSeed, officeKey) never outside the encrypted payload, verify-before-decrypt',
    extraCheck: 'the API is directly usable by the admin tool (writer) and the first-run screens W02–W03 (reader with per-item check results) as ARCHITECTURE.md §4 describes',
    spec: `Implement the .wakeel-setup file format exactly as ARCHITECTURE.md §4 paragraph «صيغة .wakeel-setup (قرار 2026-09-16)» specifies, on top of the existing ContainerWriter/ContainerReader (ContainerKind.Setup, password payload mode with Argon2id from the 16-character package password), signed by the organisation root key.
1. Models under src/Wakeel.Crypto/Setup: SetupContent (formatVersion, exportedAt, exportSeq, org {id, name, signingPub, x25519Pub, cycleStartDay 1–28, numberingFormat}, units[] {id, parentId, level 1–4, name, headTitle, headName, officeCode}, office {unitId, officeCode}, device {id, deviceNo 1–9, role manager|secretary|custodian, syncScope full|custody, certificate}, deviceSeed, employee {name, employeeNo 1–9, jobTitle}, officeKey (32 bytes), revocation (signed list), includes {logo, guide, reportTemplate}) serialised with CanonicalJson as setup.json; optional entries logo.png, guide.pdf, report-template.docx.
2. SetupPackageWriter.Write(content, optional streams, packagePassword, organisation signing identity, output stream/path, TimeProvider): produces the container; the producer certificate is the organisation root (decide and document how the existing manifest/certificate model expresses an org-root signer; extend CertificateChain if needed so a Setup container is verified against the org signing key carried in setup.json and, when the caller passes a pinned key, against that pinned key only).
3. SetupPackageReader.Open(path/stream, packagePassword, expectations {pinnedOrgSigningPub?, installedDeviceId?, installedExportSeq?}, TimeProvider) returns SetupCheckResult: a list of per-item checks (signature, organisation, office, device, employee, officeKey, logo, guide, reportTemplate, revocation) each Ok/Failed/Absent with an ErrorCode (no Arabic here; the UI maps codes to Arabic), plus the parsed SetupContent and lazy access to the optional entries. Rejections: invalid signature, wrong password (WrongPassword), device.id different from installedDeviceId when given (OtherDevice), exportSeq lower than installedExportSeq (Older), exportedAt more than one day in the future (FutureDate), org key different from the pinned key (Tampered), corrupt/missing setup.json (Corrupt).
4. PackagePassword.New(): the 16-character package password (unambiguous alphabet, cryptographically random) lives here too.
5. Tests: round trip with all optional entries, round trip without them, wrong password, tampered payload, tampered manifest, other device, older exportSeq, future date, pinned-key mismatch, revocation list carried through, canonical bytes stable across two writes with the same content and clock (except keySalt/nonces). Keep every existing test green; do not touch docs.
6. Also apply the three low findings of docs/build/reviews/B0-closeout/verify-crypto.json (read it): assert the ExtractTo path also refuses an oversized entry; one-field-at-a-time mismatch theories for PairingAccept.EnsureTrusted (OfficeId, PcDeviceId, X25519Pub alone); derive ContainerEntrySource's invalid-character set from Path.GetInvalidFileNameChars() instead of a literal array.`,
  },
  {
    key: 'core-polish', title: 'B0.5-CORE-POLISH (Wakeel.Core low findings)', model: 'sonnet',
    paths: 'src/Wakeel.Core, tests/Wakeel.Core.Tests',
    build: 'dotnet build src/Wakeel.Core && dotnet test tests/Wakeel.Core.Tests',
    security: 'transaction correctness and uniqueness of official numbers under concurrency',
    extraCheck: 'the two findings are closed with real tests and nothing else changed',
    spec: `Apply the two low findings of docs/build/reviews/B0-closeout/verify-core.json (read it for the precise text):
1. OfficialNumberService.IssueAsync: the INSERT race for a (kind, year) row that does not exist yet — when two contexts both take the 'row is null' branch, the second insert fails with a UNIQUE/PRIMARY KEY violation; catch that DbUpdateException (SqliteException constraint error) inside the bounded retry, reload the row and retry, so both callers still receive distinct numbers. Test with two DbSession contexts over the same file.
2. DateTimeConverterTests: the assertion Assert.Empty(logger.Warnings) is racy because the process-wide static DataIntegrityLog.Reported event is shared across parallel test classes; scope the sink per WakeelDb (e.g. an instance-level hook or a filter by connection/context id) or serialise those tests in a dedicated xunit collection, so the test cannot be polluted by other classes. Keep all 170 tests green plus the new ones; 0 warnings.`,
  },
  {
    key: 'e2e', title: 'B0.5-E2E-HARNESS (Playwright over CDP)', model: 'sonnet',
    paths: 'tests/Wakeel.E2E, build/e2e.ps1, src/Wakeel.Desktop (only command-line flag handling in App.xaml.cs/MainWindow.xaml.cs: --remote-debugging-port, --window-size=WxH, --start-url=/path)',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.E2E && dotnet test tests/Wakeel.E2E --no-build',
    security: 'the remote-debugging port is only honoured when passed on the command line and validated as a port number; no other Chromium switches are injectable',
    extraCheck: 'the harness is deterministic: app start/stop is robust (kills the process on failure), waits on http://127.0.0.1:<port>/json/version, and the screenshot comparison reports a diff percentage against the design PNG',
    spec: `Prototype the end-to-end harness that later packages (B1–B8) will use for acceptance screenshots and walkthroughs. ARCHITECTURE.md §11 describes it. The desktop host lives at src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.exe after 'dotnet build src/Wakeel.Desktop'.
1. Wakeel.Desktop command-line flags (App.xaml.cs / MainWindow): --remote-debugging-port=N (already exists, keep the ushort validation), --window-size=WxH (sets the WPF window client size; default stays as is), --start-url=/route (initial Blazor route; default '/'). Flags only affect the current process.
2. tests/Wakeel.E2E (xunit + Microsoft.Playwright, already pinned): a fixture that builds nothing, launches the host exe with --remote-debugging-port=9333 --window-size=1366x768 --start-url=/gallery, waits for http://127.0.0.1:9333/json/version (timeout 30 s, then kills the process and fails with the log tail), connects with Playwright.Chromium.ConnectOverCDPAsync, finds the page, and disposes/kills the app on teardown even when a test fails. No Playwright browser download is needed for CDP (say so in a comment); if Playwright's driver needs installing, do it via the NuGet-provided playwright.ps1 and document it in build/e2e.ps1.
3. Tests: GalleryOpens (heading text equals the Arabic gallery title from the Wakeel.Design Ar strings, html dir=rtl), ThemeToggles (switch to dark and assert the document theme attribute and a token colour), W08ScreenshotDiff (navigate to the attention-center placeholder route, screenshot the viewport at 1366x768, compare pixel-by-pixel with design/exports/light/W/W08 — مركز الانتباه.png resized to the same size if needed, write the diff image and the percentage to tests/Wakeel.E2E/artifacts/, and only RECORD the percentage in the test output — no threshold yet), plus one test proving the fixture kills the app when the port never opens.
4. build/e2e.ps1: builds Wakeel.Desktop (Debug) then runs 'dotnet test tests/Wakeel.E2E'; documents prerequisites in a header comment (WebView2 runtime present on this machine).
5. The build/test command must be green on this machine; include the measured W08 diff percentage in notes.`,
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
  return `You are the builder of work package ${p.title} of الوكيل v0.21. ${COMMON}\nAllowed paths (create/edit only here): ${p.paths}.\nBuild/test command: ${p.build}\n\nSPECIFICATION:\n${p.spec}\n\nWhen done, return: status, files_changed, tests_total, tests_passed, build_ok, open_issues (anything you could not do, with the reason), notes (design decisions worth knowing).`
}
function reviewPrompt(p, build, round) {
  return `You are the Opus reviewer (round ${round}) of work package ${p.title} of الوكيل v0.21. ${COMMON}\nThe builder reported: ${JSON.stringify(build)}.\nRead the specification below and the governing docs, then read every file under ${p.paths} that the package touches and run exactly: ${p.build} (read the full output). Check: (1) every spec item is implemented (list missing ones), (2) correctness bugs and edge cases, (3) security: ${p.security}, (4) ${p.extraCheck}, (5) tests assert real behaviour (not tautologies), warnings-as-errors clean. Do NOT modify any file. Return verdict ('accept' only when build+tests pass and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), missing_spec_items, notes.\n\nSPECIFICATION:\n${p.spec}`
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
const byKey = Object.fromEntries(PACKAGES.map(p => [p.key, p]))
log('B0.5: design split ∥ setup format ∥ core polish; E2E harness after the split')
const results = await parallel([
  async () => { const a = await chain(byKey['design-split']); const b = await chain(byKey['e2e']); return [a, b] },
  () => chain(byKey['setup-format']),
  () => chain(byKey['core-polish']),
])
return results.flat().filter(Boolean)
