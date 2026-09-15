using Microsoft.Data.Sqlite;

namespace Wakeel.Core.Services;

/// <summary>Translates .NET exceptions into <see cref="UserFacingError"/>s with plain, technical-term-free Arabic text.</summary>
public interface IErrorMapper
{
    UserFacingError Map(Exception exception);
}

/// <inheritdoc cref="IErrorMapper"/>
public sealed class ErrorMapper : IErrorMapper
{
    public UserFacingError Map(Exception exception)
    {
        var reference = NewReference();
        return Unwrap(exception) switch
        {
            SqliteException => new UserFacingError(
                "تعذّر الوصول إلى البيانات المحفوظة",
                "حدثت مشكلة أثناء القراءة من قاعدة البيانات المحلية أو الكتابة إليها.",
                "أغلق البرنامج وأعد فتحه؛ إن تكررت المشكلة فاستعد آخر نسخة احتياطية.",
                reference),
            IOException => new UserFacingError(
                "تعذّرت قراءة الملف أو حفظه",
                "قد يكون الملف مفتوحًا في برنامج آخر، أو أن المساحة المتاحة غير كافية، أو أن الملف غير موجود.",
                "أغلق البرامج الأخرى التي قد تستخدم الملف، وتحقق من المساحة المتاحة، ثم أعد المحاولة.",
                reference),
            UnauthorizedAccessException => new UserFacingError(
                "لا صلاحية كافية لإتمام العملية",
                "منع النظام البرنامج من الوصول إلى الملف أو المجلد المطلوب.",
                "تحقق من صلاحيات المجلد، أو شغّل البرنامج بحساب يملك صلاحية الكتابة، ثم أعد المحاولة.",
                reference),
            ArgumentException => new UserFacingError(
                "قيمة مُدخلة غير صحيحة",
                "أحد الحقول يحتوي قيمة لا يمكن للبرنامج قبولها في هذا السياق.",
                "راجع القيم المدخلة وصحّحها ثم أعد المحاولة.",
                reference),
            _ => new UserFacingError(
                "حدث خطأ غير متوقع",
                "واجه البرنامج مشكلة أثناء تنفيذ العملية ولم يتمكن من إكمالها.",
                "أعد المحاولة؛ إن تكررت المشكلة فتواصل مع الدعم واذكر الرقم المرجعي.",
                reference),
        };
    }

    // A plain random Guid, not CreateVersion7(): a v7 id's leading hex digits are time-based, so
    // two references minted within the same millisecond would otherwise share a prefix.
    private static string NewReference() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>
    /// Walks the <see cref="Exception.InnerException"/> chain and returns the first exception of
    /// a type this mapper actually recognises. EF Core wraps every SaveChanges provider failure
    /// in <c>DbUpdateException</c> (and some hosts further wrap that in
    /// <c>AggregateException</c>/<c>TargetInvocationException</c>), so mapping the outermost
    /// exception directly would send a real <see cref="SqliteException"/> — a UNIQUE-constraint
    /// violation, for instance — into the generic "unexpected error" bucket instead of the
    /// data-specific message. Returns <paramref name="exception"/> itself when nothing in the
    /// chain is recognised, so it still falls into that generic bucket as before.
    /// </summary>
    private static Exception Unwrap(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                return current;
            }
        }

        return exception;
    }
}
