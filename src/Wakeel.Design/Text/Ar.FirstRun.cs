namespace Wakeel.Design.Text;

/// <summary>
/// Every Arabic string of the first-run and sign-in path of الوكيل — W02 (setup file), W03 (package
/// check and activation), W04 (account password and recovery sheet), W05 (sign in), W06 (automatic
/// lock) and W07 (wrong password and recovery) — per ARCHITECTURE.md §12, which keeps one partial
/// <see cref="Ar"/> file per work package. Nothing here names a technology, a port, a server or an
/// error code (AGREEMENT item 15); the only Latin tokens that appear are the ones the owner allows
/// (the file extension, QR, PDF, USB) and they are isolated by the caller with
/// <c>&lt;bdi&gt;</c>/<c>Bidi.Wrap</c> (AGREEMENT item 55).
/// </summary>
public static partial class Ar
{
    /// <summary>W02–W07: the first run of a fresh installation and every later sign in.</summary>
    public static class FirstRun
    {
        /// <summary>The setup file's extension, as it is written on paper.</summary>
        public const string SetupExtension = ".wakeel-setup";

        /// <summary>
        /// The same extension ready to be dropped into an Arabic sentence. The leading dot is inside
        /// the isolate on purpose: a dot standing between an Arabic word and a Latin run is neutral,
        /// so an RTL line puts it on the far side and the reader is shown «wakeel-setup.» — the
        /// extension with its dot at the wrong end (AGREEMENT item 55). The general
        /// <see cref="Wakeel.Design.Bidi.Bidi.Wrap"/> (U+2066 / U+2069) cannot help here: it deliberately anchors a token on letters
        /// and digits so that ordinary sentence punctuation is never swallowed into the isolate.
        /// </summary>
        public const string SetupExtensionInline =
            "⁦" + SetupExtension + "⁩";

        /// <summary>The product's own version line, shown on the first-run and sign-in screens.</summary>
        public const string AppVersion = "v0.21";

        /// <summary>W02 — «التشغيل الأول — ملف الإعداد».</summary>
        public static class Setup
        {
            public const string BrandTagline = "نظام أعمال المكاتب — الإصدار 0.21";
            public const string WelcomeTitle = "مرحبًا بك في التشغيل الأول";

            public const string WelcomeBody =
                "هذه النسخة لم تُفعَّل بعد. أحضر ملف الإعداد من مدير النظام في الهيئة؛ فهو يحمل بيانات الهيئة "
                + "والمكتب وهذا الجهاز والموظف المخوّل باستخدامه، بامتداد " + SetupExtensionInline + ".";

            public const string StepExportTitle = "تصدير الحزمة";
            public const string StepExportDesc = "يُنجزه مدير النظام في الهيئة قبل تسليم الملف إليك.";
            public const string StepChooseTitle = "اختيار الملف وكلمة المرور";
            public const string StepChooseDesc = "أنت هنا — الخطوة الحالية.";
            public const string StepActivateTitle = "فحص الحزمة وتفعيل النسخة";
            public const string StepActivateDesc = "يتحقق البرنامج من التوقيع والهيئة والمكتب والجهاز.";

            public const string SignedPackageTitle = "حزمة موقَّعة ومشفَّرة لجهاز واحد";

            public const string SignedPackageDesc =
                "يعمل الوكيل دون إنترنت. تصلح هذه الحزمة لهذا الجهاز وحده، ولا يقبل البرنامج حزمة أقدم من "
                + "الحزمة المثبَّتة ولا حزمة مؤرَّخة في المستقبل.";

            public const string NeedHelp = "تحتاج مساعدة؟ اطلبها من مدير النظام في الهيئة.";

            public const string CardTitle = "ملف الإعداد";
            public const string CardDesc = "اختر الملف الذي سلّمه لك مدير النظام ثم أدخل كلمة مرور الحزمة لفحصها.";

