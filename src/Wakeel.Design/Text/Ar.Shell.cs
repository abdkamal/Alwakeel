namespace Wakeel.Design.Text;

/// <summary>
/// Every Arabic string of the daily shell's own screens — the attention center's live rows (W08),
/// the late/near/stale/pending lists (W09), the notification panel (W10), the clock banner's
/// buttons (W11), the health center (W12), the two reference sheets (W91, W92) and the quick-entry
/// dialog (W94). It sits beside <c>Ar.Common.cs</c> and <c>Ar.App.cs</c> in the same partial
/// <see cref="Ar"/> class, per ARCHITECTURE.md §12 ("كل نص للمستخدم في Ar.*").
/// </summary>
/// <remarks>
/// Numbers are written with Western digits inside otherwise Arabic sentences (DESIGN-GUIDE.md), so
/// every sentence that carries one is a format method rather than a constant: the screen passes the
/// already-formatted number and wraps the rendered run in a bidi isolate (AGREEMENT item 55).
/// </remarks>
public static partial class Ar
{
    /// <summary>Arabic calendar wording the daily screens' header lines are built from.</summary>
    /// <remarks>
    /// The day name is looked up rather than taken from a culture: the product pins Western digits
    /// and one fixed spelling of every weekday, and a machine whose Windows language pack differs
    /// must not change what the header says.
    /// </remarks>
    public static class Dates
    {
        private static readonly string[] DayNames =
        [
            "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت",
        ];

        /// <summary>«السبت» for the given local date.</summary>
        public static string DayName(DateTime localDate) => DayNames[(int)localDate.DayOfWeek];

        /// <summary>«السبت 12/09/2026» — the day name and the date, Western digits.</summary>
        public static string DayAndDate(DateTime localDate, string date) => $"{DayName(localDate)} {date}";
    }

    /// <summary>Counted Arabic phrases the daily screens need beyond the ones Core already owns.</summary>
    public static class Counts
    {
        /// <summary>«سجل واحد» / «سجلان» / «5 سجلات» / «28 سجلًا».</summary>
        public static string Records(int count, string number) => count switch
        {
            0 => "لا سجلات",
            1 => "سجل واحد",
            2 => "سجلان",
            >= 3 and <= 10 => $"{number} سجلات",
            _ => $"{number} سجلًا",
        };

        /// <summary>«دقيقة واحدة» / «دقيقتان» / «5 دقائق» / «45 دقيقة».</summary>
        public static string Minutes(int count, string number) => count switch
        {
            0 => "أقل من دقيقة",
            1 => "دقيقة واحدة",
            2 => "دقيقتان",
            >= 3 and <= 10 => $"{number} دقائق",
            _ => $"{number} دقيقة",
        };
    }

    /// <summary>Shell chrome that the daily screens share (the quick-entry button, refresh, closed session).</summary>
    public static class Shell
    {
        /// <summary>Tooltip of the quick-entry button, naming what it can create.</summary>
        public const string QuickEntryTooltip = "إدخال سريع لمهمة أو ملاحظة أو مصروف أو موعد";

        /// <summary>Tooltip of the «تحديث» button.</summary>
        public const string RefreshTooltip = "إعادة قراءة الأرقام والقوائم";

        /// <summary>Shown in place of a screen's content while the session is locked or closed.</summary>
        public const string SessionClosedTitle = "الجلسة مغلقة";

        public const string SessionClosedDesc = "افتح الجلسة من جديد لعرض هذه الشاشة";

        /// <summary>Shown when a screen's own read failed.</summary>
        public const string ReadFailedTitle = "تعذّر عرض هذه الشاشة";

        public const string ReadFailedDesc = "لم يُفقد شيء من بياناتك، حاول مجددًا";

        // The component gallery's entry for the pieces this package added to the design system.
        public const string GalleryDailyTitle = "مكوّنات الهيكل اليومي";

