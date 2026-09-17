export const meta = {
  name: 'alwakeel-b4-screens',
  description: 'B4 screens on top of the accepted B4 services: follow-up and tasks (W28–W33), meetings, brief and calendar (W34–W39), cases and parties/directory (W40–W42, W48–W50), then the B4 walkthrough + E2E — two parallel chains then one; every package reviewed, fixed and re-verified by Opus with a mandatory visual fidelity check; resumable through args.resume/args.skip',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }, { title: 'Fix3' }, { title: 'Verify3' }],
}
const REPO = 'D:/AI/Administration2'
const PROTOCOL = `RESTART PROTOCOL (mandatory — the connection sometimes drops and an agent is then restarted from scratch, while the working tree keeps everything written so far): (1) FIRST run 'git status --short' and read docs/build/progress/<your package key>.md if it exists — it lists the steps a previous attempt of THIS package completed; continue from the first unfinished step instead of starting over, and never delete or rewrite files that already implement a step correctly (read them and extend them). Uncommitted files that belong to another package's paths are that package's live work — leave them alone. (2) Do not read everything up front: read the spec lines and the code you need for the CURRENT step, write the code, build it, then move on; make your first code change within your first ten tool calls. (3) After every completed step (a screen, a test file, a green build) append one line to docs/build/progress/<your package key>.md (create it; it is the only file under docs/ you may write). (4) IMAGES: never open the full-size PNGs under design/exports/light or design/exports/dark (1–2 MB each; they overload the connection). View the small JPEG previews under design/exports/preview/light/W/<name>.jpg and design/exports/preview/dark/W/<name>.jpg (same file names as the PNGs, .jpg extension), only the screens of your own package, each at most once per comparison, right before you build or check that screen. (5) Screenshots you take must be JPEG or PNG at most 1366 px wide and viewed once. (6) When a report is handed to you as {"see": "<path>"}, read that JSON file first — it is the archived report of the previous stage.

DESIGN CONSISTENCY (owner's rule, 2026-09-16 — «انتبه للتصميم: لا يوجد تناسق وتنظيم في المكونات»): every screen is composed from Wakeel.Design components (src/Wakeel.Design/Components: WButton, WInput, WSelect, WCard, WChip, WBadge, WTabs, WTable, WKvRow, WStateCard, WDialog, WMenu, WStepper, WTree, WTimelineItem, WDocumentRow, WPager, WSearch, …). Hand-rolled lists, trees, tables, tabs, chips, forms, cards or dialogs inside a page are forbidden; page CSS may only arrange design components on the page grid and set page-specific spacing from the tokens. If a component is missing, add it to Wakeel.Design first (component + css + gallery entry + bUnit test), then use it. The screen is done only when its host screenshot, put beside the preview, shows the same anatomy: same regions and columns, same controls in the same places, same chips and states, no overlap, no clipping, aligned rows, consistent spacing. A builder writes the side-by-side comparison of every screen in its notes (deviation → fixed / accepted with reason). A reviewer opens the builder's screenshot and the preview together and reports every visible deviation as a finding: high for a hand-rolled component, medium for a layout, alignment, overlap or missing-control deviation, low for a spacing nuance.

RULES: never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/ (except your progress file), or any path outside your allowed paths (csproj files inside your allowed paths may gain ProjectReferences only). Never run git commit/checkout/stash/reset/clean. Another agent edits other pages in the same working tree concurrently: build ONLY with the exact commands given ('dotnet test --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build'); when a build fails on a file lock or on a compile error inside a file you do not own, wait 60 seconds and retry (up to five times), then report it in open_issues instead of editing that file. A package that touches Blazor pages is not done until the host actually runs: activate a temporary data folder with tests/Wakeel.Walkthrough.Tests' FirstRunWorld (or a small test helper), launch src/Wakeel.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Wakeel.Desktop.exe on it with --remote-debugging-port=<port> (use the port named in your package so concurrent packages never collide), --window-size=1366x768, --data-folder=<that folder> and --start-url=/route, confirm http://127.0.0.1:<port>/json/version answers, sign in over CDP, screenshot every screen light and dark (Page.captureScreenshot), LOOK at each beside its preview, read the host log in <data-folder>/logs for errors, then kill the process immediately — a host left running is a defect, and so is relaunching it more often than the work needs (the owner sees every window). Never touch C:\\ProgramData\\Wakeel of the real installation. All user-facing text is Arabic with no technical terms or error codes (item 15), every icon-only button has a tooltip, full RTL with bdi/isolate on Latin/numeric tokens (item 55), no mention of servers, ports, internet or PostgreSQL anywhere; Arabic strings only in src/Wakeel.Design/Text/Ar.*.cs partial files (add Ar.FollowUp.cs, Ar.Meetings.cs, Ar.Cases.cs, Ar.Parties.cs). Identifiers, comments and XML docs in English. Work until the package is complete and its build/tests are green; do not stop early.`