            public const string DropZoneChoose = "اختر ملفًا من الجهاز";
            public const string DropZoneOrDrag = "أو اسحب الملف إلى هنا";
            public const string DropZoneHint = "الحجم الأقصى 20 ميغابايت — الامتداد المقبول " + SetupExtensionInline + " فقط.";
            public const string DropZoneLabel = "منطقة اختيار ملف الإعداد أو سحبه";

            public const string FileReady = "جاهز للفحص";
            public const string RemoveFile = "إزالة الملف المختار";

            /// <summary>Meta line under a chosen file: «آخر تعديل 10/09/2026 — 1.8 ميغابايت».</summary>
            public static string FileMeta(string modified, string size) => $"آخر تعديل {modified} — {size}";

            public const string PasswordLabel = "كلمة مرور الحزمة";

            public const string PasswordHint =
                "تظهر مرة واحدة فقط لمدير النظام لحظة التصدير ولا يمكن استعادتها؛ اطلبها منه إن لم تُسلَّم إليك مع الملف.";

            public const string Inspect = "فحص الحزمة";
            public const string Inspecting = "جارٍ فحص الحزمة...";
            public const string WhereIsTheFile = "أين أجد الملف؟";

            public const string WhereIsTheFileTooltip =
                "يصدّره مدير النظام من أداة المدير ثم يُسلَّم إليك على وسيط USB أو بالبريد الداخلي مع كلمة المرور.";

            public const string WrongExtension = "هذا ليس ملف إعداد. الامتداد المقبول " + SetupExtensionInline + " فقط.";
            public const string FileTooLarge = "حجم الملف أكبر من 20 ميغابايت، وهذا أكبر من أي ملف إعداد.";
            public const string PasswordRequired = "أدخل كلمة مرور الحزمة أولًا.";
            public const string FileRequired = "اختر ملف الإعداد أولًا.";
            public const string CannotReadFile = "تعذّرت قراءة الملف من مكانه. انسخه إلى سطح المكتب ثم اختره مجددًا.";
            public const string FolderNotWritable = "لا يمكن الكتابة في مجلد البرنامج على هذا الجهاز. راجع مدير النظام.";
            public const string DiskFull = "لا توجد مساحة كافية على القرص لإتمام العملية. فرّغ بعض المساحة ثم حاول مجددًا.";
        }

        /// <summary>W03 — «فحص الحزمة والتفعيل».</summary>
        public static class Check
        {
            public const string Title = "التشغيل الأول لبرنامج الوكيل";
            public const string Sub = "عُثر على حزمة تفعيل لهذا الجهاز. راجع نتائج الفحص ثم فعّل النسخة.";

            public const string ResultsTitle = "نتائج فحص الحزمة";
            public const string ReadyChip = "جاهزة للتفعيل";
            public const string NotReadyChip = "غير صالحة للتفعيل";

            /// <summary>«آخر فحص: 10:24 — 12/09/2026».</summary>
            public static string LastCheck(string time, string date) => $"آخر فحص: {time} — {date}";

            public const string ItemPackage = "الحزمة";
            public const string ItemSignature = "التوقيع الرقمي";
            public const string ItemOrganisation = "الهيئة";
            public const string ItemOffice = "المكتب";
            public const string ItemDevice = "الجهاز";
            public const string ItemEmployee = "الموظف";
            public const string ItemOfficeKey = "مفتاح المكتب";
            public const string ItemLogo = "الشعار";
            public const string ItemGuide = "الدليل";
            public const string ItemReportTemplate = "قالب التقرير";
            public const string ItemLetterTemplate = "قالب المراسلة";
            public const string ItemRevocation = "قائمة الإلغاء";

            public const string StatusOk = "سليم";
            public const string StatusFailed = "غير مقبول";
            public const string StatusAbsent = "غير مرفق";

