using System.Globalization;

namespace Wakeel.Core.Services;

/// <summary>
/// The Arabic sentences produced by <c>Wakeel.Core</c> itself — health-center messages,
/// notification titles and bodies, quick-capture confirmations, backup/sync wording.
/// </summary>
/// <remarks>
/// ARCHITECTURE.md §12 puts every UI string in <c>Wakeel.Design/Text/Ar.*.cs</c>; that rule
/// governs the UI layer, which is the only layer that can reference <c>Wakeel.Design</c>.
/// Core cannot: it is referenced BY the design/UI libraries, not the other way round, yet the
/// B2 specification requires Core's own services to produce finished Arabic text (each health
/// card carries "رسالة عربية", the reminder scheduler writes notification rows, quick capture
/// returns a confirmation sentence). Those strings are collected here, in one file, rather than
/// scattered as literals through the services — the same shape <c>Ar.*</c> has — so a reviewer
/// can read every sentence Core can emit in one place and tests can assert against the constants
/// instead of re-typing them.
/// <para>
/// Numbers are always formatted with <see cref="CultureInfo.InvariantCulture"/> so they render
/// as western digits (AGREEMENT item 20) regardless of the thread's culture, and never as
/// Arabic-Indic digits.
/// </para>
/// <para>
/// No string here names a server, a port, the internet, or the database engine, and none carries
/// an error code (AGREEMENT item 15).
/// </para>
/// </remarks>
public static class CoreAr
{
    // ---------------------------------------------------------------------------------------
    // Relative time (AGREEMENT item 20).
    // ---------------------------------------------------------------------------------------

    /// <summary>"الآن" — less than a minute ago.</summary>
    public const string JustNow = "الآن";

    /// <summary>Prefix for a past instant, as in «قبل 10 دقائق».</summary>
    public const string AgoPrefix = "قبل";

    /// <summary>Prefix for a future instant, as in «خلال 10 دقائق».</summary>
    public const string WithinPrefix = "خلال";

    /// <summary>"أمس" — used as «أمس 16:40».</summary>
    public const string Yesterday = "أمس";

    /// <summary>"غدًا" — used as «غدًا 09:00».</summary>
    public const string Tomorrow = "غدًا";

    // ---------------------------------------------------------------------------------------
    // Attention center.
    // ---------------------------------------------------------------------------------------

    public const string AttentionLate = "متأخر";
    public const string AttentionNear = "قريب الاستحقاق";
    public const string AttentionStale = "راكد";
    public const string AttentionPendingConfirmation = "بانتظار تأكيدي";

    public const string KindCorrespondenceIn = "وارد";
    public const string KindCorrespondenceOut = "صادر";
    public const string KindTask = "مهمة";
    public const string KindCommitment = "التزام";
    public const string KindCase = "قضية";
    public const string KindDecision = "قرار";
    public const string KindPhoneExpense = "مصروف من الهاتف";
    public const string KindMeeting = "اجتماع";
    public const string KindAppointment = "موعد";

    /// <summary>Backup state shown beside the attention center's side column.</summary>
    public const string BackupNever = "لم تُؤخذ نسخة احتياطية بعد";

    public static string BackupTakenAt(string relative) => $"آخر نسخة احتياطية {relative}";

    public const string SyncNever = "لم تتم مزامنة بعد";

    public static string SyncAt(string relative) => $"آخر مزامنة {relative}";

    // ---------------------------------------------------------------------------------------
    // Reminders (notification titles and bodies).
    // ---------------------------------------------------------------------------------------

    public static string MeetingReminderTitle(string meetingTitle) => $"اجتماع قريب: {meetingTitle}";

    public static string MeetingReminderBody(string time, string? location) =>
        location is { Length: > 0 }
            ? $"يبدأ الساعة {time} في {location}"
            : $"يبدأ الساعة {time}";

    public static string AppointmentReminderTitle(string appointmentTitle) => $"موعد قريب: {appointmentTitle}";

    public static string AppointmentReminderBody(string time) => $"يبدأ الساعة {time}";

    public static string TaskDueTitle(string taskTitle) => $"مهمة مستحقة: {taskTitle}";

    public const string TaskDueBody = "حان موعد إنجاز هذه المهمة";

    public static string CommitmentDueTitle(string commitmentTitle) => $"التزام مستحق: {commitmentTitle}";

    public const string CommitmentDueBody = "حان موعد سداد هذا الالتزام";

