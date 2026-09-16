namespace Wakeel.Admin.UI.Text;

/// <summary>
/// Every Arabic string the administration tool shows, split by area across partial files so the
/// three sub-packages never have to edit the same one (AGREEMENT item 15: no technical terms and no
/// error numbers anywhere a person can see). This file carries the shell: the window title, the top
/// bar, the tabs, and the vocabulary the shared confirm/error pieces use.
/// </summary>
/// <remarks>
/// The shared design system's own <c>Ar</c> keeps the generic control chrome (buttons, fields,
/// dialogs); <c>AdminAr</c> only adds what is the tool's own. Where both would say the same word,
/// the design system's is used rather than repeated here.
/// </remarks>
public static partial class AdminAr
{
    /// <summary>The tool's name, as it appears in the window title and the top bar.</summary>
    public const string ToolName = "مدير نظام الوكيل";

    /// <summary>Top bar and tab strip.</summary>
    public static class Shell
    {
        /// <summary>Tooltip of the logo, which goes back to the dashboard.</summary>
        public const string HomeTooltip = "لوحة الهيئة";

        /// <summary>Tooltip of the light/dark switch when the light palette is showing.</summary>
        public const string DarkModeTooltip = "المظهر الداكن";

        /// <summary>Tooltip of the light/dark switch when the dark palette is showing.</summary>
        public const string LightModeTooltip = "المظهر الفاتح";

        /// <summary>Tooltip of the sign-out button.</summary>
        public const string SignOutTooltip = "الخروج من الأداة";

        /// <summary>The role line under the administrator's name in the top bar.</summary>
        public const string AdminRole = "مدير النظام";

        /// <summary>Accessible label of the tab strip.</summary>
        public const string TabsLabel = "أقسام الأداة";

        /// <summary>Shown in place of the organisation summary before an organisation has a name.</summary>
        public const string NoOrgYet = "لم تُسجَّل بيانات الهيئة بعد";

        /// <summary>The organisation summary line: name, then how much structure there is.</summary>
        public static string OrgSummary(string orgName, int departments, int sections, int units) =>
            $"{orgName} · {Counting.Departments(departments)} · {Counting.Sections(sections)} · {Counting.Units(units)}";
    }

    /// <summary>The seven areas of the tool, in the order the tab strip shows them.</summary>
    public static class Tabs
    {
        public const string Organisation = "الهيئة والهوية";
        public const string Structure = "الهيكلية";
        public const string OfficesAndDevices = "المكاتب والأجهزة";
        public const string AccountsAndKeys = "الحسابات والمفاتيح";
        public const string SetupExport = "تصدير الإعداد";
        public const string Maintenance = "الصيانة";
        public const string AuditLog = "السجل";

        /// <summary>The seven, in the order the tab strip shows them.</summary>
        public static IReadOnlyList<string> All { get; } =
        [
            Organisation, Structure, OfficesAndDevices, AccountsAndKeys, SetupExport, Maintenance, AuditLog,
        ];
    }

    /// <summary>Arabic counting, which changes shape with the number rather than adding an «s».</summary>
    public static class Counting
    {
        public static string Departments(int count) => Count(count, "دائرة", "دائرتان", "دوائر", "دائرة");

        public static string Sections(int count) => Count(count, "قسم", "قسمان", "أقسام", "قسمًا");

        public static string Units(int count) => Count(count, "وحدة", "وحدتان", "وحدات", "وحدة");

        public static string Offices(int count) => Count(count, "مكتب", "مكتبان", "مكاتب", "مكتبًا");

        public static string Devices(int count) => Count(count, "جهاز", "جهازان", "أجهزة", "جهازًا");

        public static string Changes(int count) => Count(count, "تغيير", "تغييران", "تغييرات", "تغييرًا");

        public static string Seconds(int count) => Count(count, "ثانية", "ثانيتان", "ثوانٍ", "ثانية");

        public static string Attempts(int count) => Count(count, "محاولة", "محاولتان", "محاولات", "محاولة");

        /// <summary>
        /// Arabic has four shapes, not two: nothing, one, a pair, a few (three to ten), and many
        /// (eleven and up, which takes the singular again). Written out here once so every screen
        /// counts the same way.
        /// </summary>
        private static string Count(int count, string one, string two, string few, string many) => count switch
        {
            0 => $"لا {few}",
            1 => one,
            2 => two,
            >= 3 and <= 10 => $"{count} {few}",
            _ => $"{count} {many}",
        };
    }

    /// <summary>What the tool says when something it was asked to do could not be done (A12).</summary>
    public static class Errors
    {
        public const string Title = "تعذّر إتمام الإجراء";
        public const string Retry = "إعادة المحاولة";
        public const string RequiredField = "هذا الحقل مطلوب.";

        public const string CannotWriteFolder =
            "تعذّرت الكتابة في مجلد الأداة. تأكّد من وجود مساحة على القرص ومن صلاحية الكتابة في المجلد، ثم أعد المحاولة.";

        public const string FileUnreadable =
            "تعذّرت قراءة هذا الملف. قد يكون تالفًا أو غير مكتمل النسخ.";

        public const string WrongPackagePassword =
            "كلمة المرور لا تفتح هذا الملف. تأكّد من نسخها كاملة كما ظهرت عند التصدير.";

        public const string DataUnreadable =
            "تعذّرت قراءة بيانات الأداة على هذا الحاسوب. افتح «الصيانة» لفحصها.";
    }

    /// <summary>The shared confirm and danger dialog (A12).</summary>
    public static class Confirm
    {
        public const string DangerTitle = "إجراء لا يمكن التراجع عنه";
        public const string Continue = "متابعة";
        public const string Back = "العودة";
        public const string TypeToConfirm = "اكتب «تأكيد» للمتابعة";
        public const string ConfirmWord = "تأكيد";
    }
}
