# دليل تصميم الوكيل v0.21 في Pen

الملف الوحيد للتصميم: `D:\AI\Administration2\design\alwakeel-windows.pen` (يضم ويندوز وأداة المدير والمثبّت والأندرويد). يُعدَّل عبر Pencil MCP فقط (`mcp__pencil__execute`).

## قواعد إلزامية لكل استدعاء execute

1. **حارس الملف**: أول سطر في كل snippet:
   `if (!Get(n => n.id === 'voVsM' ? 1 : undefined).length) throw new Error('wrong file');`
   (تطبيق Pen يوجّه كل الاستدعاءات إلى اللوحة النشطة مهما كان `filePath`؛ الخطأ يلغي كل التعديلات في الاستدعاء).
2. مرّر `filePath: "/D:/AI/Administration2/design/alwakeel-windows.pen"` دائمًا.
3. لا تنشئ متغيرات (SetVariables) ولا تعدّل مكوّنًا قابلًا لإعادة الاستخدام (reusable) ولا تحذف أي عقدة جذرية ليست من إنشائك.
4. لا تستخدم `Replace` على عقدة داخل instance؛ استخدم `descendants` عند الإنشاء أو `Update` بالمسار `instanceId/childId`.
5. أول لقطة في كل استدعاء `TakeScreenshot` قد تعود فارغة: مرّر `'vkZPZ'` أولًا ثم العقدة المطلوبة.
6. كل عقدة تُنشأ لها `name` عربي أو دلالي واضح.
7. الشاشة الجديدة: انسخ القالب `atJKT` بـ`Copy('atJKT', document, {name, x, y})` إلى الإحداثيات المحددة لها في SCREENS.md، ثم اعثر على `Content` و`PageHeader` و`Sidebar` داخل النسخة بالاسم (`Get(copyId, n => n.name === 'Content' ? n.id : undefined)[0]`)، واملأ `Content`. الحوارات والقوائم المنبثقة تُرسم داخل الشاشة نفسها كطبقة فوقها: frame بـ`layoutPosition:'absolute'` بحجم 1366×768 بتعبئة `$scrim` يحتوي الحوار في المنتصف.
8. ضع `placeholder:true` على الشاشة أثناء العمل وأزله عند الانتهاء.
9. اختم كل شاشة بـ: فحص المشكلات `Get(screenId, (n,c) => c.problems && Print(n.name, c.problems))` وإصلاحها، ثم لقطة واحدة للشاشة، ثم `Update(screenId, {placeholder:false})`.

## ممنوعات المحتوى (قرارات معتمدة)

- قاعدة البيانات هي SQLite مشفّرة بـSQLCipher داخل البرنامج (القرار 2): لا PostgreSQL، لا خادم، لا منفذ، لا خدمة، لا كلمة مرور قاعدة بيانات، لا "اتصال" بقاعدة. في مركز الصحة تُوصف كـ"قاعدة البيانات المحلية (مشفّرة بـSQLCipher)" بحالة سلامة وحجم وآخر فحص ونسخة احتياطية.
- لا إنترنت ولا خادم لأي وظيفة (القرار 4 في قيود البيئة): لا "الاتصال بالخادم"، لا سحابة، لا حساب عبر الإنترنت. المزامنة بالحزم الموقّعة وUSB فقط.
- لا مصطلحات تقنية ولا رموز أخطاء في نصوص الواجهة (القرار 15): لا UUID، لا HTTP، لا أسماء تقنيات في الرسائل الموجهة للمستخدم إلا SQLCipher/OCR/Word/QR/USB/PDF حيث تلزم.
- البيانات النموذجية ثابتة: المؤسسة "هيئة تنمية المناطق الريفية"، المكتب "مكتب مدير دائرة التخطيط"، الموظف "أحمد الخطيب" (مدير المكتب، الموظف رقم 2، الجهاز 1، الحاسوب PLN-PC-01)، التاريخ 12/09/2026، الترقيم 20260912/12046، العملة ₪.

