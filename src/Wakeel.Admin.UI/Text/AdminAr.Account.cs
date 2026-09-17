namespace Wakeel.Admin.UI.Text;

/// <summary>
/// A01 (creating the administrator account and the organisation recovery sheet) and A02 (signing
/// in, the temporary lock-out, and recovery). Owned by admin-1; no other sub-package edits it.
/// </summary>
public static partial class AdminAr
{
    /// <summary>Everything the account screens say.</summary>
    public static class Account
    {
        /// <summary>A01 — first run.</summary>
        public static class Create
        {
            public const string WindowTitle = "مدير نظام الوكيل — الإعداد الأول";
            public const string PasswordCardTitle = "كلمة مرور مدير النظام";
            public const string PasswordCardDesc = "تفتح أداة المدير على هذا الحاسوب فقط.";
            public const string PasswordLabel = "كلمة المرور";
            public const string PasswordHint = "تُكتب مرة واحدة الآن ولا يمكن استرجاعها لاحقًا.";
            public const string ConfirmLabel = "تأكيد كلمة المرور";
            public const string ConfirmMismatch = "الكلمتان غير متطابقتين.";
            public const string ConfirmMatch = "متطابقتان";
            public const string AdminNameLabel = "اسم مدير النظام";
            public const string AdminNameHint = "يظهر بجانب كل إجراء في سجل العمليات.";
            public const string AdminNamePlaceholder = "الاسم الكامل";
            public const string OrgNameLabel = "اسم الهيئة الكامل";
            public const string OrgNameHint = "يظهر على ورقة الاسترداد وفي كل ملف إعداد.";
            public const string OrgNamePlaceholder = "الاسم الرسمي للهيئة";
            public const string PasswordTooWeak = "اختر كلمة مرور أطول أو أكثر تنوّعًا.";

            public const string PasswordSet = "أُنشئ حساب مدير النظام";

            /// <summary>What step one turned into, once it is done: who, and for which organisation.</summary>
            public static string PasswordSetDetail(string adminName, string orgName) =>
                $"{adminName} — {orgName}. أُنشئت مفاتيح الهيئة وحُفظت مشفّرة على هذا الحاسوب.";

            public const string SheetCardTitle = "ورقة استرداد المؤسسة";
            public const string SheetCardDesc = "اطبعها الآن — تُعرض مرة واحدة فقط.";
            public const string OnceWarningTitle = "تُعرض هذه الورقة مرة واحدة فقط";

            public const string OnceWarningBody =
                "لا تحفظ نسخة منها داخل البرنامج. من دونها لا يمكن استرداد حساب المدير إن نُسيت كلمة المرور.";

            public const string SheetHeading = "ورقة استرداد حساب مدير النظام";
            public const string SheetCodeLabel = "رمز الاسترداد";
            public const string SheetQrAlt = "رمز الاسترداد كصورة";
            public const string SheetQrHint = "امسح الصورة بدل كتابة الرمز حرفًا حرفًا.";
            public const string SheetIssuedLabel = "تاريخ الإصدار";
            public const string SheetAdminLabel = "مدير النظام";
            public const string SheetNumberLabel = "رقم الورقة";
            public const string SheetFooter = "احفظ هذه الورقة في مكان مغلق بعيدًا عن الحاسوب.";

            /// <summary>
            /// The chip that appears once the print window has been put on screen. It states what
            /// the tool did, not what the printer did: whether the person went through with the
            /// print, changed the paper, or simply closed the window, nothing tells this screen —
            /// so the sheet is never called printed on the strength of a window having opened. The
            /// tick box below it is what actually stands for «I printed it and put it away».
            /// </summary>
            public static string PrintOpenedChip(DateTimeOffset at) =>
                $"فُتحت نافذة الطباعة {at.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)}";

            public const string PrintSheet = "طباعة الورقة";
            public const string SavePdf = "حفظ نسخة PDF";
            public const string CopyCode = "نسخ الرمز";
            /// <summary>
            /// Said after «نسخ الرمز». It names the one thing the person has to do next, because a
            /// copied code sits in this computer's clipboard — and in its clipboard history — until
            /// something replaces it, which undoes the care the rest of this screen takes.
            /// </summary>
            public const string CopiedToast = "نُسخ الرمز. الصقه في مكانه ثم امسح الحافظة.";

