using Wakeel.Core.Services;

namespace Wakeel.UI.Services.Shell;

/// <summary>
/// Where «معالجة» on W09, a row of «يحتاج إجراءً اليوم» on W08 and a notification on W10 send the
/// person: the screen that owns the record they point at.
/// </summary>
/// <remarks>
/// The daily shell is the first package to link OUT of itself, so the route of every record screen
/// is written down once here instead of being spelled out at each of the three call sites. The
/// screens themselves belong to later packages (B3 correspondence, B4 tasks and meetings, B5
/// finance); until one of them exists, following its link lands on the router's «الصفحة غير موجودة»,
/// which is the honest answer and changes to the real screen the moment that package adds the page.
/// </remarks>
public static class RecordRoutes
{
    /// <summary>Route for one attention row, or null when its kind has no screen of its own.</summary>
    public static string? For(AttentionEntityKind kind, Guid id) => kind switch
    {
        AttentionEntityKind.CorrespondenceIn or AttentionEntityKind.CorrespondenceOut => $"/w20/{id}",
        AttentionEntityKind.Task => $"/w29/{id}",
        AttentionEntityKind.Commitment => $"/w32/{id}",
        AttentionEntityKind.Case => $"/w41/{id}",
        AttentionEntityKind.Decision => $"/w31/{id}",
        AttentionEntityKind.PhoneExpense => "/w08",
        _ => null,
    };

    /// <summary>
    /// Route for the record a notification points at. <paramref name="entityType"/> is the table
    /// name the notification carries (DATA-MODEL.md §1 <c>notifications.entity_type</c>).
    /// </summary>
    public static string? For(string? entityType, Guid? id)
    {
        if (id is not { } recordId || string.IsNullOrWhiteSpace(entityType))
        {
            return null;
        }

        return entityType switch
        {
            "correspondence" => $"/w20/{recordId}",
            "tasks" => $"/w29/{recordId}",
            "commitments" => $"/w32/{recordId}",
            "cases" => $"/w41/{recordId}",
            "decisions" => $"/w31/{recordId}",
            "meetings" => $"/w35/{recordId}",
            "appointments" => $"/w39/{recordId}",
            "phone_expenses" => "/w08",
            "financial_cycles" => "/w08",
            _ => null,
        };
    }
}