            public const string ValuePackageOk = "حزمة سليمة البنية ومؤرَّخة تأريخًا مقبولًا";
            public const string ValueSignatureOk = "موقَّعة بمفتاح الهيئة الرسمي — التوقيع مطابق وسليم";
            public const string ValueOfficeKeyOk = "مفتاح تشفير المكتب مُثبَّت مع الحزمة";
            public const string ValueLogoAbsent = "لم تُرفق صورة شعار مع هذه الحزمة";
            public const string ValueGuideAbsent = "لم يُرفق دليل الاستخدام مع هذه الحزمة";
            public const string ValueReportTemplateAbsent = "لم يُرفق قالب التقرير الشهري مع هذه الحزمة";
            public const string ValueLetterTemplateAbsent = "لم يُرفق قالب المراسلة الرسمية مع هذه الحزمة";
            public const string ValueRevocationAbsent = "لم تُرفق قائمة أجهزة ملغاة مع هذه الحزمة";
            public const string ValueRevocationOk = "قائمة الأجهزة الملغاة موقَّعة من الهيئة";
            public const string ValueLogoOk = "شعار المؤسسة مرفق مع الحزمة";
            public const string ValueGuideOk = "دليل الاستخدام مرفق مع الحزمة";
            public const string ValueReportTemplateOk = "قالب التقرير الشهري مرفق مع الحزمة";
            public const string ValueLetterTemplateOk = "قالب المراسلة الرسمية مرفق مع الحزمة";

            /// <summary>«هيئة تنمية المناطق الريفية».</summary>
            public static string ValueOrganisation(string orgName) => orgName;

            /// <summary>«مكتب مدير دائرة التخطيط — رمز المكتب OF-01».</summary>
            public static string ValueOffice(string officeName, string officeCode) =>
                $"{officeName} — رمز المكتب {officeCode}";

            /// <summary>«مسجّل باسم المكتب — الجهاز 1».</summary>
            public static string ValueDevice(int deviceNo) => $"مسجّل باسم المكتب — الجهاز {deviceNo}";

            /// <summary>«أحمد الخطيب — الموظف رقم 2 — مدير المكتب».</summary>
            public static string ValueEmployee(string name, int employeeNo, string jobTitle) =>
                $"{name} — الموظف رقم {employeeNo} — {jobTitle}";

            /// <summary>«اكتمل الفحص — 7 من 7 عناصر مقبولة».</summary>
            public static string Summary(int passed, int total) => $"اكتمل الفحص — {passed} من {total} عناصر مقبولة";

            /// <summary>«اكتمل الفحص — 6 من 7 عناصر مقبولة، عنصر واحد يمنع التفعيل».</summary>
            public static string SummaryWithFailures(int passed, int total, int failed) =>
                $"اكتمل الفحص — {passed} من {total} عناصر مقبولة، و{ArabicCount(failed)} يمنع التفعيل";

            public const string ProgressLabel = "تقدّم الفحص";

            public const string Activate = "تفعيل هذه النسخة";
            public const string Activating = "جارٍ تفعيل النسخة...";
            public const string ChooseAnother = "اختيار حزمة أخرى";
            public const string Recheck = "إعادة الفحص";

            public const string OrgCardRole = "دور هذه النسخة في المكتب";
            public const string RoleManager = "مدير المكتب";
            public const string RoleSecretary = "سكرتير المكتب";
            public const string RoleCustodian = "موظف العُهد";
            public const string ScopeFull = "مزامنة كاملة مع أجهزة المكتب";
            public const string ScopeCustody = "مزامنة بيانات العُهد فقط";

            public const string BindingNoticeTitle = "التفعيل يربط النسخة بهذا الجهاز";

            public const string BindingNoticeDesc =
                "بعد التفعيل لا يمكن نقل هذه النسخة إلى جهاز آخر دون حزمة جديدة من مدير النظام.";

            public const string FailureDialogTitle = "تعذّر قبول حزمة التفعيل";

            public const string FailureDialogDesc =
                "الحزمة المحدَّدة لا يمكن استخدامها لتفعيل هذه النسخة. اطلب حزمة جديدة من مدير النظام أو اختر ملفًا آخر.";