            /// <summary>Said when the copy did not go through, which is nothing to do with printing.</summary>
            public const string CopyFailed = "تعذّر نسخ الرمز. اكتبه بخط اليد أو اطبع الورقة.";
            public const string PrintUnavailable = "لا تتوفّر الطباعة على هذا الحاسوب. اكتب الرمز بخط اليد واحفظه.";
            public const string PrintFailed = "تعذّرت الطباعة. تأكّد من الطابعة ثم أعد المحاولة.";
            public const string SavedToast = "حُفظت نسخة من الورقة.";
            public const string SaveFailed = "تعذّر حفظ الملف. اختر مجلدًا آخر ثم أعد المحاولة.";

            public const string PrintedCheckbox = "طبعتُ الورقة وحفظتها في مكان آمن.";
            public const string PrintedRequired = "أكّد أنك طبعت الورقة وحفظتها قبل المتابعة.";
            public const string Start = "ابدأ";
            /// <summary>Under the button of step one, while something is still missing.</summary>
            public const string CreateHint = "أكمل الحقول ثم أنشئ الحساب.";

            /// <summary>And once the fields hold up: what pressing the button will bring up.</summary>
            public const string ReadyHint = "بعد الإنشاء تُعرض ورقة الاسترداد مرة واحدة، فجهّز الطابعة.";

            /// <summary>Under «ابدأ» on the sheet, and as the description of the empty sheet card.</summary>
            public const string StartHint = "بعد الإنشاء تُفتح لوحة الهيئة مباشرة.";
            public const string CreateAccount = "إنشاء الحساب والمتابعة";
            public const string Exit = "الخروج";

            /// <summary>
            /// Asked when somebody presses «الخروج» while the recovery sheet is on screen and the
            /// tick box beneath it is still empty. The sheet is shown once and is the only way back
            /// into the organisation if the password is ever forgotten, so leaving without it is the
            /// one thing on this screen that cannot be taken back.
            /// </summary>
            public const string ExitUnsavedTitle = "إغلاق الأداة قبل حفظ ورقة الاسترداد؟";

            public const string ExitUnsavedDesc =
                "ورقة الاسترداد معروضة الآن ولن تُعرض مرة أخرى. إن أُغلقت الأداة قبل طباعتها وحفظها "
                + "فلن يبقى سبيل لاسترداد حساب المدير إذا نُسيت كلمة المرور.";

            public const string ExitUnsavedConfirm = "إغلاق دون حفظ الورقة";
            public const string ExitUnsavedCancel = "العودة إلى الورقة";

            public const string Step1 = "1";
            public const string Step2 = "2";

            public const string CreateFailed =
                "تعذّر إنشاء حساب المدير على هذا الحاسوب. تأكّد من وجود مساحة على القرص ثم أعد المحاولة.";
        }

        /// <summary>A02 — signing in.</summary>
        public static class SignIn
        {
            public const string Title = "تسجيل دخول المدير";
            public const string Desc = "أدخل كلمة مرور المدير للوصول إلى إعدادات الهيئة والهيكلية والأجهزة.";
            public const string PasswordLabel = "كلمة مرور المدير";
            public const string Submit = "دخول";
            public const string ForgotLink = "نسيت كلمة المرور؟ استرداد الحساب";

            /// <summary>The standing note under the field, before anything has gone wrong.</summary>
            public static string AttemptsNote(int maxAttempts, int seconds) =>
                $"يُقفل الدخول {Counting.Seconds(seconds)} بعد {Counting.Attempts(maxAttempts)} خاطئة.";

            /// <summary>What is said after a wrong password, while attempts remain.</summary>
            public static string AttemptsRemaining(int remaining) =>
                remaining == 1
                    ? "كلمة المرور غير صحيحة. بقيت محاولة واحدة قبل القفل المؤقت."
                    : $"كلمة المرور غير صحيحة. بقيت {Counting.Attempts(remaining)} قبل القفل المؤقت.";