## قواعد RTL

- الترتيب في التخطيط الأفقي من اليسار إلى اليمين، لذا يُكتب الأطفال بعكس ترتيب القراءة: العنصر الذي يجب أن يظهر على **اليمين** يُدرج **آخرًا**. مثال: `[Actions, Titles]` يجعل العناوين يمينًا والأزرار يسارًا؛ زر بأيقونة: `[Label, Icon]` يجعل الأيقونة على يمين النص.
- النصوص متعددة الأسطر: `textGrowth:'fixed-width'`, `width:'fill_container'`, `textAlign:'right'`.
- الأعمدة في الجداول: العمود الأول (الرقم/الأهم) يُدرج آخرًا.
- المحتوى المكدّس عموديًا يستخدم `alignItems:'end'` ليلتصق باليمين.
- الأرقام غربية (0-9) والتاريخ يوم/شهر/سنة (`14/09/2026`) والوقت `10:24`.

## الرموز (Tokens) — تُستعمل بـ`$`

ألوان: bg, surface, surface2, border, border-strong, text, muted, placeholder, primary, primary-hover, primary-pressed, primary-soft, primary-border, primary-fill, success, success-soft, success-border, success-fill, warning, warning-soft, warning-border, warning-fill, danger, danger-soft, danger-border, danger-fill, info, info-soft, info-border, info-fill, neutral-soft, disabled, disabled-bg, sidebar, sidebar-hover, sidebar-selected, sidebar-text, sidebar-muted, sidebar-divider, table-header, attention-bg, scrim, white, on-fill.
خط وقياسات: font (Noto Sans Arabic), radius 6, radius-lg 10, radius-pill 999, fs-meta 12, fs-body 14, fs-section 16, fs-title 22, fs-display 26.
كل الألوان لها قيمة فاتحة وداكنة (محور `mode`)؛ لا تكتب ألوانًا صريحة (hex) إلا لنقاط الحالة الصغيرة عند الضرورة.

الألوان الدلالية: أحمر `danger` للخطِر وغير القابل للتراجع، كهرماني `warning` للتحذير، أخضر `success` للنجاح، أزرق `info` للمعلومة، `primary` للإجراء الرئيسي والحالة "قيد المتابعة".
حالات المراسلة: جديد = info، قيد المتابعة = primary، بانتظار رد = warning، منجز = success، مغلق/مؤرشف = neutral (`neutral-soft`/`muted`)، ملغى = danger، مسودة = warning. السرية: عام (globe, neutral)، خاص (user, info)، سري (lock, warning)، سري للغاية (shield, danger).

## المكوّنات القابلة لإعادة الاستخدام (id ← الأبناء القابلة للتخصيص)

استخدمها كـ`{type:'ref', ref:ID, name:'…', descendants:{childId:{…}}}`. المفاتيح في `descendants` هي معرّفات الأبناء (أو أسماؤها إن كانت فريدة داخل المكوّن).

