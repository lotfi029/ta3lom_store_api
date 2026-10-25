using System.Text.Json;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Auditing;

/// Adds security-event rows to `audit_log` in the current unit of work. Details are serialised as JSON; callers never pass
/// secrets or personal data (only ids, reasons and times).
internal sealed class AuditTrail(StoreDbContext db, ICurrentActor actor, IClock clock) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entity, string? entityId, string? actorName = null, object? details = null)
    {
        var who = actorName ?? actor.Kind switch
        {
            ActorKind.Staff => actor.Name ?? "staff",
            ActorKind.Customer => $"customer:{actor.Id}",
            ActorKind.System => actor.Name ?? "system",
            _ => "anonymous",
        };
        db.AuditLog.Add(new AuditLogEntry(who, action, entity, clock.UtcNow, entityId,
            details is null ? null : JsonSerializer.Serialize(details, details.GetType(), Json), actor.IpHash));
    }
}
