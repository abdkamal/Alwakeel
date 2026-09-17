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
public static partial class CoreAr
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

    /// <summary>
    /// The database card when the integrity check passes: the finding and the size. The instant of
    /// the run («آخر فحص») is <c>HealthReport.CheckedAt</c> and belongs to the screen, not here.
    /// </summary>
    public static string HealthDatabaseOk(string size) => $"سليمة — الحجم {Isolate(size)}";

    public const string HealthDatabaseDamaged = "القاعدة تحتاج فحصًا؛ استعد آخر نسخة احتياطية";

    public const string HealthDatabaseUnavailable = "تعذّر فحص القاعدة الآن";

    public static string HealthVaultOk(int files) => $"المجلد متاح — {FilesPhrase(files)}";

    public static string HealthVaultCorrupt(int corrupt) => $"{FilesPhrase(corrupt)} لا تطابق سجلاتها";

    public const string HealthVaultMissing = "مجلد الخزنة غير موجود";

    /// <summary>The folder is there but could not be read this time — never claim it is missing.</summary>
    public const string HealthVaultUnreadable = "تعذّر فحص الخزنة الآن";

    public static string HealthWordOk(string version) => $"متوفر — الإصدار {Isolate(version)}";

    public const string HealthWordOkNoVersion = "متوفر";

    public const string HealthWordMissing = "غير متوفر؛ ستُطبع الكتب من البرنامج نفسه";

    public const string HealthScannerOk = "جاهز";

    public const string HealthScannerMissing = "لا يوجد ماسح متصل";

    public static string HealthModelsOk(string activeName) => $"النموذج النشط: {Isolate(activeName)}";

    public const string HealthModelsPreparing = "جارٍ تجهيز النموذج";

    public const string HealthModelsNone = "لا يوجد نموذج في مجلد النماذج؛ البحث النصي يعمل";

    public const string HealthModelsFolderMissing = "مجلد النماذج غير موجود";

    /// <summary>The folder is there but could not be read this time — never claim it is missing.</summary>
    public const string HealthModelsUnreadable = "تعذّر فحص مجلد النماذج الآن";

    public const string HealthClockOk = "الساعة سليمة";

    public const string HealthClockSuspect = "الساعة تحتاج مراجعة";

    public const string HealthClockBad = "الساعة غير صحيحة؛ صحّحها من إعدادات ويندوز";

    public static string HealthSpaceOk(string free, string total) => $"المتاح {Isolate(free)} من {Isolate(total)}";

    public static string HealthSpaceLow(string free, string total) => $"المتاح {Isolate(free)} من {Isolate(total)} فقط؛ فرّغ مساحة";

    public const string HealthSpaceUnknown = "تعذّر قياس المساحة الآن";

    public static string HealthBackupOk(string relative) => $"آخر نسخة احتياطية {Isolate(relative)}";

    public static string HealthBackupOld(string relative) => $"آخر نسخة احتياطية {Isolate(relative)}؛ خذ نسخة جديدة";

    public const string HealthBackupNever = "لم تُؤخذ نسخة احتياطية بعد";

    public static string HealthSyncOk(string details) => details;

    /// <summary>The same per-device detail, followed by the nudge that the devices have drifted apart.</summary>
    public static string HealthSyncOld(string details) => $"{details}؛ آخر مزامنة قديمة";

    public const string HealthSyncNever = "لم تتم مزامنة مع أي جهاز بعد";

    public static string HealthSyncDevice(string deviceName, string relative) => $"{deviceName}: {Isolate(relative)}";

    public const string HealthDevicePc = "الحاسوب";

    public const string HealthDevicePhone = "الهاتف";

    public const string HealthRuntimeOk = "جاهزة";

    public const string HealthRuntimeMissing = "مكوّنات العرض ناقصة؛ أعد تثبيت البرنامج";

    public static string HealthVersionOk(string version, string buildDate) => $"الإصدار {Isolate(version)} — {Isolate(buildDate)}";

    public const string HealthVersionUnknown = "لم يُفعَّل البرنامج بعد";

    // ---------------------------------------------------------------------------------------
    // Health report export (W12, "تصدير تقرير الصحة").
    // ---------------------------------------------------------------------------------------

    public const string HealthReportHeading = "تقرير حالة الوكيل";

    public static string HealthReportGeneratedAt(string at) => $"أُعدَّ في {Isolate(at)}";

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
    // Correspondence (B3-1): state names, step names, readiness checklist, refusals and the
    // follow-up / referral / duplicate / exchange wording. Every sentence here is shown to the
    // user as it stands, so none of them names a table, a file format or an error code.
    // ---------------------------------------------------------------------------------------

    public const string CorrStatusDraft = "مسودة";
    public const string CorrStatusNew = "جديد";
    public const string CorrStatusInProgress = "قيد المتابعة";
    public const string CorrStatusAwaitingReply = "بانتظار رد";
    public const string CorrStatusDone = "منجز";
    public const string CorrStatusClosed = "مغلق";
    public const string CorrStatusCancelled = "ملغى";
    public const string CorrStatusArchived = "مؤرشف";

    public const string CorrConfidentialityPublic = "عادي";
    public const string CorrConfidentialityPrivate = "خاص";
    public const string CorrConfidentialitySecret = "سري";
    public const string CorrConfidentialityTopSecret = "سري للغاية";

    /// <summary>AGREEMENT item 7: this correspondence leaves the office only towards its recipient.</summary>
    public const string CorrRecipientOnly = "للمستلم فقط";

    public const string CorrCounterpartyExternal = "جهة خارجية";
    public const string CorrCounterpartyInternal = "جهة داخلية";

    // Outgoing wizard steps (AGREEMENT item 18).
    public const string CorrStepData = "البيانات";
    public const string CorrStepRecipient = "المستلم";
    public const string CorrStepTemplate = "القالب";
    public const string CorrStepDocument = "المستند";
    public const string CorrStepReview = "المراجعة";
    public const string CorrStepApproval = "الاعتماد";

    // Readiness checklist rows (the outgoing review screen).
    public const string CorrReadySubject = "الموضوع مكتوب";
    public const string CorrReadyRecipient = "المستلم محدَّد";
    public const string CorrReadyTemplate = "القالب مختار";
    public const string CorrReadyDocument = "نص الكتاب أو مستنده جاهز";
    public const string CorrReadyNotApproved = "لم تُعتمد بعد";
    public const string CorrReadySubjectHint = "اكتب موضوع الكتاب";
    public const string CorrReadyRecipientHint = "اختر الجهة أو الوحدة المستلمة";
    public const string CorrReadyTemplateHint = "اختر قالب الكتاب";
    public const string CorrReadyDocumentHint = "اكتب نص الكتاب أو أرفق مستنده";
    public const string CorrReadyNotApprovedHint = "الكتاب معتمد ومرقّم؛ استخدم التصحيح لأي تعديل";

    public static string CorrReadyRemaining(int remaining) => remaining switch
    {
        0 => "الكتاب جاهز للاعتماد",
        1 => "يبقى بند واحد قبل الاعتماد",
        2 => "يبقى بندان قبل الاعتماد",
        >= 3 and <= 10 => $"تبقى {N(remaining)} بنود قبل الاعتماد",
        _ => $"يبقى {N(remaining)} بندًا قبل الاعتماد",
    };

    // Field validation.
    public const string CorrValidationSubjectRequired = "اكتب موضوع المراسلة";
    public const string CorrValidationRecipientRequired = "حدّد الجهة المستلمة";
    public const string CorrValidationPartyRequired = "اختر الجهة الخارجية من الدليل";
    public const string CorrValidationUnitRequired = "اختر الوحدة الداخلية من الهيكلية";
    public const string CorrValidationExternalNumberRequired = "اكتب رقم الجهة على الوارد";
    public const string CorrValidationExternalDateRequired = "اكتب تاريخ الجهة على الوارد";
    public const string CorrValidationExternalDateFuture = "تاريخ الجهة لاحق لتاريخ اليوم";
    public const string CorrValidationDueBeforeToday = "تاريخ الاستحقاق سابق لتاريخ اليوم";
    public const string CorrValidationSubjectTooLong = "الموضوع طويل؛ اختصره";

    /// <summary>Longest subject accepted by the field check; keeps a subject line printable on the letter.</summary>
    public const int CorrSubjectMaxLength = 300;

    // Refusals.
    public const string CorrRefusedNotFound = "لم تعد هذه المراسلة موجودة";
    public const string CorrRefusedNotDraft = "هذه المراسلة لم تعد مسودة";
    public const string CorrRefusedAlreadyNumbered = "هذه المراسلة مرقّمة؛ لا يمكن تعديلها إلا بتصحيح";
    public const string CorrRefusedDeleteNumbered = "المراسلة المرقّمة لا تُحذف؛ ألغِها بسبب ويبقى رقمها";
    public const string CorrRefusedNotNumbered = "المراسلة غير مرقّمة؛ لا يوجد ما يُصحَّح";
    public const string CorrRefusedNotReady = "أكمل بنود الجاهزية قبل الاعتماد";
    public const string CorrRefusedDirectionOut = "هذا الإجراء للصادر فقط";
    public const string CorrRefusedDirectionIn = "هذا الإجراء للوارد فقط";
    public const string CorrRefusedCancelReason = "اكتب سبب الإلغاء";
    public const string CorrRefusedCloseNote = "اكتب ملاحظة الإغلاق";
    public const string CorrRefusedCorrectionReason = "اكتب سبب التصحيح";
    public const string CorrRefusedCorrectionEmpty = "لم تُغيَّر أي قيمة";
    public const string CorrRefusedLinkSelf = "لا يمكن ربط المراسلة بنفسها";
    public const string CorrRefusedCancelDraft = "المسودة غير المرقّمة تُحذف ولا تُلغى";
    public const string CorrRefusedReferralText = "اكتب نص الإحالة";
    public const string CorrRefusedReferralTarget = "حدّد الوحدة أو الشخص المُحال إليه";
    public const string CorrRefusedFollowupStatus = "الإلغاء والإغلاق والأرشفة لها أزرارها الخاصة لأنها تحتاج سببًا أو ملاحظة";

    /// <summary>
    /// A draft is never promoted into an open status by hand: registration (incoming) and approval
    /// (outgoing) are the two operations that move it, and they are the ones that issue its number.
    /// </summary>
    public const string CorrRefusedDraftNeedsNumbering = "المسودة تصبح جديدة بتسجيل الوارد أو اعتماد الصادر، وعندها يصدر رقمها";

    /// <summary>A withdrawn or filed correspondence is not something a unit can still be asked to act on.</summary>
    public const string CorrRefusedReferralStatus = "المراسلة الملغاة أو المؤرشفة لا تُحال إلى أحد";

    public static string CorrRefusedTransition(string fromAr, string toAr) =>
        $"لا يمكن الانتقال من «{fromAr}» إلى «{toAr}»";

    /// <summary>
    /// Refusal for a field that may not be corrected. Takes the field's Arabic label, never its
    /// English key: a technical term has no place in user text (AGREEMENT item 15), and a Latin
    /// run baked into an Arabic sentence can no longer be isolated by the screen (item 55).
    /// </summary>
    public static string CorrCorrectionFieldUnknown(string fieldLabelAr) =>
        $"الحقل «{fieldLabelAr}» لا يقبل التصحيح";

    /// <summary>The same refusal for a field key this build does not recognise at all.</summary>
    public const string CorrCorrectionFieldNotCorrectable = "هذا الحقل لا يقبل التصحيح";

    /// <summary>The value, not the field, is what failed: an unreadable date.</summary>
    public const string CorrCorrectionBadDate = "تاريخ غير مقروء؛ أعد كتابته";

    /// <summary>The value, not the field, is what failed: an unknown confidentiality level.</summary>
    public const string CorrCorrectionBadConfidentiality = "درجة سرية غير معروفة";

    // What each correctable field is called in the correction record and in its refusals.
    public const string CorrFieldSubject = "الموضوع";
    public const string CorrFieldType = "نوع المراسلة";
    public const string CorrFieldConfidentiality = "درجة السرية";
    public const string CorrFieldParty = "الجهة";
    public const string CorrFieldUnit = "الوحدة";
    public const string CorrFieldExternalNumber = "رقم الجهة";
    public const string CorrFieldExternalDate = "تاريخ الجهة";
    public const string CorrFieldDueAt = "تاريخ الاستحقاق";
    public const string CorrFieldNextStep = "الإجراء التالي";
    public const string CorrFieldBodyText = "نص الكتاب";
    public const string CorrFieldCc = "نسخة إلى";
    public const string CorrFieldPartyName = "اسم الجهة";

    /// <summary>What a stored correction calls a field key this build does not recognise.</summary>
    public const string CorrFieldUnnamed = "حقل آخر";

    // Duplicates (AGREEMENT item 14 review).
    public const string CorrDuplicateExact = "هذا الوارد مسجَّل من قبل بالرقم والجهة نفسيهما";
    public const string CorrDuplicateSameNumberOtherParty = "الرقم نفسه مسجَّل لجهة أخرى";
    public const string CorrDuplicateSimilarSubject = "موضوع قريب جدًا من وارد سابق";
    public const string CorrDuplicateNotDuplicate = "ليست مكررة";
    public const string CorrDuplicateConfirmed = "مكررة";

    // Follow-up.
    public const string CorrFollowupCall = "اتصال";
    public const string CorrFollowupVisit = "زيارة";
    public const string CorrFollowupReply = "رد";
    public const string CorrFollowupNote = "ملاحظة";
    public const string CorrFollowupStatus = "تغيير الحالة";

    /// <summary>The «بانتظار رد منذ N أيام» card of the correspondence screen.</summary>
    public static string CorrAwaitingReplySince(int days) => $"بانتظار رد منذ {DaysPhrase(days)}";

    public const string CorrFollowupReminderTitle = "متابعة مراسلة";

    public static string CorrFollowupReminderBody(string subject) => $"حان موعد متابعة: {subject}";

    // Referrals (AGREEMENT item 31).
    public const string CorrReferralOpen = "مفتوحة";
    public const string CorrReferralAnswered = "أُجيبت";
    public const string CorrReferralOverdue = "متأخرة";
    public const string CorrReferralClosed = "مغلقة";
    public const string CorrReferralExtraPage = "لا تكفي المساحة؛ ستُضاف صفحة للإحالة";
    public const string CorrReferralOriginalUnchanged = "الأصل يبقى كما هو؛ الإحالة تظهر على نسخة الطباعة";

    // Exchange (AGREEMENT items 22 and 49).
    public const string CorrExchangeNotApproved = "تُصدَّر المراسلات المعتمدة فقط";
    public const string CorrExchangeNoRecipientKey = "لا يوجد مفتاح للجهة المستلمة؛ حدّثه من ملف الجهة أو الهيكلية";
    public const string CorrExchangeSignatureOk = "التوقيع سليم";
    public const string CorrExchangeSignatureBad = "التوقيع غير سليم؛ لا تسجّل هذا الملف";
    public const string CorrExchangeInternal = "وارد داخلي";
    public const string CorrExchangeExternal = "وارد خارجي";
    public const string CorrExchangeAlreadyImported = "هذا الملف مسجَّل من قبل";
    public const string CorrExchangeCancelled = "المراسلة ملغاة؛ لا تُرسل";

    /// <summary>
    /// A package from another organisation: the signature holds against the certificate the file
    /// carries, but there is no shared root that proves who issued that certificate (AGREEMENT
    /// item 22), so the wording must not promise more than was actually checked.
    /// </summary>
    public const string CorrExchangeSignatureExternal = "التوقيع متماسك، لكن الجهة المرسِلة غير موثّقة لدينا";

    public static string CorrExchangeFrom(string orgName, string officeName) =>
        string.IsNullOrWhiteSpace(officeName) ? $"من {orgName}" : $"من {orgName} — {officeName}";

    // Audit summaries (audit_log.summary_ar). Every value the office typed or the machine produced
    // — a subject, an official number, a file name — is wrapped in Isolate: these sentences are
    // composed once and STORED, so a mixed Arabic/Latin value has to carry its own direction with
    // it (the owner's right-to-left requirement) because no screen can add a <bdi> later.
    public static string CorrAuditDraftCreated(string subject) => $"أُنشئت مسودة: {Isolate(subject)}";

    public static string CorrAuditRegistered(string number) => $"سُجّل وارد برقم {Isolate(number)}";

    public static string CorrAuditApproved(string number) => $"اعتُمد صادر برقم {Isolate(number)}";

    public static string CorrAuditTransition(string fromAr, string toAr) => $"تغيّرت الحالة من «{fromAr}» إلى «{toAr}»";

    public static string CorrAuditCancelled(string number, string reason) =>
        $"أُلغيت المراسلة {Isolate(number)} — {Isolate(reason)}";

    public static string CorrAuditClosed(string note) => $"أُغلقت المراسلة — {Isolate(note)}";

    public const string CorrAuditArchived = "أُرشفت المراسلة";

    public const string CorrAuditRecipientOnlySet = "جُعلت المراسلة للمستلم فقط";

    public const string CorrAuditRecipientOnlyCleared = "رُفع قيد «للمستلم فقط» عن المراسلة";

    public const string CorrAuditLinksChanged = "حُدّثت ارتباطات المراسلة";

    public static string CorrAuditDraftDeleted(string subject) => $"حُذفت مسودة غير مرقّمة: {Isolate(subject)}";

    public static string CorrAuditCorrected(string reason) => $"صُحّحت بيانات مراسلة مرقّمة — {Isolate(reason)}";

    public static string CorrAuditReferred(string toAr) => $"أُحيلت المراسلة إلى {Isolate(toAr)}";

    /// <summary>«أُجيبت الإحالة إلى فلان» / «أُغلقت الإحالة إلى فلان» — one line per referral status change.</summary>
    public static string CorrAuditReferralStatus(string toAr, string statusAr) =>
        $"صارت الإحالة إلى {Isolate(toAr)} «{statusAr}»";

    public static string CorrAuditExported(string fileName) => $"صُدّرت المراسلة إلى ملف {Isolate(fileName)}";

    public static string CorrAuditImported(string number) => $"استُورد وارد وسُجّل برقم {Isolate(number)}";

    // ---------------------------------------------------------------------------------------
    // Small shared phrases.
    // ---------------------------------------------------------------------------------------

    /// <summary>FIRST STRONG ISOLATE — opens an embedded run whose own direction must be kept.</summary>
    private const char FirstStrongIsolate = '⁨';

    /// <summary>POP DIRECTIONAL ISOLATE — closes the run opened by <see cref="FirstStrongIsolate"/>.</summary>
    private const char PopDirectionalIsolate = '⁩';

    /// <summary>
    /// Wraps a value embedded in an Arabic sentence in the Unicode isolate pair, so an official
    /// number («20260916/12001»), a file name, a version or a size keeps its own reading order
    /// instead of being reordered by the surrounding right-to-left text.
    /// </summary>
    /// <remarks>
    /// The sentences that use this are COMPOSED AND STORED — audit-log summaries, the exported
    /// health report — so no screen can wrap the run in a <c>&lt;bdi&gt;</c> element afterwards;
    /// the isolate has to travel with the text. The pair is invisible and never changes what the
    /// reader sees, only the order in which the runs are laid out.
    /// </remarks>
    public static string Isolate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : $"{FirstStrongIsolate}{value}{PopDirectionalIsolate}";

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
