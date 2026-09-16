namespace Wakeel.Design.Text;

/// <summary>
/// Every Arabic string used by the shared design system's generic component chrome (fields,
/// buttons, dialogs, states, menus, pagination, breadcrumbs, the sidebar's navigation tree, the
/// top bar, and similar shell vocabulary), per AGREEMENT item 15 and ARCHITECTURE.md §12 ("كل نص
/// للمستخدم في Ar.*"). This partial file covers the vocabulary a component itself owns; screen/app
/// specific text lives in <c>Ar.App.cs</c> (and future per-package partials such as
/// <c>Ar.FirstRun.cs</c>) alongside it in the same partial <see cref="Ar"/> class.
/// </summary>
public static partial class Ar
{
    /// <summary>Sidebar navigation item labels.</summary>
    public static class Nav
    {
        public const string AttentionCenter = "مركز الانتباه";
        public const string Correspondence = "المراسلات";
        public const string TasksAndFollowUp = "المتابعة والمهام";
        public const string Meetings = "الاجتماعات";
        public const string Calendar = "التقويم";
        public const string Cases = "القضايا";
        public const string Documents = "الوثائق";
        public const string Parties = "الجهات";
        public const string Employees = "الموظفون";
        public const string AssetsAndCustody = "الأصول والعُهد";
        public const string Finance = "المالية";
        public const string Reports = "التقارير";

        /// <summary>Accessible label for the sidebar's main navigation landmark.</summary>
        public const string MainNavigationLabel = "التنقل الرئيسي";

        /// <summary>Tooltip on a sidebar group header's collapse/expand control when the group is open.</summary>
        public const string CollapseGroup = "طي المجموعة";

        /// <summary>Tooltip on a sidebar group header's collapse/expand control when the group is collapsed.</summary>
        public const string ExpandGroup = "توسيع المجموعة";
    }

    /// <summary>Sidebar footer: sync status and version line.</summary>
    public static class Footer
    {
        public const string Synced = "متزامن";
        public const string Syncing = "يُزامَن الآن";
        public const string SyncFailed = "تعذّرت المزامنة";
        public const string MinutesAgoFormat = "منذ {0} دقائق";

        /// <summary>Version line, e.g. «الوكيل • الإصدار v0.21».</summary>
        public static string VersionLine(string version) => $"{AppName} • الإصدار {version}";
    }

    /// <summary>Top bar strings.</summary>
    public static class TopBar
    {
        public const string GlobalSearchPlaceholder = "ابحث في المراسلات والمهام والقضايا والوثائق...";
        public const string CalendarTooltip = "التقويم";
        public const string NotificationsTooltip = "الإشعارات";
    }

    /// <summary>Shared field-control chrome strings (WInput/WSelect).</summary>
    public static class Fields
    {
        public const string ShowPassword = "إظهار كلمة المرور";
        public const string HidePassword = "إخفاء كلمة المرور";

        /// <summary>Placeholder for WInput's masked date control (ARCHITECTURE.md §12: dd/MM/yyyy, Western digits).</summary>
        public const string DatePlaceholder = "يوم/شهر/سنة";
    }

    /// <summary>Local-search box (AGREEMENT item 41): appears above every list screen.</summary>
    public static class LocalSearch
    {
        public const string Placeholder = "ابحث في هذه القائمة...";
        public const string KbdHint = "Ctrl+F";
        public const string NoResults = "لا توجد نتائج مطابقة";
    }

    /// <summary>Generic button labels shared across the design system.</summary>
    public static class Buttons
    {
        public const string Save = "حفظ";
        public const string Cancel = "إلغاء";
        public const string Confirm = "تأكيد";
        public const string Close = "إغلاق";
        public const string Delete = "حذف";
        public const string Edit = "تعديل";
        public const string Add = "إضافة";
        public const string AddNew = "إضافة جديد";
        public const string Retry = "إعادة المحاولة";
        public const string Refresh = "تحديث";
        public const string Filter = "تصفية";
        public const string Print = "طباعة";
        public const string Copy = "نسخ";
        public const string Download = "تنزيل";
        public const string Upload = "رفع";
        public const string Next = "التالي";
        public const string Back = "السابق";
        public const string ShowMore = "عرض المزيد";
    }

    /// <summary>Loading / async-action state text (e.g. WButton's Loading state).</summary>
    public static class Loading
    {
        public const string Working = "جارٍ التنفيذ...";
        public const string Saving = "جارٍ الحفظ...";
    }

    /// <summary>Generic empty/error states used by WStateCard across screens (see W91).</summary>
    public static class States
    {
        public const string NoDataTitle = "لا توجد بيانات بعد";
        public const string NoDataDesc = "ابدأ بإضافة أول عنصر إلى هذه القائمة";
        public const string NoResultsTitle = "لا توجد نتائج مطابقة";
        public const string NoResultsDesc = "جرّب كلمات بحث أو تصفية مختلفة";
        public const string ErrorTitle = "فشلت العملية";
        public const string ErrorDesc = "لم تُحفظ بياناتك، حاول مجددًا";
        public const string LoadingTitle = "جارٍ التحميل";
        public const string LoadingDesc = "يرجى الانتظار قليلًا";
    }

    /// <summary>Standard dialog action labels (WDialog: cancel on the left, confirm on the right).</summary>
    public static class Dialog
    {
        public const string ConfirmDeleteTitle = "حذف نهائيًا؟";
        public const string ConfirmDeleteDesc = "هذا الإجراء لا يمكن التراجع عنه.";
    }

