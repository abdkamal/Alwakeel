export const meta = {
  name: 'alwakeel-bidi-content-pass',
  description: 'Fix mixed Arabic/English strings on every screen by content-only rewrites (Sonnet per screen group), then Opus verification per group against exported PNGs',
  phases: [{ title: 'Rewrite', detail: 'Sonnet: content-only fixes per screen group' }, { title: 'Verify', detail: 'Opus: export, read, fix leftovers' }],
}
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const OUT = { type: 'object', properties: { group: { type: 'string' }, changed: { type: 'number' }, unchanged: { type: 'number' }, notes: { type: 'string' } }, required: ['group', 'changed', 'unchanged', 'notes'] }
const VER = { type: 'object', properties: { group: { type: 'string' }, fixed: { type: 'number' }, remaining: { type: 'array', items: { type: 'string' } }, notes: { type: 'string' } }, required: ['group', 'fixed', 'remaining', 'notes'] }
const EXP = 'C:/Users/abdka/AppData/Local/Temp/claude/D--AI-Administration/f10e20c9-0335-4b7b-9855-1b077265173d/scratchpad/bidi-pass'
const RULES = `القواعد (المالك طلب دعم RTL كاملًا مع النص المختلط، البند 55):
- محرّك عرض Pen ضعيف في الاتجاه المختلط: يتجاهل علامات العزل (U+2066–U+2069) وRLM، ويخطئ عندما يجاور رقمٌ كلمةً لاتينية بعد كلمة عربية (مثل «512 GB» أو «Canon DR-C240 ... 09:12»)، أو عندما يلتصق حرف عربي بكلمة لاتينية («وOCR»)، أو عندما يبدأ السطر برمز لاتيني. البرنامج الحقيقي (متصفح داخل WebView2 وأندرويد) يعرض النص الصحيح منطقيًا بشكل سليم، لذلك يجب أن يبقى النص صحيحًا منطقيًا وطبيعيًا بالعربية، ولا نلجأ لحيل تُفسد المعنى.
- الإصلاح بتعديل content فقط (Update). ممنوع منعًا باتًا إدراج عقد جديدة أو Replace أو Move أو Delete أو تغيير أي خاصية غير content (محرّك التخطيط متوقف عن معالجة التغييرات البنيوية في هذا الملف الكبير).
- الصياغة المفضّلة: الجملة تبدأ بكلمة عربية؛ الرمز اللاتيني (اسم جهاز، ملف، إصدار، مصطلح) يُوضع بعد كلمة عربية ويفضَّل في آخر الجملة أو بعد شرطة «—»؛ الوحدات بالعربية (غيغابايت/ميغابايت/كيلوبايت)؛ الأرقام والأوقات والتواريخ تجاور كلمات عربية لا رموزًا لاتينية؛ «و» + كلمة لاتينية تُستبدل بمصطلح عربي مع اللاتيني بين قوسين عند الحاجة (مثل «والتعرّف الضوئي (OCR)»)، وإن تعذّر فاكتب «و» ثم مسافة ثم الكلمة اللاتينية؛ لا أقواس تحوي مزيجًا عربيًا ولاتينيًا معًا إن أمكن تجنّبها.
- لا تغيّر الأرقام الرسمية ولا أسماء الملفات ولا معرّفات الأجهزة ولا التواريخ ولا المبالغ؛ لا تحذف معلومات؛ لا تترجم أسماء المنتجات (Word, WebView2, Canon…).
- النص الطويل الملتف على أسطر (فقرات) غالبًا يُعرض سليمًا ما لم يحوِ نمط «رقم + رمز لاتيني»؛ عدّله فقط عند وجود النمط.`
const rewritePrompt = g => `أنت محرر نصوص واجهات عربية يعمل على ملف Pen عبر أدوات Pencil MCP. مهمتك إصلاح السطور المختلطة (عربي + إنجليزي) في مجموعة شاشات محددة من الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen بتعديل المحتوى النصي فقط.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم استدعِ mcp__pencil__read_skill بالمسار "execute.md".
2. مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
3. مجموعتك: «${g.title}» = الشاشات التي تطابق ${g.re} في الصفوف y ∈ [${g.y0}, ${g.y1}] وx بين 0 و11999 (النسخ الفاتحة فقط؛ لا تلمس النسخ الداكنة عند x ≥ 12000 ولا المكوّنات reusable).
4. اجمع السطور المختلطة بهذا الاستدعاء (عدّل النطاق):
   const AR=/[؀-ۿ]/, LT=/[A-Za-z]/; const roots=Get(n=>(n.type==='frame'&&!n.reusable&&n.x>=0&&n.x<12000&&n.y>=${g.y0}&&n.y<=${g.y1}&&${g.re}.test(n.name||''))?n.id:undefined); for(const r of roots){const rn=Get(r,{depth:0}).name.slice(0,3); Get(r,(n,c)=>{ if(n.type==='text'&&typeof n.content==='string'&&AR.test(n.content)&&LT.test(n.content)) Print(rn,'|',n.id,'|',Math.round(c.bounds.height)>(n.fontSize||14)*2?'multi':'single','|',n.content); return undefined; }); }
5. ${RULES}
6. لكل سطر قرّر: سليم (يبقى) أو يُعاد صياغته. أعد الصياغة بـ Update(id,{content:'...'}) في استدعاءات مجمّعة (عدة تحديثات في الاستدعاء الواحد). السطور التي يظهر فيها الرمز اللاتيني وحده في عقدة مستقلة (مثل قيمة في صف مفتاح/قيمة) سليمة ولا تُمس.
7. بعد التعديل صدّر كل شاشة عدّلتها للتحقق: Export([screenId],'png','${EXP}/${g.key}',{scale:2}) ثم اقرأ الصورة بأداة Read وتحقق بصريًا من السطور المعدّلة؛ أصلح ما بقي خاطئًا بتعديل المحتوى مرة أخرى.
8. الحد الأقصى 24 استدعاء execute. لا تعدّل شيئًا خارج مجموعتك.
أعد النتيجة: group، changed (عدد العقد المعدّلة)، unchanged (السليمة)، notes (أهم ما غيّرته وأي سطر بقي مشكوكًا فيه مع معرّفه).`
const verifyPrompt = (g, r) => `أنت مراجع تصميم أول. تحقق من عرض النص المختلط (عربي/إنجليزي) في مجموعة الشاشات «${g.title}» من الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen بعد أن أصلحها محرر أقل خبرة (عدّل ${r ? r.changed : '?'} عقدة؛ ملاحظاته: ${r ? r.notes.slice(0, 900) : 'لا ملاحظات'}).

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم اقرأ mcp__pencil__read_skill بالمسار "execute.md". مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute وابدأ كل snippet بسطر الحارس: ${GUARD}
2. الشاشات: تطابق ${g.re} في الصفوف y ∈ [${g.y0}, ${g.y1}] وx بين 0 و11999. صدّرها كلها: Export(ids,'png','${EXP}/${g.key}-verify',{scale:2}) ثم اقرأ كل صورة بأداة Read وافحص كل سطر يحوي رمزًا لاتينيًا أو رقمًا: هل ترتيب الكلمات صحيح للقارئ العربي؟ هل الرقم بجانب الكلمة التي يخصّها؟ هل «و» ملتصقة بكلمتها؟ هل الأقواس في موضعها؟
3. ${RULES}
4. أصلح ما بقي خاطئًا بتعديل content فقط، ثم صدّر الشاشة المعدّلة مرة أخرى وتحقق. الحد الأقصى 20 استدعاء execute.
أعد النتيجة: group، fixed (عدد العقد التي أصلحتها)، remaining (سطور لم تستطع إصلاحها بتعديل المحتوى: معرّف العقدة + النص + السبب)، notes.`
const groups = args.groups
const results = await pipeline(groups,
  g => agent(rewritePrompt(g), { label: `bidi:${g.key}`, phase: 'Rewrite', schema: OUT, model: 'sonnet' }),
  (r, g) => agent(verifyPrompt(g, r), { label: `verify:${g.key}`, phase: 'Verify', schema: VER, model: 'opus' }).then(v => ({ group: g.key, rewrite: r, verify: v })))
return results.filter(Boolean)