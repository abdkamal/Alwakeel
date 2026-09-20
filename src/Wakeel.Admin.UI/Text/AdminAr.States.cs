namespace Wakeel.Admin.UI.Text;

/// <summary>
/// A12 «حالات وحوارات المدير»: the five moments the tool has to get right, gathered on one screen so
/// they can be read, compared and checked without walking the whole tool to reach each of them.
/// </summary>
public static partial class AdminAr
{
    /// <summary>Everything A12 says, over and above the wording each dialog already owns.</summary>
    public static class States
    {
        public const string Title = "حالات وحوارات المدير";

        public const string Sub =
            "الحوارات والحالات التي تظهر لمدير النظام أثناء الصيانة وإدارة الأجهزة وتصدير الإعداد. "
            + "كل واحدة منها هي الحوار نفسه الذي تعرضه شاشته، لا صورة عنه.";

        public const string Show = "عرض الحالة";
        public const string ShowTooltip = "عرض هذا الحوار كما يظهر في شاشته";
        public const string Close = "إغلاق";

        public const string RevokeCard = "تأكيد إلغاء جهاز";
        public const string RevokeCardDesc = "حوار خطِر يُكتب فيه «تأكيد» بخط اليد قبل أن يعمل زره الأحمر (البند 24).";

        public const string NumberingCard = "تحذير تغيير صيغة الترقيم";
        public const string NumberingCardDesc = "ما يظهر قبل تغيير صيغة الأرقام الرسمية (البند 5).";

        public const string FileErrorCard = "ملف تالف أو كلمة مرور خاطئة";
        public const string FileErrorCardDesc = "ما يظهر حين لا يُفتح ملف اختير للفحص أو للتفعيل.";
        public const string FileErrorTitle = "تعذّر فتح ملف الإعداد";
        public const string FileErrorChoose = "اختيار ملف آخر";
        public const string FileErrorChooseTooltip = "اختيار ملف آخر بدل هذا";

        public const string ExportDoneCard = "نجاح تصدير ملف الإعداد";
        public const string ExportDoneCardDesc = "ما يظهر بعد كتابة ملف الإعداد، مع اسم الملف وطريق مجلده.";

        public const string LogoCard = "شعار كبير جدًا";
        public const string LogoCardDesc = "ما يظهر حين تتجاوز صورة الشعار المختارة الحدّ المسموح.";
        public const string LogoTitle = "تعذّر استعمال هذه الصورة";

        /// <summary>The example device every dialog on this screen speaks about.</summary>
        public const string SampleDevice = "الجهاز 2 · سكرتير المكتب · منى العلي";

        /// <summary>The example office of that device.</summary>
        public const string SampleOffice = "مكتب مدير دائرة المالية · OF-001";

        /// <summary>The example file name the two file dialogs speak about.</summary>
        public const string SampleFile = "OF-001-2-20260917.wakeel-setup";
    }
}
