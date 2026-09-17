export const meta = {
  name: 'alwakeel-b5-screens',
  description: 'B5 screens on top of the accepted B5 services: employees and payroll (W51–W57), assets and custody (W58–W63), finance (W64–W69), monthly report and other reports (W70–W74), then the B5 walkthrough + E2E — two parallel chains then one; every package reviewed, fixed and re-verified by Opus with a mandatory visual fidelity check; resumable through args.resume/args.skip',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }, { title: 'Fix3' }, { title: 'Verify3' }],
}
const REPO = 'D:/AI/Administration2'
const PROTOCOL = `RESTART PROTOCOL (mandatory — the connection sometimes drops and an agent is then restarted from scratch, while the working tree keeps everything written so far): (1) FIRST run 'git status --short' and read docs/build/progress/<your package key>.md if it exists — it lists the steps a previous attempt of THIS package completed; continue from the first unfinished step instead of starting over, and never delete or rewrite files that already implement a step correctly (read them and extend them). Uncommitted files that belong to another package's paths are that package's live work — leave them alone. (2) Do not read everything up front: read the spec lines and the code you need for the CURRENT step, write the code, build it, then move on; make your first code change within your first ten tool calls. (3) After every completed step (a screen, a test file, a green build) append one line to docs/build/progress/<your package key>.md (create it; it is the only file under docs/ you may write). (4) IMAGES: never open the full-size PNGs under design/exports/light or design/exports/dark (1–2 MB each; they overload the connection). View the small JPEG previews under design/exports/preview/light/W/<name>.jpg and design/exports/preview/dark/W/<name>.jpg (same file names as the PNGs, .jpg extension), only the screens of your own package, each at most once per comparison, right before you build or check that screen. (5) Screenshots you take must be JPEG or PNG at most 1366 px wide and viewed once. (6) When a report is handed to you as {"see": "<path>"}, read that JSON file first — it is the archived report of the previous stage.

DESIGN CONSISTENCY (owner's rule, 2026-09-16 — «انتبه للتصميم: لا يوجد تناسق وتنظيم في المكونات»): every screen is composed from Wakeel.Design components (src/Wakeel.Design/Components: WButton, WInput, WSelect, WCard, WChip, WBadge, WTabs, WTable, WKvRow, WStateCard, WDialog, WMenu, WStepper, WTree, WTimelineItem, WDocumentRow, WPager, WSearch, …). Hand-rolled lists, trees, tables, tabs, chips, forms, cards or dialogs inside a page are forbidden; page CSS may only arrange design components on the page grid and set page-specific spacing from the tokens. If a component is missing, add it to Wakeel.Design first (component + css + gallery entry + bUnit test), then use it. The screen is done only when its host screenshot, put beside the preview, shows the same anatomy: same regions and columns, same controls in the same places, same chips and states, no overlap, no clipping, aligned rows, consistent spacing. A builder writes the side-by-side comparison of every screen in its notes (deviation → fixed / accepted with reason). A reviewer opens the builder's screenshot and the preview together and reports every visible deviation as a finding: high for a hand-rolled component, medium for a layout, alignment, overlap or missing-control deviation, low for a spacing nuance.

RULES: never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/ (except your progress file), or any path outside your allowed paths (csproj files inside your allowed paths may gain ProjectReferences only). Never run git commit/checkout/stash/reset/clean. Another agent edits other pages in the same working tree concurrently: build ONLY with the exact commands given ('dotnet test --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build'); when a build fails on a file lock or on a compile error inside a file you do not own, wait 60 seconds and retry (up to five times), then report it in open_issues instead of editing that file. A package that touches Blazor pages is not done until the host actually runs: activate a temporary data folder with tests/Wakeel.Walkthrough.Tests' FirstRunWorld (or a small test helper), launch src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.exe on it with --remote-debugging-port=<port> (use the port named in your package so concurrent packages never collide), --window-size=1366x768, --data-folder=<that folder> and --start-url=/route, confirm http://127.0.0.1:<port>/json/version answers, sign in over CDP, screenshot every screen light and dark (Page.captureScreenshot), LOOK at each beside its preview, read the host log in <data-folder>/logs for errors, then kill the process immediately — a host left running is a defect, and so is relaunching it more often than the work needs (the owner sees every window). Never touch C:\\ProgramData\\Wakeel of the real installation. All user-facing text is Arabic with no technical terms or error codes (item 15), every icon-only button has a tooltip, full RTL with bdi/isolate on Latin/numeric tokens (item 55), no mention of servers, ports, internet or PostgreSQL anywhere; Arabic strings only in src/Wakeel.Design/Text/Ar.*.cs partial files (add Ar.Employees.cs, Ar.Assets.cs, Ar.Finance.cs, Ar.Reports.cs). Identifiers, comments and XML docs in English. Work until the package is complete and its build/tests are green; do not stop early.`