        public const string GalleryDailySub = "بطاقة مؤشر تفتح قائمتها، شريط منسدل للإشعارات، شريط اختيار بأيقونات، وشريط حفظ فوري مع «تراجع»";

        public const string GalleryKpiTooltip = "مثال على بطاقة مؤشر تفتح قائمتها";

        public const string GalleryNotificationTooltip = "مثال على سطر إشعار يفتح سجله";

        public const string GalleryNotificationTitle = "اجتماع اللجنة الفنية يبدأ بعد نصف ساعة";

        public const string GalleryNotificationBody = "قاعة الاجتماعات الرئيسية · 6 مشاركين";

        public const string GalleryNotificationTime = "قبل 10 دقائق";

        public const string GalleryNotificationKind = "اجتماع";

        public const string GalleryUndoShow = "عرض شريط التراجع";

        public const string GalleryUndoShowTooltip = "مثال على شريط الحفظ الفوري مع «تراجع»";

        public const string GalleryUndoText = "حُفظت المهمة: متابعة تقرير اللجنة";
    }

    /// <summary>W08 — the attention center's live text, on top of <see cref="AttentionCenter"/>.</summary>
    public static class Attention
    {
        /// <summary>Subtitle: «ما يحتاج انتباهك اليوم — الأحد 12/09/2026، مكتب المتابعة».</summary>
        public static string Sub(string dayAndDate, string? office) =>
            string.IsNullOrWhiteSpace(office)
                ? $"ما يحتاج انتباهك اليوم — {dayAndDate}"
                : $"ما يحتاج انتباهك اليوم — {dayAndDate}، {office}";

        /// <summary>Tooltip on a KPI card, naming the tab it opens.</summary>
        public static string KpiTooltip(string tab) => $"فتح قائمة «{tab}»";

        /// <summary>Monthly-report banner: «بقي 3 أيام على نهاية دورة أكتوبر 2026».</summary>
        public static string CycleDaysLeft(string days, string cycleName) => $"بقي {days} على نهاية {cycleName}";

        /// <summary>Monthly-report banner on the closing day itself.</summary>
        public static string CycleEndsToday(string cycleName) => $"اليوم آخر أيام {cycleName}";

        /// <summary>Monthly-report banner after the cycle closed and before the report is issued.</summary>
        public static string CycleClosed(string cycleName) => $"انتهت {cycleName} ولم يصدر تقريرها بعد";

        /// <summary>Readiness figure on the banner: «الجاهزية 80 بالمئة».</summary>
        public static string Readiness(string percent) => $"الجاهزية {percent} بالمئة";

        /// <summary>Progress label under the banner: «8 من 10 بنود مكتملة».</summary>
        public static string ReadinessItems(string done, string total) => $"{done} من {total} بنود مكتملة";

        /// <summary>Said instead of the readiness figure while no report checklist exists yet.</summary>
        public const string ReadinessUnknown = "لم تُحسب بنود الجاهزية بعد";

        public const string NothingToday = "لا شيء يحتاج إجراءً اليوم";

        public const string NothingTodayDesc = "كل ما في المكتب ضمن مواعيده";

        public const string NoExpenses = "لا مصروفات هاتف بانتظار التأكيد";

        public const string NoExpensesDesc = "ما يصل من الهاتف يظهر هنا قبل اعتماده";

        public const string NoMeetings = "لا اجتماعات اليوم";

        /// <summary>Second line of a meeting row: «قاعة الاجتماعات · 60 دقيقة».</summary>
        public static string MeetingMeta(string minutes, string? location) =>
            string.IsNullOrWhiteSpace(location) ? $"{minutes} دقيقة" : $"{location} · {minutes} دقيقة";

        public const string ExpenseConfirmed = "تم تأكيد المصروف";

        public const string ExpenseRejected = "تم رفض المصروف";

        public const string ExpenseAlreadyDecided = "سبق البتّ في هذا المصروف";

        public const string ExpenseConfirmTooltip = "اعتماد المصروف وقيده على الدورة الحالية";

        public const string ExpenseRejectTooltip = "رفض المصروف دون قيده";