            /// <summary>What is said while the temporary lock-out is running.</summary>
            public static string LockedFor(int seconds) =>
                $"الدخول مقفل مؤقتًا. أعد المحاولة بعد {Counting.Seconds(seconds)}.";

            public const string LockedTitle = "الدخول مقفل مؤقتًا";

            public const string LockedBody =
                "كثرت المحاولات الخاطئة. انتظر انتهاء المهلة، أو استرد الحساب بورقة الاسترداد.";

            /// <summary>The line under the card: which computer this is.</summary>
            public static string MachineLine(string machineName) => $"وضع مدير النظام · الجهاز {machineName}";
        }

        /// <summary>A02 — recovery with the printed organisation sheet.</summary>
        public static class Recovery
        {
            public const string Title = "استرداد حساب المدير";
            public const string Desc = "أدخل رمز ورقة الاسترداد، أو اختر صورة الرمز من الورقة المطبوعة.";
            public const string CodeLabel = "رمز الاسترداد";
            public const string CodePlaceholder = "XXXX-XXXX-XXXX-XXXX-XXXXX";
            public const string CodeHint = "خمس مجموعات كما هي مكتوبة على الورقة. الشرطات اختيارية.";
            public const string CodeInvalid = "هذا ليس رمز ورقة استرداد صحيحًا. راجع الحروف ثم أعد المحاولة.";
            public const string CodeWrong = "هذا الرمز لا يفتح حساب المدير على هذا الحاسوب.";
            public const string PickImage = "اختيار صورة الرمز";
            public const string ImageUnavailable = "تعذّرت قراءة الصور على هذا الحاسوب. اكتب الرمز بدل ذلك.";
            public const string ImageNoCode = "لم يُعثر على رمز في هذه الصورة. جرّب صورة أوضح أو اكتب الرمز.";
            public const string ImageTooLarge = "هذه الصورة كبيرة جدًا. اختر صورة أصغر أو اكتب الرمز.";
            public const string ImageRead = "قُرئ الرمز من الصورة.";
            public const string NewPasswordLabel = "كلمة المرور الجديدة";
            public const string NewPasswordConfirmLabel = "تأكيد كلمة المرور الجديدة";
            public const string Submit = "تعيين كلمة المرور والدخول";
            public const string Cancel = "العودة إلى الدخول";
            public const string Done = "غُيّرت كلمة مرور المدير.";
            public const string SheetStillValid = "ورقة الاسترداد نفسها تبقى صالحة. احتفظ بها في مكانها.";
            public const string NoAccount = "لا يوجد حساب مدير على هذا الحاسوب بعد.";
        }

        /// <summary>The four rules printed under the password field, and the strength meter.</summary>
        public static class Strength
        {
            public const string RuleLength = "12 حرفًا فأكثر";
            public const string RuleCase = "حرف كبير وحرف صغير";
            public const string RuleDigit = "رقم واحد على الأقل";
            public const string RuleSymbol = "رمز واحد على الأقل مثل ! أو #";

            public const string TooShort = "قصيرة جدًا";
            public const string Weak = "ضعيفة";
            public const string Fair = "مقبولة";
            public const string Good = "جيدة";
            public const string Strong = "قوية";

            public const string Label = "قوة كلمة المرور";
        }
    }

    /// <summary>What the audit log records for the account actions of A01 and A02.</summary>
    public static class AccountAudit
    {
        public const string AccountCreated = "أُنشئ حساب مدير النظام ومفاتيح الهيئة.";
        public const string RecoverySheetIssued = "صدرت ورقة استرداد المؤسسة.";
        public const string SignedIn = "دخل مدير النظام إلى الأداة.";
        public const string SignedOut = "خرج مدير النظام من الأداة.";
        public const string SignInFailed = "محاولة دخول بكلمة مرور غير صحيحة.";
        public const string LockedOut = "قُفل الدخول مؤقتًا بعد محاولات خاطئة متتالية.";
        public const string PasswordRecovered = "غُيّرت كلمة مرور المدير بورقة الاسترداد.";
    }
}