أزرار (ارتفاع 38): Button.Primary `voVsM` (Label `Ds0MU`, Icon `N2zNb`) · Button.Secondary `zObxv` (LL17I, v5XRL) · Button.Danger `a31uFu` (PEpiF, waf4Q) · Button.Text `jhm7L` (g4RbI5, OhLBU) · Button.Disabled `HDJC2` (VusDL, F8zO8Q) · Button.Icon `u1krF2` (Icon `Jwr9X`) · Button.Split `i2kMQ` (Label `JpabU`) · Tooltip `vkZPZ` (Text `X9BOQJ`) · Kbd `UVzZJ` (Key).
حقول (عرض 260، غيّره بـwidth): Input.Text `b88Gl` (Label `kJnIz`, Required `rkdDK`, Value `N5oAg`, Hint `x2o34Q`) · Input.Select `U7OLVt` (qoJcV, k5GtPC, Value `l8EQY8`, Hint `Z7OeTV`) · Input.Search `iRj78` (a8VAIP, u5YRn, vn5fF, C0nTmA) · Input.Date `n3zFw` (O0o49, kxKyY, oZ57f, NTOks) · Input.Textarea `M7PARe` (EUKbl, SgIE5, Tp0V2, fx4Dt) · Input.Error `nGfOt` (P5EfXx, WvPzY, UEV82, Hint `eCoc8` = نص الخطأ).
اختيار: Checkbox `Us8YS` (Label `pOSE0`, Check `fEKbe`) · Radio `MhpyY` (Label `a8vGAd`, Inner `A188K`) · Toggle `aA8KP` (Label `ce3k1`, Knob `ZB2rD`) · Segmented `js2FB` (SegLabel1..3: uF788, OpQyL, I7k7H).
شارات: Chip.Status `HKz4z` (Label `IkHOY`, Dot `N2P3d`; غيّر fill للإطار و fill للنص والنقطة حسب الحالة) · Confidentiality.Row `IzP1c` (Label `j6kZB`, Icon `yulQD`) · Badge.Count `J6gxKY` (أحمر، Count) · Badge.Count.Tab `LotpO` · Badge.Count.Group `ZP4WP` · Avatar `d79gRp` (Initial).
بطاقات: Card.Standard `RxuDt` (Title `D9CmLO`, Body `QsGnU` فتحة تُملأ بـInsert(refId + '/QsGnU', …) أو ببناء بطاقة خاصة) · Card.Info `uiYQZ` (Title `KncSJ`, Desc `Gyli0`, Icon `BULvg`) · Card.Warning `K0bmsm` (dufI4, NtYnt, sjzkt) · Card.Danger `MCc3W` (Fz8XS, WUHjQ, nRCw0) · Card.Success `BZLTc` (X3p3PP, hedIA, Wl3QA) · KPI.Card `cYa48` (Label `oA1Pk`, Icon `x9RV3d`, Value `KNIaA`, Sub `csA8E`) · State.Card `RjHWE` (Icon `WsJU3`, Title `v0Gln`, Desc `qtQul`, Action `UxO4D` زر ثانوي: `UxO4D/LL17I`).
تنقل داخلي: Tabs.Bar `uIN6p` (عينة 4 تبويبات) · Tab.Item `L9MBd` (Label, Count، عطّل الشارة بـ`Badge:{enabled:false}`) · Tab.Item.Active `LKzbL` · Stepper `vVbSi` (7 خطوات: StepLabel1..7 = IJuQ3, kdLnp, ZFvRa, ZPiEC, J9XEbq, nlQC6, jXW77؛ Circle1..7 = sK23w, JdzCk, H8w5QG, WrstZ, exij4, LX4uC, mNX72) · Pager `MoThh` · Breadcrumb `d0D9M4` (Crumb0..2: SvKwB, d3ZJ3y, tRpUT) · Progress.Bar `P6NBxe` (Percent `osi1a`, Label `TExor`, Fill `k7actW` عرضه بالبكسل من 0 إلى 420) · Section.Header `BDj54` (Title `h8VnZ`, Sub `wxCLj`, Action `kNBF4` زر نصي) · KV.Row `MH2VG` (Key `RVsKO`, Value `CnhSE`) · Search.Local `TQr2O` (Placeholder، Kbd) · Autosave.Indicator `Tb1aR` (Text).
جداول وقوائم: Table.Header `IUMJ0` وTable.Row `RJz7S` (أعمدة المراسلات: Number `hdLsU`, Date `cgzxQ`, Subject `aefj4`, Party `fCin2`, StateChip `e0t78` (ref Chip.Status: `e0t78/IkHOY`)، FollowUp `QzqRn`, Confidentiality `yyMDJ`, More `MqTnb`؛ لصفوف بأعمدة مختلفة ابنِ جدولك الخاص بنفس الأسلوب: صف بارتفاع 44، خط 13، فاصل سفلي `$border`، رأس بتعبئة `$table-header`) · Document.Row `TuG5g` (FileName `U5Crw9`, Meta `rhmRP`, FileIcon `ReyQP`) · Timeline.Item `mwsIs` (Title `yn213`, Desc `F8HcH`, Meta `Fwrva`) · Menu `zc3s1` (MenuLabel1..4, MenuIcon1..4) · Calendar.Day `Ds00A` (DayNum `VYSMa`, Event1Text `GbnC7`, Event2Text `xLvxR`).
رسائل: Toast `Lyfyh` (Text `m4fVtS`, Icon `HCpqg`) · Dialog `D6y5Hg` (Title `K3zUoZ`, Desc `KkWTG`, Body `MldEN` فتحة، Cancel `qVHXG` (`qVHXG/LL17I`)، Confirm `rDG0R` (`rDG0R/Ds0MU`, `rDG0R/N2zNb`)؛ للحوار الخطِر استبدل Confirm بزر Danger عبر `descendants:{rDG0R:{type:'ref', ref:'a31uFu', …}}`) · Banner.Clock `hoORs`.
هيكل: Sidebar `Ydoka` (عناصر: مركز الانتباه `TSIzy`، المراسلات `X4Lvx`، المتابعة والمهام `IcWyW`، الاجتماعات `D1FlJJ`، التقويم `M5aKjQ`، القضايا `yWUfK`، الوثائق `v6IkJQ`، الجهات `uRqSr`، الموظفون `hpdxm`، الأصول والعُهد `l9fq0`، المالية `Y1Fgf9`، التقارير `DkJxG`؛ المجموعات: العمل اليومي `gW2Ih`، السجلات `sWMAS`، المالية والتقارير `qzBSq`) · Nav.Item `syE0P` (Label `cyL6U`, Icon `XxcvK`, Badge `vVZ6H` معطّلة افتراضيًا، `vVZ6H/Count`) · Nav.Group `xxr5B` (Label `Trwy4`, Chevron `xeZyY`, Badge `iLzvK`) · TopBar `HRbEo` (GlobalSearch `XiL9U`، CalendarBtn `Fz3h2`، BellBtn `YHIj9` مع Badge `emZzf`/`JgUGm`، User: UserName `NoDVI`, UserRole `DAOeA`, Avatar `RJSlu`/`H35U1M`).

