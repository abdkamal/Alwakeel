# Workflow scripts for the design phase

Run with the Claude Code `Workflow` tool (`scriptPath` + `args`).

- `alwakeel-design-rows-routed.js` — Windows screens (template atJKT). `args.rows = [{title, y, screens:[{id, name, col, nav, model?}], existing?:[{id, nodeId}], reviewModel?}]`; x = 1450 × col. Builders default to Sonnet, reviewers to Opus.
- `alwakeel-design-rows-routed-v2.js` — same, plus per-row `template` (`atJKT` | `ACap6` | `HpNIs`), `xStep`, `w`, `h`. For ACap6 `nav` is the admin tab name ("ATab <name>"), `—` for no tab, `بلا قائمة` for the standalone pre-login screens.
- `alwakeel-android-kit.js` — builds the M.* kit (three Opus builders by x-range) then one Opus reviewer that fixes the kit and creates the phone template TM0 at (10200, 21000). No args.
- `batch6-args.json`, `batch7-args.json` — the row arguments used for batches 6 and 7.

Every execute snippet starts with the guard from docs/design/DESIGN-GUIDE.md; save the .pen with `../pen-save.ps1` after each batch and commit.
- `alwakeel-android-kit-review.js` — review-only pass over an already-inserted M.* kit (args.comps = [{name,nodeId}]); used after the kit builders died at the usage limit. Fixes the kit and builds TM0.
