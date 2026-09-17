export const meta = {
  name: 'alwakeel-b4-services',
  description: 'B4 services (no screens): follow-up and tasks, meetings/brief/calendar, cases + parties + internal directory — one sequential chain of three Opus packages in Wakeel.Core, each reviewed, fixed and re-verified by Opus; resumable through args.resume/args.skip',
  phases: [{ title: 'Build' }, { title: 'Review' }, { title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }, { title: 'Fix3' }, { title: 'Verify3' }],
}
const REPO = 'D:/AI/Administration2'
const PROTOCOL = `RESTART PROTOCOL (mandatory — the connection sometimes drops and an agent is then restarted from scratch, while the working tree keeps everything written so far): (1) FIRST run 'git status --short' and read docs/build/progress/<your package key>.md if it exists — it lists the steps a previous attempt of THIS package completed; continue from the first unfinished step instead of starting over, and never delete or rewrite files that already implement a step correctly (read them and extend them). Uncommitted files that belong to another package's paths are that package's live work — leave them alone. (2) Do not read everything up front: read the spec lines and the code you need for the CURRENT step, write the code, build it, then move on; make your first code change within your first ten tool calls. (3) After every completed step (a service, a test file, a green build) append one line to docs/build/progress/<your package key>.md (create it; it is the only file under docs/ you may write). (4) IMAGES: never open the full-size PNGs under design/exports; this package has no screens — if you need a screen's intent, read docs/design/SCREENS.md or view the small preview under design/exports/preview/light/W/ once. (5) Any screenshot you take must be JPEG or PNG at most 1366 px wide and viewed once. (6) When a report is handed to you as {"see": "<path>"}, read that JSON file first — it is the archived report of the previous stage.

RULES: never add NuGet packages that are not already pinned in Directory.Packages.props (if one is truly required, report it in open_issues). Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/ (except your progress file), or any path outside your allowed paths (csproj files inside your allowed paths may gain PackageReferences to pinned packages and ProjectReferences). Never run git commit/checkout/stash/reset/clean. Other agents edit other projects in the same working tree concurrently (design-system and screen packages): build ONLY with the exact commands given ('dotnet test --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build'); when a build fails on a file lock or on a compile error inside a project you do not own, wait 60 seconds and retry (up to five times), then report it in open_issues instead of editing that project. Never touch C:\\ProgramData\\Wakeel of the real installation: every test uses a temporary folder. All user-facing text is Arabic with no technical terms or error codes (item 15), numerals are always Western digits 0–9 and never Arabic-Indic (design guide «الأرقام غربية», item 20) — Core-layer strings live in the static partial class CoreAr (src/Wakeel.Core/Services/CoreAr*.cs, ratified in ARCHITECTURE §12), other layers' strings in their own Ar file; identifiers, comments and XML docs in English. Passwords, keys and seeds must never reach logs, the database in clear text, or memory longer than needed. Work until the package is complete and its build/tests are green; do not stop early.`