        public const string Refreshed = "حُدّثت الأرقام";

        public const string TabDecisions = "قرارات";

        public const string TabCases = "قضايا";

        public const string TabExpenses = "مصروفات";

        // The side column's two status cards read as field/value rows, the way W08 draws them,
        // rather than as one sentence: the state first, then when it last happened.
        public const string StateLabel = "الحالة";

        public const string TimeLabel = "الوقت";

        public const string SyncOk = "ناجحة";

        public const string SyncNever = "لم تتم بعد";

        public const string BackupLastLabel = "آخر نسخة";

        public const string BackupOk = "ناجحة";

        public const string BackupOverdue = "متأخرة";

        public const string BackupNever = "لا توجد";

        /// <summary>Tooltip of the refresh control in the «آخر مزامنة» card's own header.</summary>
        public const string SyncRefreshTooltip = "إعادة قراءة حالة المزامنة";

        /// <summary>The link out of the meetings card, to the calendar.</summary>
        public const string OpenCalendar = "التقويم";

        public const string OpenCalendarTooltip = "فتح التقويم على اليوم";
    }

    /// <summary>W09 — «مركز الانتباه — المتأخر».</summary>
    public static class Overdue
    {
        public const string Title = "مركز الانتباه";

        /// <summary>«كل ما تجاوز موعده أو اقترب منه في المكتب — 28 بانتظار انتباهك، الأحد 12/09/2026».</summary>
        public static string Sub(string count, string dayAndDate) =>
            $"كل ما تجاوز موعده أو اقترب منه في المكتب — {count} بانتظار انتباهك، {dayAndDate}";

        public const string TabLate = "متأخر";

        public const string TabNear = "قرب الاستحقاق";

        public const string TabStale = "راكد";

        public const string TabPending = "بانتظار تأكيدي";

        public const string SearchPlaceholder = "ابحث في هذه القائمة بالموضوع أو الرقم أو الجهة...";

        /// <summary>The header button that opens the first record of the open tab.</summary>
        public const string HandleNext = "معالجة التالي";

        public const string ColType = "النوع";

        public const string ColTitle = "العنوان / الرقم";

        public const string ColParty = "الجهة";

        public const string ColDue = "الاستحقاق";

        public const string ColDaysLate = "التأخر بالأيام";

        public const string ColNextStep = "الإجراء التالي";

        public const string ColAction = "المعالجة";

        public const string Handle = "معالجة";

        public static string HandleTooltip(string title) => $"فتح سجل «{title}»";

        /// <summary>
        /// Due column when the row is already late: «متأخر 4 أيام». After «متأخر» the dual takes the
        /// OBLIQUE form «يومين»; the standalone phrase carries the nominative «يومان», which is right
        /// on its own (W09's «التأخر بالأيام» column) and wrong here.
        /// </summary>
        public static string LateByDays(int days, string daysPhrase) =>
            $"متأخر {(days == 2 ? DualDaysOblique : daysPhrase)}";

        private const string DualDaysOblique = "يومين";

        public const string DueToday = "اليوم";

        public const string DueTomorrow = "غدًا";

        public const string NoDueDate = "بلا موعد";

        public const string NoNextStep = "لم يُحدَّد بعد";

        public const string EmptyLateTitle = "لا شيء متأخر";

        public const string EmptyNearTitle = "لا شيء يقترب من موعده";

        public const string EmptyStaleTitle = "لا شيء راكد";

        public const string EmptyPendingTitle = "لا شيء بانتظار تأكيدك";

        public const string EmptyDesc = "ستظهر السجلات هنا فور دخولها هذه الحالة";

        // ---------------------------------------------------------------------------------------
        // The second control strip under the tabs, as the export draws it: a type filter, a sort
        // toggle, a one-line summary of how bad the list is, and the list's own export.
        // ---------------------------------------------------------------------------------------

        /// <summary>The type filter's label and its «all types» option.</summary>
        public const string Filter = "تصفية";

