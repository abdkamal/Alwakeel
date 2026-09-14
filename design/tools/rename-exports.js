// node rename-exports.js <dir> '<json map [[id,name],...]>'  → renames <id>.png to <Wxx> — <name>.png (Windows-safe)
const fs = require('fs'), path = require('path');
const [dir, json] = process.argv.slice(2);
for (const [id, name] of JSON.parse(json)) {
  const src = path.join(dir, id + '.png');
  if (!fs.existsSync(src)) { console.log('missing', src); continue; }
  const safe = name.replace(/[\/:*?"<>|]/g, '-').trim();
  fs.renameSync(src, path.join(dir, safe + '.png'));
}
console.log('renamed', JSON.parse(json).length);