const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents: docs/build/packages/B5-employees-assets-finance-reports.md (the B5 specification), docs/design/SCREENS.md (rows W51–W74) and docs/design/DESIGN-GUIDE.md, docs/AGREEMENT.md (items 13, 37, 44–48, 50–53, 55), docs/build/ARCHITECTURE.md (§4, §9, §10, §12), docs/build/DATA-MODEL.md (§7–§10). Foundation — the ACCEPTED B5 services (read their progress files docs/build/progress/b5-*.md and public APIs before designing a screen): Wakeel.Core Services/Assets (AssetService, AssetTransferService, InventoryService, CustodyConflictResolver), Services/Employees (EmployeeService, PayrollService, BonusService), Services/Finance (FinanceService, PhoneExpenseService, CashCountService, FinancialCycleService), Services/Reports (ReportDraftService, ReadinessService, ReportIssueService, OtherReports) with the HTML/docx generation in Wakeel.Reports; the accepted screens of B2–B4 (follow their page patterns: tabs with badges + local search + WTable + WPager for lists; header + command bar + tabs for details; WDialog forms with validation; print previews like W27) and the shell from B2. Money is shown from integer agorot through the shekel formatting helper with bidi-isolated digits; the current cycle's name follows FinancialCycleService, never the literal «دورة أكتوبر» of the previews.\n\n${PROTOCOL}`

