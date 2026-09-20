export const meta = {
  name: 'alwakeel-logo-apply',
  description: 'One Opus designer replaces the temporary app-logo tiles with the approved seal logo on the first-run, admin login, about, installer and phone onboarding screens',
  phases: [{ title: 'Logo', detail: 'Opus designer applies the approved logo' }],
}
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const OUT = { type: 'object', properties: { changed: { type: 'array', items: { type: 'object', properties: { screen: { type: 'string' }, nodeId: { type: 'string' }, what: { type: 'string' } }, required: ['screen', 'nodeId', 'what'] } }, skipped: { type: 'array', items: { type: 'string' } }, ok: { type: 'boolean' }, notes: { type: 'string' } }, required: ['changed', 'skipped', 'ok', 'notes'] }
const prompt = `أنت مصمم هوية بصرية أول يعمل على ملف Pen عبر أدوات Pencil MCP. اعتمد المالك شعار «الختم الرسمي» ومهمتك تطبيقه على الشاشات التي تعرض شعار التطبيق «الوكيل» نفسه في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم استدعِ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md كاملًا (خاصة قسم «العلامة المعتمدة» في آخره وقواعد execute والممنوعات).
2. مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
3. الشعار المعتمد: الإطار LogoArt1 (المعرّف K92L6h، 280×280) داخل لوحة العلامة s7Urbn، ونسخته البيضاء (نقش على خلفية $primary-fill) موجودة داخل InstallerHeader1 (المعرّف M3mcX) وPhoneSplash1 (Y6Oirx). طريقة الاشتقاق المعتمدة: Copy لإطار LogoArt1 إلى الهدف ثم تحجيم أبنائه بمعامل الحجم/280 (x, y, width, height, strokeWidth)، أو Copy للنسخة البيضاء من InstallerHeader1 حين تكون الخلفية ملوّنة. افحص بنية K92L6h وM3mcX أولًا بـ Get.
4. الأهداف (شعار التطبيق فقط — لا تلمس شعار المؤسسة الظاهر في القائمة الجانبية أو M.OrgHeader أو شاشة الدخول W05 أو M04، فذلك شعار المؤسسة الذي يأتي من ملف الإعداد):
   - W02 «التشغيل الأول — ملف الإعداد»: بلاطة الشعار المؤقتة.
   - A01 وA02 (أداة مدير النظام): إن وُجدت بلاطة شعار للتطبيق.
   - W90 «الإعدادات — حول والدورة المالية» وM25 «الإعدادات» (صف «حول» إن كان فيه شعار).
   - قالب المثبّت TI0 (المعرّف HpNIs) ونوافذ I01–I06: بلاطة الشعار في الترويسة → النسخة البيضاء 40px كما في InstallerHeader1.
   - M01 «الترحيب» وM24 «إعادة الاقتران»: بلاطة الشعار.
   اعثر على الشاشات بالاسم عبر Get(n => (n.name || '').startsWith('W02 — ') ? n.id : undefined) وهكذا، وعلى بلاطة الشعار داخلها بالاسم أو بكونها إطارًا مربعًا صغيرًا يحوي أيقونة (غالبًا landmark أو stamp أو shield) بجوار اسم «الوكيل». استبدل الأيقونة بالشعار المشتق بالحجم نفسه (أو أدرجه داخل البلاطة وعطّل الأيقونة القديمة enabled:false)، مع الحفاظ على التخطيط والأبعاد كي لا يتغير شيء آخر في الشاشة. حيث لا يوجد شعار للتطبيق في الشاشة فلا تُضِف شيئًا وسجّلها في skipped.
5. ممنوع: تعديل أي مكوّن reusable باستثناء قالب المثبّت HpNIs المذكور صراحةً (وهو غير reusable)، SetVariables، ألوان hex، لمس النسخ الداكنة (x ≥ 12000) — سأعيد توليدها بعدك، أو أي عقدة خارج الأهداف. الحد الأقصى 40 استدعاء execute؛ إن فشل استدعاء أصلحه بمعامل edits وeditId.
6. تحقق من كل شاشة معدّلة: Get problems (لا قص جديد) ثم TakeScreenshot لها (أو لبلاطة الشعار مكبّرة) وتأكد من وضوح الشعار في حجمه الصغير؛ إن كان غير مقروء دون 32px فاستخدم نسخة مبسّطة (الخاتم بلا التفاصيل الداخلية الدقيقة) واذكر ذلك.
أعد النتيجة: changed (الشاشة، معرّف العقدة المعدّلة، ماذا تغيّر)، skipped، ok، notes.`
phase('Logo')
const r = await agent(prompt, { label: 'logo:apply', phase: 'Logo', schema: OUT, model: 'opus' })
return r