            public const string FailureFile = "الملف";
            public const string FailureIssuedBy = "أصدرها";
            public const string FailureOrganisation = "الهيئة";
            public const string FailureUnknownIssuer = "غير معروف";
            public const string Quit = "إنهاء البرنامج";

            /// <summary>Arabic counting used by the check summary: «عنصر واحد», «عنصران», «3 عناصر».</summary>
            private static string ArabicCount(int count) => count switch
            {
                1 => "عنصر واحد",
                2 => "عنصران",
                _ => $"{count} عناصر",
            };
        }

        /// <summary>Why a setup file was refused — one Arabic sentence per refusal, no codes.</summary>
        public static class Refusal
        {
            public const string BadSignature = "توقيع الحزمة غير صالح. الملف ليس من مدير النظام أو تغيّر بعد إصداره.";
            public const string Tampered = "محتوى الحزمة لا يطابق توقيعها، ويبدو أنها عُدّلت بعد إصدارها.";
            public const string WrongPassword = "كلمة مرور الحزمة غير صحيحة. راجع الكلمة التي سلّمها لك مدير النظام.";
            public const string OtherOrganisation = "هذه الحزمة صادرة عن هيئة أخرى غير الهيئة المثبَّتة على هذا الجهاز.";
            public const string OtherDevice = "هذه الحزمة أُعدّت لجهاز آخر ولا تصلح لهذا الجهاز.";
            public const string Older = "هذه الحزمة أقدم من الحزمة المثبَّتة على هذا الجهاز. اطلب من مدير النظام أحدث حزمة.";
            public const string FutureDate = "تاريخ الحزمة في المستقبل. تحقق من ساعة الجهاز أو اطلب حزمة جديدة.";
            public const string Revoked = "أُلغي هذا الجهاز من قِبل مدير النظام، ولا يمكن تفعيل نسخة عليه.";
            public const string Expired = "انتهت صلاحية الحزمة ولم تعد مقبولة للتفعيل.";
            public const string UnknownKind = "صيغة هذا الملف غير معروفة لهذا الإصدار من الوكيل.";
            public const string Corrupt = "الملف تالف أو ناقص، ولا يمكن قراءته.";
            public const string Unknown = "تعذّر قبول الحزمة لسبب غير معروف. اطلب حزمة جديدة من مدير النظام.";
        }

        /// <summary>W04 — «كلمة المرور وورقة الاسترداد».</summary>
        public static class Account
        {
            public const string StepCheck = "فحص الحزمة والتفعيل";
            public const string StepPassword = "كلمة المرور وورقة الاسترداد";
            public const string StepStart = "ابدأ العمل";

            /// <summary>«الوكيل — التشغيل الأول» under the organisation name in the top bar.</summary>
            public const string TopBarSub = "الوكيل — التشغيل الأول";

            /// <summary>«أهلًا بك أحمد — أكمل تهيئة حسابك».</summary>
            public static string Title(string firstName) => $"أهلًا بك {firstName} — أكمل تهيئة حسابك";

            public const string Sub =
                "أنشئ كلمة مرور الحساب، ثم اطبع ورقة الاسترداد أو احفظها قبل الضغط على «ابدأ العمل».";

            public const string PasswordCardTitle = "إنشاء كلمة مرور الحساب";
            public const string PasswordCardDesc = "تفتح هذه الكلمة الحساب على هذا الجهاز فقط، لا تشاركها مع أحد.";

            /// <summary>«مدير المكتب · الموظف رقم 2».</summary>
            public static string IdentityLine(string jobTitle, int employeeNo) => $"{jobTitle} · الموظف رقم {employeeNo}";

            public const string PasswordLabel = "كلمة المرور";
            public const string ConfirmLabel = "تأكيد كلمة المرور";
            public const string StrengthLabel = "قوة كلمة المرور";
            public const string ConfirmMatches = "متطابقة مع كلمة المرور";
            public const string ConfirmMismatch = "الكلمتان غير متطابقتين.";

