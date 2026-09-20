using System.Globalization;

namespace Wakeel.Admin.UI.Text;

/// <summary>A10 «توزيع التحديثات»: what the offices have not been told, and telling them.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A10 says.</summary>
    public static class Distribution
    {
        public const string Title = "توزيع التحديثات";
        public const string Sub = "ما لم تُبلَّغ به المكاتب بعد، وتوليد ملفات الإعداد المحدّثة دفعة واحدة.";

        public const string WaitingHeading = "تغييرات بانتظار التوزيع";
        public const string WaitingDesc = "كل سطر شيء تغيّر في الأداة ولم يصل بعد إلى الحواسيب التي تحتاجه.";
        public const string ColumnChange = "التغيير";
        public const string ColumnWhen = "منذ";
        public const string ColumnKind = "النوع";
        public const string ColumnReach = "من يصله";
        public const string ColumnWaitingFor = "تغييرات معلّقة";
        public const string ColumnLastExport = "آخر تصدير";
        public const string WaitingChip = "لم يُوزّع";
        public const string NeverExported = "لم يُصدَّر بعد";
        public const string NothingReady = "لا حاسوب جاهز لتوليد ملف له. عالج الأسباب في العمود الأخير أولًا.";

        /// <summary>What kind of thing changed, in the words the screens use for it.</summary>
        public static string KindName(string entityType) => entityType switch
        {
            "org" => "الهيئة",
            "unit" => "الهيكلية",
            "office" => "مكتب",
            "device" => "جهاز",
            _ => "تغيير",
        };

        /// <summary>Which offices a change of that kind reaches.</summary>
        public static string Reach(string entityType) => entityType switch
        {
            "office" or "device" => "مكتب واحد",
            _ => "كل المكاتب",
        };

        /// <summary>How many changes one office is waiting for.</summary>
        public static string WaitingCount(int count) => Bidi(Counting.Changes(count));

        public const string NothingWaiting = "لا شيء بانتظار التوزيع";
        public const string NothingWaitingDesc = "كل ما تغيّر في الأداة وصل إلى المكاتب المعنيّة.";

        public const string TargetsHeading = "المكاتب المتأثرة";
        public const string TargetsDesc = "لكل حاسوب في هذه المكاتب ملف إعداد جديد بكلمة مروره.";
        public const string ColumnOffice = "المكتب";
        public const string ColumnDevices = "الأجهزة";
        public const string ColumnState = "الحالة";
        public const string ReadyChip = "جاهز";
        public const string BlockedChip = "يحتاج إصلاحًا";

        public const string BlockedRevoked = "الجهاز مُلغى";
        public const string BlockedNoAccount = "سجلّ الجهاز ناقص";
        public const string BlockedNoOfficeKey = "لم يُصدر مفتاح المكتب";

        public const string Distribute = "توليد الملفات";
        public const string DistributeTooltip = "توليد ملف إعداد محدّث لكل حاسوب في المكاتب المتأثرة";
        public const string DistributeTitle = "توليد ملفات الإعداد المحدّثة؟";

        public const string DistributeWarning =
            "تُستبدَل مفاتيح كل حاسوب في هذه المكاتب. بعد تسليم الملفات الجديدة لن يعمل أي ملف إعداد أقدم، "
            + "ولا تُعرض كلمات المرور إلا مرة واحدة على هذه الشاشة.";

        public const string DistributeWhatStays = "لا يتغيّر شيء من عمل المكاتب: المراسلات والعُهد والمهام كما هي.";

        public const string ResultHeading = "الملفات التي تولّدت";
        public const string ResultDesc = "انسخ الجدول أو احفظه الآن؛ لن تظهر كلمات المرور بعد مغادرة هذه الشاشة.";
        public const string ColumnFile = "الملف";
        public const string ColumnDevice = "الجهاز";
        public const string ColumnEmployee = "الموظف";
        public const string ColumnPassword = "كلمة المرور";
        public const string CopyAll = "نسخ الجدول";
        public const string CopyAllTooltip = "نسخ أسماء الملفات وكلمات مرورها إلى الحافظة";
        public const string Copied = "نُسخ الجدول.";
        public const string CopyFailed = "تعذّر النسخ. اكتب كلمات المرور كما تظهر.";
        public const string OpenFolder = "فتح مجلد الملفات";
        public const string OpenFolderTooltip = "فتح المجلد الذي حُفظت فيه الملفات";
        public const string FailuresHeading = "لم تُولَّد لهذه الأجهزة";

        public const string DoneToast = "تولّدت ملفات الإعداد المحدّثة.";
        public const string NoneGenerated = "لم يتولّد أي ملف. راجع الأسباب أسفل الجدول.";

        /// <summary>«4 ملفات لمكتبين» — what the batch did, for the log.</summary>
        public static string Log(int fileCount, int officeCount) =>
            Bidi($"وُزّعت التحديثات: {Counting.Files(fileCount)} لـ{Counting.Offices(officeCount)}.");

        /// <summary>«المكتب · الجهاز 2» when a file could not be made.</summary>
        public static string Failure(string officeName, int deviceNo) =>
            Bidi($"{officeName} · الجهاز {Digits(deviceNo)}: تعذّر توليد الملف.");

        /// <summary>The same line for a device that was never eligible.</summary>
        public static string Blocked(string officeName, int deviceNo, string reason) =>
            Bidi($"{officeName} · الجهاز {Digits(deviceNo)}: {reason}.");

        /// <summary>How many computers an office would receive files for.</summary>
        public static string DeviceCount(int count) => Bidi(Counting.Devices(count));

        /// <summary>How long a change has been waiting, in days.</summary>
        public static string Since(DateTimeOffset at, DateTimeOffset now)
        {
            var days = (int)Math.Floor((now - at).TotalDays);
            return Bidi(days switch
            {
                <= 0 => "اليوم",
                1 => "أمس",
                < 30 => $"{Digits(days)} يومًا",
                _ => at.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            });
        }

        /// <summary>One line of the copied table: file, device and password.</summary>
        public static string ClipboardLine(string officeName, int deviceNo, string fileName, string password) =>
            $"{officeName} · الجهاز {Digits(deviceNo)} · {fileName} · {password}";
    }
}
