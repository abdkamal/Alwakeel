// Pencil MCP execute snippet: exports every screen (light + dark) as PNG at 1x into design/exports/<group>/<Wxx>.png.
// Export writes <nodeId>.png; the rename map printed at the end is applied by tools/rename-exports.js.
if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');
const GROUP = 'W'; const DARK = false; const LIMIT = 40;
const re = new RegExp('^' + GROUP + '\d\d — ');
const screens = Get(n => (n.type === 'frame' && !n.reusable && re.test(n.name || '') && (DARK ? n.x >= 12000 : (n.x >= 0 && n.x < 12000))) ? {id: n.id, name: n.name} : undefined)
  .sort((a, b) => a.name.localeCompare(b.name)).slice(0, LIMIT);
const dir = 'D:/AI/Administration2/design/exports/' + (DARK ? 'dark/' : 'light/') + GROUP;
Export(screens.map(s => s.id), 'png', dir, {scale: 1});
Print('MAP ' + JSON.stringify(screens.map(s => [s.id, s.name])));
