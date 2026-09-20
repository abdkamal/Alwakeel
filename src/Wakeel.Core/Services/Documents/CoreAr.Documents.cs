using System.Globalization;

namespace Wakeel.Core.Services;

/// <summary>
/// The documents package's slice of Core's Arabic sentences (AGREEMENT item 15, B3-2).
/// </summary>
/// <remarks>
/// ARCHITECTURE §12 keeps every sentence Core can emit inside <see cref="CoreAr"/>, so a reviewer
/// reads them all in one place; this file is the part of that class the vault, the import path,
/// the scanner and OCR produce. No sentence here names a folder, a file format, a library, a
/// drive letter or a code — only what a person needs to read and what they can do next. Values
/// that travel inside a sentence (a file name, a size) are wrapped in <see cref="CoreAr.Isolate"/>
/// so a Latin file name cannot break the Arabic sentence around it.
/// </remarks>
public static partial class CoreAr
{
    /// <summary>The vault, the documents list, the scanner and OCR.</summary>
    public static class Documents
    {
        // -----------------------------------------------------------------------------------
        // The two vault states of W46.
        // -----------------------------------------------------------------------------------

        /// <summary>«الخزنة غير متاحة» — the folder holding the originals cannot be reached.</summary>
        public const string VaultUnavailableTitle = "الخزنة غير متاحة";

        /// <summary>What "unavailable" means: the files are not lost, they are out of reach from here.</summary>
        public const string VaultUnavailableMessage =
            "لم نتمكّن من الوصول إلى مكان حفظ الملفات الأصلية. الملفات لم تُفقد، لكنها غير متاحة الآن من هذا الجهاز.";

        /// <summary>What to do about it.</summary>
        public const string VaultUnavailableAction =
            "تأكّد من توصيل القرص الذي يحفظ عليه الوكيل ملفاته، ثم أعد المحاولة.";

        /// <summary>«الملف تالف — الأصل محفوظ» — the stored bytes no longer match what was put in.</summary>
        public const string FileCorruptTitle = "الملف تالف — الأصل محفوظ";

        /// <summary>What "corrupt" means for a document whose record is still intact.</summary>
        public const string FileCorruptMessage =
            "بيانات هذا الملف لم تعد مطابقة لما حُفظ عند إضافته، ولذلك لا يمكن عرضه. سجلّ المستند وبياناته ونصّه المقروء ما زالت كما هي.";

        /// <summary>What to do about it.</summary>
        public const string FileCorruptAction =
            "أضف الملف من مصدره مرة أخرى، أو استعِد نسخة احتياطية سابقة.";

        /// <summary>The vault is reachable but holds no file for this document.</summary>
        public const string FileMissingTitle = "الملف غير موجود";

        /// <summary>What "missing" means.</summary>
        public const string FileMissingMessage =
            "سجلّ هذا المستند موجود، لكن الملف نفسه غير محفوظ في هذا الجهاز.";

        /// <summary>What to do about it.</summary>
        public const string FileMissingAction = "أضف الملف من مصدره مرة أخرى.";

        // -----------------------------------------------------------------------------------
        // Importing a file.
        // -----------------------------------------------------------------------------------

        /// <summary>The largest file the office may add, in megabytes (AGREEMENT §6, B3-2).</summary>
        public const int MaxFileSizeMegabytes = 20;

        /// <summary>The file is bigger than what may be added.</summary>
        public static string FileTooLarge(string fileName, long sizeBytes) =>
            $"الملف {Isolate(fileName)} حجمه {Isolate(FileSize(sizeBytes))} وهو أكبر من الحد المسموح به ({MaxFileSizeMegabytes} ميجابايت).";

        /// <summary>What to do with a file that is too large.</summary>
        public const string FileTooLargeAction = "اضغط الملف أو قسّمه إلى أجزاء، ثم أضفه مرة أخرى.";

        /// <summary>The file's kind is not one الوكيل stores.</summary>
        public static string FileKindNotAccepted(string fileName) =>
            $"لا يمكن إضافة الملف {Isolate(fileName)}؛ الوكيل يحفظ صور المستندات وملفات PDF وملفات Word فقط.";

        /// <summary>What to do with a file of an unaccepted kind.</summary>
        public const string FileKindNotAcceptedAction = "احفظ الملف بصيغة PDF أو كصورة، ثم أضفه مرة أخرى.";

        /// <summary>An empty file.</summary>
        public static string FileEmpty(string fileName) => $"الملف {Isolate(fileName)} فارغ، ولا يوجد فيه ما يُحفظ.";

        /// <summary>The document already existed and the new copy was linked to it instead.</summary>
        public static string FileAlreadyStored(string fileName) =>
            $"الملف {Isolate(fileName)} محفوظ من قبل، وقد رُبِط المستند الموجود بدل حفظ نسخة ثانية منه.";

