export const meta = {
  name: 'alwakeel-android-kit',
  description: 'Build the Android M.* component kit in the Pen file with three Opus builders, then one Opus reviewer that fixes the kit and builds the phone screen template',
  phases: [{ title: 'Build', detail: 'three Opus builders, one x-range each' }, { title: 'Review', detail: 'Opus reviewer fixes the kit and builds TM0' }],
}
const GUARD = "if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');"
const COMMON = `مرّر filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen" في كل استدعاء execute، وابدأ كل snippet بسطر الحارس: ${GUARD}
ملاحظة بيئية: عند الضغط على التطبيق قد تعود لقطة شاشة عقدة جديدة فارغة أو تُبلّغ bounds عن إزاحة غريبة أو "clipped" لدقيقة أو أكثر؛ هذا تأخر في المحرّك لا خطأ في بياناتك. لا تُعد البناء؛ تابع ثم أعد الفحص لاحقًا. إن فشل الاتصال بالتطبيق مؤقتًا فأعد المحاولة بعد قليل. وكلاء آخرون يبنون شاشات ويندوز في صفوف أخرى من الملف نفسه في الوقت ذاته؛ لا تلمس أي عقدة خارج نطاقك.`
const BUILD = { type: 'object', properties: { components: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' }, nodeId: { type: 'string' }, x: { type: 'number' }, w: { type: 'number' }, h: { type: 'number' } }, required: ['name', 'nodeId'] } }, ok: { type: 'boolean' }, notes: { type: 'string' } }, required: ['components', 'ok', 'notes'] }
const REVIEW = { type: 'object', properties: { templateId: { type: 'string' }, results: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' }, nodeId: { type: 'string' }, status: { type: 'string' }, issues: { type: 'string' } }, required: ['name', 'nodeId', 'status', 'issues'] } }, notes: { type: 'string' } }, required: ['templateId', 'results', 'notes'] }
const GROUPS = [
  { key: 'bars', x0: 0, x1: 3100, names: ['M.StatusBar', 'M.TopBar', 'M.OrgHeader', 'M.BottomNav', 'M.FAB', 'M.FAB.Extended', 'M.Tabs', 'M.SyncRow'], extra: 'أنت أيضًا من يُدرج إطار عنوان الصف "§ مكوّنات الأندرويد" عند (0, 20960): نص 14/700 بلون $muted داخل إطار بلا تعبئة (reusable:false).' },
  { key: 'lists', x0: 3200, x1: 6300, names: ['M.ListItem', 'M.ListItem.Attention', 'M.Card', 'M.Card.Info', 'M.Card.Warning', 'M.Card.Danger', 'M.Card.Success', 'M.Chip', 'M.Chip.Selected', 'M.Badge', 'M.Avatar', 'M.Avatar.Photo', 'M.SegmentedRow'], extra: '' },
  { key: 'inputs', x0: 6400, x1: 9900, names: ['M.Button.Filled', 'M.Button.Outlined', 'M.Button.Text', 'M.Button.Danger', 'M.TextField', 'M.TextField.Error', 'M.TextField.Amount', 'M.SearchBar', 'M.Sheet', 'M.Dialog', 'M.Snackbar', 'M.StateView'], extra: '' },
]
const buildPrompt = g => `أنت مصمم واجهات أول يعمل على ملف Pen عبر أدوات Pencil MCP. مهمتك بناء مجموعة من مكوّنات الأندرويد القابلة لإعادة الاستخدام (M.*) في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen وفق المواصفة حرفيًا.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill". ثم استدعِ mcp__pencil__read_skill بلا مسار، ثم بالمسار "execute.md".
2. اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md كاملًا (الرموز $، قواعد RTL: العنصر الذي يجب أن يظهر يمينًا يُدرج أخيرًا، قواعد execute) ثم D:\\AI\\Administration2\\docs\\design\\ANDROID-KIT.md كاملًا (المواصفة).
3. ${COMMON}
4. مكوّناتك (بهذه الأسماء بالضبط): ${g.names.join('، ')}. ضعها في الصف y=21000 داخل النطاق الأفقي x من ${g.x0} إلى ${g.x1} بالترتيب المذكور من اليسار إلى اليمين بفواصل 40px (المكوّن الأول عند x=${g.x0}). كل مكوّن إطار جذري (أب = document) بـ reusable:true والاسم المطلوب، وبأبعاده المحددة في المواصفة، بالرموز $ فقط، والخط $font، وأيقونات lucide. المتغيرات (مثل M.Card.Info) مكوّنات مستقلة reusable:true بالاسم الكامل (يمكن بناؤها بـ Copy من الأساس ثم تعديلها). سمِّ الأبناء الداخلية بأسماء إنجليزية واضحة (Title, Subtitle, Icon, Badge, Leading, Trailing…) ليسهل ضبطها لاحقًا عبر descendants. الشارات الاختيارية تُدرج معطّلة (enabled:false) بالاسم المذكور. ${g.extra}
5. ممنوع: تعديل أي مكوّن أو عقدة خارج نطاقك، SetVariables، ألوان hex صريحة، الخروج عن النطاق الأفقي المخصص لك. إن وُجد مكوّن بالاسم نفسه من محاولة سابقة فاحذفه أولًا ثم أعد بناءه.
6. تحقق: Get لكل مكوّن مع problems، ثم TakeScreenshot لمجموعات من 3–4 مكوّنات معًا وتحقق بصريًا (RTL، لا قص، الأبعاد، الألوان الدلالية)، وأصلح ما يلزم.
7. الحد الأقصى 30 استدعاء execute. إن فشل استدعاء أصلحه بمعامل edits وeditId كما يشرح execute.md.

أعد النتيجة بالبنية المطلوبة: components (الاسم، معرّف العقدة، x، العرض، الارتفاع) وok وnotes.`
const reviewPrompt = comps => `أنت مراجع تصميم أول لملف Pen. راجع عدّة مكوّنات الأندرويد (M.*) في الملف D:\\AI\\Administration2\\design\\alwakeel-windows.pen مقابل المواصفة، وأصلح ما يلزم مباشرة، ثم ابنِ قالب شاشة الهاتف.

1. حمّل أدوات Pencil بأداة ToolSearch بالاستعلام "select:mcp__pencil__execute,mcp__pencil__read_skill" ثم اقرأ mcp__pencil__read_skill بلا مسار ثم بالمسار "execute.md". اقرأ D:\\AI\\Administration2\\docs\\design\\DESIGN-GUIDE.md وD:\\AI\\Administration2\\docs\\design\\ANDROID-KIT.md كاملين.
2. ${COMMON}
3. المكوّنات المبنية (الاسم | معرّف العقدة): ${comps.map(c => `${c.name} | ${c.nodeId}`).join(' ؛ ')}. إن كان معرّف مفقودًا أو خاطئًا اعثر عليه بالاسم عبر Get(n => n.name === NAME && n.reusable ? n.id : undefined). إن كان مكوّن من المواصفة مفقودًا كليًا فابنِه أنت في مكانه المنطقي بالصف y=21000 (x بعد آخر مكوّن + 40).
4. لكل مكوّن: افحص problems ثم صوّر مجموعات من 3–4 مكوّنات بـ TakeScreenshot وقارن بالمواصفة: الأبعاد، الترتيب RTL (الرئيسية في أقصى يمين M.BottomNav، العنوان يمينًا في M.TopBar…)، الرموز $ فقط، الخط $font، أسماء الأبناء واضحة، الشارات معطّلة بالاسم الصحيح، لا تكرار للأسماء بين المكوّنات (اسم واحد = مكوّن reusable واحد)، ولا تداخل بين المكوّنات في الصف. أصلح مباشرة.
5. بعد الإصلاح ابنِ القالب "TM0 — قالب شاشة الهاتف" عند x=10200, y=21000: إطار جذري 412×915 بتعبئة $bg وclip:true وتخطيط عمودي بلا فجوة: M.StatusBar (ref) ثم M.TopBar (ref) ثم إطار "Content" (fill_container عرضًا وارتفاعًا، عمودي، حشو [12,16] فجوة 12، فارغ) ثم M.BottomNav (ref)، وM.FAB (ref) بموضع مطلق x=16, y=815. reusable:false وplaceholder:false. تحقق منه بلقطة شاشة.
6. الحد الأقصى 40 استدعاء execute. لا تلمس أي عقدة خارج الصف y=21000 (باستثناء إطار العنوان عند y=20960).
أعد النتيجة: templateId (معرّف TM0)، results لكل مكوّن (name, nodeId, status من {ok, fixed, rebuilt}, issues)، notes.`
phase('Build')
const built = await parallel(GROUPS.map(g => () => agent(buildPrompt(g), { label: `kit:${g.key}`, phase: 'Build', schema: BUILD, model: 'opus' })))
const comps = built.filter(Boolean).flatMap(b => b.components)
log(`kit builders returned ${comps.length} components`)
phase('Review')
const review = await agent(reviewPrompt(comps), { label: 'kit:review+TM0', phase: 'Review', schema: REVIEW, model: 'opus' })
return { built, review }
