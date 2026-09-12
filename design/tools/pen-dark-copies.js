// Pencil MCP execute snippet: creates the dark-theme copy of every screen frame at x + 12000.
// Idempotent: skips screens whose dark copy (same name, x + 12000) already exists. Run in chunks (LIMIT) to keep the app responsive.
if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');
const LIMIT = 12;
const screens = Get(n => (n.type === 'frame' && !n.reusable && n.x >= 0 && n.x < 12000 && n.y >= 3900 && /^(W\d\d|A\d\d|I\d\d|M\d\d) — /.test(n.name || '')) ? {id: n.id, name: n.name, x: n.x, y: n.y} : undefined);
const dark = new Set(Get(n => (n.type === 'frame' && n.x >= 12000 && /^(W\d\d|A\d\d|I\d\d|M\d\d) — /.test(n.name || '')) ? n.name : undefined));
const todo = screens.filter(s => !dark.has(s.name)).sort((a, b) => a.y - b.y || a.x - b.x).slice(0, LIMIT);
for (const s of todo) Copy(s.id, document, {theme: {mode: 'dark'}, x: s.x + 12000, y: s.y});
Print(`screens ${screens.length}, dark existing ${dark.size}, copied now ${todo.length}, remaining ${screens.length - dark.size - todo.length}`);