### تحديد العنصر النشط في القائمة الجانبية

على ref القائمة داخل شاشتك (اسمه `Sidebar`) استعمل:
`Update(sidebarRefId, {descendants:{ 'ITEM':{fill:'$sidebar-selected'}, 'ITEM/cyL6U':{fill:'$white', fontWeight:'700'}, 'ITEM/XxcvK':{fill:'$white'}, …الشارات… }})`
حيث ITEM هو معرّف العنصر (مثل `X4Lvx`). القالب `atJKT` يأتي بلا عنصر محدد ومع شارات عينة (`X4Lvx/vVZ6H` 12، `TSIzy/vVZ6H` 5، `IcWyW/vVZ6H` 3، `gW2Ih/iLzvK` 18، `Y1Fgf9/vVZ6H` 2)؛ أعد ضبط `descendants` كاملًا لشاشتك (Update يستبدل الخريطة كلها) مع إبقاء الشارات، ولا تترك أكثر من عنصر واحد محددًا.

## بنية الشاشة القياسية (1366×768)

`Screen{fill:$bg, clip} → [Main(vertical, fill) → [TopBar ref, Page(vertical, padding [22,28], gap 16) → [PageHeader(space_between: Actions | Titles), Content(vertical, gap 16, fill)]], Sidebar ref]`.
- محتوى القوائم: شريط تبويبات (Tabs.Bar أو Tab.Item) بشارات، ثم صف بحث محلي (Search.Local يمينًا + زر التصفية يسارًا)، ثم الجدول، ثم تذييل (Pager + "عرض 1–20 من 128").
- صفوف تحتاج انتباهًا: تعبئة `$attention-bg` وحافة يمنى 3px بلون `$danger` (rectangle داخل الصف).
- النماذج: عمود مركزي بعرض ~900 من بطاقات مكدّسة، وشريط أوامر سفلي ثابت أبيض بحافة علوية (إلغاء يسارًا، الإجراء الرئيسي يمينًا) ومؤشر الحفظ التلقائي.
- التفاصيل: رأس بالحالة والخطوة المطلوبة (Chip.Status + نص "الخطوة التالية: …")، شريط أوامر، تبويبات، ثم شبكة 2/3 + 1/3.
- كل زر غير بديهي له Tooltip ظاهر في شاشة الحالات على الأقل، وكل خطأ نص عربي مفهوم بلا أرقام.
- الشارات الدائرية: على عناصر القائمة الجانبية ورؤوس المجموعات (مجموع السجلات العالقة دون تكرار)، وعلى الجرس، وعلى التبويبات الداخلية.
- أعلى القائمة الجانبية: شعار المؤسسة واسمها الكامل واسم المكتب (من ملف الإعداد). اسم الموظف وصورته في الشريط العلوي.

