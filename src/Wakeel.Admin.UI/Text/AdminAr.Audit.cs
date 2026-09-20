using System.Globalization;
using Wakeel.Design.Components;

namespace Wakeel.Admin.UI.Text;

/// <summary>A11 «سجل العمليات»: who did what and when, searched and written out.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A11 says.</summary>
    public static class Audit
    {
        public const string Title = "سجل العمليات";
        public const string Sub = "كل ما جرى في الأداة: من فعله ومتى. لا تُكتب في السجل كلمات المرور ولا المفاتيح.";

        public const string SearchLabel = "بحث في السجل";
        public const string SearchPlaceholder = "اكتب اسمًا أو كلمة من السطر";
        public const string ColumnWhen = "متى";
        public const string ColumnWho = "من";
        public const string ColumnWhat = "ماذا";
        public const string ColumnDetail = "الوصف";

        public const string Empty = "لا شيء في السجل بعد";
        public const string EmptyDesc = "يُكتب السطر الأول عند أول إجراء تقوم به في الأداة.";
        public const string NoneFound = "لا سطر يطابق البحث";
        public const string NoneFoundDesc = "جرّب كلمة أقصر، أو امسح حقل البحث لعرض السجل كاملًا.";

        public const string ExportCsv = "تصدير جدول";
        public const string ExportCsvTooltip = "حفظ السجل كجدول يمكن فتحه في برنامج الجداول";
        public const string ExportFileKind = "جدول";
        public const string Exported = "حُفظ الجدول.";
        public const string ExportNotSaved = "لم يُحفظ الجدول.";
        public const string ExportFailed = Errors.CannotWriteFolder;
        public const string NoDialog = "هذا الحاسوب لا يفتح نافذة اختيار الملفات.";
        public const string Refresh = "تحديث";
        public const string RefreshTooltip = "قراءة السجل من جديد";

        /// <summary>The name a table file is offered under.</summary>
        public static string FileName(DateTimeOffset at) =>
            $"سجل-العمليات-{at.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.csv";

        /// <summary>How many rows are on screen, and out of how many.</summary>
        public static string Showing(int shown, int total) =>
            shown == total
                ? Bidi(Counting.Rows(total))
                : Bidi($"{Counting.Rows(shown)} من {Digits(total)}");

        /// <summary>The day of a row.</summary>
        public static string Day(DateTimeOffset at) =>
            at.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        /// <summary>The day and the hour of a row, as the table shows it.</summary>
        public static string Moment(DateTimeOffset at) =>
            at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        /// <summary>
        /// How a row reads at a glance: almost everything in this log is something that was done and
        /// succeeded, so the exceptions are named one by one — the attempt that failed, and the few
        /// actions that leave something switched off or replaced behind them.
        /// </summary>
        public static WChipVariant ActionTone(string action) => action switch
        {
            "admin_sign_in_failed" => WChipVariant.Danger,
            "device_revoked" or "device_removed" or "unit_deleted" or "unit_disabled"
                or "office_key_rotated" or "numbering_changed" or "account_recovered"
                or "device_reissued" => WChipVariant.Warning,
            _ => WChipVariant.Success,
        };

        /// <summary>
        /// The Arabic name of an action. The stable English token is what the database carries and
        /// what a filter matches on; a person never sees it (AGREEMENT item 15), so anything this
        /// build does not have a name for is shown by its own sentence alone.
        /// </summary>
        public static string ActionName(string action) => action switch
        {
            "admin_created" => "إنشاء حساب المدير",
            "admin_signed_in" => "الدخول إلى الأداة",
            "admin_sign_in_failed" => "محاولة دخول غير موفّقة",
            "admin_password_changed" => "تغيير كلمة مرور المدير",
            "org_created" => "إنشاء الهيئة",
            "org_updated" => "تعديل بيانات الهيئة",
            "org_logo_changed" => "تغيير شعار الهيئة",
            "org_logo_cleared" => "إزالة شعار الهيئة",
            "numbering_changed" => "تغيير صيغة الترقيم",
            "report_template_changed" => "تغيير قالب التقرير",
            "report_template_cleared" => "إزالة قالب التقرير",
            "letter_template_changed" => "تغيير قالب المراسلة",
            "letter_template_cleared" => "إزالة قالب المراسلة",
            "unit_added" => "إضافة عنصر إلى الهيكلية",
            "unit_updated" => "تعديل عنصر في الهيكلية",
            "unit_moved" => "نقل عنصر في الهيكلية",
            "unit_disabled" => "إيقاف عنصر من الهيكلية",
            "unit_enabled" => "إعادة تشغيل عنصر",
            "unit_deleted" => "حذف عنصر من الهيكلية",
            "office_set" => "تعليم مكتب",
            "office_cleared" => "إلغاء تعليم مكتب",
            "device_added" => "تسجيل جهاز",
            "device_updated" => "تعديل جهاز",
            "device_removed" => "حذف جهاز",
            "device_activated" => "تفعيل حساب جهاز",
            "device_reissued" => "إعادة إصدار مفاتيح جهاز",
            "device_revoked" => "إلغاء جهاز",
            "office_key_issued" => "إصدار مفتاح مكتب",
            "office_key_rotated" => "تدوير مفتاح مكتب",
            "setup_exported" => "تصدير ملف إعداد",
            "changes_distributed" => "توزيع التحديثات",
            "maintenance_file_opened" => "فتح ملف للفحص",
            "maintenance_folder_opened" => "فحص مجلد نسخة",
            "account_recovered" => "استرداد حساب",
            _ => "إجراء في الأداة",
        };
    }
}
