namespace Wakeel.Admin.UI.Text;

/// <summary>A03 — the organisation dashboard. Owned by admin-1.</summary>
public static partial class AdminAr
{
    /// <summary>Everything the dashboard says.</summary>
    public static class Dashboard
    {
        public const string Title = "لوحة الهيئة";
        public const string Sub = "نظرة عامة على الهيكلية والمكاتب والأجهزة وآخر تصدير لملف الإعداد";

        public const string Refresh = "تحديث";
        public const string RefreshTooltip = "إعادة قراءة الأرقام من بيانات الأداة";
        public const string ExportSetup = "تصدير ملف الإعداد";
        public const string ExportSetupTooltip = "بدء معالج تصدير ملف إعداد لمكتب وجهاز";

        /// <summary>
        /// The tooltip of a button whose area this build does not carry yet. A control that looks
        /// live and does nothing when pressed is indistinguishable from a broken tool, so the button
        /// is shown switched off and says in words why it will not open.
        /// </summary>
        public const string NotReadyYetTooltip = "هذا الجزء غير متاح في هذه النسخة بعد.";

        public const string StructureCard = "الهيكلية";
        public const string StructureCardSub = "دوائر / أقسام / وحدات";
        public const string OfficesCard = "المكاتب المفعّلة";
        public const string DevicesCard = "الأجهزة المسجّلة";
        public const string LastExportCard = "آخر تصدير";
        public const string NoExportYet = "لم يُصدَّر ملف إعداد بعد";

        public const string StructureSummary = "الهيكلية بإيجاز";
        public const string StructureEmpty = "لم تُسجَّل دوائر بعد";
        public const string StructureEmptyDesc = "ابدأ من «الهيكلية» بإضافة أول دائرة، ثم أقسامها ووحداتها.";

        public const string Alerts = "تنبيهات";
        public const string AlertsAll = "عرض الكل";
        public const string AlertsAllTooltip = "فتح سجل العمليات لقراءة التنبيهات كاملة";
        public const string AlertsNone = "لا تنبيهات";

        /// <summary>
        /// The sentence under «لا تنبيهات». A tool whose organisation is still empty has no offices
        /// to say anything about, so it must not claim that every office has a setup file — it says
        /// only that nothing is waiting yet, the same care the offices card already takes.
        /// </summary>
        /// <param name="nothingRegisteredYet">Whether the organisation has nothing in it at all yet.</param>
        public static string AlertsNoneDesc(bool nothingRegisteredYet) => nothingRegisteredYet
            ? "لا شيء بانتظار الانتباه بعد."
            : "كل المكاتب لديها ملف إعداد، ولا تغييرات بانتظار التوزيع.";

        public const string AlertOfficeNoSetup = "بانتظار إنشاء ملف الإعداد الأول";
        public const string AlertPendingChanges = "تغييرات بانتظار التوزيع على المكاتب";
        public const string AlertRevokedDevice = "جهاز مُلغى ما زال ضمن المكتب";

        /// <summary>
        /// The line under «تنبيهات»: what the alerts actually are, counted by kind. Only the kinds
        /// that are present are named, so a board whose alerts are all revoked devices never says
        /// anything about setup files.
        /// </summary>
        /// <param name="offices">Offices that have no setup file yet.</param>
        /// <param name="changes">Changes still waiting to be distributed.</param>
        /// <param name="revoked">Revoked devices still listed inside an office.</param>
        public static string AlertsSummary(int offices, int changes, int revoked)
        {
            var parts = new List<string>(3);
            if (offices > 0)
            {
                parts.Add($"{Counting.Offices(offices)} بانتظار ملف الإعداد");
            }

            if (changes > 0)
            {
                parts.Add($"{Counting.Changes(changes)} بانتظار التوزيع");
            }

            if (revoked > 0)
            {
                parts.Add(RevokedDevices(revoked));
            }

            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }

        /// <summary>
        /// «جهاز مُلغى» and its other three shapes. The adjective has to agree with the noun the
        /// number picked: only the three-to-ten shape takes the broken plural «أجهزة مُلغاة», while
        /// eleven and up goes back to the singular noun and so to «جهازًا مُلغى».
        /// </summary>
        /// <param name="count">How many devices are revoked.</param>
        public static string RevokedDevices(int count) => count switch
        {
            1 => "جهاز مُلغى",
            2 => "جهازان مُلغيان",
            >= 3 and <= 10 => $"{count} أجهزة مُلغاة",
            _ => $"{count} جهازًا مُلغى",
        };

        /// <summary>«من ٦» under the activated-offices figure.</summary>
        public static string OfOffices(int total) => $"من {total}";

        /// <summary>The sub-line of the offices card: how many are still waiting for a setup file.</summary>
        /// <param name="waiting">Offices with no setup file yet.</param>
        /// <param name="total">Offices altogether, so a tool with none says so rather than «كل المكاتب مفعّلة».</param>
        public static string OfficesWaiting(int waiting, int total) => total == 0
            ? "لم تُسجَّل مكاتب بعد"
            : waiting == 0
                ? "كل المكاتب مفعّلة"
                : $"{Counting.Offices(waiting)} بانتظار ملف الإعداد";

        /// <summary>The sub-line of the devices card: how many are revoked.</summary>
        public static string DevicesRevoked(int active, int revoked) => revoked == 0
            ? $"{Counting.Devices(active)} نشطة"
            : $"{Counting.Devices(active)} نشطة · {RevokedDevices(revoked)}";

        /// <summary>One line of the structure summary: a department and what is inside it.</summary>
        public static string BranchSummary(int sections, int units, int offices, int waiting)
        {
            var line = $"{Counting.Sections(sections)} · {Counting.Units(units)} · {Counting.Offices(offices)}";
            return waiting == 0 ? line : $"{line} · {Counting.Offices(waiting)} بانتظار ملف الإعداد";
        }

        /// <summary>The «الهيكلية» figure: departments / sections / units.</summary>
        public static string StructureCounts(int departments, int sections, int units) =>
            $"{departments} / {sections} / {units}";
    }
}