        /// <summary>Confirmation after one file was added.</summary>
        public static string FileAdded(string fileName) => $"أُضيف الملف {Isolate(fileName)}.";

        /// <summary>Human size, always in western digits (AGREEMENT item 20).</summary>
        public static string FileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes} بايت");
            }

            if (bytes < 1024L * 1024L)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} كيلوبايت");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} ميجابايت");
        }

        // -----------------------------------------------------------------------------------
        // Scanning.
        // -----------------------------------------------------------------------------------

        /// <summary>«لا ماسح ضوئي» — nothing answered when the office asked to scan.</summary>
        public const string NoScannerTitle = "لا ماسح ضوئي";

        /// <summary>What it means.</summary>
        public const string NoScannerMessage = "لم يُعثر على ماسح ضوئي متصل بهذا الجهاز.";

        /// <summary>What to do instead — the path never stops at the scanner.</summary>
        public const string NoScannerAction = "وصّل الماسح وشغّله ثم أعد المحاولة، أو أضف الملف من الجهاز أو من الهاتف.";

        /// <summary>The scanner was there but the scan did not finish.</summary>
        public const string ScanFailedTitle = "لم يكتمل المسح";

        /// <summary>What it means.</summary>
        public const string ScanFailedMessage = "توقّف المسح قبل أن يكتمل، ولم تُحفظ أي صفحة.";

        /// <summary>What to do about it.</summary>
        public const string ScanFailedAction = "تأكّد من وضع الورقة في الماسح ومن أنه جاهز، ثم أعد المحاولة.";

        /// <summary>The scan produced no page at all.</summary>
        public const string ScanNoPages = "لم يُنتج المسح أي صفحة.";

        /// <summary>How many pages a finished scan produced, counted the way Arabic counts.</summary>
        public static string ScanPagesDone(int pages) => pages switch
        {
            <= 0 => ScanNoPages,
            1 => "تم مسح صفحة واحدة.",
            2 => "تم مسح صفحتين.",
            <= 10 => $"تم مسح {Digits(pages)} صفحات.",
            _ => $"تم مسح {Digits(pages)} صفحة.",
        };

        /// <summary>A count as western digits (AGREEMENT item 20), isolated inside its sentence.</summary>
        private static string Digits(int value) => Isolate(value.ToString(CultureInfo.InvariantCulture));

        // -----------------------------------------------------------------------------------
        // Reading the text of a document (OCR).
        // -----------------------------------------------------------------------------------

        /// <summary>The documents-list value for a document whose text has not been read yet.</summary>
        public const string OcrPending = "بانتظار قراءة النص";

        /// <summary>The value while the text is being read.</summary>
        public const string OcrRunning = "جارٍ قراءة النص";

        /// <summary>The value once the text has been read.</summary>
        public const string OcrDone = "النص مقروء";

        /// <summary>The value for a kind of file whose text الوكيل does not read.</summary>
        public const string OcrUnsupported = "لا يُقرأ نصه";

        /// <summary>The value when reading the text did not succeed.</summary>
        public const string OcrFailed = "تعذّرت قراءة النص";

        /// <summary>The reading tools are not installed, so text reading is off.</summary>
        public const string OcrModelMissingTitle = "قراءة النصوص غير مهيّأة";

        /// <summary>What it means, with no mention of a model file or a folder path.</summary>
        public const string OcrModelMissingMessage =
            "ملفات قراءة النصوص غير متوفرة على هذا الجهاز، لذلك تُحفظ المستندات كما هي بدون قراءة نصّها.";

        /// <summary>What to do about it.</summary>
        public const string OcrModelMissingAction = "اطلب من مسؤول التجهيز إضافة ملفات قراءة النصوص، ثم أعد المحاولة.";

        /// <summary>Progress while a queue of documents is being read.</summary>
        public static string OcrQueueProgress(int done, int total) =>
            $"جارٍ قراءة نصوص المستندات: {Isolate(done.ToString(CultureInfo.InvariantCulture))} من {Isolate(total.ToString(CultureInfo.InvariantCulture))}.";

        /// <summary>Progress while the pages of one document are being read.</summary>
        public static string OcrPageProgress(int page, int pages) =>
            $"الصفحة {Isolate(page.ToString(CultureInfo.InvariantCulture))} من {Isolate(pages.ToString(CultureInfo.InvariantCulture))}.";

        /// <summary>Nothing is waiting to be read.</summary>
        public const string OcrQueueEmpty = "لا توجد مستندات بانتظار قراءة النص.";

        /// <summary>The queue is done.</summary>
        public const string OcrQueueDone = "اكتملت قراءة نصوص المستندات.";
    }
}