const PACKAGES = {
  employees: {
    key: 'b5-employees-screens', title: 'B5-1b (الموظفون والرواتب والمكافآت — W51–W57)', model: 'opus', port: 9333,
    paths: 'src/Wakeel.UI/Pages/Employees (new), src/Wakeel.UI/Services/Employees (new), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — add your own lines), src/Wakeel.Design/Text/Ar.Employees.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, each with css + gallery entry + bUnit test), src/Wakeel.Desktop (only the print/PDF/file-dialog hooks the screens need), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'salary figures never reach logs or toasts, a committed run is read-only in the UI, the import preview shows the signature state truthfully and refuses a bad file with a plain Arabic reason, no MarkupString with user data',
    extraCheck: 'W51–W57 match their previews side by side; in-line editing in the payroll run updates the net and the footer total immediately; the pre-commit summary dialog shows totals, head count and warnings; the print preview mirrors W55 with letterhead and signatures; the termination warning names the remaining custody items',
    spec: `Build W51 (employees hub: tabs الموظفون/الرواتب/المكافآت/الكشوف with counts, the employees table with the preview's columns), W52 (employee file: data, salary components editable with validity dates, custody, payroll history, the termination warning and the blocked termination message), W53 (payroll run: month choice, one row per employee with in-line editable cells, footer totals, «تثبيت الكشف», draft/committed state), W54 (pre-commit summary dialog over W53), W55 (payroll sheet print preview with unit choice, print and PDF), W56 (import of department sheets for a directorate office: signed file choice, preview — section, month, totals, signature state —, merge, imported files log), W57 (bonuses granted in the cycle + the approved-rules card + the grant dialog). Wire routes and the sidebar entry. bUnit tests for every screen and state, in-line edit arithmetic, commit gate, import preview good/bad, termination block. Host run on port 9333 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  assets: {
    key: 'b5-assets-screens', title: 'B5-2b (الأصول والعُهد — W58–W63)', model: 'opus', port: 9336,
    paths: 'src/Wakeel.UI/Pages/Assets (new), src/Wakeel.UI/Services/Assets (new), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — add your own lines), src/Wakeel.Design/Text/Ar.Assets.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, e.g. WDiffPair for side-by-side versions, each with css + gallery entry + bUnit test; reuse WQrCode), src/Wakeel.Desktop (only the file-dialog/print hooks the screens need), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'a transfer or inventory package is verified before its preview is trusted and nothing is applied on a failed check, the conflict centre never writes automatically (item 44), no MarkupString with user data',
    extraCheck: 'W58–W63 match their previews side by side; the movements timeline is cumulative with «بانتظار تأكيد الاستلام» and «قيد التسليم»; the inventory session shows progress and the three results; the conflict centre shows both versions with the differences highlighted and offers choose-one or per-field merge',
    spec: `Build W58 (assets hub: tabs الأصول/العُهد/الحركات/الجرد/النقل with counts, local search, the table with the preview's columns), W59 (asset card with QR, the cumulative movements timeline, the in-transfer state, buttons تسليم عهدة/نقل/جرد), W60 (custody handover dialog: asset, from, to employee, date, note, print the handover receipt; pending-confirmation state), W61 (transfer between two offices: export a signed package — receiving office from the structure, chosen assets, summary — and accept an incoming package with preview then accept/reject), W62 (inventory session: the assets list with present/missing/damaged and progress, results scanned by the phone shown with their source, end session, export signed results for the higher authority), W63 (custody conflict centre: list, both versions side by side with differences, choose a version or merge field by field, the «لا كتابة تلقائية» note). Wire routes and the sidebar entry. bUnit tests for every screen and state, handover validation, transfer preview good/bad, inventory progress, conflict merge. Host run on port 9336 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  finance: {
    key: 'b5-finance-screens', title: 'B5-3b (المالية — W64–W69)', model: 'opus', port: 9337,
    paths: 'src/Wakeel.UI/Pages/Finance (new), src/Wakeel.UI/Services/Finance (new), src/Wakeel.UI/Pages/W08AttentionCenter.razor (only to route its phone-expense confirm/reject through PhoneExpenseService if it does not already), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — add your own lines), src/Wakeel.Design/Text/Ar.Finance.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, each with css + gallery entry + bUnit test), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'an issued cycle is read-only in the UI with the lock shown, the add dialog warns when the date falls in an issued cycle and explains where the entry will be booked (item 51), amounts are entered and shown as shekels and stored as agorot without floating point, no MarkupString with user data',
    extraCheck: 'W64–W69 match their previews side by side; the current-cycle strip shows the real cycle name, range and state; the phone-expense cards allow editing before confirming, reject with an optional reason and «تأكيد الكل» with the count, plus the empty state; the cash count shows the difference with its semantic colour; the cycles table shows the lock and the read-only start-day card',
    spec: `Build W64 (finance hub: tabs المصروفات/الإيرادات/النقدية/مصروفات الهاتف/الدورات with counts, the current-cycle strip, local search, the expenses table with the preview's columns and edit/delete actions, the total), W65 (add expense/income dialog: kind, amount ₪, purpose, category, date inside an open cycle only with the issued-cycle warning, receipt, note), W66 (phone expenses awaiting confirmation: cards with amount, purpose, the phone's date and time, receipt thumbnail, editable fields, confirm / reject with reason, «تأكيد الكل» with the count, empty state), W67 (financial cycles table: name, range, state, totals, «التقرير الشهري» button, lock for issued ones, the start-day card «يحدده مدير النظام»), W68 (cash count form: book balance, counted by denominations, difference, note, history), W69 (income table in the W64 style with source and collection method). Wire routes and the sidebar entry with its badge. bUnit tests for every screen and state, amount parsing/formatting, the issued-cycle warning, batch confirm, cash difference colour. Host run on port 9337 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  reports: {
    key: 'b5-reports-screens', title: 'B5-4b (التقرير الشهري والتقارير الأخرى — W70–W74 وربط شريط W08)', model: 'opus', port: 9338,
    paths: 'src/Wakeel.UI/Pages/Reports (new), src/Wakeel.UI/Services/Reports (new), src/Wakeel.UI/Pages/W08AttentionCenter.razor (only to feed its monthly-report banner from ReadinessService), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — add your own lines), src/Wakeel.Design/Text/Ar.Reports.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, e.g. WBarChart, WTrendArrow, WSortableList, each with css + gallery entry + bUnit test), src/Wakeel.Desktop (only the print/PDF hooks the screens need), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'issuing requires the explicit confirmation dialog with the «سيُقفل الشهر نهائيًا» warning and cannot be triggered twice, the issued report is read-only, previews render the service HTML in a sandboxed frame or as encoded text (no MarkupString of user data)',
    extraCheck: 'W70–W74 match their previews side by side; W70 has the three columns (sections with counts, the selected section preview with indicators, trend arrow and bar chart, the readiness card with gaps as links); W71 edits generated sentences, toggles «يُضمَّن/يُبرَز» with the comment, hides and reorders sections and edits the director word; W72 handles pending phone expenses inside the dialog; W73 shows the frozen copy with the outgoing number and the addendum dialog; the W08 banner now shows the real readiness',
    spec: `Build W70 (monthly report draft and readiness: header with the real cycle and the days left, sections list with counts, the selected section's preview — executive summary with indicators, comparison with the previous cycle, trend arrow and a simple bar chart —, the readiness card with the percentage and the gaps as links to their screens), W71 (section editor: generated sentences editable, included items with the «يُضمَّن/يُبرَز» switches and the comment line, hide section, reorder, the director's word as free text), W72 (issue dialog over W70: cycle summary, the pending phone-expenses alert with confirm/reject inside the dialog, the final-lock warning, the option «إرسال إلى الجهة الأعلى كمراسلة صادرة», confirm), W73 (issued report: the «مُصدَر في …» badge, the outgoing correspondence number when sent, page preview, PDF/print, the correcting-addendum dialog with reason and corrected item), W74 (other reports: cards for the correspondence register from–to, payroll sheet, custody and inventory, late tasks, meetings and decisions; quick filters; PDF/CSV export). Feed the W08 monthly-report banner from ReadinessService (one row as the preview: title, bar, percentage, «من N مكتملة N», the link). Wire routes and the sidebar entry. bUnit tests for every screen and state, reorder/hide persistence, the issue gate, addendum validation, export actions. Host run on port 9338 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  walkthrough: {
    key: 'b5-walkthrough', title: 'B5-WALKTHROUGH (الدورة الكاملة + لقطات E2E)', model: 'sonnet', port: 9333,
    paths: 'tests/Wakeel.Walkthrough.Tests, tests/Wakeel.E2E',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.Walkthrough.Tests --no-dependencies && dotnet test tests/Wakeel.Walkthrough.Tests --no-build && dotnet build tests/Wakeel.E2E --no-dependencies && dotnet test tests/Wakeel.E2E --no-build',
    security: 'tests use temporary folders only and never touch the real installation',
    extraCheck: 'the walkthrough covers every step of the B5 acceptance list through the public services and asserts rows, ledger balance, vault files and audit entries; E2E screenshots of the B5 screens are compared with the design PNGs with per-screen upper bounds',
    spec: `Build section «القبول» of docs/build/packages/B5-employees-assets-finance-reports.md: (1) tests/Wakeel.Walkthrough.Tests — a full cycle: expenses (computer + a phone expense confirmed and one rejected) → cash count → a committed payroll run → custody handover and confirmation → an assets inventory → the report draft updates → issuing locks the cycle and generates docx/PDF (the PDF step skips with a message when Word is absent and the WebView2 path is not available in tests) → an attempt to edit an issued transaction is refused → a correcting entry in the next cycle → a correcting addendum; every step asserts rows, the balanced ledger, vault files and audit entries. (2) tests/Wakeel.E2E: screenshot tests for W51–W74 at 1366x768 light and dark compared with design/exports/{light,dark}/W/*.png (the test code may read the PNGs; you look only at the small previews and your own screenshots), diff percentages recorded in tests/Wakeel.E2E/artifacts/ with per-screen upper bounds; host on port 9333 with a temporary folder seeded through the walkthrough helpers. Report the diff percentages per screen in notes.`,
  },
}

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
  return `You are the builder of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}; your CDP port: ${p.port}). ${COMMON}\nAllowed paths (create/edit only here, plus docs/build/progress/${p.key}.md): ${p.paths}.\nBuild/test command: ${p.build}\n\nSPECIFICATION:\n${p.spec}\n\nWhen done, return: status, files_changed, tests_total, tests_passed, build_ok, open_issues (anything you could not do, with the reason), notes (the per-screen side-by-side comparison table and design decisions).`
}
function reviewPrompt(p, build, round) {
  return `You are the Opus reviewer (round ${round}) of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}; your CDP port: ${p.port}). ${COMMON}\nThe builder reported: ${JSON.stringify(build)}.\nRead the specification below and the governing docs, then read every file under ${p.paths} that the package touches and run exactly: ${p.build} (read the full output). VISUAL FIDELITY CHECK: launch the host yourself on a seeded temporary installation, screenshot every screen the package covers in light and dark, open each screenshot together with its preview, and report every visible deviation as a finding (high: a hand-rolled component where a Wakeel.Design one exists or should exist; medium: layout, alignment, overlap, clipping, missing control/column/chip/state; low: spacing nuance). Also check: (1) every spec item and screen is implemented (list missing ones), (2) correctness bugs and edge cases, (3) security: ${p.security}, (4) ${p.extraCheck}, (5) tests assert real behaviour (not tautologies), warnings-as-errors clean, (6) Arabic-only user text without technical terms, tooltips on icon-only buttons, RTL/bidi compliance. From round 2 on, first confirm that each finding of the previous round is really closed, then look for regressions; do not reopen supervisor rulings. Do NOT modify any file (you may not write the progress file either). Kill the host when done. Return verdict ('accept' only when build+tests pass and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), missing_spec_items, notes.\n\nSPECIFICATION:\n${p.spec}`
}
function fixPrompt(p, review) {
  return `You are the builder of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}; your CDP port: ${p.port}) returning to apply review findings. ${COMMON}\nAllowed paths (plus docs/build/progress/${p.key}.md): ${p.paths}. Build/test command: ${p.build}\nReview result to address (fix every high and medium finding and every missing spec item; fix low ones too unless genuinely out of scope): ${JSON.stringify(review)}\n\nOriginal specification for reference:\n${p.spec}\n\nAfter fixing, run the build/test command until green, re-screenshot what you changed and compare again. Return status, files_changed, tests_total, tests_passed, build_ok, open_issues, notes (what you changed per finding).`
}
const ORDER = ['build', 'review1', 'fix', 'verify', 'fix2', 'verify2', 'fix3', 'verify3']
const PHASE = { build: 'Build', review1: 'Review', fix: 'Fix', verify: 'Verify', fix2: 'Fix2', verify2: 'Verify2', fix3: 'Fix3', verify3: 'Verify3' }
function isReview(st) { return st.startsWith('review') || st.startsWith('verify') }
// An agent that dies on a connection error returns null: start it again (the restart protocol makes the
// new attempt continue from the working tree and the progress file). Three attempts, then give up.
async function tryAgent(prompt, opts) {
  for (let n = 1; n <= 3; n++) {
    const r = await agent(prompt, n === 1 ? opts : { ...opts, label: `${opts.label}#${n}` })
    if (r) return r
    log(`${opts.label}: attempt ${n} returned nothing${n < 3 ? ' — starting it again' : ' — giving up'}`)
  }
  return null
}
// args = { skip: [package keys already accepted], resume: { '<package key>': { stage, file, state, rulings, extraPaths } } }
//   stage: build|review1|fix|verify|fix2|verify2|fix3|verify3 — the stage to run first;
//   file: archived report of the stage before it (handed over as {see: file}).
async function chainFrom(p0, r0) {
  const r = r0 || {}
  const p = { ...p0,
    paths: p0.paths + (r.extraPaths ? ', ' + r.extraPaths : ''),
    spec: p0.spec + (r.state ? '\n\nSTATE OF THIS PACKAGE: ' + r.state : '') + (r.rulings ? '\n\nSUPERVISOR RULINGS FOR THIS ROUND (final, do not reopen): ' + r.rulings : '') }
  const s = { key: p.key, accepted: false }
  let prev = r.file ? { see: r.file } : null
  for (let i = r.stage ? ORDER.indexOf(r.stage) : 0; i < ORDER.length; i++) {
    const st = ORDER[i]
    if (isReview(st)) {
      const round = ORDER.slice(0, i + 1).filter(isReview).length
      const rev = await tryAgent(reviewPrompt(p, prev, round), { label: `${st}:${p.key}`, phase: PHASE[st], schema: REVIEW_SCHEMA, model: 'opus', effort: round === 1 ? 'high' : 'medium' })
      s[st] = rev
      if (!rev) return { ...s, failed: st }
      if (rev.verdict === 'accept') return { ...s, accepted: true }
      prev = rev
    } else {
      const prompt = st === 'build' ? builderPrompt(p) : fixPrompt(p, prev)
      const out = await tryAgent(prompt, { label: `${st}:${p.key}`, phase: PHASE[st], schema: BUILD_SCHEMA, model: p.model, effort: 'high' })
      s[st] = out
      if (!out) return { ...s, failed: st }
      prev = out
    }
  }
  return s
}
const RESUME = (args && args.resume) || {}
const SKIP = (args && args.skip) || []
async function sequence(name, names) {
  const out = []
  for (const n of names) {
    const p = PACKAGES[n]
    if (SKIP.includes(p.key)) { out.push({ key: p.key, skipped: true, accepted: true }); continue }
    log(`${name}: ${p.key}${RESUME[p.key] ? ' (resuming at ' + RESUME[p.key].stage + ')' : ''}`)
    const s = await chainFrom(p, RESUME[p.key])
    out.push(s)
    if (!s.accepted) { log(`${name}: ${p.key} not accepted${s.failed ? ' (stage ' + s.failed + ' died)' : ''} — stopping this sequence for the supervisor`); break }
  }
  return out
}
log('B5 screens: employees/payroll (W51–W57) → finance (W64–W69) ∥ assets/custody (W58–W63) → reports (W70–W74); then the walkthrough')
const both = await parallel([
  () => sequence('employees and finance', ['employees', 'finance']),
  () => sequence('assets and reports', ['assets', 'reports']),
])
const ok = both.every(seq => seq && seq.length === 2 && seq.every(s => s.accepted))
if (!ok) { log('B5 screens: a chain was not accepted — the walkthrough waits for the supervisor'); return { screens: both } }
const walk = await sequence('walkthrough', ['walkthrough'])
return { screens: both, walkthrough: walk }
