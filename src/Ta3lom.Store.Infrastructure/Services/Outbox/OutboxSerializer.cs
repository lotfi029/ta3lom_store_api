using System.Text.Json;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Services.Outbox;

/// `type` = the event record name, `payload` = camelCase JSON (09 §3.4). The dispatcher (S-17) maps the type back.
internal static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static OutboxMessage ToMessage(IDomainEvent domainEvent, DateTimeOffset now) =>
        new(domainEvent.GetType().Name, JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options), now);
}
