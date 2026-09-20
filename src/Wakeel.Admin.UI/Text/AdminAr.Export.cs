using System.Globalization;

namespace Wakeel.Admin.UI.Text;

/// <summary>A08 «تصدير ملف الإعداد»: the four-step wizard that hands a computer its whole beginning.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A08 says.</summary>
    public static class Export
    {
        public const string Title = "تصدير ملف الإعداد";
        public const string Sub = "ملف واحد يحمل إلى الحاسوب كل ما يحتاجه ليبدأ العمل في مكتبه.";

        /// <summary>The four steps, in order, as the stepper shows them.</summary>
        public static IReadOnlyList<string> Steps { get; } =
        [
            "المكتب والجهاز",
            "المحتوى",
            "كلمة مرور الملف",
            "الحفظ",
        ];

        // ── Step one ────────────────────────────────────────────────────────────────────────────
        public const string StepDeviceHeading = "اختر المكتب والجهاز";
        public const string StepDeviceDesc = "يُصنع الملف لجهاز واحد بعينه، ولا يعمل على أي حاسوب آخر.";
        public const string OfficeLabel = "المكتب";
        public const string DeviceLabel = "الجهاز";
        public const string NoDevices = "لا أجهزة في هذا المكتب";
        public const string NoDevicesDesc = "سجّل جهازًا في «المكاتب والأجهزة» ثم عد إلى هنا.";
        public const string OpenDevices = "فتح المكاتب والأجهزة";
        public const string OpenDevicesTooltip = "فتح شاشة المكاتب والأجهزة لتسجيل جهاز";
        public const string NoOffices = "لا مكاتب بعد";
        public const string NoOfficesDesc = "علّم وحدة في «الهيكلية» بأنها مكتب، ثم سجّل أجهزتها.";
        public const string OpenStructure = "فتح الهيكلية";
        public const string OpenStructureTooltip = "فتح شاشة الهيكلية لتعليم مكتب";

        public const string FirstFileChip = "أول ملف";
        public const string RepeatFileChip = "ملف بديل";

        public const string FirstFileNote =
            "بذرة مفتاح هذا الجهاز ما زالت محفوظة في الأداة، وستخرج داخل هذا الملف ثم تُمحى من هنا.";

        public const string RepeatFileNote =
            "خرجت بذرة مفتاح هذا الجهاز في ملف سابق ولم تعد محفوظة هنا، فيُصدَر للجهاز مفتاح جديد مع هذا الملف. "
            + "بعد تطبيقه لن يعمل أي ملف إعداد أقدم لهذا الجهاز.";

        // ── Step two ────────────────────────────────────────────────────────────────────────────
        public const string StepContentHeading = "ما الذي يحمله الملف";
        public const string StepContentDesc = "الأساسيات تُضمَّن دائمًا، والمرفقات الأربعة اختيارية.";
        public const string AlwaysHeading = "يُضمَّن دائمًا";
        public const string OptionalHeading = "مرفقات اختيارية";
        public const string ColumnItem = "المحتوى";
        public const string ColumnAttachment = "المرفق";
        public const string ColumnDetail = "التفاصيل";
        public const string ColumnSize = "الحجم";
        public const string ColumnState = "الحالة";
        public const string IncludedChip = "مضمّن";
        public const string MissingChip = "غير متوفّر";
        public const string NotCarriedChip = "لا شيء ليُضمَّن";
        public const string UnknownSize = "—";

        public const string ItemStructure = "الهيكلية";
        public const string ItemOfficeKey = "مفتاح المكتب";
        public const string ItemCertificate = "شهادة الجهاز وبذرة مفتاحه";
        public const string ItemCycleDay = "يوم بداية الدورة المالية";
        public const string ItemNumbering = "صيغة ترقيم الملفات";
        public const string ItemEmployee = "الموظف ودوره";
        public const string ItemLogo = "شعار الهيئة";
        public const string ItemReportTemplate = "قالب التقرير الشهري";
        public const string ItemLetterTemplate = "قالب المراسلة الرسمية";
        public const string ItemGuide = "دليل الوكيل";
        public const string ItemRevocation = "قائمة الأجهزة الملغاة";

        public const string LogoMissing = "لم يُرفع شعار للهيئة بعد.";
        public const string ReportTemplateMissing = "لم يُرفع قالب تقرير شهري.";
        public const string LetterTemplateBuiltIn = "القالب المدمج مع الأداة.";
        public const string GuideGenerated = "دليل مبدئي من صفحة واحدة يُولَّد الآن.";
        public const string RevocationEmpty = "لا أجهزة ملغاة حتى الآن.";

        public const string NoOfficeKey = "لم يُصدر مفتاح لهذا المكتب بعد";

        public const string NoOfficeKeyDesc =
            "المفتاح الذي تشترك فيه حواسيب المكتب يُصدَر من «الحسابات والمفاتيح»، ولا يكتمل ملف الإعداد بدونه.";

        public const string OpenKeys = "فتح الحسابات والمفاتيح";
        public const string OpenKeysTooltip = "فتح شاشة الحسابات والمفاتيح لإصدار مفتاح المكتب";

        // ── Step three ──────────────────────────────────────────────────────────────────────────
        public const string StepPasswordHeading = "كلمة مرور الملف";

        public const string StepPasswordDesc =
            "تُولَّد الآن مرة واحدة، ولا تُحفظ في الأداة ولا في السجل. انقلها مع الملف إلى من سيفتحه.";

        public const string GeneratePassword = "توليد كلمة المرور";
        public const string GeneratePasswordTooltip = "توليد كلمة مرور جديدة لهذا الملف";
        public const string CopyPassword = "نسخ";
        public const string CopyPasswordTooltip = "نسخ كلمة المرور إلى الحافظة";
        public const string PasswordCopied = "نُسخت كلمة المرور.";
        public const string PasswordCopyFailed = "تعذّر النسخ. اكتب كلمة المرور كما تظهر.";

        public const string PasswordWarning =
            "لن تظهر كلمة المرور بعد إغلاق هذه الشاشة. إن ضاعت فلا يمكن فتح الملف، ويلزم تصدير ملف جديد.";

        public const string PasswordNotYet = "أكّد أنك حفظت كلمة المرور في الخطوة السابقة أولًا.";
        public const string PasswordKept = "حفظت كلمة المرور في مكان آمن";

        // ── The summary rail, on every step ─────────────────────────────────────────────────────
        public const string SummaryHeading = "ملخص الملف";
        public const string SummaryDesc = "ما سيحمله الملف عن هذا الحاسوب.";
        public const string SummaryEmpty = "اختر المكتب والجهاز ليظهر الملخص هنا.";
        public const string SequenceLabel = "رقم الملف";

        // ── Step four ───────────────────────────────────────────────────────────────────────────
        public const string StepSaveHeading = "حفظ الملف";
        public const string StepSaveDesc = "يُكتب الملف في مجلد الأداة، ويمكنك حفظ نسخة منه حيث تشاء.";
        public const string FileNameLabel = "اسم الملف";
        public const string SavedAtLabel = "مكان الحفظ";
        public const string CreateFile = "إنشاء الملف";
        public const string CreateFileTooltip = "إنشاء ملف الإعداد وحفظه في مجلد الأداة";
        public const string SaveCopy = "حفظ نسخة…";
        public const string SaveCopyTooltip = "حفظ نسخة من الملف في مكان تختاره";
        public const string CopySaved = "حُفظت النسخة.";
        public const string CopyNotSaved = "لم تُحفظ نسخة.";
        public const string OpenFolder = "فتح المجلد";
        public const string OpenFolderTooltip = "فتح المجلد الذي حُفظ فيه الملف";

        public const string SuccessTitle = "تمّ إنشاء ملف الإعداد";

        public const string SuccessDesc =
            "انقل الملف وكلمة مروره إلى الحاسوب المقصود. عند أول تشغيل يطلب الملف ثم كلمة المرور.";

        public const string NewExport = "تصدير ملف آخر";
        public const string NewExportTooltip = "بدء تصدير ملف إعداد جديد";

        // ── Movement between the steps ──────────────────────────────────────────────────────────
        public const string Next = "التالي";
        public const string NextTooltip = "الانتقال إلى الخطوة التالية";
        public const string Previous = "السابق";
        public const string PreviousTooltip = "العودة إلى الخطوة السابقة";

        // ── What went wrong ─────────────────────────────────────────────────────────────────────
        public const string FailedNoOrganisation = "لم تُسجَّل بيانات الهيئة بعد. أكملها في «الهيئة والهوية».";
        public const string FailedNoDevice = "لم يعد هذا الجهاز مسجّلًا. حدّث الشاشة واختر جهازًا آخر.";
        public const string FailedRevoked = "هذا الجهاز مُلغى، ولا يُصدَر له ملف إعداد.";
        public const string FailedNoAccount = "لا يوجد موظف مسجّل على هذا الجهاز. أكمل بياناته في «المكاتب والأجهزة».";
        public const string FailedNoOfficeKey = "لم يُصدر مفتاح لهذا المكتب بعد. أصدره من «الحسابات والمفاتيح».";
        public const string FailedStructure = "بيانات الهيكلية ناقصة، فتعذّر تجهيز الملف. راجع «الهيكلية».";
        public const string FailedWrite = Errors.CannotWriteFolder;
        public const string FailedPassword = "كلمة المرور غير مكتملة. ولّدها من جديد.";

        /// <summary>The office and its inventory code, as the chooser and the summary line write it.</summary>
        public static string OfficeOption(string name, string code) => Bidi($"{name} · {code}");

        /// <summary>«الجهاز 2 · سكرتير المكتب · أحمد».</summary>
        public static string DeviceOption(int deviceNo, string roleName, string employeeName) =>
            Bidi($"الجهاز {Digits(deviceNo)} · {roleName} · {employeeName}");

        /// <summary>The line above the content list: whose file this is.</summary>
        public static string ForDevice(string officeName, int deviceNo, string employeeName) =>
            Bidi($"{officeName} · الجهاز {Digits(deviceNo)} · {employeeName}");

        /// <summary>«الملف رقم 2 لهذا الجهاز».</summary>
        public static string SequenceLine(long sequence) => Bidi($"الملف رقم {Digits((int)sequence)} لهذا الجهاز");

        /// <summary>How many nodes of the structure travel in the file.</summary>
        public static string StructureCount(int departments, int sections, int units) =>
            Bidi($"{Counting.Departments(departments)} · {Counting.Sections(sections)} · {Counting.Units(units)}");

        /// <summary>«مفتاح المكتب، الإصدار 2».</summary>
        public static string OfficeKeyVersion(int version) => Bidi($"الإصدار {Digits(version)}");

        /// <summary>The cycle day as the content list states it.</summary>
        public static string CycleDayValue(int day) => Bidi($"اليوم {Digits(day)} من كل شهر");

        /// <summary>How many revoked devices the signed list names.</summary>
        public static string RevocationCount(int count) => Bidi(Counting.Devices(count));

        /// <summary>The size of an attachment, in whole kilobytes, so «2 ميجابايت» never hides a surprise.</summary>
        public static string Size(long bytes)
        {
            var kilobytes = Math.Max(1, (bytes + 1023) / 1024);
            return Bidi(kilobytes >= 1024
                ? $"{(kilobytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} ميجابايت"
                : $"{Digits((int)kilobytes)} كيلوبايت");
        }

        /// <summary>The line in the operations log.</summary>
        public static string Log(string officeName, int deviceNo, string fileName) =>
            Bidi($"صُدّر ملف إعداد للجهاز {Digits(deviceNo)} في «{officeName}» باسم {fileName}.");
    }
}
