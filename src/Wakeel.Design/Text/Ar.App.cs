namespace Wakeel.Design.Text;

/// <summary>
/// Every Arabic string specific to the الوكيل desktop app itself (its brand name, sidebar section
/// headers, business-domain vocabulary, the component gallery reference page, the fixed sample
/// dataset, and the W08 Attention Center screen) rather than to the design system's generic
/// component chrome (which lives in <c>Ar.Common.cs</c>). Future packages add their own partial
/// file here (e.g. <c>Ar.FirstRun.cs</c>) per ARCHITECTURE.md §12.
/// </summary>
public static partial class Ar
{
    /// <summary>Product name shown in the sidebar footer and window title.</summary>
    public const string AppName = "الوكيل";

    /// <summary>Sidebar group titles (collapsible headers).</summary>
    public static class Groups
    {
        public const string DailyWork = "العمل اليومي";
        public const string Records = "السجلات";
        public const string FinanceAndReports = "المالية والتقارير";
    }

    /// <summary>Confidentiality levels (AGREEMENT/DATA-MODEL correspondence.confidentiality).</summary>
    public static class Confidentiality
    {
        public const string Public = "عام";
        public const string Private = "خاص";
        public const string Secret = "سري";
        public const string TopSecret = "سري للغاية";
    }

    /// <summary>Correspondence status vocabulary (DESIGN-GUIDE.md "حالات المراسلة"), used by WChip across list/detail screens.</summary>
    public static class CorrespondenceStatus
    {
        public const string New = "جديد";
        public const string InProgress = "قيد المتابعة";
        public const string AwaitingReply = "بانتظار رد";
        public const string Done = "منجز";
        public const string Closed = "مغلق";
        public const string Archived = "مؤرشف";
        public const string Cancelled = "ملغى";
        public const string Draft = "مسودة";
    }

    /// <summary>Component gallery (route /gallery — internal reference page, not part of the delivered product screens).</summary>
    public static class Gallery
    {
        public const string Title = "معرض المكوّنات";
        public const string Sub = "كل مكوّنات نظام تصميم الوكيل وحالاتها، لمراجعة الاتساق البصري";
        public const string SectionTheme = "المظهر";
        public const string SectionThemeSub = "فاتح، داكن، أو حسب النظام";
        public const string ThemeLight = "فاتح";
        public const string ThemeDark = "داكن";
        public const string ThemeSystem = "النظام";

        public const string DisabledLabel = "معطّل";
        public const string SplitActionLabel = "إجراء";
        public const string SplitMenuText = "خيارات إضافية";

        public const string SectionButtons = "الأزرار";
        public const string SectionInputs = "الحقول";
        public const string SectionSelection = "أدوات الاختيار";
        public const string SectionChips = "الشارات والحالات";
        public const string SectionCards = "البطاقات";
        public const string SectionNav = "التنقل الداخلي";
        public const string SectionMisc = "متفرقات";
        public const string SectionTable = "الجدول";
        public const string SectionLists = "قوائم وسجل زمني";
        public const string SectionOverlays = "الحوارات والقوائم المنبثقة والتنبيهات";
        public const string SectionCalendar = "التقويم";

        public const string FieldNameLabel = "الاسم الكامل";
        public const string FieldNameHint = "كما يظهر في السجلات الرسمية";
        public const string FieldPasswordLabel = "كلمة المرور";
        public const string FieldNumberLabel = "المبلغ";
        public const string FieldDateLabel = "التاريخ";
        public const string FieldNotesLabel = "ملاحظات";
        public const string FieldErrorLabel = "رقم الهاتف";
        public const string FieldErrorMessage = "صيغة الرقم غير صحيحة، يلزم 10 أرقام";
        public const string SelectLabel = "المكتب المعني";
        public const string SelectPlaceholder = "اختر مكتبًا";
        public const string SelectOptionPlanning = "مكتب مدير دائرة التخطيط";
        public const string SelectOptionLegal = "الإدارة القانونية";
        public const string SelectOptionFinance = "وزارة المالية";

        public const string CheckboxLabel = "أوافق على الشروط";
        public const string RadioLabelA = "بريد داخلي";
        public const string RadioLabelB = "بريد وارد";
        public const string ToggleLabel = "إشعارات سطح المكتب";
        public const string SegmentedOptionDay = "يوم";
        public const string SegmentedOptionWeek = "أسبوع";
        public const string SegmentedOptionMonth = "شهر";
        public const string ViewOptionList = "قائمة";
        public const string ViewOptionGrid = "شبكة";