            public const string RuleLength = "8 أحرف على الأقل";
            public const string RuleCase = "حرف كبير وحرف صغير";
            public const string RuleDigit = "رقم واحد على الأقل";
            public const string RuleSymbol = "رمز خاص مثل @ أو #";

            public const string RecoveryCardTitle = "ورقة الاسترداد";

            public const string RecoveryCardDesc =
                "تُستخدم لاستعادة الوصول إلى الحساب إذا نسيت كلمة المرور. تُصدر مرة واحدة لكل حساب.";

            public const string OnceWarningTitle = "تُعرض ورقة الاسترداد مرة واحدة فقط";

            public const string OnceWarningDesc =
                "بعد الضغط على «ابدأ العمل» لن تُعرض مجددًا ولن يمكن استرجاعها. اطبعها الآن أو احفظها ملف PDF "
                + "في مكان آمن بعيدًا عن الجهاز.";

            public const string SheetTitle = "ورقة استرداد الحساب — الوكيل";
            public const string SheetCodeLabel = "رمز الاسترداد";
            public const string SheetQrAlt = "رمز QR لرمز الاسترداد";

            /// <summary>«أُصدرت 12/09/2026 · 10:24».</summary>
            public static string SheetIssued(string date, string time) => $"أُصدرت {date} · {time}";

            /// <summary>«الموظف أحمد الخطيب · الجهاز 1».</summary>
            public static string SheetOwner(string employeeName, int deviceNo) =>
                $"الموظف {employeeName} · الجهاز {deviceNo}";

            public const string SheetValidity = "صالح لهذا الحساب فقط · يُستخدم مرة واحدة ثم يُصدر رمز جديد.";

            public const string SheetWarning =
                "تحذير: أي شخص يملك هذه الورقة يستطيع استعادة الوصول إلى الحساب. احفظها في مكان مقفل ولا تصوّرها بالهاتف.";

            public const string Print = "طباعة";
            public const string SavePdf = "حفظ PDF";
            public const string NotPrintedYet = "لم تُطبع بعد";
            public const string Printed = "أُرسلت إلى الطابعة";
            public const string PdfSaved = "حُفظت ملف PDF";
            public const string PdfFailed = "تعذّر حفظ الملف. اختر مجلدًا آخر ثم حاول مجددًا.";
            public const string PrintShortcut = "Ctrl+P";

            /// <summary>Suggested file name of the saved recovery sheet.</summary>
            public const string SheetFileName = "ورقة-استرداد-الوكيل.pdf";

            public const string Confirmed = "طبعتُ ورقة الاسترداد وحفظتها في مكان آمن";
            public const string ConfirmRequired = "أكّد أنك طبعت ورقة الاسترداد أو حفظتها قبل البدء.";

            public const string Start = "ابدأ العمل";
            public const string Starting = "جارٍ تهيئة النسخة...";
            public const string StartNote = "بعد البدء لن تُعرض ورقة الاسترداد هذه مرة أخرى.";
            public const string Back = "رجوع";

            public const string ActivationFailed = "تعذّر إنشاء الحساب على هذا الجهاز. راجع مدير النظام.";

            /// <summary>
            /// W04 refuses to build a second installation over one that already exists: writing new
            /// keys over the old ones would leave the account and its data unopenable by anybody.
            /// </summary>
            public const string AlreadyActivated =
                "هذا الجهاز مُفعَّل بالفعل ويحمل حسابًا وبياناته. سجّل الدخول بكلمة مرور الحساب، "
                + "أو راجع مدير النظام إن أردت إعادة التهيئة من جديد.";
        }

        /// <summary>Strength meter wording, from the weakest to the strongest.</summary>
        public static class Strength
        {
            public const string TooShort = "قصيرة جدًا";
            public const string Weak = "ضعيفة";
            public const string Fair = "مقبولة";
            public const string Good = "جيدة";
            public const string Strong = "قوية";

