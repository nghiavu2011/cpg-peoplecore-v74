using System.Text.Json;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Runtime;

namespace PeopleCore.Api.Services.Audit;

public interface IAuditService
{
    void Record(string action, string entityType, string entityId, object? safeMetadata = null, string? correlationId = null);
}

public sealed class AuditService(PeopleCoreDbContext db, ICurrentUser current, IHttpContextAccessor http) : IAuditService
{
    public void Record(string action, string entityType, string entityId, object? safeMetadata = null, string? correlationId = null)
    {
        correlationId ??= http.HttpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString();
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorEmployeeId = current.EmployeeId?.ToString(),
            ActorObjectId = current.EntraObjectId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            CorrelationId = correlationId,
            DataJson = safeMetadata is null ? null : JsonSerializer.Serialize(safeMetadata)
        });
    }
}