        public const string CardTitle = "بطاقة قياسية";
        public const string CardBody = "محتوى حر يوضع داخل البطاقة القياسية.";
        public const string CardInfoTitle = "معلومة";
        public const string CardInfoDesc = "هذا نص إرشادي لا يستدعي إجراءً.";
        public const string CardWarningTitle = "تنبيه";
        public const string CardWarningDesc = "يلزم انتباهك قبل المتابعة.";
        public const string CardDangerTitle = "خطأ";
        public const string CardDangerDesc = "تعذّر إتمام العملية المطلوبة.";
        public const string CardSuccessTitle = "تم بنجاح";
        public const string CardSuccessDesc = "اكتملت العملية دون مشكلات.";

        public const string KpiLabel = "متأخر";
        public const string KpiValue = "12";
        public const string KpiSub = "تجاوز موعد الاستحقاق";

        public const string SectionHeaderTitle = "قسم فرعي";
        public const string SectionHeaderSub = "وصف مختصر لمحتوى هذا القسم";
        public const string SectionHeaderAction = "عرض الكل";

        public const string DocumentFileName = "عقد_2026.pdf";
        public const string DocumentMeta = "1.2 MB · رُفع 12/09/2026";

        public const string TimelineTitle1 = "تسجيل المراسلة الواردة";
        public const string TimelineDesc1 = "استُلمت من مديرية الأشغال العامة وقُيدت";
        public const string TimelineMeta1 = "12/09/2026 · بواسطة أحمد الخطيب";
        public const string TimelineTitle2 = "إحالة إلى دائرة المشاريع والتنفيذ";
        public const string TimelineDesc2 = "لإعداد البيانات المطلوبة";
        public const string TimelineMeta2 = "12/09/2026 · 10:24";

        public const string MenuTriggerLabel = "خيارات";
        public const string ToastTriggerLabel = "إظهار رسالة";
        public const string ToastSampleText = "تم حفظ التغييرات بنجاح";
        public const string ToastSuccessTriggerLabel = "نجاح";
        public const string ToastWarningTriggerLabel = "تحذير";
        public const string ToastWarningText = "راجع البيانات قبل الإرسال";
        public const string ToastDangerTriggerLabel = "خطأ";
        public const string ToastDangerText = "تعذّر حفظ التغييرات";
        public const string DialogTriggerLabel = "فتح حوار";
        public const string DialogNormalTriggerLabel = "فتح حوار عادي";
        public const string DialogNormalTitle = "تحديث العرض؟";
        public const string DialogDangerTriggerLabel = "فتح حوار خطِر";
        public const string DialogSampleDesc = "هذا نص توضيحي داخل حوار قياسي.";

        public const string KvKeySample = "رقم القيد";
        public const string KvValueSample = "20260912/12046";

        public const string BreadcrumbHome = "الرئيسية";
        public const string BreadcrumbSection = "المراسلات";
        public const string BreadcrumbCurrent = "طلب تزويد بيانات";

        public const string StepperStep1 = "البيانات";
        public const string StepperStep2 = "المرفقات";
        public const string StepperStep3 = "المراجعة";
        public const string StepperStep4 = "الاعتماد";

        public const string StateCardTitle = "لا توجد نتائج مطابقة";
        public const string StateCardDesc = "جرّب كلمات بحث أو تصفية مختلفة";

        public const string SearchDemoPlaceholder = "ابحث في هذه القائمة...";

        public const string TableColNumber = "الرقم";
        public const string TableColSubject = "الموضوع";
        public const string TableColStatus = "الحالة";
        public const string TableColDate = "التاريخ";
        public const string TableRowSubject1 = "طلب تزويد بيانات مشروع الطريق الدائري";
        public const string TableRowSubject2 = "دعوة اجتماع اللجنة العليا";
        public const string TableRowSubject3 = "مراجعة مسودة الاتفاقية";

        public const string CalendarEvent1 = "اجتماع اللجنة";
        public const string CalendarEvent2 = "تسليم تقرير";
        public const string CalendarEvent3 = "متابعة";
    }

    /// <summary>Sample data for Pages/W08AttentionCenter.razor, fixed per DESIGN-GUIDE.md (organization/office/user sample).</summary>
    public static class Sample
    {
        public const string OrgName = "هيئة تنمية المناطق الريفية";
        public const string OfficeName = "مكتب مدير دائرة التخطيط";
        public const string UserName = "أحمد الخطيب";
        public const string UserRole = "مدير المكتب";
        public const string DeviceName = "الجهاز 1";
        public const string ComputerName = "PLN-PC-01";
        public const string Today = "12/09/2026";
    }

    /// <summary>Pages/W08AttentionCenter.razor — مركز الانتباه (attention center) placeholder page text.</summary>
    public static class AttentionCenter
    {
        public const string Title = "مركز الانتباه";
        public const string Sub = "ما يحتاج انتباهك اليوم — السبت 12/09/2026، مكتب نابلس";
        public const string Refresh = "تحديث";
        public const string QuickEntry = "إدخال سريع";

