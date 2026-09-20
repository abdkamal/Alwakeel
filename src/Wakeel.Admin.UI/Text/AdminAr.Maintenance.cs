using System.Globalization;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Text;

/// <summary>A09 «الصيانة»: opening a copy of الوكيل, opening a correspondence, giving an account back.</summary>
public static partial class AdminAr
{
    /// <summary>Everything A09 says.</summary>
    public static class Maintenance
    {
        public const string Title = "الصيانة";
        public const string Sub = "فتح نسخة من الوكيل أو مراسلة بمفتاح الهيئة، واسترداد حساب موظف.";

        public const string OpenHeading = "فتح نسخة أو ملف";

        public const string OpenDesc =
            "تفتح الأداة ملفات الوكيل بمفتاح الهيئة للعرض والتحقق فقط، ولا تُغيّر شيئًا فيما تفتحه.";

        public const string ChooseFolder = "فتح مجلد نسخة";
        public const string ChooseFolderTooltip = "اختيار مجلد نسخة من الوكيل لفحص ملفاتها";
        public const string ChooseFolderPrompt = "اختر مجلد نسخة الوكيل";
        public const string ChooseBackup = "فتح نسخة احتياطية";
        public const string ChooseBackupTooltip = "اختيار ملف نسخة احتياطية لفحصه";
        public const string ChooseMessage = "فتح مراسلة";
        public const string ChooseMessageTooltip = "اختيار ملف مراسلة لفحصه";
        public const string FileKindLabel = "ملفات الوكيل";
        public const string FolderTitle = "مجلد نسخة من الوكيل";

        public const string NothingOpenYet = "لم يُفتح شيء بعد";
        public const string NothingOpenYetDesc = "اختر مجلد نسخة أو ملفًا من الأزرار أعلاه ليظهر فحصه هنا.";
        public const string ResultHeading = "نتيجة الفحص";
        public const string ItemsHeading = "محتويات الملف";
        public const string ColumnItem = "العنصر";
        public const string ColumnSize = "الحجم";
        public const string ColumnCheck = "الفحص";
        public const string ColumnResult = "النتيجة";
        public const string NoItems = "لا عناصر في هذا الملف.";
        public const string ResultOk = "كل الفحوص سليمة.";
        public const string ResultProblem = "ظهرت مشكلة في الفحص. راجع الأسطر المعلّمة.";

        public const string CheckKind = "نوع الملف";
        public const string CheckSignature = "التوقيع";
        public const string CheckOrganisation = "الهيئة";
        public const string CheckProducer = "الجهاز المُنتِج";
        public const string CheckCreated = "تاريخ الإنشاء";
        public const string CheckContent = "المحتوى";
        public const string CheckFileCount = "عدد ملفات الوكيل";
        public const string CheckSound = "سليمة";
        public const string CheckDamaged = "تعذّر فحصها";
        public const string SignatureOk = "سليم وموقّع بمفتاح الهيئة";
        public const string ContentTooBig = "المحتوى كبير، وعُرضت قائمته دون فتحه.";
        public const string ContentUnreadable = "تعذّر فتح المحتوى بمفتاح الهيئة.";

        // ── Recovering an account ───────────────────────────────────────────────────────────────
        public const string RecoverHeading = "استرداد حساب موظف";

        public const string RecoverDesc =
            "للموظف الذي نسي كلمة مروره وضاعت ورقة استرداده: يُصدَر لحاسوبه ملف إعداد استرداد، "
            + "ويختار كلمة مرور جديدة وورقة استرداد جديدة عند فتحه.";

        public const string RecoverOfficeLabel = "المكتب";
        public const string RecoverDeviceLabel = "الجهاز";
        public const string RecoverButton = "إصدار ملف استرداد";
        public const string RecoverButtonTooltip = "إصدار ملف إعداد استرداد لهذا الجهاز";
        public const string RecoverTitle = "إصدار ملف استرداد لهذا الجهاز؟";