            /// <summary>«قوية — 14 حرفًا».</summary>
            public static string WithLength(string label, int length) => $"{label} — {Letters(length)}";

            private static string Letters(int count) => count switch
            {
                0 => "بلا أحرف",
                1 => "حرف واحد",
                2 => "حرفان",
                >= 3 and <= 10 => $"{count} أحرف",
                _ => $"{count} حرفًا",
            };
        }

        /// <summary>W05 — «شاشة الدخول».</summary>
        public static class Login
        {
            public const string PasswordLabel = "كلمة المرور";
            public const string PasswordHint = "كلمة مرور حسابك في الوكيل، وليست كلمة مرور ويندوز.";
            public const string SignIn = "دخول";
            public const string SigningIn = "جارٍ الدخول...";
            public const string ForgotPassword = "نسيت كلمة المرور؟ استخدم ورقة الاسترداد";
            public const string NotYou = "لست أنت؟ تبديل الحساب";

            public const string OneAccountTitle = "حساب واحد لكل نسخة";

            public const string OneAccountDesc =
                "لهذه النسخة حساب واحد فقط يحدده مدير النظام في ملف الإعداد. لتغيير الموظف يلزم ملف إعداد جديد.";

            /// <summary>«مدير المكتب • الموظف رقم 2 • الجهاز 1».</summary>
            public static string IdentityLine(string jobTitle, int employeeNo, int deviceNo) =>
                $"{jobTitle} • الموظف رقم {employeeNo} • الجهاز {deviceNo}";

            public const string ClockOk = "ساعة الجهاز سليمة";
            public const string ClockSuspect = "ساعة الجهاز تحتاج مراجعة";
            public const string ClockBad = "ساعة الجهاز غير صحيحة";

            /// <summary>«ساعة الجهاز سليمة • السبت 12/09/2026 — 10:24».</summary>
            public static string ClockLine(string verdict, string weekday, string date, string time) =>
                $"{verdict} • {weekday} {date} — {time}";

            /// <summary>«إصدار الوكيل v0.21 — الجهاز 1».</summary>
            public static string FooterLine(string version, int deviceNo) =>
                $"إصدار الوكيل {version} — الجهاز {deviceNo}";

            /// <summary>
            /// The same line for a machine whose sealed card cannot be opened here: the product
            /// still names its own version, and says nothing it does not actually know.
            /// </summary>
            public static string FooterLineWithoutDevice(string version) => $"إصدار الوكيل {version}";

            public const string PasswordRequired = "أدخل كلمة المرور.";
            public const string WrongPassword = "كلمة المرور غير صحيحة. تحقق من لغة لوحة المفاتيح وحالة الأحرف ثم حاول مجددًا.";
            /// <summary>
            /// The Latin key name carries its own isolate, exactly as <see cref="SetupExtensionInline"/>
            /// does: it stands between two Arabic words on a right-to-left line, and only an explicit
            /// isolate keeps its two words together in the order they are printed on the keyboard
            /// (AGREEMENT item 55).
            /// </summary>
            public const string CapsLockHint = "مفتاح ⁦Caps Lock⁩ مفعّل — قد يكون سبب الخطأ.";
            public const string KeysMissing = "ملفات هذه النسخة ناقصة. راجع مدير النظام لاسترداد الحساب.";
            public const string DatabaseUnreadable = "تعذّر فتح بيانات هذه النسخة. راجع مدير النظام.";

            /// <summary>«المحاولة 3 من 5 • بقيت محاولتان قبل الإيقاف المؤقت 15 دقيقة».</summary>
            public static string AttemptLine(int attempt, int max, int remaining, int lockMinutes) =>
                $"المحاولة {attempt} من {max} • {Remaining(remaining)} قبل الإيقاف المؤقت {Minutes(lockMinutes)}";