const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents: docs/build/packages/B4-tasks-meetings-cases-contacts.md (the B4 specification), docs/AGREEMENT.md (items 12 — the merged calendar of §3 —, 26, 41, 53 (a) — report options on every record —, 55, 56), docs/design/SCREENS.md (rows W28–W42, W48–W50 — what each screen shows, so that the view models carry it), docs/build/ARCHITECTURE.md (§5, §9, §12), docs/build/DATA-MODEL.md (§0, §2, §4, §5, §6 — the tables already exist in src/Wakeel.Core/Migrations/0001_initial.sql and the entities in src/Wakeel.Core/Data/Entities/FollowUp.cs, Meetings.cs, Cases.cs, Parties.cs), docs/build/BUILD-PLAN.md (acceptance criteria). Foundation (read the public API of what you actually use, when you use it): src/Wakeel.Core (WakeelDb with audit stamps and enforced soft delete, DbSession, AuditService, SettingsService, InstallationService, FinancialCycleService, IClock, IdGenerator, ArabicText, ArabicRelativeTime, CoreAr — a static partial class, one file per area: add CoreAr.<Area>.cs —, and the ACCEPTED services you must stay consistent with: AttentionService/BadgeService (they already count overdue/near tasks, meetings of today and stale records — extend their queries only through the entities, never fork them), NotificationService, ReminderScheduler (meeting reminders per item 56: default from settings, per-meeting override), QuickCaptureService (creates tasks, notes, report notes, expenses and appointments — your services must read what it writes), Services/Correspondence (CorrespondenceService links to case/meeting, the party-name snapshot at issue time, FollowUpService, ReferralService)), tests/Wakeel.Core.Tests (DailyShellWorld/DailyShellSeed helpers, TestClock). Every service returns Arabic-ready view models for its screens (state names from CoreAr, relative days through ArabicRelativeTime, Latin/digit runs in stored audit text wrapped in U+2068…U+2069 by the CoreAr formatters). Migrations: add src/Wakeel.Core/Migrations/000N_*.sql only for a missing index or a column that DATA-MODEL names and 0001 lacks — never rewrite an applied migration.\n\n${PROTOCOL}`

const PACKAGES = [
  {
    key: 'b4-followup-services', title: 'B4-1a (خدمات المتابعة والمهام: المهام والقرارات والالتزامات والعوائق والاحتياجات)', model: 'opus',
    paths: 'src/Wakeel.Core (Services/FollowUp/* new, Services/CoreAr.FollowUp.cs new, Services/ServiceCollectionExtensions.cs registration lines, Data/Entities additions only if a column is missing against DATA-MODEL §4, Migrations/000N_*.sql only as allowed), tests/Wakeel.Core.Tests',
    build: 'dotnet build src/Wakeel.Core && dotnet build tests/Wakeel.Core.Tests --no-dependencies && dotnet test tests/Wakeel.Core.Tests --no-build',
    security: 'every state change audited, soft delete only, amounts stored as integer minor units (never floating point), report flags never leak a record marked confidential, no clear-text secrets',
    extraCheck: 'computed states are exact (commitment open/partial/paid/overdue from the payments; decision execution percentage; the current-cycle execution rate of W31 through FinancialCycleService), postponement requires a reason, conversion of a decision into a task keeps the source link, AttentionService/BadgeService counts stay correct with the new rows, and every view model carries what W28–W33 draw',
    spec: `Build section «B4-1 المتابعة والمهام» services: TaskService (title, description, due date, priority, assignee, source — correspondence/meeting/decision —, progress; complete, postpone with a reason, transfer to another assignee; reminder through ReminderScheduler; the two report options on every record per item 53 (a): «يُضمَّن في التقرير» / «يُبرَز» plus a comment; creation validation for the W30 dialog; list view model with the W28 columns and the overdue edge flag; details view model for W29 with linked records and a timeline), DecisionService (decisions sourced from a meeting or a correspondence, owner, execution state and percentage, due date, the current cycle's execution rate for the W31 board), CommitmentService (party, amount or deliverable, due date; separate payments in commitment_payments with an optional link to a finance transaction id — do not build finance here; computed states open/partial/paid/overdue; W32 table and the record-payment dialog validation), ObstacleService and NeedService (obstacle: description, impact, what is required from the higher authority, state; need: item, justification, priority; both feed the recommendations section of the monthly report — expose a query for B5), the shared ReportOptions value object. Tests: every transition allowed/refused, postponement reason, computed commitment states across partial payments and due dates with TestClock, decision → task conversion, cycle execution rate on seeded data, report options round trip, attention/badge counts with the new rows, Arabic view-model values. Register everything in the Core DI extension.`,
  },
  {
    key: 'b4-meetings-services', title: 'B4-2a (خدمات الاجتماعات وتجهيز الاجتماع والتقويم المدمج)', model: 'opus',
    paths: 'src/Wakeel.Core (Services/Meetings/* new, Services/CoreAr.Meetings.cs new, DI lines, Data/Entities additions only if a column is missing against DATA-MODEL §5, Migrations/000N_*.sql only as allowed), src/Wakeel.Reports (Briefs/* new: the HTML rendering of the meeting brief for print/PDF), tests/Wakeel.Core.Tests',
    build: 'dotnet build src/Wakeel.Core && dotnet build src/Wakeel.Reports && dotnet build tests/Wakeel.Core.Tests --no-dependencies && dotnet test tests/Wakeel.Core.Tests --no-build',
    security: 'brief HTML built from encoded text only (no raw user HTML), reminders never fire for cancelled meetings, the calendar never shows «للمستلم فقط» correspondence items to other users, every state change audited',
    extraCheck: 'the item-56 reminder default and the per-meeting override both reach ReminderScheduler; extracting decisions from the minutes creates DecisionService/TaskService rows with the source link in one transaction; the calendar union is exact on seeded data (meetings, appointments, commitment and task due dates, the financial-cycle reminder) with the right colour key per type and the configured first day of week (default Sunday); marks update after edit/cancel; the brief finds related correspondence by link and by normalised subject',
    spec: `Build section «B4-2 الاجتماعات والتقويم» services: MeetingService (title, date and time, duration, place, attendees from the directory — employees/parties/units —, ordered agenda items, links to correspondence/case, reminder per meeting or the default of item 56, minutes as text, decision rows extracted from the minutes that turn into decisions or tasks with one call, attachments through document_links, states planned/held/cancelled, the «بلا محضر» tab query; W34 list, W35 details and W37 form view models with validation), MeetingBriefBuilder (purpose, related correspondence by link and by normalised-subject search, pending tasks and decisions with the same parties, commitments, obstacles, the last similar meeting — same title or parties —, suggested points from pending decisions and overdue follow-ups; a view model for W36 plus an HTML rendering in Wakeel.Reports/Briefs for print/PDF that mirrors the W36 layout, RTL, A4), CalendarService (the computed union described in the spec: meetings, appointments — including those QuickCaptureService writes —, commitment and task due dates, the financial-cycle reminder; month view with Arabic day names and the first-day setting, coloured dots per type under the day, the day panel with items, times and states, adding an appointment or a commitment from the day, marks refreshed after edit/cancel; mark what syncs to the office and the phone through the existing sync columns only). Tests: reminder default vs override with TestClock driving ReminderScheduler, cancelled meeting fires nothing, minutes → decisions/tasks conversion in one transaction, brief on crafted data (related letters by link and by subject, last similar meeting, suggested points), brief HTML has encoded text and no placeholders, calendar union and dots for a seeded month, first-day-of-week setting, day panel ordering. Register everything in the Core DI extension.`,
  },
  {
    key: 'b4-cases-parties-services', title: 'B4-3a/4a (خدمات القضايا والجهات والدليل الداخلي)', model: 'opus',
    paths: 'src/Wakeel.Core (Services/Cases/* new, Services/Parties/* new, Services/CoreAr.Cases.cs and CoreAr.Parties.cs new, DI lines, Data/Entities additions only if a column is missing against DATA-MODEL §2/§6, Migrations/000N_*.sql only as allowed; Services/Correspondence only if the party snapshot must call the new PartyService — keep its accepted behaviour and tests), tests/Wakeel.Core.Tests',
    build: 'dotnet build src/Wakeel.Core && dotnet build tests/Wakeel.Core.Tests --no-dependencies && dotnet test tests/Wakeel.Core.Tests --no-build',
    security: 'the party QR token is random (CSPRNG), unique and carries no personal data; the internal directory is read-only (changes come from the admin tool through a setup file); every state change audited; soft delete only',
    extraCheck: 'a party rename writes a party_names snapshot and old correspondence keeps the name it was issued with; party statistics (last letter, count) are exact; the next case session shows in CalendarService; the directory tree is derived from the installed structure with heads and offices and can return a unit as an internal recipient for the outgoing wizard',
    spec: `Build sections «B4-3 القضايا» and «B4-4 الجهات والدليل» services: CaseService (case number, title, parties, stage, next session/appointment — exposed to CalendarService —, owner, state open/suspended/closed, timeline events, linked correspondence, documents through document_links, notes; W40 tabs and table, W41 details, W42 card form view models with validation), PartyService (external parties: type, contact person, phone, e-mail, address, notes; name snapshots over time in party_names with correspondence bound to the name at issue time — reconcile with the snapshot CorrespondenceService already takes; statistics: last correspondence and count; the party QR token qr_token; W48 list with local search and W50 party card view models — snapshots list, linked correspondence, QR payload with the «امسحه من الهاتف» hint text in CoreAr), DirectoryService (the internal directory derived from the installed structure, read-only: authority → departments → sections → units with heads and offices as a tree view model for W49 (shaped for the Wakeel.Design WTree: key, parent, depth, name, level, head), choosing a unit as an internal recipient, the note that editing happens in the admin tool). Tests: case transitions and timeline, next session in the calendar union, party rename keeps the old name on old correspondence and shows the new one on new correspondence, statistics on seeded data, QR token uniqueness and format, directory tree from a seeded structure with four levels, internal recipient selection, Arabic view-model values. Register everything in the Core DI extension.`,
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
  return `You are the builder of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}). ${COMMON}\nAllowed paths (create/edit only here, plus docs/build/progress/${p.key}.md): ${p.paths}.\nBuild/test command: ${p.build}\n\nSPECIFICATION:\n${p.spec}\n\nWhen done, return: status, files_changed, tests_total, tests_passed, build_ok, open_issues (anything you could not do, with the reason), notes (design decisions worth knowing, measurements, the public API the screen packages will consume).`
}
function reviewPrompt(p, build, round) {
  return `You are the Opus reviewer (round ${round}) of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}). ${COMMON}\nThe builder reported: ${JSON.stringify(build)}.\nRead the specification below and the governing docs, then read every file under ${p.paths} that the package touches and run exactly: ${p.build} (read the full output). Check: (1) every spec item is implemented (list missing ones), (2) correctness bugs and edge cases, (3) security: ${p.security}, (4) ${p.extraCheck}, (5) tests assert real behaviour (not tautologies; tests that skip must say why), warnings-as-errors clean, (6) Arabic-only user text without technical terms, RTL/bidi-safe strings. Do NOT modify any file (you may not write the progress file either). Return verdict ('accept' only when build+tests pass and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), missing_spec_items, notes.\n\nSPECIFICATION:\n${p.spec}`
}
function fixPrompt(p, review) {
  return `You are the builder of work package ${p.title} of الوكيل v0.21 (package key: ${p.key}) returning to apply review findings. ${COMMON}\nAllowed paths (plus docs/build/progress/${p.key}.md): ${p.paths}. Build/test command: ${p.build}\nReview result to address (fix every high and medium finding and every missing spec item; fix low ones too unless genuinely out of scope): ${JSON.stringify(review)}\n\nOriginal specification for reference:\n${p.spec}\n\nAfter fixing, run the build/test command until green. Return status, files_changed, tests_total, tests_passed, build_ok, open_issues, notes (what you changed per finding).`
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
// args = { skip: [keys already accepted], resume: { '<key>': { stage, file, state, rulings, extraPaths } } }
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
log('B4 services: follow-up and tasks → meetings/brief/calendar → cases, parties and directory')
const results = []
for (const p of PACKAGES) {
  if (SKIP.includes(p.key)) { results.push({ key: p.key, skipped: true }); continue }
  log(`B4 services: ${p.key}${RESUME[p.key] ? ' (resuming at ' + RESUME[p.key].stage + ')' : ''}`)
  const s = await chainFrom(p, RESUME[p.key])
  results.push(s)
  if (!s.accepted) { log(`B4 services: ${p.key} not accepted${s.failed ? ' (stage ' + s.failed + ' died)' : ''} — stopping the sequence for the supervisor`); break }
}
return results
