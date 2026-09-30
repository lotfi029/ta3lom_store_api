using System.Text.Json;

namespace Ta3lom.Store.Application.Features.Outbox;

/// A claimed `outbox_message` row (09 §5.11).
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt, int Attempts);

/// One handler per message type (audit §2.1), resolved by `MessageType` = the event record name. A handler throws to ask
/// for a retry (backoff up to 24 h); it must be idempotent or at-most-once per recipient.
public interface IOutboxHandler
{
    string MessageType { get; }

    Task HandleAsync(OutboxEnvelope envelope, CancellationToken ct);
}

public abstract class OutboxHandler<TMessage> : IOutboxHandler
    where TMessage : IDomainEvent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string MessageType => typeof(TMessage).Name;

    public Task HandleAsync(OutboxEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var message = JsonSerializer.Deserialize<TMessage>(envelope.Payload, Json)
                      ?? throw new InvalidOperationException($"Outbox message {envelope.Id} has an empty payload.");
        return HandleAsync(message, envelope, ct);
    }

    protected abstract Task HandleAsync(TMessage message, OutboxEnvelope envelope, CancellationToken ct);
}

/// Thrown when a provider refused a message; the dispatcher schedules a retry.
public sealed class OutboxDeliveryException(string message) : Exception(message);