            /// <summary>«أُوقف الدخول مؤقتًا. حاول بعد 14 دقيقة.».</summary>
            public static string LockedOut(int minutesLeft) =>
                $"أُوقف الدخول مؤقتًا بعد محاولات خاطئة متتالية. حاول بعد {Minutes(minutesLeft)}.";

            public const string LockedOutNow = "أُوقف الدخول مؤقتًا بعد محاولات خاطئة متتالية. حاول بعد قليل.";

            private static string Remaining(int count) => count switch
            {
                0 => "لم تبقَ محاولات",
                1 => "بقيت محاولة واحدة",
                2 => "بقيت محاولتان",
                _ => $"بقيت {count} محاولات",
            };

            internal static string Minutes(int count) => count switch
            {
                0 => "أقل من دقيقة",
                1 => "دقيقة واحدة",
                2 => "دقيقتين",
                >= 3 and <= 10 => $"{count} دقائق",
                _ => $"{count} دقيقة",
            };
        }

        /// <summary>W06 — «القفل التلقائي».</summary>
        public static class Lock
        {
            public const string Title = "قُفلت الجلسة";

            /// <summary>«قُفلت الجلسة بعد 10 دقائق من عدم النشاط (منذ 10:34). أدخل كلمة المرور للمتابعة من حيث توقفت.».</summary>
            public static string Desc(int idleMinutes, string sinceTime) =>
                $"قُفلت الجلسة بعد {Login.Minutes(idleMinutes)} من عدم النشاط (منذ {sinceTime}). "
                + "أدخل كلمة المرور للمتابعة من حيث توقفت.";

            public const string Continue = "متابعة";
            public const string Continuing = "جارٍ المتابعة...";
            public const string EnterHint = "للمتابعة مباشرة، اضغط";
            public const string EnterKey = "Enter";
            public const string SwitchUser = "تبديل المستخدم";

            public const string DraftsTitle = "مسوداتك محفوظة تلقائيًا";
            public const string DraftsDesc = "كل ما كنت تكتبه محفوظ، وستعود إلى المكان نفسه بعد المتابعة.";

            public const string LockNow = "قفل الجلسة الآن";
        }

        /// <summary>W07 — «الاسترداد بورقة الاسترداد».</summary>
        public static class Recovery
        {
            public const string DialogTitle = "الاسترداد بورقة الاسترداد";

            public const string DialogDesc =
                "أدخل رمز الاسترداد المطبوع على الورقة أو امسح رمز QR من صورة، ثم عيّن كلمة مرور جديدة. "
                + "تُلغى كلمة المرور القديمة فور التعيين.";

            public const string StepCode = "رمز الاسترداد";
            public const string StepNewPassword = "كلمة مرور جديدة";

            public const string SourceLabel = "مصدر الرمز";
            public const string SourceType = "إدخال الرمز";
            public const string SourceScan = "مسح QR";

            /// <summary>«رمز الاسترداد (21 خانة)».</summary>
            public static string CodeLabel(int characters) => $"رمز الاسترداد ({characters} خانة)";

            public const string CodePlaceholder = "XXXX-XXXX-XXXX-XXXX-XXXXX";
            public const string CodeVerified = "تم التحقق من الرمز.";

            /// <summary>«تم التحقق من الرمز — ورقة الاسترداد الصادرة في 02/09/2026».</summary>
            public static string CodeVerifiedWithDate(string issuedDate) =>
                $"تم التحقق من الرمز — ورقة الاسترداد الصادرة في {issuedDate}";

            public const string CodeIncomplete = "الرمز ناقص أو غير مكتمل. انسخه كما هو مطبوع على الورقة.";
            public const string CodeWrong = "هذا الرمز لا يفتح هذا الحساب. تأكد أنها ورقة استرداد هذا الجهاز.";
            public const string CodeCheck = "أعد قراءة الرمز؛ يبدو أن فيه حرفًا خاطئًا.";

