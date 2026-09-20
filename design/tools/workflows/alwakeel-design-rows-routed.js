export const meta = {
  name: 'alwakeel-design-rows-routed',
  description: 'Build Pen screens row by row with cheap builders (Sonnet) and strong reviewers (Opus); complex screens on Opus',
  phases: [{ title: 'Build', detail: 'Sonnet builders, Opus for complex screens' }, { title: 'Review', detail: 'Opus reviewer per row, fixes in place' }],
}
const BUILD_MODEL = 'sonnet'
const REVIEW_MODEL = 'opus'
const RESULT = { type: 'object', properties: { screen: { type: 'string' }, nodeId: { type: 'string' }, ok: { type: 'boolean' }, notes: { type: 'string' } }, required: ['screen', 'nodeId', 'ok', 'notes'] }
const REVIEW = { type: 'object', properties: { results: { type: 'array', items: { type: 'object', properties: { screen: { type: 'string' }, status: { type: 'string' }, issues: { type: 'string' } }, required: ['screen', 'status', 'issues'] } } }, required: ['results'] }
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const COMMON = `مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
ملاحظة بيئية مهمة: عند الضغط على التطبيق قد تعود لقطة شاشة عقدة جديدة فارغة أو تُبلّغ bounds عن إزاحة غريبة (مثل y=50) أو "clipped" لدقيقة أو أكثر؛ هذا تأخر في المحرّك لا خطأ في بياناتك. لا تُعد البناء ولا تنسخ الشاشة من جديد؛ تابع العمل ثم أعد الفحص لاحقًا باستدعاء رخيص. إن فشل الاتصال بالتطبيق مؤقتًا فأعد المحاولة بعد قليل.`
const buildPrompt = (s, row) => `أنت مصمم واجهات يعمل على ملف Pen عبر أدوات Pencil MCP. مهمتك بناء شاشة واحدة فقط في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen.

الخطوات الإلزامية بالترتيب:
1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill". ثم استدعِ mcp__pencil__read_skill بلا مسار، ثم بالمسار "execute.md".
2. اقرأ الدليل D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md كاملًا بأداة Read. ثم اقرأ سطر شاشتك في D:\\AI\\Administration2\\docs\\design\\SCREENS.md (أداة Grep عن النص "| ${s.id} |" مع output_mode content) وهو وصف المحتوى المطلوب.
3. شاشتك: المعرّف ${s.id}، الاسم "${s.name}"، اسم الإطار الجذري "${s.id} — ${s.name}"، الإحداثيات x=${s.col * 1450}, y=${row.y}، العنصر النشط في القائمة الجانبية: ${s.nav === 'بلا قائمة' ? 'بلا قائمة (شاشة مستقلة 1366×768 بتعبئة $bg بلا قائمة جانبية ولا شريط علوي: انسخ القالب atJKT ثم احذف Sidebar وTopBar من النسخة وابنِ محتوى مركزيًا داخل Page)' : s.nav === '—' ? 'لا عنصر محدد (انسخ القالب واضبط descendants للقائمة بحيث لا يوجد عنصر محدد مع إبقاء الشارات)' : s.nav + ' (انسخ القالب واضبط descendants للقائمة ليكون هذا العنصر محددًا مع إبقاء الشارات)'}.
4. ${COMMON}
5. ابنِ الشاشة كما في الدليل: Copy للقالب atJKT إلى الإحداثيات مع placeholder:true، ثم عدّل PageHeader (العنوان والوصف والأزرار المناسبة) واملأ Content بمحتوى كامل واقعي حسب وصف السطر: بيانات عربية واقعية متسقة مع بقية النظام (المؤسسة "هيئة تنمية المناطق الريفية"، المكتب "مكتب مدير دائرة التخطيط"، الموظف "أحمد الخطيب"، أرقام رسمية بصيغة 20260912/12046، تواريخ سبتمبر وأكتوبر 2026، مبالغ بالشيكل ₪)، شارات دائرية على التبويبات، بحث محلي في القوائم، تلميحات وألوان دلالية. استخدم المكوّنات القابلة لإعادة الاستخدام (ref مع descendants) قدر الإمكان، وابنِ الباقي بإطارات ونصوص وأيقونات lucide. الحوارات تُرسم كطبقة فوق الشاشة (frame بـlayoutPosition:'absolute' 1366×768 بتعبئة $scrim والحوار في المنتصف أو بجانب المحتوى الذي يجب أن يبقى مقروءًا).
6. ممنوع: تعديل أي مكوّن reusable، SetVariables، حذف أو تحريك أي عقدة جذرية أخرى، إنشاء شاشات أخرى، ألوان hex صريحة (استخدم رموز $). المحتوى كله داخل 1366×768: لا تتجاوز الارتفاع، وقلّل عدد الصفوف عند الحاجة. إن وُجد إطار جذري بالاسم نفسه من محاولة سابقة فاحذفه أولًا.
7. في النهاية: افحص المشكلات بـ Get(screenId, (n,c) => c.problems && Print(n.name, c.problems)) وأصلح أي قص أو تداخل حقيقي، ثم TakeScreenshot(['vkZPZ', screenId]) وتحقق بصريًا (RTL صحيح، لا تداخل، نص واضح، القائمة الجانبية والشريط العلوي سليمان)، وأصلح ما يلزم، ثم Update(screenId, {placeholder:false}).
8. الحد الأقصى 16 استدعاء execute. إن فشل استدعاء أصلحه بمعامل edits وeditId كما يشرح execute.md.

أعد النتيجة بالبنية المطلوبة: screen="${s.id}"، nodeId=معرّف الإطار الجذري للشاشة، ok=هل اكتملت بلا مشكلات ظاهرة، notes=ملاحظات مختصرة.`
const reviewPrompt = (row, list) => `أنت مراجع تصميم أول (جودة عالية) لملف Pen. راجع شاشات الصف "${row.title}" في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen وأصلح ما يلزم مباشرة. بعض الشاشات بناها مصمم أقل خبرة؛ دقّق في الاتساق والاكتمال.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم اقرأ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md.
2. ${COMMON}
3. الشاشات (المعرّف | معرّف العقدة): ${list.map(b => `${b.screen} | ${b.nodeId}`).join(' ؛ ')}. إن كان معرّف عقدة مفقودًا أو خاطئًا اعثر على الشاشة بالاسم عبر Get(n => (n.name || '').startsWith(ID + ' — ') ? n.id : undefined).
4. لكل شاشة: افحص Get(screenId, (n,c) => c.problems && Print(n.name, c.problems)) ثم TakeScreenshot(['vkZPZ', screenId]) (يمكن تصوير شاشتين في استدعاء واحد). قيّم: هل اتجاه RTL صحيح (العناوين يمينًا، الأيقونات يمين النص، الأعمدة تبدأ من اليمين)؟ هل يوجد قص أو تداخل أو نص غير مقروء أو فراغ كبير غير مبرر؟ هل المحتوى يطابق وصف السطر في SCREENS.md (اقرأه بـ Grep "| ID |") ويغطي كل عناصره؟ هل البيانات متسقة مع بقية النظام (المؤسسة "هيئة تنمية المناطق الريفية"، المكتب "مكتب مدير دائرة التخطيط"، الموظف "أحمد الخطيب"، الجهاز PLN-PC-01، التاريخ 12/09/2026)؟ هل القائمة الجانبية تُظهر العنصر الصحيح محددًا والشارات ظاهرة؟ هل الشاشة داخل 1366×768 وplaceholder=false؟ هل الألوان دلالية والتلميحات موجودة على الأزرار غير البديهية؟
5. أصلح مباشرة بـ Update/Insert/Delete داخل الشاشة فقط (لا تلمس المكوّنات reusable ولا المتغيرات ولا شاشات صفوف أخرى). إن كانت شاشة فارغة أو ناقصة أو مفقودة فأكملها أو ابنِها وفق الدليل ووصف SCREENS.md.
6. الحد الأقصى 24 استدعاء execute.
أعد النتيجة: لكل شاشة status من {ok, fixed, broken} وissues مختصرة بالعربية.`
const out = []
for (const row of args.rows) {
  const existing = (row.existing || []).map(e => ({ screen: e.id, nodeId: e.nodeId }))
  let built = []
  if ((row.screens || []).length) {
    const res = await parallel(row.screens.map(s => () => agent(buildPrompt(s, row), { label: `build:${s.id}`, phase: 'Build', schema: RESULT, model: s.model || BUILD_MODEL })))
    built = row.screens.map((s, i) => res[i] || { screen: s.id, nodeId: '?' })
    log(`${row.title}: ${res.filter(Boolean).length}/${row.screens.length} screens built`)
  }
  const list = existing.concat(built)
  const review = await agent(reviewPrompt(row, list), { label: `review:${row.title}`, phase: 'Review', schema: REVIEW, model: row.reviewModel || REVIEW_MODEL })
  out.push({ row: row.title, built: list, review })
}
return out