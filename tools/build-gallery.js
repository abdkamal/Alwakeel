// node build-gallery.js → writes design/exports/index.html: an RTL gallery of every exported screen (light with a dark toggle).
const fs = require('fs'), path = require('path');
const root = path.resolve(__dirname, '..', 'exports');
const groups = [['W', 'ويندوز — البرنامج الرئيسي'], ['A', 'أداة مدير النظام'], ['I', 'المثبّت'], ['M', 'الهاتف (أندرويد)']];
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
let cards = '', total = 0;
for (const [g, title] of groups) {
  const dir = path.join(root, 'light', g);
  if (!fs.existsSync(dir)) continue;
  const files = fs.readdirSync(dir).filter(f => f.endsWith('.png')).sort();
  if (!files.length) continue;
  cards += `<h2 id="g-${g}">${title} <span class="count">${files.length}</span></h2><div class="grid ${g === 'M' ? 'phone' : ''}">`;
  for (const f of files) {
    const name = f.replace(/\.png$/, '');
    const dark = fs.existsSync(path.join(root, 'dark', g, f)) ? `dark/${g}/${encodeURIComponent(f)}` : '';
    cards += `<figure><a href="light/${g}/${encodeURIComponent(f)}" target="_blank"><img loading="lazy" src="light/${g}/${encodeURIComponent(f)}" data-light="light/${g}/${encodeURIComponent(f)}" data-dark="${dark}" alt="${esc(name)}"></a><figcaption>${esc(name)}</figcaption></figure>`;
    total++;
  }
  cards += '</div>';
}
const html = `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><title>الوكيل — معرض الشاشات</title>
<style>body{font-family:"Noto Sans Arabic","Segoe UI",sans-serif;margin:0;background:#f4f6f8;color:#1f2937}header{position:sticky;top:0;background:#fff;border-bottom:1px solid #e5e7eb;padding:12px 24px;display:flex;gap:16px;align-items:center;z-index:2}header h1{font-size:18px;margin:0}header nav a{margin-inline-start:12px;color:#0f766e;text-decoration:none}button{border:1px solid #cbd5e1;background:#fff;border-radius:8px;padding:6px 12px;cursor:pointer;font:inherit}main{padding:16px 24px}h2{font-size:16px;margin:24px 0 8px}.count{color:#64748b;font-weight:400;font-size:13px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:16px}.grid.phone{grid-template-columns:repeat(auto-fill,minmax(220px,1fr))}figure{margin:0;background:#fff;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden}figure img{display:block;width:100%;height:auto;background:#e5e7eb}figcaption{padding:8px 10px;font-size:13px}body.dark{background:#0b1220;color:#e5e7eb}body.dark figure{background:#111827;border-color:#1f2937}body.dark header{background:#111827;border-color:#1f2937;color:#e5e7eb}</style></head>
<body><header><h1>الوكيل v0.21 — معرض التصميم (${total} شاشة)</h1><button id="t">عرض النسخة الداكنة</button><nav>${groups.map(([g, t]) => `<a href="#g-${g}">${t}</a>`).join('')}</nav></header>
<main>${cards}</main>
<script>const b=document.getElementById('t');let d=false;b.onclick=()=>{d=!d;document.body.classList.toggle('dark',d);b.textContent=d?'عرض النسخة الفاتحة':'عرض النسخة الداكنة';for(const i of document.querySelectorAll('img[data-dark]')){const s=d&&i.dataset.dark?i.dataset.dark:i.dataset.light;i.src=s;i.closest('a').href=s;}};</script></body></html>`;
fs.writeFileSync(path.join(root, 'index.html'), html);
console.log('gallery written:', total, 'screens');
