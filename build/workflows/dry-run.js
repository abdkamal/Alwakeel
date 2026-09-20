// Dry-run a workflow script with a fake agent: checks the syntax, the stage order, labels, models and the
// handling of args.resume / args.skip without spending a token.
//   node build/workflows/dry-run.js <script.js> [args.json] [--fix-first] [--lose=<label>]
//   --fix-first   every first review answers 'fix' (exercises the fix/verify rounds)
//   --lose=<l>    the agent with that exact label returns nothing once (exercises the retry)
const fs = require('fs')
const [script, ...rest] = process.argv.slice(2)
if (!script) { console.error('usage: node dry-run.js <script.js> [args.json] [--fix-first] [--lose=<label>]'); process.exit(2) }
const argsFile = rest.find(a => !a.startsWith('--'))
const fixFirst = rest.includes('--fix-first')
const lose = (rest.find(a => a.startsWith('--lose=')) || '').slice(7)
const src = fs.readFileSync(script, 'utf8').replace(/^export const meta/m, 'const meta')
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const run = new AsyncFunction('args', 'agent', 'parallel', 'pipeline', 'log', 'phase', 'budget', 'workflow', src)
const calls = []
const agent = async (prompt, o) => {
  const see = (/\{"see":"([^"]+)"/.exec(prompt) || [])[1]
  calls.push(`${o.label}  [${o.model || 'inherit'}/${o.effort || '-'}]  prompt=${prompt.length}${see ? '  see=' + see : ''}`)
  if (lose && o.label === lose) return null
  if (o.schema && o.schema.properties && o.schema.properties.verdict) {
    return { verdict: fixFirst && o.label.startsWith('review1') ? 'fix' : 'accept', build_ok: true, tests_ok: true, findings: [], missing_spec_items: [], notes: '' }
  }
  return { status: 'done', files_changed: [], tests_total: 0, tests_passed: 0, build_ok: true, open_issues: [], notes: '' }
}
const parallel = ts => Promise.all(ts.map(t => t().catch(e => { console.log('THUNK ERROR', e.message); return null })))
const pipeline = async (items, ...stages) => Promise.all(items.map(async (it, i) => { let v = it; for (const s of stages) v = await s(v, it, i); return v }))
const args = argsFile ? JSON.parse(fs.readFileSync(argsFile, 'utf8')) : undefined
run(args, agent, parallel, pipeline, m => console.log('LOG ', m), () => {}, { total: null, spent: () => 0, remaining: () => Infinity }, null)
  .then(r => { console.log(calls.join('\n')); console.log('RETURN', JSON.stringify(r).slice(0, 400)) })
  .catch(e => { console.log(calls.join('\n')); console.error('SCRIPT ERROR', e.message); process.exit(1) })