    /// <summary>AGREEMENT item 52 — N days before the cycle ends.</summary>
    public static string CycleEndingSoonTitle(string cycleName) => $"تقترب نهاية {cycleName}";

    public static string CycleEndingSoonBody(int days, string range) =>
        $"{DaysPhrase(days)} على نهاية الدورة ({range})؛ جهّز التقرير الشهري";

    /// <summary>AGREEMENT item 52 — the day the cycle ends, then daily until the report is issued.</summary>
    public static string CycleEndedTitle(string cycleName) => $"انتهت {cycleName}";

    public const string CycleEndedBody = "جهّز التقرير الشهري";

    public const string BackupReminderTitle = "حان وقت النسخ الاحتياطي";

    public static string BackupReminderBody(int days) => $"مضى {DaysPhrase(days)} على آخر نسخة احتياطية";

    public const string BackupNeverReminderBody = "لم تُؤخذ نسخة احتياطية بعد";

    // ---------------------------------------------------------------------------------------
    // Clock guard (W11, AGREEMENT item 20).
    // ---------------------------------------------------------------------------------------

    public const string ClockBannerTitle = "ساعة الجهاز غير صحيحة";

    public const string ClockBannerBodySuspect = "ساعة الجهاز تختلف عن ساعة جهاز آخر في المكتب";

    public const string ClockBannerBodyBad = "ساعة الجهاز متأخرة عن آخر نشاط مسجَّل";

    public const string ClockBannerNumberingBlocked = "لن تُصدر أرقام رسمية حتى التصحيح";

    // ---------------------------------------------------------------------------------------
    // Health center (W12).
    // ---------------------------------------------------------------------------------------

    public const string HealthAllWell = "كل شيء سليم";

    /// <summary>«هناك N مشكلة تحتاج تدخلًا», agreeing in number (ARCHITECTURE.md §12).</summary>
    public static string HealthProblems(int count) => count switch
    {
        <= 0 => HealthAllWell,
        1 => "هناك مشكلة واحدة تحتاج تدخلًا",
        2 => "هناك مشكلتان تحتاجان تدخلًا",
        >= 3 and <= 10 => $"هناك {N(count)} مشكلات تحتاج تدخلًا",
        _ => $"هناك {N(count)} مشكلة تحتاج تدخلًا",
    };

    public const string HealthTitleDatabase = "قاعدة البيانات";
    public const string HealthTitleVault = "الخزنة";
    public const string HealthTitleWord = "Word";
    public const string HealthTitleScanner = "الماسح الضوئي";
    public const string HealthTitleModels = "نماذج البحث والتعرّف على النص";
    public const string HealthTitleClock = "ساعة الجهاز";
    public const string HealthTitleSpace = "مساحة القرص";
    public const string HealthTitleBackup = "النسخ الاحتياطي";
    public const string HealthTitleSync = "المزامنة";
    public const string HealthTitleRuntime = "مكوّنات العرض";
    public const string HealthTitleVersion = "إصدار البرنامج";

    public static string HealthDatabaseOk(string size) => $"سليمة — الحجم {size}";

    public const string HealthDatabaseDamaged = "القاعدة تحتاج فحصًا؛ استعد آخر نسخة احتياطية";

    public const string HealthDatabaseUnavailable = "تعذّر فحص القاعدة الآن";

    public static string HealthVaultOk(int files) => $"المجلد متاح — {FilesPhrase(files)}";

    public static string HealthVaultCorrupt(int corrupt) => $"{FilesPhrase(corrupt)} لا تطابق سجلاتها";

    public const string HealthVaultMissing = "مجلد الخزنة غير موجود";

    /// <summary>The folder is there but could not be read this time — never claim it is missing.</summary>
    public const string HealthVaultUnreadable = "تعذّر فحص الخزنة الآن";

    public static string HealthWordOk(string version) => $"متوفر — الإصدار {version}";

    public const string HealthWordOkNoVersion = "متوفر";

    public const string HealthWordMissing = "غير متوفر؛ ستُطبع الكتب من البرنامج نفسه";

    public const string HealthScannerOk = "جاهز";

    public const string HealthScannerMissing = "لا يوجد ماسح متصل";

    public static string HealthModelsOk(string activeName) => $"النموذج النشط: {activeName}";

    public const string HealthModelsPreparing = "جارٍ تجهيز النموذج";

    public const string HealthModelsNone = "لا يوجد نموذج في مجلد النماذج؛ البحث النصي يعمل";

    public const string HealthModelsFolderMissing = "مجلد النماذج غير موجود";