        public const string FilterTooltip = "حصر القائمة بنوع واحد من السجلات";

        public const string FilterAllTypes = "كل الأنواع";

        /// <summary>The sort toggle: pressed, the most overdue rows come first.</summary>
        public const string SortMostLate = "الأكثر تأخرًا أولًا";

        public const string SortMostLateTooltip = "ترتيب القائمة تنازليًا بعدد أيام التأخر";

        /// <summary>«أطول تأخر: 18 يومًا · 4 سجلات تجاوزت أسبوعين».</summary>
        public static string Summary(string longestDays, string overTwoWeeks) =>
            $"أطول تأخر: {longestDays} · {overTwoWeeks} تجاوزت أسبوعين";

        /// <summary>Said instead of the summary while nothing in the open tab is late at all.</summary>
        public const string SummaryNoDelay = "لا تأخر في هذه القائمة";

        public const string Export = "تصدير القائمة";

        public const string ExportTooltip = "حفظ سطور هذه القائمة في ملف";

        /// <summary>The name the save dialog suggests for the list: «المتأخر-2026-09-17-1024.txt».</summary>
        public static string ExportFileName(string tabLabel, string stamp) => $"{tabLabel}-{stamp}.txt";

        public const string Exported = "حُفظت القائمة";

        public const string ExportFailed = "تعذّر حفظ القائمة";

        public const string ExportCancelled = "أُلغي حفظ القائمة";

        /// <summary>Heading line of the exported file: the tab, the office and when it was taken.</summary>
        public static string ExportHeading(string tabLabel, string when) => $"{tabLabel} — {when}";

        /// <summary>«عرض 1–8 من 12 سجلًا متأخرًا» — the footer, with the record noun of the open tab.</summary>
        public static string ShowingRange(int from, int to, string records) =>
            $"عرض {from}–{to} من {records}";

        /// <summary>The counted noun the footer of each tab ends with.</summary>
        public static string RecordsLate(int count, string number) => count switch
        {
            0 => "لا سجلات متأخرة",
            1 => "سجل واحد متأخر",
            2 => "سجلين متأخرين",
            >= 3 and <= 10 => $"{number} سجلات متأخرة",
            _ => $"{number} سجلًا متأخرًا",
        };

        public static string RecordsNear(int count, string number) => count switch
        {
            0 => "لا سجلات قاربت موعدها",
            1 => "سجل واحد قارب موعده",
            2 => "سجلين قاربا موعدهما",
            >= 3 and <= 10 => $"{number} سجلات قاربت موعدها",
            _ => $"{number} سجلًا قارب موعده",
        };

        public static string RecordsStale(int count, string number) => count switch
        {
            0 => "لا سجلات راكدة",
            1 => "سجل واحد راكد",
            2 => "سجلين راكدين",
            >= 3 and <= 10 => $"{number} سجلات راكدة",
            _ => $"{number} سجلًا راكدًا",
        };

        public static string RecordsPending(int count, string number) => count switch
        {
            0 => "لا سجلات بانتظار التأكيد",
            1 => "سجل واحد بانتظار التأكيد",
            2 => "سجلين بانتظار التأكيد",
            >= 3 and <= 10 => $"{number} سجلات بانتظار التأكيد",
            _ => $"{number} سجلًا بانتظار التأكيد",
        };
    }

    /// <summary>W10 — the bell's notification panel.</summary>
    public static class Notifications
    {
        public const string Title = "الإشعارات";

        public const string TabAll = "الكل";

        public const string TabUnread = "غير المقروء";

        public const string MarkAllRead = "تحديد الكل كمقروء";

        public const string MarkAllReadTooltip = "تعليم كل الإشعارات كمقروءة";

        public const string SettingsTooltip = "إعدادات الإشعارات";

        public const string ViewAll = "عرض كل الإشعارات";

        public const string ViewAllTooltip = "فتح قائمة الإشعارات كاملة";