    /// <summary>Table footer pagination text, e.g. «عرض 1–20 من 128».</summary>
    public static class Table
    {
        public static string ShowingRange(int from, int to, int total) => $"عرض {from}–{to} من {total}";
        public const string NoRows = "لا توجد صفوف لعرضها";
    }

    /// <summary>Autosave-indicator states (ARCHITECTURE.md §10: draft autosave every 3 seconds).</summary>
    public static class Autosave
    {
        public const string Saved = "تم الحفظ تلقائيًا";
        public const string Saving = "جارٍ الحفظ التلقائي...";
        public const string Failed = "تعذّر الحفظ التلقائي";
    }

    /// <summary>WMenu built-in item labels reused across popovers (row "more" menu, avatar menu).</summary>
    public static class Menu
    {
        public const string View = "عرض";
        public const string Edit = "تعديل";
        public const string Archive = "أرشفة";
        public const string Delete = "حذف نهائيًا";
        public const string Print = "طباعة";

        /// <summary>Accessible name (tooltip + aria-label) for WMenu's default icon-only "more" trigger.</summary>
        public const string MoreActions = "خيارات إضافية";
    }

    /// <summary>WPager (table footer pagination) strings.</summary>
    public static class Pager
    {
        public const string Label = "ترقيم الصفحات";
        public const string Previous = "السابق";
        public const string Next = "التالي";
    }

    /// <summary>WBreadcrumb strings.</summary>
    public static class Breadcrumb
    {
        public const string Label = "مسار التصفح";
    }

    /// <summary>Top bar user menu.</summary>
    public static class UserMenu
    {
        public const string Settings = "الإعدادات";
        public const string Help = "المساعدة";
        public const string Lock = "قفل الجهاز";
        public const string SignOut = "تسجيل الخروج";
    }

    /// <summary>Banner.Clock: shown when the machine clock fails the sanity check (AGREEMENT item 20).</summary>
    public static class ClockBanner
    {
        public const string Title = "تعذّر التحقق من ساعة الجهاز";
        public const string Desc = "ساعة هذا الحاسوب غير موثوقة، وقد تتعطل عمليات الترقيم حتى تصحيحها.";
        public const string Action = "كيف أصحّح الساعة؟";
    }

    /// <summary>Shown by the shell's router when a route matches no page.</summary>
    public static class NotFound
    {
        public const string Title = "الصفحة غير موجودة";
    }

    /// <summary>WTree / WTreeToolbar vocabulary: the generic hierarchy component reused by the
    /// organisation structure, unit pickers and document folders.</summary>
    public static class Tree
    {
        /// <summary>Accessible name of the tree landmark when the caller supplies none.</summary>
        public const string Label = "شجرة العناصر";

        /// <summary>Text button that opens every branch.</summary>
        public const string ExpandAll = "توسيع الكل";

        /// <summary>Text button that closes every branch.</summary>
        public const string CollapseAll = "طي الكل";

        /// <summary>Tooltip on a closed branch's caret.</summary>
        public const string ExpandNode = "توسيع الفرع";

        /// <summary>Tooltip on an open branch's caret.</summary>
        public const string CollapseNode = "طي الفرع";

        /// <summary>Tooltip on a movable row's reorder handle.</summary>
        public const string DragHandle = "مقبض ترتيب العنصر";

        /// <summary>Shown when the search text matches no node.</summary>
        public const string NoMatch = "لا عنصر بهذا الاسم";

        /// <summary>Shown when the tree itself has no nodes at all.</summary>
        public const string Empty = "لا توجد عناصر بعد";

        /// <summary>Search box placeholder above a tree.</summary>
        public const string SearchPlaceholder = "ابحث باسم العنصر أو رئيسه...";

        /// <summary>
        /// Sample vocabulary for the design gallery's tree section. It lives here (rather than beside
        /// the other gallery strings) because the gallery text file belongs to another work package;
        /// these strings are demonstration data only and no screen reads them.
        /// </summary>
        public static class Demo
        {
            public const string Title = "شجرة الهيكل التنظيمي";
            public const string Authority = "هيئة تنمية المناطق الريفية";
            public const string PlanningDept = "دائرة التخطيط";
            public const string StrategicSection = "قسم التخطيط الاستراتيجي";
            public const string FollowUpUnit = "وحدة المتابعة والتقارير";
            public const string StudiesUnit = "وحدة الدراسات";
            public const string ProjectsSection = "قسم متابعة المشاريع";
            public const string AdminDept = "دائرة الشؤون الإدارية";
            public const string HumanResources = "قسم الموارد البشرية";
            public const string ArchiveUnit = "وحدة الأرشيف Archive";
            public const string FinanceDept = "الدائرة المالية";
            public const string LevelAuthority = "هيئة";
            public const string LevelDepartment = "دائرة";
            public const string LevelSection = "قسم";
            public const string LevelUnit = "وحدة";
            public const string HeadSami = "د. سامي العبد الله";
            public const string HeadLayla = "أ. ليلى الحسن";
            public const string HeadAhmad = "أحمد الخطيب";
            public const string HeadSara = "سارة النعيمي";
            public const string HeadNour = "نور الدين حمدان";
            public const string HeadImad = "عماد الشريف";
            public const string HeadHuda = "هدى السنان";
            public const string HeadRania = "رانيا القاسم";
            public const string NoHead = "بلا رئيس معيّن";
            public const string OutOfService = "خارج الخدمة";
            public const string Hint = "مقبض الترتيب في نهاية كل صف محجوز لتغيير التبعية لاحقًا.";
        }
    }
}