        public const string RecoverWarning =
            "تُستبدَل مفاتيح هذا الحاسوب. بعد تطبيق الملف الجديد لن يعمل أي ملف إعداد أقدم له، "
            + "ولن يفتح أحد حسابه بكلمة المرور القديمة.";

        public const string RecoverWhatStays = "تبقى مراسلات هذا الحاسوب وعُهده كما هي في المكتب.";
        public const string RecoverDone = "أُصدر ملف الاسترداد. انقله مع كلمة مروره إلى صاحب الحاسوب.";
        public const string RecoverNoDevices = "لا أجهزة يمكن استردادها";
        public const string RecoverNoDevicesDesc = "سجّل جهازًا في «المكاتب والأجهزة» ثم عد إلى هنا.";

        // ── What this screen has done lately ────────────────────────────────────────────────────
        public const string RecentHeading = "آخر أعمال الصيانة";
        public const string RecentDesc = "ما فُتح وما استُرد من هذه الشاشة. السجل الكامل في «السجل».";
        public const string RecentEmpty = "لم تجرِ صيانة بعد";
        public const string RecentEmptyDesc = "يظهر هنا كل ما تفتحه أو تسترده من هذه الشاشة.";

        // ── What went wrong ─────────────────────────────────────────────────────────────────────
        public const string FailedNotFound = "لم يُعثر على هذا الملف أو المجلد. تأكّد من مكانه ثم أعد المحاولة.";
        public const string FailedNotOurs = "هذا الملف ليس من ملفات الوكيل.";
        public const string FailedUnreadable = Errors.FileUnreadable;
        public const string FailedOtherOrganisation = "هذا الملف يخصّ هيئة أخرى، ولا يفتحه مفتاح هذه الهيئة.";
        public const string FailedNoOrganisation = "لم تُسجَّل بيانات الهيئة بعد. أكملها في «الهيئة والهوية».";
        public const string FailedDevice = "لم يعد هذا الجهاز مسجّلًا. حدّث الشاشة واختر جهازًا آخر.";
        public const string FailedWrite = Errors.CannotWriteFolder;
        public const string NoDialog = "هذا الحاسوب لا يفتح نافذة اختيار الملفات.";

        /// <summary>The Arabic name of a container kind, with never a technical word in it.</summary>
        public static string KindName(ContainerKind kind) => kind switch
        {
            ContainerKind.Backup => "نسخة احتياطية من الوكيل",
            ContainerKind.Msg => "مراسلة بين مكتبين",
            ContainerKind.Setup => "ملف إعداد",
            ContainerKind.Sync => "حزمة مزامنة داخل المكتب",
            ContainerKind.Transfer => "مناقلة عُهدة",
            ContainerKind.Inventory => "نتائج جرد",
            _ => "ملف من ملفات الوكيل",
        };

        /// <summary>«الجهاز 2 · مدير المكتب» — who made the file.</summary>
        public static string Producer(int deviceNo, string roleName) =>
            Bidi($"الجهاز {Digits(deviceNo)} · {roleName}");

        /// <summary>A day as this screen writes it.</summary>
        public static string Day(DateTimeOffset at) =>
            Bidi(at.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

        /// <summary>How many of the product's own files a folder holds.</summary>
        public static string FileCount(int count) => Bidi(Counting.Files(count));

        /// <summary>«فُتح ومحتواه سليم (2 كيلوبايت)».</summary>
        public static string ContentOk(int bytes) => Bidi($"فُتح ومحتواه سليم · {Export.Size(bytes)}");

        /// <summary>The size of one item inside an opened file.</summary>
        public static string ItemSize(long bytes) => Export.Size(bytes);

        /// <summary>What goes into the operations log.</summary>
        public static class Log
        {
            public static string Opened(string fileName) => Bidi($"فُتح الملف {fileName} للفحص.");

            public static string FolderOpened(int fileCount) =>
                Bidi($"فُحص مجلد نسخة من الوكيل يحوي {Counting.Files(fileCount)}.");

            public static string Recovered(string officeName, int deviceNo) =>
                Bidi($"أُصدر ملف استرداد للجهاز {Digits(deviceNo)} في «{officeName}».");
        }
    }
}