        // No day headings: the panel draws one continuous list, as the export does. The service's
        // day groups still decide the ORDER of that list, but they are never named on screen, so
        // there are no «اليوم»/«أمس»/«أقدم» headings to spell here.

        public const string EmptyTitle = "لا إشعارات";

        public const string EmptyDesc = "ما يستجد من تذكيرات ومراسلات يظهر هنا";

        public const string EmptyUnreadTitle = "لا إشعارات غير مقروءة";

        public const string EmptyUnreadDesc = "قرأت كل ما وصلك";

        public const string AllMarkedRead = "حُدّدت كل الإشعارات كمقروءة";

        /// <summary>Accessible name of one notification row, which opens its record.</summary>
        public static string OpenTooltip(string title) => $"فتح «{title}»";

        public const string CloseTooltip = "إغلاق لوحة الإشعارات";

        /// <summary>The kind of one notification, in words, on the row's first line.</summary>
        /// <param name="kind">One of <c>Wakeel.Core.Services.NotificationKinds</c>.</param>
        public static string KindLabel(string kind) => kind switch
        {
            "meeting" => "اجتماع",
            "appointment" => "موعد",
            "task_due" => "مهمة",
            "commitment_due" => "التزام",
            "financial_cycle" => "الدورة المالية",
            "backup" => "نسخ احتياطي",
            "clock" => "الساعة",
            "phone_expense" => "مصروف هاتف",
            "sync" => "مزامنة",
            _ => "تنبيه",
        };
    }

    /// <summary>W11 — the clock banner's own actions, beside <see cref="ClockBanner"/>.</summary>
    public static class Clock
    {
        public const string Fix = "تصحيح الساعة";

        public const string FixTooltip = "فتح إعدادات الوقت والتاريخ في ويندوز";

        public const string Dismiss = "تجاهل مؤقتًا";

        public const string DismissTooltip = "يُخفي التنبيه حتى إعادة تشغيل التطبيق، وتبقى الأرقام الرسمية موقوفة";

        public const string FixOpened = "فُتحت إعدادات الوقت والتاريخ";

        public const string FixFailed = "تعذّر فتح إعدادات الوقت والتاريخ من هنا";
    }

    /// <summary>W12 — the health center.</summary>
    public static class Health
    {
        public const string Title = "مركز الصحة";

        /// <summary>«حالة مكوّنات النظام وأجهزة المكتب — آخر فحص شامل … · يتكرر كل 15 دقيقة».</summary>
        public static string Sub(string lastCheck, string minutes) =>
            $"حالة مكوّنات النظام وأجهزة المكتب — آخر فحص شامل {lastCheck} · يتكرر كل {minutes} دقيقة";

        public const string CheckNow = "فحص شامل الآن";

        public const string CheckNowTooltip = "إعادة تشغيل كل الفحوص الآن";

        public const string Export = "تصدير تقرير الصحة";

        public const string ExportTooltip = "حفظ ملخص الحالة في ملف نصي";

        /// <summary>
        /// The name the save dialog suggests: «تقرير-الصحة-2026-09-17-1024.txt». The stamp keeps
        /// Western digits and dashes so the file sorts by date in any folder.
        /// </summary>
        public static string ExportFileName(string stamp) => $"تقرير-الصحة-{stamp}.txt";

        public const string Exported = "حُفظ تقرير الصحة";

        public const string ExportFailed = "تعذّر حفظ تقرير الصحة";

        public const string ExportCancelled = "أُلغي حفظ التقرير";

        public const string Checked = "اكتمل الفحص الشامل";

        public const string TabAll = "الكل";

        public const string TabWarnings = "تنبيهات";

        public const string TabErrors = "أعطال";

        public const string TabOk = "سليم";

        public const string SearchPlaceholder = "ابحث في المكوّنات...";

        public const string StatusOk = "سليم";

        public const string StatusWarning = "تحذير";

        public const string StatusError = "عطل";

        /// <summary>Per-card footer: «آخر فحص 10:24».</summary>
        public static string LastCheck(string time) => $"آخر فحص {time}";

