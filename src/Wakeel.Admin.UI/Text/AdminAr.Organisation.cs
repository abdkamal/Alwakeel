using System.Globalization;

namespace Wakeel.Admin.UI.Text;

/// <summary>A04 «الهيئة والهوية»: the organisation's name, logo, cycle, numbering and templates.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A04 says.</summary>
    public static class Organisation
    {
        public const string Title = "الهيئة والهوية";
        public const string Sub = "اسم الهيئة وشعارها والدورة المالية وصيغة الترقيم وقوالب المراسلات.";

        public const string Save = "حفظ";
        public const string SaveTooltip = "حفظ بيانات الهيئة";
        public const string Saved = "حُفظت بيانات الهيئة.";
        public const string Discard = "التراجع عن التعديلات";
        public const string DiscardTooltip = "إعادة الحقول إلى آخر ما حُفظ";

        /// <summary>The identity block.</summary>
        public static class Identity
        {
            public const string Heading = "اسم الهيئة";
            public const string NameLabel = "الاسم الكامل للهيئة";
            public const string NameHint = "يظهر في أعلى كل مراسلة وفي كل ملف إعداد.";
            public const string NamePlaceholder = "اكتب الاسم كما يُكتب في المراسلات الرسمية";
            public const string NameRequired = "اسم الهيئة مطلوب.";
        }

        /// <summary>The logo block.</summary>
        public static class Logo
        {
            public const string Heading = "الشعار";
            public const string Desc = "صورة مربّعة تظهر في ترويسة المراسلات وفي أعلى الأداة.";
            public const string Choose = "اختيار صورة";
            public const string ChooseTooltip = "اختيار صورة الشعار من هذا الحاسوب";
            public const string Replace = "استبدال الصورة";
            public const string Remove = "إزالة الشعار";
            public const string RemoveTooltip = "إزالة الشعار الحالي";
            public const string Removed = "أُزيل الشعار.";
            public const string Empty = "لا يوجد شعار بعد";
            public const string EmptyDesc = "اختر صورة بصيغة PNG أو JPG، ولا يتجاوز حجمها ٢ ميغابايت.";
            public const string PreviewLabel = "معاينة الشعار بعد القصّ";
            public const string FocusLabel = "موضع القصّ المربّع";
            public const string FocusHint = "حرّك المؤشّر لتختار أي جزء من الصورة يبقى داخل المربّع.";
            public const string Saved = "حُفظ الشعار.";
            public const string CropUnavailable =
                "هذا الحاسوب لا يستطيع قصّ الصور، فحُفظت الصورة كما هي. اختر صورة مربّعة لتظهر كما تتوقّع.";

            public const string TooLarge = "الصورة كبيرة جدًا. اختر صورة لا يتجاوز حجمها ٢ ميغابايت.";
            public const string WrongKind = "هذا الملف ليس صورة بصيغة PNG أو JPG.";
            public const string Unreadable = "تعذّرت قراءة هذه الصورة. قد تكون ناقصة أو تالفة.";
            public const string TooSmall = "الصورة صغيرة جدًا. اختر صورة لا يقلّ ضلعها عن ٤٨ نقطة.";

            /// <summary>The line under the preview: how big the stored picture is.</summary>
            public static string Measure(int width, int height) =>
                Bidi($"{width.ToString(CultureInfo.InvariantCulture)} × {height.ToString(CultureInfo.InvariantCulture)}");
        }

        /// <summary>The financial cycle block (AGREEMENT item 52).</summary>
        public static class Cycle
        {
            public const string Heading = "الدورة المالية";
            public const string DayLabel = "يوم بداية الدورة";
            public const string DayHint = "من ١ إلى ٢٨ فقط، لأن بعض الأشهر لا تحوي ما بعدها.";
            public const string OutOfRange = "اختر يومًا بين ١ و٢٨.";

            public const string Explain =
                "كل ما يُحتسب شهريًا — المصروفات والعُهد والتقارير — يبدأ من هذا اليوم وينتهي في اليوم السابق له من الشهر التالي.";

            /// <summary>The worked example under the explanation, so the choice is not abstract.</summary>
            public static string Example(int day)
            {
                var previous = day == 1 ? 28 : day - 1;
                return Bidi(
                    $"مع اليوم {Digits(day)}: تبدأ دورة شهر ٣ في {Digits(day)}/٣ وتنتهي في {Digits(previous)}/٤.");
            }
        }

        /// <summary>The numbering block (AGREEMENT item 5).</summary>
        public static class Numbering
        {
            public const string Heading = "صيغة الترقيم";
            public const string FormatLabel = "صيغة رقم الصادر";
            public const string FormatHint = "الافتراضي: تاريخ اليوم ثم رمز الدائرة والقسم ثم رقم متسلسل.";
            public const string Invalid = "الصيغة غير صالحة. لا بدّ أن تحوي موضع الرقم المتسلسل.";
            public const string Reset = "العودة إلى الصيغة الافتراضية";
            public const string ResetTooltip = "استخدام الصيغة الافتراضية للترقيم";

            public const string ChangeTitle = "تغيير صيغة الترقيم؟";

            public const string ChangeWarning =
                "الأرقام الصادرة سابقًا تبقى كما هي، والمراسلات الجديدة وحدها تأخذ الصيغة الجديدة. هذا يعني أن سجلّ الصادر سيحمل صيغتين، ولا يمكن التراجع عن هذا لاحقًا.";

            /// <summary>The sample number the field draws under itself from the format in the box.</summary>
            public static string Sample(string format) => Bidi($"مثال: {Preview(format)}");

            /// <summary>
            /// What a number written in this format would look like. Only the letters the format
            /// knows are replaced; anything else the organisation put in its format is left alone.
            /// </summary>
            public static string Preview(string format)
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    return "—";
                }

                var text = format
                    .Replace("YYYY", "2026", StringComparison.Ordinal)
                    .Replace("MM", "09", StringComparison.Ordinal)
                    .Replace("DD", "16", StringComparison.Ordinal);

                var builder = new System.Text.StringBuilder(text.Length);
                var serial = 0;
                foreach (var character in text)
                {
                    builder.Append(character switch
                    {
                        'D' => '3',
                        'E' => '2',
                        'S' => (char)('0' + (serial++ % 10 == 0 ? 0 : serial % 10)),
                        _ => character,
                    });
                }

                return builder.ToString();
            }
        }

        /// <summary>The monthly report template block (AGREEMENT item 53).</summary>
        public static class ReportTemplate
        {
            public const string Heading = "قالب التقرير الشهري";
            public const string Desc = "ملف Word يُبنى عليه التقرير الشهري. اختياري: بدونه يُكتب التقرير بالشكل المدمج.";
            public const string Choose = "اختيار قالب";
            public const string ChooseTooltip = "اختيار ملف قالب التقرير الشهري";
            public const string Remove = "إزالة القالب";
            public const string RemoveTooltip = "إزالة قالب التقرير الشهري";
            public const string None = "لم يُختر قالب بعد";
            public const string Saved = "حُفظ قالب التقرير الشهري.";
            public const string Removed = "أُزيل قالب التقرير الشهري.";
            public const string WrongKind = "هذا الملف ليس ملف Word بصيغة docx.";
            public const string TooLarge = "الملف كبير جدًا. اختر ملفًا لا يتجاوز ٤ ميغابايت.";
        }

        /// <summary>The official letter template block (AGREEMENT item 57).</summary>
        public static class LetterTemplate
        {
            public const string Heading = "قالب المراسلة الرسمية";

            public const string Desc =
                "ملف Word يحمل ترويسة الهيئة وعلاماتها. تُكتب العلامة بعلامة @ ويملؤها الوكيل عند كتابة كل مراسلة.";

            public const string Choose = "اختيار قالب";
            public const string ChooseTooltip = "اختيار ملف قالب المراسلة";
            public const string Remove = "العودة إلى القالب المدمج";
            public const string RemoveTooltip = "إزالة القالب المرفوع والعودة إلى القالب المدمج";
            public const string BuiltIn = "القالب المدمج";
            public const string BuiltInChip = "مدمج";
            public const string Saved = "حُفظ قالب المراسلة.";
            public const string Removed = "عاد القالب المدمج.";
            public const string Unreadable = "تعذّرت قراءة هذا الملف كملف Word. تأكّد من أنه ملف docx كامل.";
            public const string WrongKind = "هذا الملف ليس ملف Word بصيغة docx.";
            public const string TooLarge = "الملف كبير جدًا. اختر ملفًا لا يتجاوز ٤ ميغابايت.";

            public const string KnownHeading = "العلامات المعروفة في القالب";
            public const string UnknownHeading = "علامات لا يعرفها الوكيل";

            public const string UnknownDesc =
                "هذه العلامات ستبقى كما هي في المراسلة لأن الوكيل لا يعرف بماذا يملؤها. راجع كتابتها في القالب.";

            public const string MissingHeading = "علامات معروفة لا يستعملها القالب";
            public const string MissingDesc = "لا شيء يمنع ذلك؛ ما لا يُذكر في القالب لا يظهر في المراسلة.";
            public const string AllKnown = "كل العلامات في هذا القالب معروفة.";

            public const string PreviewHeading = "معاينة المراسلة";
            public const string PreviewDesc = "المعاينة تملأ العلامات ببيانات تجريبية لترى شكل المراسلة قبل اعتماد القالب.";
            public const string PreviewA4 = "مقاس A4";
            public const string PreviewA5 = "مقاس A5";
            public const string PreviewA4Tooltip = "معاينة المراسلة بمقاس A4";
            public const string PreviewA5Tooltip = "معاينة المراسلة بمقاس A5";
            public const string SavePdf = "حفظ المعاينة ملف PDF";
            public const string SavePdfTooltip = "حفظ المعاينة الظاهرة ملف PDF";
            public const string SavePdfUnavailable = "هذا الحاسوب لا يستطيع حفظ ملفات PDF.";
            public const string SavePdfDone = "حُفظت المعاينة.";
            public const string SavePdfFailed = "تعذّر حفظ المعاينة. تأكّد من المجلد الذي اخترته ثم أعد المحاولة.";
            public const string SavePdfCancelled = "لم تُحفظ المعاينة.";
            public const string PreviewFileName = "معاينة-المراسلة";

            /// <summary>How many times a mark appears, when more than once.</summary>
            public static string Times(int count) => count <= 1
                ? string.Empty
                : Bidi($"×{Digits(count)}");

            /// <summary>The line naming the template in force.</summary>
            public static string InUse(string fileName) => Bidi($"القالب المستعمل: {fileName}");
        }

        /// <summary>What the operations log records about A04.</summary>
        public static class Log
        {
            public const string LogoSet = "غُيّر شعار الهيئة.";
            public const string LogoCleared = "أُزيل شعار الهيئة.";
            public const string ReportTemplateCleared = "أُزيل قالب التقرير الشهري.";
            public const string LetterTemplateCleared = "أُزيل قالب المراسلة وعاد القالب المدمج.";

            public static string Renamed(string name) => $"صار اسم الهيئة «{name}».";

            public static string CycleChanged(int day) => $"صارت الدورة المالية تبدأ في اليوم {Digits(day)}.";

            public static string NumberingChanged(string was, string now) =>
                Bidi($"تغيّرت صيغة الترقيم من «{was}» إلى «{now}».");

            public static string ReportTemplateSet(string fileName) => Bidi($"حُفظ قالب التقرير الشهري «{fileName}».");

            public static string LetterTemplateSet(string fileName) => Bidi($"حُفظ قالب المراسلة «{fileName}».");
        }

        /// <summary>The sample values the letter preview fills the marks with.</summary>
        public static IReadOnlyDictionary<string, string> SampleLetterValues { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["@التاريخ الهجري"] = "٤ ربيع الأول ١٤٤٨",
                ["@التاريخ الميلادي"] = "١٦/٩/٢٠٢٦",
                ["@رقم الصادر"] = "٢٠٢٦٠٩١٦/٣٢٠٠٧",
                ["@اسم رئيس المكتب"] = "فلان الفلاني",
                ["@اسم مكتب المستقبل"] = "دائرة الشؤون الإدارية",
                ["@اسم الموضوع"] = "طلب تزويد بأثاث مكتبي",
                ["@نص المراسلة"] = "نرجو التكرّم بالموافقة على تزويد المكتب بما يلزمه من أثاث، وفق الكشف المرفق.",
                ["@اسم المرسل"] = "فلان الفلاني",
                ["@اسم مكتب المرسل"] = "قسم الخدمات",
            };
    }
}
