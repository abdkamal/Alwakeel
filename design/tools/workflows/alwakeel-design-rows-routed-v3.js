export const meta = {
  name: 'alwakeel-design-rows-routed-v3',
  description: 'Build Pen screens row by row (Windows, admin, installer and Android phone templates) with Sonnet builders, Opus reviewers and Opus for complex screens',
  phases: [{ title: 'Build', detail: 'Sonnet builders, Opus for complex screens' }, { title: 'Review', detail: 'Opus reviewer per row, fixes in place' }],
}
const BUILD_MODEL = 'sonnet'
const REVIEW_MODEL = 'opus'
const RESULT = { type: 'object', properties: { screen: { type: 'string' }, nodeId: { type: 'string' }, ok: { type: 'boolean' }, notes: { type: 'string' } }, required: ['screen', 'nodeId', 'ok', 'notes'] }
const REVIEW = { type: 'object', properties: { results: { type: 'array', items: { type: 'object', properties: { screen: { type: 'string' }, status: { type: 'string' }, issues: { type: 'string' } }, required: ['screen', 'status', 'issues'] } } }, required: ['results'] }
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const COMMON = `مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
ملاحظة بيئية مهمة: عند الضغط على التطبيق قد تعود لقطة شاشة عقدة جديدة فارغة أو تُبلّغ bounds عن إزاحة غريبة (مثل y=50) أو "clipped" لدقيقة أو أكثر؛ هذا تأخر في المحرّك لا خطأ في بياناتك. لا تُعد البناء ولا تنسخ الشاشة من جديد؛ تابع العمل ثم أعد الفحص لاحقًا باستدعاء رخيص. إن فشل TakeScreenshot بخطأ "Failed to export an image" فهذا عطل عام مؤقت في المحرّك: لا تكرر المحاولة أكثر من مرتين، وتحقق بديلًا عبر Get (bounds وproblems) واذكر ذلك في الملاحظات. إن فشل الاتصال بالتطبيق مؤقتًا فأعد المحاولة بعد قليل.`
const dims = row => { const t = row.template || 'atJKT'; const ph = t === 'TM0'; return { w: row.w || (ph ? 412 : 1366), h: row.h || (ph ? 915 : 768), step: row.xStep || (ph ? 500 : 1450), template: t } }
const phoneShell = s => {
  const common = ` الأوراق السفلية (M.Sheet) والحوارات (M.Dialog) تُرسم كطبقة مطلقة 412×915 بتعبئة $scrim والعنصر في الأسفل أو المنتصف مع بقاء الشاشة خلفها مقروءة. استخدم مكوّنات M.* بالمرجع (ref مع descendants): M.ListItem للقوائم (ارتفاع 72، فواصل)، M.Card.* للبطاقات، M.TextField.* للحقول، M.Chip للفئات، M.Tabs للتبويبات بشارات، M.Snackbar للرسائل، M.StateView للحالات الفارغة؛ أهداف اللمس 48px على الأقل والنصوص 12 فأكثر؛ الاتجاه RTL (العناوين والأيقونات الأساسية يمينًا). لا تعديل على مكوّنات M.* نفسها.`
  if (s.nav === 'بلا قائمة') return `قالب شاشة الهاتف TM0 (412×915، Material 3): انسخه بـ Copy('TM0', document, {name, x, y}) ثم احذف من النسخة M.TopBar وM.BottomNav وM.FAB (أبقِ M.StatusBar) وابنِ محتوى مركزيًا داخل الإطار Content (شعار أو رسم توضيحي، عنوان ونص شرح، أزرار M.Button.Filled/Text بعرض كامل في الأسفل).` + common
  if (s.nav === '—') return `قالب شاشة الهاتف TM0 (412×915، Material 3): انسخه بـ Copy('TM0', document, {name, x, y}) ثم احذف من النسخة M.BottomNav وM.FAB (شاشة تفاصيل أو نموذج مدفوعة فوق التنقل)، واضبط M.TopBar عبر descendants (عنوان الشاشة، وزر الرجوع بأيقونة arrow-right يمينًا بدل menu)، واملأ الإطار Content بحيث يمتد حتى أسفل الشاشة؛ في النماذج ضع زر الحفظ الرئيسي (M.Button.Filled بعرض كامل) في أسفل المحتوى.` + (s.org ? ' أدرج M.OrgHeader (ref) بين M.StatusBar وM.TopBar.' : '') + common
  return `قالب شاشة الهاتف TM0 (412×915، Material 3): انسخه بـ Copy('TM0', document, {name, x, y}). شاشة تبويب رئيسية: اضبط في M.BottomNav عبر descendants العنصر «${s.nav}» وحده نشطًا (كبسولة $primary-soft خلف الأيقونة ونص $primary 600) مع تفعيل الشارات المطلوبة فقط (Badge<N> بالعدد)، واضبط عنوان M.TopBar على اسم الشاشة، وأبقِ M.FAB عند (16,815).` + (s.org ? ' أدرج M.OrgHeader (ref) بين M.StatusBar وM.TopBar (اسم المؤسسة والمكتب).' : '') + common
}
const shellText = (s, row) => {
  const d = dims(row)
  if (d.template === 'TM0') return phoneShell(s)
  if (d.template === 'HpNIs') return `قالب المثبّت HpNIs (نافذة ${d.w}×${d.h}): انسخه بـ Copy('HpNIs', document, {name, x, y}) ثم املأ الإطار المسمى Body بمحتوى الخطوة، وعدّل نصوص أزرار التذييل Cancel/Back/Next عبر descendants (مثل Next/Ds0MU) بما يناسب الخطوة (مثلًا "تثبيت" أو "إنهاء")، وعطّل أو أخفِ الزر غير المناسب (مثل "السابق" في الترحيب). لا قائمة جانبية ولا شريط علوي.`
  if (d.template === 'ACap6') {
    if (s.nav === 'بلا قائمة') return `شاشة مستقلة قبل الدخول (${d.w}×${d.h}) بتعبئة $bg بلا شريط علوي ولا تبويبات: انسخ القالب ACap6 ثم احذف من النسخة الشريط العلوي وAdminTabs (أو ابنِ إطارًا جذريًا جديدًا بالاسم نفسه) وابنِ بطاقة محتوى مركزية.`
    if (s.nav === '—') return `قالب أداة مدير النظام ACap6: انسخه بـ Copy('ACap6', document, {name, x, y}) ثم اعثر بالاسم على PageHeader وContent وAdminTabs. لا تبويب نشط في هذه الشاشة: أزل التمييز من تبويب "الهيئة والهوية" (stroke '$surface' وعرض 0 والنص 500/$muted) ولا تميّز غيره.`
    return `قالب أداة مدير النظام ACap6: انسخه بـ Copy('ACap6', document, {name, x, y}) ثم اعثر بالاسم على PageHeader وContent وAdminTabs. التبويب النشط هو "ATab ${s.nav}": اضبط عليه stroke:'$primary', strokeWidth:{bottom:2} والنص fontWeight:'700', fill:'$primary'، وأزل التمييز من تبويب "الهيئة والهوية" (stroke '$surface' وعرض 0 والنص 500/$muted) إن لم يكن هو المطلوب.`
  }
  if (s.nav === 'بلا قائمة') return `بلا قائمة (شاشة مستقلة ${d.w}×${d.h} بتعبئة $bg بلا قائمة جانبية ولا شريط علوي: انسخ القالب atJKT ثم احذف Sidebar وTopBar من النسخة وابنِ محتوى مركزيًا داخل Page)`
  if (s.nav === '—') return `قالب ويندوز atJKT مع القائمة الجانبية بلا عنصر محدد (انسخ القالب واضبط descendants للقائمة بحيث لا يوجد عنصر محدد مع إبقاء الشارات؛ شاشات الإعدادات والمزامنة تُفتح من تذييل القائمة)`
  return `قالب ويندوز atJKT والعنصر النشط في القائمة الجانبية ${s.nav} (انسخ القالب واضبط descendants للقائمة ليكون هذا العنصر وحده محددًا مع إبقاء الشارات)`
}
const buildPrompt = (s, row) => {
  const d = dims(row)
  const kit = d.template === 'TM0' ? ' ثم اقرأ D:\\AI\\Administration2\\docs\\design\\ANDROID-KIT.md كاملًا (مواصفة مكوّنات M.* وهيكل شاشة الهاتف).' : ''
  return `أنت مصمم واجهات يعمل على ملف Pen عبر أدوات Pencil MCP. مهمتك بناء شاشة واحدة فقط في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen.

الخطوات الإلزامية بالترتيب:
1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill". ثم استدعِ mcp__pencil__read_skill بلا مسار، ثم بالمسار "execute.md".
2. اقرأ الدليل D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md كاملًا بأداة Read.${kit} ثم اقرأ سطر شاشتك في D:\\AI\\Administration2\\docs\\design\\SCREENS.md (أداة Grep عن النص "| ${s.id} |" مع output_mode content) وهو وصف المحتوى المطلوب.
3. شاشتك: المعرّف ${s.id}، الاسم "${s.name}"، اسم الإطار الجذري "${s.id} — ${s.name}"، الإحداثيات x=${s.col * d.step}, y=${row.y}، الحجم ${d.w}×${d.h}. القالب والإطار: ${shellText(s, row)}
4. ${COMMON}
5. ابنِ الشاشة كما في الدليل: Copy للقالب إلى الإحداثيات مع placeholder:true، ثم عدّل الترويسة (العنوان والوصف والأزرار المناسبة) واملأ المحتوى بمحتوى كامل واقعي حسب وصف السطر: بيانات عربية واقعية متسقة مع بقية النظام (المؤسسة "هيئة تنمية المناطق الريفية"، المكتب "مكتب مدير دائرة التخطيط"، الموظف "أحمد الخطيب"، الجهاز PLN-PC-01، أرقام رسمية بصيغة 20260912/12046، تواريخ سبتمبر وأكتوبر 2026، مبالغ بالشيكل ₪)، شارات دائرية على التبويبات، بحث محلي في القوائم، تلميحات وألوان دلالية. استخدم المكوّنات القابلة لإعادة الاستخدام (ref مع descendants) قدر الإمكان، وابنِ الباقي بإطارات ونصوص وأيقونات lucide. الحوارات تُرسم كطبقة فوق الشاشة (frame بـlayoutPosition:'absolute' ${d.w}×${d.h} بتعبئة $scrim والحوار في المنتصف أو بجانب المحتوى الذي يجب أن يبقى مقروءًا).
6. ممنوع: تعديل أي مكوّن reusable، SetVariables، حذف أو تحريك أي عقدة جذرية أخرى، إنشاء شاشات أخرى، ألوان hex صريحة (استخدم رموز $)، وأي ذكر لخادم أو منفذ أو إنترنت أو PostgreSQL (قاعدة البيانات محلية مشفّرة داخل البرنامج). المحتوى كله داخل ${d.w}×${d.h}: لا تتجاوز الارتفاع، وقلّل عدد الصفوف عند الحاجة. إن وُجد إطار جذري بالاسم نفسه من محاولة سابقة فاحذفه أولًا.
7. في النهاية: افحص المشكلات بـ Get(screenId, (n,c) => c.problems && Print(n.name, c.problems)) وأصلح أي قص أو تداخل حقيقي، ثم TakeScreenshot(['vkZPZ', screenId]) وتحقق بصريًا (RTL صحيح، لا تداخل، نص واضح، الإطار والقالب سليمان)، وأصلح ما يلزم، ثم Update(screenId, {placeholder:false}).
8. الحد الأقصى 16 استدعاء execute. إن فشل استدعاء أصلحه بمعامل edits وeditId كما يشرح execute.md.

أعد النتيجة بالبنية المطلوبة: screen="${s.id}"، nodeId=معرّف الإطار الجذري للشاشة، ok=هل اكتملت بلا مشكلات ظاهرة، notes=ملاحظات مختصرة.`
}
const reviewPrompt = (row, list) => {
  const d = dims(row)
  const shell = d.template === 'TM0' ? `شاشات هاتف ${d.w}×${d.h} من القالب TM0 (Material 3، RTL): اقرأ أيضًا D:\\AI\\Administration2\\docs\\design\\ANDROID-KIT.md. تحقق أن M.BottomNav يُظهر التبويب الصحيح وحده نشطًا في شاشات التبويبات، وأن شاشات التهيئة والتفاصيل والنماذج بلا تنقل سفلي (زر رجوع في M.TopBar)، وأن الرئيسية والإعدادات تحملان M.OrgHeader، وأن أهداف اللمس ≥48 والنصوص ≥12، والـFAB عند (16,815) في شاشات التبويبات فقط، والأوراق والحوارات طبقة $scrim.` : d.template === 'HpNIs' ? `نوافذ مثبّت ${d.w}×${d.h} من القالب HpNIs: ترويسة $primary-fill، محتوى في Body، أزرار التذييل (إلغاء يسارًا، السابق/التالي يمينًا) بنصوص مناسبة للخطوة.` : d.template === 'ACap6' ? `شاشات أداة مدير النظام ${d.w}×${d.h} من القالب ACap6: شريط علوي داكن وتبويبات علوية؛ تحقق أن التبويب النشط في AdminTabs هو الصحيح للشاشة وأنه لا يوجد تبويبان مميّزان معًا، وأن A01/A02 مستقلتان بلا شريط.` : `شاشات ويندوز ${d.w}×${d.h} من القالب atJKT: تحقق أن القائمة الجانبية تُظهر العنصر الصحيح وحده محددًا (أو لا عنصر عندما تكون الشاشة من تذييل القائمة) والشارات ظاهرة.`
  return `أنت مراجع تصميم أول (جودة عالية) لملف Pen. راجع شاشات الصف "${row.title}" في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen وأصلح ما يلزم مباشرة. بعض الشاشات بناها مصمم أقل خبرة؛ دقّق في الاتساق والاكتمال.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم اقرأ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md.
2. ${COMMON}
3. الشاشات (المعرّف | معرّف العقدة): ${list.map(b => `${b.screen} | ${b.nodeId}`).join(' ؛ ')}. إن كان معرّف عقدة مفقودًا أو خاطئًا اعثر على الشاشة بالاسم عبر Get(n => (n.name || '').startsWith(ID + ' — ') ? n.id : undefined). ${shell}
4. لكل شاشة: افحص Get(screenId, (n,c) => c.problems && Print(n.name, c.problems)) ثم TakeScreenshot(['vkZPZ', screenId]) (يمكن تصوير شاشتين في استدعاء واحد). قيّم: هل اتجاه RTL صحيح (العناوين يمينًا، الأيقونات يمين النص، الأعمدة تبدأ من اليمين)؟ هل يوجد قص أو تداخل أو نص غير مقروء أو فراغ كبير غير مبرر؟ هل المحتوى يطابق وصف السطر في SCREENS.md (اقرأه بـ Grep "| ID |") ويغطي كل عناصره؟ هل البيانات متسقة مع بقية النظام (المؤسسة "هيئة تنمية المناطق الريفية"، المكتب "مكتب مدير دائرة التخطيط"، الموظف "أحمد الخطيب"، الجهاز PLN-PC-01، التاريخ 12/09/2026) ومع الشاشات المجاورة في الصف نفسه (الأسماء والأرقام والتواريخ لا تتناقض)؟ هل الشاشة داخل ${d.w}×${d.h} وplaceholder=false؟ هل الألوان دلالية والتلميحات موجودة على الأزرار غير البديهية؟ هل يخلو النص من أي ذكر لخادم أو منفذ أو إنترنت أو PostgreSQL؟
5. أصلح مباشرة بـ Update/Insert/Delete داخل الشاشة فقط (لا تلمس المكوّنات reusable ولا المتغيرات ولا شاشات صفوف أخرى). إن كانت شاشة فارغة أو ناقصة أو مفقودة فأكملها أو ابنِها وفق الدليل ووصف SCREENS.md.
6. الحد الأقصى 24 استدعاء execute.
أعد النتيجة: لكل شاشة status من {ok, fixed, broken} وissues مختصرة بالعربية.`
}
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