        public const string NoMatches = "لا مكوّن يطابق البحث";

        public const string NoMatchesDesc = "جرّب كلمة أخرى أو تبويبًا آخر";

        public const string ActionRestoreBackup = "استعادة نسخة";

        public const string ActionTakeBackup = "نسخ الآن";

        public const string ActionOpenVault = "إدارة المفاتيح";

        public const string ActionOpenModelsFolder = "إعادة الفهرسة";

        public const string ActionFixClock = "مزامنة الوقت";

        public const string ActionFreeSpace = "تنظيف المؤقتات";

        public const string ActionOpenSync = "مزامنة الآن";

        public const string ActionInstallWord = "تفعيل الوظيفة الإضافية";

        public const string ActionConnectScanner = "إعادة الاكتشاف";

        public const string ActionReinstallRuntime = "إعادة التجهيز";

        /// <summary>Said when a card's action has no screen behind it yet.</summary>
        public const string ActionNotReadyYet = "هذا الإجراء يُفتح من شاشته الخاصة لاحقًا";
    }

    /// <summary>
    /// W91 — the reference sheet of every standard state card. The four states the design system's
    /// own components already use are aliased from <see cref="States"/>, so the sheet and the
    /// components can never drift apart; the other nine live here.
    /// </summary>
    public static class StandardStates
    {
        public const string Title = "لوحة الحالات القياسية";

        public const string RefreshTooltip = "إعادة عرض بطاقات الحالات";

        public const string Sub = "مرجع الحالات الموحّدة المستخدمة في شاشات النظام";

        public const string LoadingTitle = States.LoadingTitle;

        public const string LoadingDesc = States.LoadingDesc;

        public const string LoadingAction = "إلغاء";

        public const string EmptyTitle = States.NoDataTitle;

        public const string EmptyDesc = States.NoDataDesc;

        public const string EmptyAction = Buttons.AddNew;

        public const string NoResultsTitle = States.NoResultsTitle;

        public const string NoResultsDesc = States.NoResultsDesc;

        public const string NoResultsAction = "مسح التصفية";

        public const string FileBrokenTitle = "تعذّر فتح الملف";

        public const string FileBrokenDesc = "الملف تالف أو بصيغة غير مدعومة";

        public const string FileBrokenAction = Buttons.Retry;

        public const string VaultTitle = "الخزنة غير متاحة";

        public const string VaultDesc = "تحقّق من توصيل وسيط الخزنة الآمن";

        public const string VaultAction = "إعادة الفحص";

        public const string DatabaseTitle = "القاعدة غير متاحة";

        public const string DatabaseDesc = "تعذّر الوصول إلى سجلات المكتب المشفّرة";

        public const string DatabaseAction = Buttons.Retry;

        public const string FailedTitle = States.ErrorTitle;

        public const string FailedDesc = States.ErrorDesc;

        public const string FailedAction = Buttons.Retry;

        public const string RecipientOnlyTitle = "للمستلم فقط";

        public const string RecipientOnlyDesc = "لا تملك صلاحية عرض هذا العنصر السري";

        public const string RecipientOnlyAction = "طلب الاطلاع";

        public const string SuccessTitle = "تم الحفظ بنجاح";

        public const string SuccessDesc = "حُفظت التغييرات في السجل";

        public const string SuccessAction = "متابعة";

        public const string SessionEndedTitle = "انتهت الجلسة";

        public const string SessionEndedDesc = "بسبب عدم النشاط، سجّل الدخول مجددًا";

        public const string SessionEndedAction = "تسجيل الدخول";

        public const string BadPackageTitle = "حزمة غير صالحة";

        public const string BadPackageDesc = "تعذّر التحقق من الحزمة أو أنها تالفة";

        public const string BadPackageAction = "اختيار حزمة أخرى";

        public const string RecordChangedTitle = "تغيّر هذا السجل";

        public const string RecordChangedDesc = "وصلت نسخة أحدث ضمن حزمة مزامنة، حدّث العرض";