            public const string ScanChooseImage = "اختر صورة رمز QR";
            public const string ScanHint = "اختر صورة واضحة لرمز QR المطبوع على ورقة الاسترداد.";
            public const string ScanFailed = "تعذّرت قراءة رمز QR من هذه الصورة. جرّب صورة أوضح أو أدخل الرمز يدويًا.";
            public const string ScanUnavailable = "قراءة الصور غير متاحة هنا. أدخل الرمز يدويًا.";
            public const string ScanSucceeded = "قُرئ الرمز من الصورة.";

            public const string NewPasswordLabel = "كلمة المرور الجديدة";
            public const string ConfirmLabel = "تأكيد كلمة المرور";

            public const string LogNoticeTitle = "ما الذي سيحدث";

            public const string LogNoticeDesc =
                "ستُلغى كلمة المرور القديمة، وتُسجَّل عملية الاسترداد في سجل هذا الجهاز مع التاريخ والوقت.";

            public const string IssueNewSheet = "أصدر ورقة استرداد جديدة";
            public const string IssueNewSheetHint = "الرمز الحالي يبقى صالحًا إن لم تُصدر ورقة جديدة.";

            public const string Submit = "تعيين كلمة المرور والدخول";
            public const string Submitting = "جارٍ تعيين كلمة المرور...";
            public const string Failed = "تعذّر إتمام الاسترداد. حاول مجددًا أو راجع مدير النظام.";
            public const string Succeeded = "عُيّنت كلمة مرور جديدة.";

            /// <summary>
            /// The new password is already the one that opens this installation, but the workspace
            /// did not open behind it. The sentence has to make both halves plain: what has already
            /// changed, and what to do next.
            /// </summary>
            public const string SucceededNotSignedIn =
                "عُيّنت كلمة المرور الجديدة واحفظ الورقة الجديدة إن ظهرت. لم تُفتح مساحة العمل الآن؛ أغلق الوكيل ثم افتحه وادخل بكلمة المرور الجديدة.";

            public const string NewSheetTitle = "ورقة الاسترداد الجديدة";
            public const string NewSheetDesc = "احفظ الورقة الجديدة الآن؛ الرمز القديم لم يعد صالحًا.";
            public const string NewSheetDone = "حفظتُ الورقة الجديدة";

            /// <summary>Shown while the typed code is being checked, which takes a noticeable moment.</summary>
            public const string Verifying = "جارٍ التحقق من الرمز...";

            /// <summary>The new sheet gets the same two ways out of the screen that W04's sheet has.</summary>
            public const string NewSheetPrint = "طباعة الورقة الجديدة";

            public const string NewSheetSavePdf = "حفظ الورقة الجديدة ملف PDF";
        }

        /// <summary>Audit-log summaries written by the activation, sign-in, lock and recovery services.</summary>
        public static class Audit
        {
            public const string Activated = "فُعّلت النسخة من ملف الإعداد";
            public const string SignedIn = "دخول إلى الحساب";
            public const string SignedInAfterLock = "متابعة الجلسة بعد القفل التلقائي";
            public const string Locked = "قُفلت الجلسة تلقائيًا بعد عدم النشاط";
            public const string Recovered = "استُرد الحساب بورقة الاسترداد وعُيّنت كلمة مرور جديدة";
            public const string RecoverySheetReissued = "أُصدرت ورقة استرداد جديدة";

            /// <summary>«محاولة دخول خاطئة واحدة قبل الدخول الناجح».</summary>
            public static string FailedAttempts(int count) => count switch
            {
                1 => "محاولة دخول خاطئة واحدة قبل الدخول الناجح",
                2 => "محاولتا دخول خاطئتان قبل الدخول الناجح",
                >= 3 and <= 10 => $"{count} محاولات دخول خاطئة قبل الدخول الناجح",
                _ => $"{count} محاولة دخول خاطئة قبل الدخول الناجح",
            };

            public const string ActorSystem = "النظام";
        }
    }
}
