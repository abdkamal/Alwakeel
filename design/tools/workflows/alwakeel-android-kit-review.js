export const meta = {
  name: 'alwakeel-android-kit-review',
  description: 'Review-only pass over the already-inserted Android M.* kit: one Opus reviewer fixes the components against ANDROID-KIT.md and builds the TM0 phone template',
  phases: [{ title: 'Review', detail: 'Opus reviewer fixes the kit and builds TM0' }],
}
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const REVIEW = { type: 'object', properties: { templateId: { type: 'string' }, results: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' }, nodeId: { type: 'string' }, status: { type: 'string' }, issues: { type: 'string' } }, required: ['name', 'nodeId', 'status', 'issues'] } }, notes: { type: 'string' } }, required: ['templateId', 'results', 'notes'] }
const comps = args.comps
const prompt = `أنت مراجع تصميم أول لملف Pen. راجع عدّة مكوّنات الأندرويد (M.*) في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen مقابل المواصفة، وأصلح ما يلزم مباشرة، ثم ابنِ قالب شاشة الهاتف. المكوّنات أُدرجت من قبل وكلاء انقطعوا قبل أن يُبلغوا عن حالتها النهائية، فافترض أن أيًا منها قد يكون ناقصًا.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم اقرأ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md وD:\\AI\\Administration2\\docs\\design\\ANDROID-KIT.md كاملين.
2. مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
ملاحظة بيئية: وكلاء آخرون يبنون شاشات ويندوز في صفوف أخرى من الملف نفسه الآن؛ لا تلمس أي عقدة خارج الصف y=21000–21300 وإطار العنوان عند y=20960. قد تعود bounds بإزاحة غريبة أو "clipped" لدقيقة (تأخر محرّك)؛ أعد الفحص لاحقًا بدل إعادة البناء. إن فشل TakeScreenshot بخطأ "Failed to export an image" فهذا عطل عام في المحرّك: لا تكرر المحاولة أكثر من مرتين، وتحقق بديلًا عبر Get (bounds وproblems والأبناء) واذكر في notes أن التحقق البصري لم يتم.
3. المكوّنات الموجودة (الاسم | معرّف العقدة): ${comps.map(c => `${c.name} | ${c.nodeId}`).join(' ؛ ')}. أربعة منها (M.Chip, M.Chip.Selected, M.Badge, M.Avatar, M.Avatar.Photo, M.SegmentedRow) تقع في سطر ثانٍ y=21200 تحت مجموعة القوائم؛ هذا مقبول ما دام لا تداخل. إن كان مكوّن من المواصفة مفقودًا كليًا فابنِه أنت في مكانه المنطقي (x بعد آخر مكوّن في السطر + 40). إن وُجد اسم مكرر (مكوّنان reusable بالاسم نفسه) فاحتفظ بالأكمل واحذف الآخر.
4. لكل مكوّن: افحص problems والأبناء وقارن بالمواصفة: الأبعاد، الترتيب RTL (الرئيسية في أقصى يمين M.BottomNav، العنوان يمينًا في M.TopBar…)، الرموز $ فقط (لا hex)، الخط $font، أيقونات lucide، أسماء الأبناء واضحة (Title, Subtitle, Icon, Badge, Leading, Trailing…)، الشارات الاختيارية معطّلة (enabled:false) بالاسم الصحيح، reusable:true لكل مكوّن، ولا تداخل بين المكوّنات في الصف. صوّر مجموعات من 3–4 مكوّنات معًا إن عمل التصوير. أصلح مباشرة.
5. بعد الإصلاح ابنِ القالب "TM0 — قالب شاشة الهاتف" عند x=10200, y=21000: إطار جذري 412×915 بتعبئة $bg وclip:true وتخطيط عمودي بلا فجوة: M.StatusBar (ref) ثم M.TopBar (ref) ثم إطار "Content" (fill_container عرضًا وارتفاعًا، عمودي، حشو [12,16] فجوة 12، فارغ) ثم M.BottomNav (ref)، وM.FAB (ref) بموضع مطلق x=16, y=815. reusable:false وplaceholder:false. تحقق منه (لقطة إن أمكن وإلا Get).
6. الحد الأقصى 45 استدعاء execute. إن فشل استدعاء أصلحه بمعامل edits وeditId كما يشرح execute.md. ممنوع SetVariables وتعديل أي عقدة خارج نطاقك.
أعد النتيجة: templateId (معرّف TM0)، results لكل مكوّن (name, nodeId, status من {ok, fixed, rebuilt}, issues)، notes.`
phase('Review')
const review = await agent(prompt, { label: 'kit:review+TM0', phase: 'Review', schema: REVIEW, model: 'opus' })
return review