        public const string RecordChangedAction = "تحديث العرض";

        public const string WordTitle = "برنامج Word غير متاح";

        public const string WordDesc = "بدلًا من ذلك تُصدَّر الوثيقة بصيغة محمولة";

        public const string WordAction = "تصدير محمول";
    }

    /// <summary>W92 — the reference sheet of the standard dialogs.</summary>
    public static class StandardDialogs
    {
        public const string Title = "الحوارات القياسية";

        public const string Sub = "نماذج حوارات التأكيد والتحقق وحل التعارض المعتمدة في نظام الوكيل";

        public const string NormalTitle = "أرشفة المراسلة؟";

        public const string NormalDesc = "تنتقل المراسلة إلى الأرشيف، ويمكنك استرجاعها لاحقًا من قائمة الأرشيف عند الحاجة.";

        public const string NormalConfirm = "أرشفة";

        public const string NormalConfirmTooltip = "نقل المراسلة إلى الأرشيف";

        public const string DangerTitle = "حذف المرفق نهائيًا؟";

        public const string DangerDesc = "يُحذف هذا المرفق من المراسلة ولن تتمكن من استرجاعه.";

        public const string DangerConfirm = "حذف نهائي";

        public const string DangerConfirmTooltip = "إجراء نهائي لا يمكن التراجع عنه";

        public const string ValidationTitle = "تعذّر حفظ المتابعة";

        public const string ValidationDesc = "يوجد حقلان بحاجة إلى تصحيح قبل المتابعة.";

        public const string ValidationFieldDate = "موعد المتابعة التالي";

        public const string ValidationFieldDateError = "يجب اختيار تاريخ للمتابعة";

        public const string ValidationFieldPhone = "رقم هاتف الجهة";

        public const string ValidationFieldPhoneError = "صيغة الرقم غير صحيحة، يلزم 10 أرقام";

        public const string ValidationConfirm = "مراجعة الحقول";

        public const string ValidationConfirmTooltip = "العودة إلى أول حقل يحتاج تصحيحًا";

        public const string ConflictTitle = "تعارض في بيانات مصروف";

        public const string ConflictDesc = "عُدّل المصروف نفسه على جهازين أثناء انقطاع المزامنة، اختر النسخة التي تريد اعتمادها.";

        public const string ConflictMine = "نسختك";

        public const string ConflictIncoming = "النسخة الواردة";

        public const string ConflictKeepMine = "استخدام نسختي";

        public const string ConflictKeepMineTooltip = "الإبقاء على ما سجّلته على هذا الجهاز";

        public const string ConflictTakeIncoming = "استخدام النسخة الواردة";

        public const string ConflictTakeIncomingTooltip = "اعتماد النسخة القادمة مع المزامنة";

        public const string RecordChangedTitle = "تم تحديث هذا السجل";

        public const string RecordChangedDesc = "وصلت نسخة أحدث من هذا السجل ضمن حزمة مزامنة بعد فتحك له. حدّث العرض لرؤية آخر نسخة قبل المتابعة.";

        public const string RecordChangedKeepEditing = "متابعة التعديل";

        public const string RecordChangedKeepEditingTooltip = "يُبقي تعديلك دون أخذ النسخة الأحدث";

        public const string RecordChangedRefresh = "تحديث العرض";

        public const string RecordChangedRefreshTooltip = "عرض آخر نسخة وصلت";

        /// <summary>The consequence strip of the dangerous confirmation.</summary>
        public const string DangerNotice = "إجراء نهائي لا يمكن التراجع عنه";

        /// <summary>The strip that names when the newer version of the record arrived.</summary>
        public static string RecordChangedNotice(string when) => $"وارد: {when}";

        // The figures the reference sheet draws with. They are the fixed sample of DESIGN-GUIDE.md,
        // not live data: W92 is a sheet of shapes, and nothing on it decides anything.
        public const string ConflictSampleIncomingAmount = "480.00 ₪";