const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents: docs/build/packages/B4-tasks-meetings-cases-contacts.md (the B4 specification), docs/design/SCREENS.md (rows W28–W42, W48–W50) and docs/design/DESIGN-GUIDE.md, docs/AGREEMENT.md (items 12, 26, 41, 53 (a), 55, 56), docs/build/ARCHITECTURE.md (§9, §10, §12), docs/build/DATA-MODEL.md (§2, §4, §5, §6). Foundation — the ACCEPTED B4 services (read their progress files docs/build/progress/b4-*.md and public APIs before designing a screen): Wakeel.Core Services/FollowUp (TaskService, DecisionService, CommitmentService, ObstacleService, NeedService, ReportOptions), Services/Meetings (MeetingService, MeetingBriefBuilder, CalendarService) with the brief HTML in Wakeel.Reports/Briefs, Services/Cases (CaseService), Services/Parties (PartyService, DirectoryService); the B3 screens and services (correspondence details, documents, search — link to them, never rebuild them); the shell from B2 (src/Wakeel.UI/Services/Shell, MainLayout, sidebar, badges, notifications, quick capture) and the account services from B1. The screens of this milestone reuse the page patterns already accepted in B2/B3 (tabs with badges + local search + WTable + WPager for lists; header + command bar + tabs for details; WDialog forms with validation) — open the accepted page closest to yours and follow its structure before writing a new one.\n\n${PROTOCOL}`

const PACKAGES = {
  followup: {
    key: 'b4-followup-screens', title: 'B4-1b (المتابعة والمهام: القائمة، التفاصيل، الإنشاء، القرارات، الالتزامات والسداد، العوائق والاحتياجات — W28–W33)', model: 'opus', port: 9333,
    paths: 'src/Wakeel.UI/Pages/FollowUp (new), src/Wakeel.UI/Services/FollowUp (new), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only), src/Wakeel.Design/Text/Ar.FollowUp.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, each with css + gallery entry + bUnit test), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'no MarkupString with user data, postponement and payment dialogs validate before saving, amounts shown from integer minor units with the shekel formatting helper, report options never expose a confidential record',
    extraCheck: 'W28–W33 match their previews side by side; tabs with badges, local search, the task table columns and the overdue attention edge come from the design components; W29 shows the three actions, linked records, the timeline and the two report checkboxes with the comment; W31 shows the cycle execution rate; W32 records a partial payment and the state changes',
    spec: `Build W28 (follow-up hub: tabs with badges المهام/القرارات/الالتزامات/العوائق/الاحتياجات, local search, the tasks WTable with the preview's columns, overdue rows with the attention edge, «مهمة جديدة»), W29 (task details: header with state and due date, the three buttons إنجاز/تأجيل/تحويل with their dialogs — postponement requires a reason —, description, linked records, timeline, the two report options with the comment per item 53 (a)), W30 (create-task dialog with validation, source link, assignee, priority, reminder), W31 (decisions board: execution states and percentages, the current cycle's execution rate, convert a decision into a task), W32 (commitments table with computed states + the record-payment dialog), W33 (obstacles and needs: two lists with the monthly-report note and their add/edit dialogs). Wire the routes, the sidebar entry and its badge; quick capture's «مهمة» must land in W28. bUnit tests for every screen and state (empty, loaded, error), dialogs' validation, the overdue edge, badges. Host run on port 9333 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  meetings: {
    key: 'b4-meetings-screens', title: 'B4-2b (الاجتماعات والتقويم: القائمة، التفاصيل، Meeting Brief، الإنشاء، الشهر، لوحة اليوم — W34–W39)', model: 'opus', port: 9336,
    paths: 'src/Wakeel.UI/Pages/Meetings (new), src/Wakeel.UI/Pages/Calendar (new), src/Wakeel.UI/Services/Meetings (new), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — coordinate: add your own lines), src/Wakeel.Design/Text/Ar.Meetings.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, e.g. WCalendarMonth/WDayPanel, each with css + gallery entry + bUnit test), src/Wakeel.Desktop (only the print/PDF hook of the brief if the existing one does not fit), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'the brief preview renders the service HTML inside a sandboxed frame or as encoded text (no MarkupString of user data), cancelled meetings never show a reminder, confidential correspondence never appears in a brief for another user',
    extraCheck: 'W34–W39 match their previews side by side; the month grid has Arabic day names in the configured order, coloured dots per type with the colour key, navigation and today; the day panel lists items with times and states and adds an appointment or a commitment; the reminder field of W37 shows the item-56 default and accepts an override; minutes → decisions/tasks conversion works from W35',
    spec: `Build W34 (meetings list: tabs القادمة/السابقة/بلا محضر, cards or rows as the preview, the Meeting Brief button), W35 (meeting details: data, attendees, ordered agenda, minutes editor, decision rows extracted from the minutes that become decisions or tasks with one click, attachments, links), W36 (Meeting Brief page as the preview: purpose, related correspondence, pending tasks and decisions with the parties, commitments, obstacles, the last similar meeting, suggested points; print/PDF through the service HTML), W37 (create/edit meeting form with attendees from the directory, agenda ordering, links, the reminder field with the default of item 56), W38 (calendar month: the merged calendar with the colour key, navigation, first-day setting), W39 (day panel with items, times and states + the add-appointment dialog; adding a commitment from the day). Wire the routes and the sidebar entries; quick capture's «موعد» must appear in the calendar. bUnit tests for every screen and state, month grid generation for months starting on each weekday, dots per type, day panel ordering, reminder default/override, minutes conversion. Host run on port 9336 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  cases: {
    key: 'b4-cases-parties-screens', title: 'B4-3b/4b (القضايا والجهات والدليل الداخلي — W40–W42, W48–W50)', model: 'opus', port: 9337,
    paths: 'src/Wakeel.UI/Pages/Cases (new), src/Wakeel.UI/Pages/Parties (new), src/Wakeel.UI/Services/Cases and Services/Parties (new), src/Wakeel.UI/Routes.razor and Layout (route/sidebar wiring only — coordinate: add your own lines), src/Wakeel.Design/Text/Ar.Cases.cs and Ar.Parties.cs (new), src/Wakeel.Design/Components (new components only if the spec needs them, e.g. WQrCode, each with css + gallery entry + bUnit test), tests/Wakeel.UI.Tests',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.UI.Tests --no-dependencies && dotnet test tests/Wakeel.UI.Tests --no-build',
    security: 'the party QR carries only the random token, the internal directory is read-only with the note that editing happens in the admin tool, no MarkupString with user data',
    extraCheck: 'W40–W42 and W48–W50 match their previews side by side; W49 uses the Wakeel.Design WTree (never a hand-rolled tree) with the detail pane; W50 shows the name snapshots, linked correspondence and the QR with the «امسحه من الهاتف» hint; the next case session appears in the calendar',
    spec: `Build W40 (cases list: tabs and WTable as the preview), W41 (case details: header with number/title/stage/state, parties, next session, timeline events, linked correspondence, documents, notes), W42 (create/edit case as the preview's card form), W48 (parties: tabs خارجية/الدليل الداخلي, local search, table, the QR button), W49 (internal directory: WTree of authority → departments → sections → units with heads and offices + the selected item's details, read-only with the admin-tool note), W50 (party card: data, name snapshots over time, linked correspondence, statistics, QR with the hint; rename dialog that explains old correspondence keeps the old name). Wire the routes and the sidebar entries. QR rendering: the pinned QRCoder package already draws the recovery sheets (src/Wakeel.UI/Services/Account/RecoverySheet.cs) — wrap it once in a Wakeel.Design WQrCode component (SVG output, quiet zone, light/dark safe) with a gallery entry and a bUnit test, and use that component everywhere a QR is shown. bUnit tests for every screen and state, the tree/detail interaction, rename dialog, QR component. Host run on port 9337 with a seeded temporary installation: screenshot every screen light and dark, compare side by side (table in notes), kill.`,
  },
  walkthrough: {
    key: 'b4-walkthrough', title: 'B4-WALKTHROUGH (السيناريو الكامل + لقطات E2E)', model: 'sonnet', port: 9333,
    paths: 'tests/Wakeel.Walkthrough.Tests, tests/Wakeel.E2E',
    build: 'dotnet build src/Wakeel.Desktop && dotnet build tests/Wakeel.Walkthrough.Tests --no-dependencies && dotnet test tests/Wakeel.Walkthrough.Tests --no-build && dotnet build tests/Wakeel.E2E --no-dependencies && dotnet test tests/Wakeel.E2E --no-build',
    security: 'tests use temporary folders only and never touch the real installation',
    extraCheck: 'the walkthrough covers every step of the B4 acceptance list through the public services and asserts database rows and audit entries; E2E screenshots of the B4 screens are compared with the design PNGs with per-screen upper bounds',
    spec: `Build section «القبول» of docs/build/packages/B4-tasks-meetings-cases-contacts.md: (1) tests/Wakeel.Walkthrough.Tests: create a meeting with a 30-minute reminder → it shows in the calendar, the attention center and the bell at its time (drive the scheduler clock) → minutes with two decisions → convert one decision into a task with a due date → a commitment with a partial payment (state partial) → a case whose session shows in the calendar → a party renamed while the old correspondence keeps the old name; each step asserts rows and audit entries. (2) tests/Wakeel.E2E: screenshot tests for W28–W42 and W48–W50 at 1366x768 light and dark compared with design/exports/{light,dark}/W/*.png (the test code may read the PNGs; you look only at the small previews and your own screenshots), diff percentages recorded in tests/Wakeel.E2E/artifacts/ with per-screen upper bounds; host on port 9333 with a temporary folder seeded through the walkthrough helpers. Report the diff percentages per screen in notes.`,
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
log('B4 screens: follow-up (W28–W33) → cases and parties (W40–W42, W48–W50) ∥ meetings and calendar (W34–W39); then the walkthrough')
const both = await parallel([
  () => sequence('follow-up, cases and parties', ['followup', 'cases']),
  () => sequence('meetings and calendar', ['meetings']),
])
const need = [2, 1]
const ok = both.every((seq, i) => seq && seq.length === need[i] && seq.every(s => s.accepted))
if (!ok) { log('B4 screens: a chain was not accepted — the walkthrough waits for the supervisor'); return { screens: both } }
const walk = await sequence('walkthrough', ['walkthrough'])
return { screens: both, walkthrough: walk }
