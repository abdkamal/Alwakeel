using Microsoft.EntityFrameworkCore;

namespace Wakeel.Core.Data;

/// <summary>Query-filter opt-outs for <see cref="WakeelDb"/>.</summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Opts a query out of the global <c>deleted_at IS NULL</c> filter applied to every synced
    /// table, so soft-deleted rows are included. Thin wrapper over EF Core's
    /// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/> under the name
    /// used throughout this codebase.
    /// </summary>
    public static IQueryable<T> IgnoreDeleted<T>(this IQueryable<T> source)
        where T : class
        => source.IgnoreQueryFilters();
}