        public const string ConflictSampleMineAmount = "450.00 ₪";

        public const string ConflictSampleIncomingWhen = "12/09/2026 · 11:05";

        public const string ConflictSampleMineWhen = "12/09/2026 · 10:24";

        public const string RecordChangedSampleWhen = "12/09/2026 · 05:11";

        public const string ValidationSamplePhone = "05912";

        /// <summary>What the sheet reports after a dialog closes, so the page shows the outcome.</summary>
        public static string Chosen(string choice) => $"اختيارك: {choice}";
    }

    /// <summary>W94 — the quick-entry dialog (Ctrl+N).</summary>
    public static class QuickCapture
    {
        public const string Title = "إدخال سريع";

        public const string Desc = "أضف مهمة أو ملاحظة أو مصروفًا أو موعدًا دون مغادرة الشاشة الحالية";

        public const string Shortcut = "Ctrl+N";

        public const string KindTask = "مهمة";

        public const string KindNote = "ملاحظة";

        public const string KindReportNote = "ملاحظة للتقرير";

        public const string KindExpense = "مصروف";

        public const string KindAppointment = "موعد";

        public const string TaskTitleLabel = "العنوان";

        public const string TaskTitleHint = "اكتب عنوانًا موجزًا وواضحًا";

        public const string TaskTitlePlaceholder = "ماذا يجب أن يُنجز؟";

        public const string TaskDueLabel = "تاريخ الاستحقاق";

        public static string TaskDueHint(string days) => $"افتراضيًا بعد {days} من اليوم";

        public const string TaskPriorityLabel = "الأولوية";

        public const string PriorityLow = "منخفضة";

        public const string PriorityNormal = "عادية";

        public const string PriorityHigh = "عالية";



        public const string AssigneeLabel = "المكلّف (اختياري)";

        public const string AssigneePlaceholder = "اسم الموظف";

        public const string NoteLabel = "نص الملاحظة";

        public const string NotePlaceholder = "اكتب ما تريد تذكّره";

        public const string ReportNoteLabel = "ملاحظة تُدرج في التقرير الشهري";

        public const string ExpenseAmountLabel = "المبلغ";

        public const string ExpenseAmountHint = "بالشيكل، مثال 42.50";

        public const string ExpensePurposeLabel = "البيان";

        public const string ExpensePurposePlaceholder = "على ماذا صُرف المبلغ؟";

        public const string ExpenseCategoryLabel = "البند (اختياري)";

        public const string AppointmentTitleLabel = "عنوان الموعد";

        public const string AppointmentWhenLabel = "التاريخ";

        public const string AppointmentTimeLabel = "الوقت";

        /// <summary>
        /// Placeholder of the appointment's time field. It says what to write in words, the way the
        /// date field beside it does: a format token like «HH:mm» is a technical term in a screen
        /// that is meant to carry none (AGREEMENT item 15).
        /// </summary>
        public const string AppointmentTimePlaceholder = "ساعة:دقيقة";

        public const string AppointmentReminderLabel = "التنبيه قبل (اختياري)";

        public const string ExtraNoteLabel = "ملاحظة (اختياري)";

        public const string ExtraNotePlaceholder = "أضف تفاصيل إضافية إن لزم";

        public const string Save = "حفظ";

        public const string Undo = "تراجع";

        public const string UndoTooltip = "إلغاء ما حُفظ للتو";

        public const string Undone = "تم التراجع";

        public const string UndoExpired = "انتهت مهلة التراجع";

        public const string CloseTooltip = "إغلاق دون حفظ";

        public const string RequiredTitle = "العنوان مطلوب";

        public const string RequiredNote = "نص الملاحظة مطلوب";

        public const string RequiredPurpose = "البيان مطلوب";

        public const string RequiredAmount = "أدخل مبلغًا أكبر من صفر";

        public const string RequiredWhen = "اختر تاريخًا ووقتًا صحيحين";

        public const string SaveFailed = "تعذّر الحفظ، حاول مجددًا";
    }
}
