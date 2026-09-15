export const meta = {
  name: 'alwakeel-b0-closeout',
  description: 'B0 closeout: apply remaining review findings + supervisor rulings per package, then Opus verify (max 2 rounds)',
  phases: [{ title: 'Fix' }, { title: 'Verify' }, { title: 'Fix2' }, { title: 'Verify2' }],
}
const REPO = 'D:/AI/Administration2'
const COMMON = `Repository: ${REPO} (branch main, .NET 10 SDK 10.0.401, solution Wakeel.slnx, central package versions in Directory.Packages.props, TreatWarningsAsErrors=true). Governing documents you MUST read first: docs/build/ARCHITECTURE.md, docs/build/DATA-MODEL.md (both amended on 2026-09-16 with supervisor rulings marked "قرار 2026-09-16" / "قرارات (2026-09-16"), docs/build/BUILD-PLAN.md, docs/AGREEMENT.md, docs/design/DESIGN-GUIDE.md. The original package specification is the matching entry of the PACKAGES array in build/workflows/alwakeel-b0-foundation.js — read it. Never add NuGet packages that are not already pinned in Directory.Packages.props. Never edit Directory.Build.props, Directory.Packages.props, Wakeel.slnx, docs/, or any project outside your allowed paths. Never run git commit/checkout/stash/reset — leave changes in the working tree. Other agents edit other projects in the same working tree concurrently: build ONLY your own projects with the exact commands given (note: 'dotnet test ... --no-dependencies' is rejected by this SDK; use 'dotnet build <tests> --no-dependencies && dotnet test <tests> --no-build' where the command says so). All user-facing text is Arabic with no technical terms or error codes; identifiers, comments and XML docs in English. Work until every listed item is done and the build/tests are green; do not stop early.`

const PACKAGES = (args && args.packages) || []

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
  unresolved_items: { type: 'array', items: { type: 'string' } },
  notes: { type: 'string' } }, required: ['verdict', 'build_ok', 'tests_ok', 'findings', 'unresolved_items', 'notes'] }

function fixPrompt(p, round, prev) {
  return `You are the builder of work package ${p.title} of الوكيل v0.21 applying the closeout items (round ${round}). ${COMMON}\nAllowed paths: ${p.paths}. Build/test command: ${p.build}\n\nITEMS TO IMPLEMENT (every one, no exceptions; when an item says "as the reviewer suggested" read the reviewer's finding in the review file for the precise fix):\n${p.items}\n\nReview file(s) with the full findings text: ${p.reviewFiles}\n${prev ? `\nThe previous round's Opus verification returned: ${JSON.stringify(prev)} — resolve every high/medium finding and every unresolved item it lists.` : ''}\n\nAfter the changes, run the build/test command until it is green (0 warnings, 0 failures). Return status, files_changed, tests_total, tests_passed, build_ok, open_issues (only things you truly could not do, with the reason), notes (what you changed per item).`
}
function verifyPrompt(p, fix, round) {
  return `You are the Opus verifier (closeout round ${round}) of work package ${p.title} of الوكيل v0.21. ${COMMON}\nThe builder reported: ${JSON.stringify(fix)}.\nThe items the builder had to implement:\n${p.items}\nReview file(s) with the earlier findings: ${p.reviewFiles}\n\nRead every file under ${p.paths} that the items touch, run exactly: ${p.build} (read the full output), and check that EVERY item is implemented correctly and tested with real assertions, that nothing regressed, that the rulings in DATA-MODEL.md/ARCHITECTURE.md marked 2026-09-16 are honoured, and that no new high/medium defect was introduced. Do NOT modify any file. Return verdict ('accept' only when build+tests pass, every item is done, and there are no high or medium findings), build_ok, tests_ok, findings (severity, file, issue, precise fix), unresolved_items (items not done or done wrongly), notes.`
}

log(`B0 closeout: ${PACKAGES.map(p => p.key).join(', ')}`)
const results = await pipeline(
  PACKAGES,
  p => agent(fixPrompt(p, 1, null), { label: `fix:${p.key}`, phase: 'Fix', schema: BUILD_SCHEMA, model: p.model || 'sonnet', effort: 'high' }),
  (fix, p) => agent(verifyPrompt(p, fix, 1), { label: `verify:${p.key}`, phase: 'Verify', schema: REVIEW_SCHEMA, model: 'opus', effort: 'medium' }).then(v => ({ fix, verify: v })),
  (s, p) => (s.verify && s.verify.verdict === 'fix')
    ? agent(fixPrompt(p, 2, s.verify), { label: `fix2:${p.key}`, phase: 'Fix2', schema: BUILD_SCHEMA, model: p.model || 'sonnet', effort: 'high' }).then(f => ({ ...s, fix2: f }))
    : Promise.resolve({ ...s, fix2: null }),
  (s, p) => (s.fix2)
    ? agent(verifyPrompt(p, s.fix2, 2), { label: `verify2:${p.key}`, phase: 'Verify2', schema: REVIEW_SCHEMA, model: 'opus', effort: 'medium' }).then(v => ({ ...s, verify2: v }))
    : Promise.resolve({ ...s, verify2: null }),
)
return PACKAGES.map((p, i) => ({ key: p.key, ...(results[i] || { failed: true }) }))