        public const string AwaitingConfirmationLabel = "بانتظار تأكيدي";
        public const string AwaitingConfirmationSub = "مصروفات وطلبات تنتظر ردك";
        public const string StaleLabel = "راكد";
        public const string StaleSub = "بلا تحديث منذ 14 يومًا";
        public const string NearDueLabel = "قرب الاستحقاق";
        public const string NearDueSub = "يستحق خلال 3 أيام";
        public const string OverdueLabel = "متأخر";
        public const string OverdueSub = "تجاوز موعد الاستحقاق";

        public const string MonthlyReportBanner = "بقي 3 أيام على نهاية دورة سبتمبر — الجاهزية 80 بالمئة";
        public const string MonthlyReportProgressLabel = "من 10 بنود مكتملة 8";
        public const string OpenMonthlyReport = "فتح التقرير الشهري";

        public const string TodayMeetingsTitle = "اجتماعات اليوم";
        public const string TodayActionTitle = "يحتاج إجراء اليوم";

        /// <summary>Count badge on the "يحتاج إجراء اليوم" WSectionHeader (matches the WTabs "الكل" total).</summary>
        public const int TodayActionCount = 9;

        public const string TabAll = "الكل";
        public const string TabCorrespondence = "مراسلات";
        public const string TabTasks = "مهام";
        public const string TabCommitments = "التزامات";

        public const string PendingExpensesTitle = "مصروفات الهاتف بانتظار التأكيد";
        public const string SyncStatusTitle = "آخر مزامنة";
        public const string BackupStatusTitle = "حالة النسخ الاحتياطي";

        public const string ColType = "النوع";
        public const string ColSubject = "الموضوع";
        public const string ColAssignee = "المكلّف";
        public const string ColDueDate = "الاستحقاق";
        public const string TypeMessage = "مراسلة";
        public const string TypeTask = "مهمة";
        public const string TypeCommitment = "التزام";

        public const string Meeting1Title = "اجتماع اللجنة الفنية — الطريق الدائري";
        public const string Meeting1Time = "10:30";
        public const string Meeting1Meta = "قاعة الاجتماعات · بعد 40 دقيقة";
        public const string Meeting2Title = "لقاء وفد بلدية جنين";
        public const string Meeting2Time = "13:00";
        public const string Meeting2Meta = "مكتب المدير · 45 دقيقة";

        public const string ActionRow1Subject = "طلب تزويد بيانات مشروع الطريق الدائري";
        public const string ActionRow1Meta = "بلدية نابلس • 20260912/12046";
        public const string ActionRow1Assignee = "سامر أبو غزالة";
        public const string ActionRow1Due = "متأخر يومين (10/09)";
        public const string ActionRow2Subject = "الرد على استفسار وزارة الحكم المحلي";
        public const string ActionRow2Meta = "مهمة مرتبطة بالمراسلة 20260908/12019";
        public const string ActionRow2Assignee = "ليلى الشريف";
        public const string ActionRow2Due = "اليوم 16:00";
        public const string ActionRow3Subject = "تسليم كشف مصروفات أغسطس لدائرة المالية";
        public const string ActionRow3Meta = "التزام شهري • دائرة المالية";
        public const string ActionRow3Assignee = "أحمد الخطيب";
        public const string ActionRow3Due = "غدًا 13/09";

        /// <summary>Count badge on the "مصروفات الهاتف بانتظار التأكيد" WSectionHeader.</summary>
        public const int PendingExpensesCount = 6;

        public const string ExpensesSearchPlaceholder = "ابحث باسم الموظف أو الرقم";
        public const string ViewAllExpenses = "عرض كل المصروفات";
        public const string ExpensesColEmployee = "الموظف";
        public const string ExpensesColSubject = "البيان";
        public const string ExpensesColAmount = "المبلغ";
        public const string ExpensesColDate = "التاريخ";
        public const string ExpensesActionsColLabel = "إجراءات الموافقة";
        public const string Expense1Employee = "محمد عوض";
        public const string Expense1Subject = "اتصالات دولية — مناقصة الطريق";
        public const string Expense1Meta = "فاتورة جوال • 0599-482-117";
        public const string Expense1Date = "11/09/2026";
        public const string Expense1Amount = "42.50 ₪";
        public const string Expense2Employee = "رنا حمدان";
        public const string Expense2Subject = "رصيد شحن — هاتف المكتب الأرضي";
        public const string Expense2Meta = "إيصال شحن • 09-2381-440";
        public const string Expense2Date = "10/09/2026";
        public const string Expense2Amount = "25.00 ₪";
        public const string ExpenseConfirm = "تأكيد";
        public const string ExpenseReject = "رفض";

        /// <summary>Visually-hidden label for the trailing chevron column of the "يحتاج إجراء اليوم" table.</summary>
        public const string ActionRowDetailsColLabel = "فتح التفاصيل";

        public const string SyncStatusDetail = "متزامن · منذ 8 دقائق · 38 سجلًا";
        public const string BackupStatusDetail = "11/09/2026 23:00 — ناجحة";
    }
}
