using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Core.Services;

/// <summary>
/// The letter package's slice of Core's Arabic sentences (AGREEMENT items 15 and 57).
/// </summary>
/// <remarks>
/// ARCHITECTURE §12 keeps every sentence Core can emit inside <see cref="CoreAr"/>, so a reviewer
/// reads them all in one place; this file is the part of that class the official-letter template
/// and composer produce. No sentence here names Word's version, a file format, a path or a code —
/// only what a person needs to read. Values that travel inside a sentence are wrapped in
/// <see cref="CoreAr.Isolate"/> because these strings are written into a saved document and
/// printed: no screen can wrap them in a <c>&lt;bdi&gt;</c> afterwards.
/// </remarks>
public static partial class CoreAr
{
    /// <summary>The official letter's own wording.</summary>
    public static class Letter
    {
        /// <summary>What stands in the number's place until the letter is approved.</summary>
        public const string DraftNumber = "مسودة";

        /// <summary>Beside «فتح قالب المراسلة في Word» in the settings screen (W88).</summary>
        public const string LocalCopyWarning =
            "التعديل هنا يغيّر نسخة المكتب من القالب فقط، وأي ملف إعداد جديد يستبدلها.";

        /// <summary>Said when the template in force is the one built into الوكيل.</summary>
        public const string BuiltInTemplateInUse = "قالب الوكيل العام — لم تصل ترويسة الهيئة بعد";

        /// <summary>Said when the template in force came with the setup file.</summary>
        public const string SetupTemplateInUse = "قالب الهيئة كما وصل في ملف الإعداد";

        /// <summary>Said when the office has edited its own copy.</summary>
        public const string LocalTemplateInUse = "نسخة المكتب من القالب";

        /// <summary>The file could not be opened as a Word file.</summary>
        public const string TemplateUnreadable = "تعذّرت قراءة الملف؛ اختر ملف Word للقالب";

        /// <summary>The file is a Word file but has nowhere to put the letter's text.</summary>
        public const string TemplateHasNoBodyMark =
            "لا توجد في القالب علامة «@نص المراسلة»؛ أضفها في المكان الذي يبدأ منه نص الكتاب";

        /// <summary>Heading over the marks الوكيل will fill.</summary>
        public const string KnownMarksHeading = "العلامات التي يملؤها الوكيل";

        /// <summary>Heading over the marks nobody recognises.</summary>
        public const string UnknownMarksHeading = "علامات غير معروفة";

        /// <summary>Under the unknown marks.</summary>
        public const string UnknownMarksNote =
            "هذه العلامات ستبقى كما هي في الكتاب المطبوع؛ راجع كتابتها في ملف القالب.";

        /// <summary>Heading over the known marks this template leaves out.</summary>
        public const string MissingMarksHeading = "علامات معروفة غير موجودة في القالب";

        /// <summary>Under the missing marks.</summary>
        public const string MissingMarksNote = "البيانات المقابلة لها لن تظهر في الكتاب.";

        /// <summary>Said when every mark in the template is one of the nine.</summary>
        public const string AllMarksKnown = "كل العلامات معروفة";

        /// <summary>The card shown where the «تحرير في Word» button would be (AGREEMENT item 10).</summary>
        public const string WordUnavailable = "Word غير متوفر على هذا الجهاز";

        /// <summary>What to do instead, under that card.</summary>
        public const string WordUnavailableAction =
            "اكتب نص الكتاب في محرر الوكيل؛ الكتاب يُطبع ويُحفظ بصيغة PDF دون Word";

        /// <summary>Shown while the letter is open in Word.</summary>
        public const string WordOpen = "الكتاب مفتوح في Word؛ أغلق نافذته لاستيراد التعديلات";

        /// <summary>The user closed Word and what they wrote came back.</summary>
        public const string WordImported = "استُوردت تعديلات Word";

        /// <summary>Word was there but the attempt did not finish.</summary>
        public const string WordFailed = "تعذّر فتح الكتاب في Word؛ الكتاب كما هو ولم يتغيّر";

        /// <summary>Neither Word nor the preview could produce a PDF.</summary>
        public const string PdfUnavailable = "تعذّر حفظ الكتاب بصيغة PDF؛ استخدم الطباعة من المعاينة";

        /// <summary>The referral text fitted under the letter on its last page.</summary>
        public const string ReferralOnLastPage = "نص الإحالة يظهر أسفل الصفحة الأخيرة من نسخة الطباعة";

        /// <summary>A page had to be added for the referral text (AGREEMENT item 31).</summary>
        public const string ReferralOnAddedPage =
            "لا تكفي المساحة أسفل الصفحة الأخيرة؛ ستُضاف صفحة تحمل نص الإحالة، والأصل يبقى كما هو";

        /// <summary>The heading written above the referral text on the print copy.</summary>
        public const string ReferralHeading = "الإحالة";

        /// <summary>Introduces who the letter was referred to.</summary>
        /// <param name="toAr">The unit or person.</param>
        public static string ReferralTo(string toAr) => $"إلى: {Isolate(toAr)}";

        /// <summary>Introduces the referral's deadline.</summary>
        /// <param name="dateAr">The date, already written out.</param>
        public static string ReferralDue(string dateAr) => $"المهلة: {Isolate(dateAr)}";

        /// <summary>Names the page a letter is printed on.</summary>
        /// <param name="size">A4 or A5.</param>
        public static string PageSize(LetterPageSize size) =>
            size == LetterPageSize.A5
                ? $"{Isolate("A5")} (نصف صفحة)"
                : $"{Isolate("A4")} (صفحة كاملة)";

        /// <summary>Names where the template in force came from.</summary>
        /// <param name="origin">Which copy won.</param>
        public static string Origin(LetterTemplateOrigin origin) => origin switch
        {
            LetterTemplateOrigin.LocalCopy => LocalTemplateInUse,
            LetterTemplateOrigin.Setup => SetupTemplateInUse,
            _ => BuiltInTemplateInUse,
        };
    }
}