    /// <summary>The folder is there but could not be read this time — never claim it is missing.</summary>
    public const string HealthModelsUnreadable = "تعذّر فحص مجلد النماذج الآن";

    public const string HealthClockOk = "الساعة سليمة";

    public const string HealthClockSuspect = "الساعة تحتاج مراجعة";

    public const string HealthClockBad = "الساعة غير صحيحة؛ صحّحها من إعدادات ويندوز";

    public static string HealthSpaceOk(string free, string total) => $"المتاح {free} من {total}";

    public static string HealthSpaceLow(string free, string total) => $"المتاح {free} من {total} فقط؛ فرّغ مساحة";

    public const string HealthSpaceUnknown = "تعذّر قياس المساحة الآن";

    public static string HealthBackupOk(string relative) => $"آخر نسخة احتياطية {relative}";

    public static string HealthBackupOld(string relative) => $"آخر نسخة احتياطية {relative}؛ خذ نسخة جديدة";

    public const string HealthBackupNever = "لم تُؤخذ نسخة احتياطية بعد";

    public static string HealthSyncOk(string details) => details;

    /// <summary>The same per-device detail, followed by the nudge that the devices have drifted apart.</summary>
    public static string HealthSyncOld(string details) => $"{details}؛ آخر مزامنة قديمة";

    public const string HealthSyncNever = "لم تتم مزامنة مع أي جهاز بعد";

    public static string HealthSyncDevice(string deviceName, string relative) => $"{deviceName}: {relative}";

    public const string HealthDevicePc = "الحاسوب";

    public const string HealthDevicePhone = "الهاتف";

    public const string HealthRuntimeOk = "جاهزة";

    public const string HealthRuntimeMissing = "مكوّنات العرض ناقصة؛ أعد تثبيت البرنامج";

    public static string HealthVersionOk(string version, string buildDate) => $"الإصدار {version} — {buildDate}";

    public const string HealthVersionUnknown = "لم يُفعَّل البرنامج بعد";

    // ---------------------------------------------------------------------------------------
    // Health report export (W12, "تصدير تقرير الصحة").
    // ---------------------------------------------------------------------------------------

    public const string HealthReportHeading = "تقرير حالة الوكيل";

    public static string HealthReportGeneratedAt(string at) => $"أُعدَّ في {at}";

    public const string HealthReportStatusOk = "سليم";
    public const string HealthReportStatusWarning = "تحذير";
    public const string HealthReportStatusError = "عطل";

    // ---------------------------------------------------------------------------------------
    // Quick capture (W94).
    // ---------------------------------------------------------------------------------------

    public const string QuickCaptureTaskSaved = "حُفظت المهمة";
    public const string QuickCaptureNoteSaved = "حُفظت الملاحظة";
    public const string QuickCaptureReportNoteSaved = "حُفظت الملاحظة للتقرير الشهري";
    public const string QuickCaptureExpenseSaved = "سُجّل المصروف";
    public const string QuickCaptureAppointmentSaved = "حُفظ الموعد";
    public const string QuickCaptureUndone = "تم التراجع";

    // ---------------------------------------------------------------------------------------
    // Small shared phrases.
    // ---------------------------------------------------------------------------------------

    /// <summary>«يوم واحد» / «يومان» / «5 أيام» / «13 يومًا».</summary>
    public static string DaysPhrase(int days) => days switch
    {
        0 => "أقل من يوم",
        1 => "يوم واحد",
        2 => "يومان",
        >= 3 and <= 10 => $"{N(days)} أيام",
        _ => $"{N(days)} يومًا",
    };

    /// <summary>«ملف واحد» / «ملفان» / «5 ملفات» / «13 ملفًا».</summary>
    public static string FilesPhrase(int files) => files switch
    {
        0 => "لا ملفات",
        1 => "ملف واحد",
        2 => "ملفان",
        >= 3 and <= 10 => $"{N(files)} ملفات",
        _ => $"{N(files)} ملفًا",
    };

    /// <summary>Formats a number with western digits regardless of the thread's culture (AGREEMENT item 20).</summary>
    public static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a size in megabytes/gigabytes with western digits.</summary>
    public static string Size(long bytes)
    {
        const long Kb = 1024;
        const long Mb = Kb * 1024;
        const long Gb = Mb * 1024;
        return bytes switch
        {
            >= Gb => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / Gb:0.#} غيغابايت"),
            >= Mb => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / Mb:0.#} ميغابايت"),
            >= Kb => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / Kb:0.#} كيلوبايت"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} بايت"),
        };
    }
}
