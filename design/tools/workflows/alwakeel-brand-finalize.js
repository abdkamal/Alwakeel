export const meta = {
  name: 'alwakeel-brand-finalize',
  description: 'One Opus designer completes the brand board: app icon set, Android adaptive icon, splash screens and installer header derived from each logo candidate (owner picks later)',
  phases: [{ title: 'Brand', detail: 'Opus designer builds the derivative assets inside the BRAND frame' }],
}
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const OUT = { type: 'object', properties: { assets: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' }, nodeId: { type: 'string' } }, required: ['name', 'nodeId'] } }, ok: { type: 'boolean' }, notes: { type: 'string' } }, required: ['assets', 'ok', 'notes'] }
const prompt = `أنت مصمم هوية بصرية أول يعمل على ملف Pen عبر أدوات Pencil MCP. مهمتك إكمال لوحة العلامة في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم استدعِ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md كاملًا (الرموز $، قواعد execute، الممنوعات).
2. مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
ملاحظة بيئية: وكلاء آخرون يبنون شاشات في صفوف أخرى من الملف الآن؛ لا تلمس أي عقدة خارج إطار العلامة. قد يفشل TakeScreenshot بخطأ "Failed to export an image" (عطل عام مؤقت): لا تكرره أكثر من مرتين وتحقق بديلًا عبر Get (bounds وproblems). قد تُبلّغ bounds عن إزاحة غريبة لدقيقة (تأخر محرّك) فأعد الفحص لاحقًا بدل إعادة البناء.
3. الوضع الحالي: الإطار الجذري "BRAND — الهوية البصرية" (المعرّف s7Urbn) عند (0, -700) بعرض 1366، تخطيط عمودي، يحوي عنوانًا ووصفًا وإطار LogoCandidates (YaEHy) بثلاثة مقترحات: Candidate1 (PZqRW، الفن LogoArt1 = K92L6h "الختم الرسمي")، Candidate2 (lt5T2، LogoArt2 = d1n0v "الوكيل الحارس")، Candidate3 (mVGZS، LogoArt3 = l8mSZL "حرف الواو"). افحصها أولًا بـ Get لتفهم بنيتها وألوانها. المالك سيختار أحدها لاحقًا، لذا ابنِ المشتقات لكل مقترح على حدة بحيث يكون الاختيار بصريًا ومباشرًا.
4. أضف داخل الإطار s7Urbn (كأبناء جديدة بعد LogoCandidates، كل قسم إطار باسم واضح مع عنوان نصي 14/700 بلون $muted) لكل مقترح i من 1 إلى 3 صفًا أفقيًا باسم "Derivatives<i>" يحوي:
   أ. "AppIcon<i>": أيقونة ويندوز — مربع 256×256 بزوايا 48 وتعبئة $primary-fill (أو تدرج خفيف من رموز $ فقط) يحمل نسخة مبسطة من شعار المقترح (Copy لإطار LogoArt<i> ثم تصغير/تبسيط)، وبجانبه معاينات 48 و32 و16 (نُسخ مصغّرة بالحجم الحقيقي) لتقييم الوضوح.
   ب. "AdaptiveIcon<i>": الأيقونة التكيفية لأندرويد — إطار 108×108 (الخلفية بلون $primary-fill) مع طبقة أمامية فيها الشعار داخل منطقة الأمان الدائرية 66×66 في المنتصف، ومعاينتان بقناع دائري وقناع مربع مستدير (48×48 كل واحدة) على يمينها.
   ج. "Splash<i>": شاشة بداية ويندوز 480×300 بتعبئة $bg: الشعار 96 في الوسط، اسم البرنامج "الوكيل" 28/700 بلون $text، سطر "مساعد مكتب المدير" 14 بلون $muted، شريط تقدم رفيع بلون $primary 200×4 أسفلها، وسطر "الإصدار 0.21" 12 بلون $muted في أسفل اليمين.
   د. "PhoneSplash<i>": شاشة بداية أندرويد 206×457 (نصف مقياس 412×915) بتعبئة $primary-fill: الشعار بالأبيض 72 في المنتصف واسم "الوكيل" 20/700 أبيض تحته.
   هـ. "InstallerHeader<i>": ترويسة المثبّت 720×72 بتعبئة $primary-fill: الشعار 40 أبيض يمينًا ثم "تثبيت الوكيل" 18/700 أبيض و"الإصدار 0.21 — مكتب مدير دائرة التخطيط" 12 بلون $on-fill-muted (إن لم يوجد الرمز فاستخدم $white مع opacity 0.8).
   الاتجاه RTL: ما يجب أن يظهر يمينًا يُدرج أخيرًا في التخطيطات الأفقية. الألوان كلها رموز $ فقط (لا hex)، الخط $font، الأيقونات lucide. كل المشتقات تُشتق من فن المقترح نفسه (Copy لإطار LogoArt<i> ثم تحجيم أبنائه)، لا من شعار مختلف.
5. أضف في آخر الإطار s7Urbn بطاقة نصية قصيرة "ملاحظة الاختيار": "يختار المالك مقترحًا واحدًا؛ تُعتمد مشتقاته وتُحذف البقية قبل بدء البناء."
6. تحقق: Get(s7Urbn, (n,c) => c.problems && Print(n.name, c.problems)) وأصلح أي قص أو تداخل حقيقي؛ صوّر إن أمكن. الحد الأقصى 40 استدعاء execute؛ إن فشل استدعاء أصلحه بمعامل edits وeditId.
7. ممنوع: تعديل المقترحات الثلاثة الأصلية أو أي عقدة خارج s7Urbn، SetVariables، ألوان hex، عقد بلا اسم.
أعد النتيجة: assets (الاسم ومعرّف العقدة لكل مشتق)، ok، notes (ما تحقق منه وما لم يتحقق بصريًا).`
phase('Brand')
const r = await agent(prompt, { label: 'brand:derivatives', phase: 'Brand', schema: OUT, model: 'opus' })
return r