## أداة مدير النظام (1366×768)

نفس الرموز والمكوّنات. لا قائمة جانبية؛ شريط علوي داكن بعنوان "مدير نظام الوكيل" وملخص الهيئة واسم المدير، وتبويبات علوية: الهيئة والهوية | الهيكلية | المكاتب والأجهزة | الحسابات والمفاتيح | تصدير الإعداد | الصيانة | السجل. محتوى بعرض كامل بحافة 28.
القالب: `ACap6` (TA0). انسخه بـ`Copy('ACap6', document, {name, x, y})`، ثم اعثر بالاسم على `PageHeader` و`Content` و`AdminTabs`. التبويب النشط: داخل `AdminTabs` كل تبويب اسمه `ATab <الاسم>`؛ انقل التمييز بضبط `stroke:'$primary', strokeWidth:{bottom:2}` والنص `fontWeight:'700', fill:'$primary'` على التبويب المطلوب وإزالة الحدّ من "الهيئة والهوية" (stroke `'$surface'` وعرض 0) وإعادة نصه إلى `500`/`$muted`. شاشات A01 وA02 (قبل الدخول) مستقلة بلا شريط: إطار 1366×768 بتعبئة `$bg` ومحتوى مركزي.

## المثبّت (720×520)

نافذة مثبّت كلاسيكية RTL: ترويسة بلون `$primary-fill` مع الشعار والاسم، محتوى أبيض، شريط أزرار سفلي (إلغاء يسارًا، السابق/التالي يمينًا)، شريط تقدم.
القالب: `HpNIs` (TI0). انسخه بـ`Copy('HpNIs', document, {name, x, y})` ثم املأ الإطار المسمى `Body`. أزرار التذييل أسماؤها `Cancel`, `Back`, `Next` (refs؛ غيّر النص عبر `descendants`: `Next/Ds0MU` مثلًا "تثبيت").

## الأندرويد (412×915)

Material 3 بنفس الرموز: شريط حالة 44، شريط علوي بعنوان الشاشة وسطر المؤسسة/المكتب، تنقل سفلي 5 عناصر (الرئيسية، المراسلات، المهام، الاجتماعات، البحث) بشارات، زر عائم "إدخال سريع"، القوائم كعناصر قائمة بارتفاع 64 وفواصل، الأوراق السفلية للإجراءات، الحوارات M3. مكوّنات الأندرويد تبدأ بـ`M.` وتُبنى في صف مستقل قبل الشاشات (انظر SCREENS.md).

## الوضع الداكن

لا تصمم نسخًا داكنة يدويًا. بعد اكتمال كل الشاشات تُنسخ آليًا بـ`Copy(screenId, document, {theme:{mode:'dark'}, x: x + 12000})`.
