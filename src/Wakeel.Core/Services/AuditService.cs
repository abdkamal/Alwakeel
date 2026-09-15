using System.Text.Json;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;

namespace Wakeel.Core.Services;

/// <summary>Writes sensitive-operation entries to <c>audit_log</c> (ARCHITECTURE.md §12: "no secrets").</summary>
public interface IAuditService
{
    /// <summary>Logs one sensitive operation. <paramref name="details"/>, if given, is serialized to JSON and must never contain secrets.</summary>
    Task LogAsync(
        string actor,
        string action,
        string summaryAr,
        string? entityType = null,
        Guid? entityId = null,
        object? details = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAuditService"/>
public sealed class AuditService(WakeelDb db, IClock clock) : IAuditService
{
    public async Task LogAsync(
        string actor,
        string action,
        string summaryAr,
        string? entityType = null,
        Guid? entityId = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            At = clock.UtcNow,
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            SummaryAr = summaryAr,
            Details = details is null ? null : JsonSerializer.Serialize(details),
        };

        db.AuditLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
