namespace Ta3lom.Store.Infrastructure.Persistence.Records;

/// Table `idempotency_key`: stored responses for Idempotency-Key requests (11 §1.5, 09 §5.16).
/// An HTTP mechanism, not a domain concept, so it lives in Infrastructure.
internal sealed class IdempotencyRecord
{
    public IdempotencyRecord(string key, string scope, string requestHash, DateTimeOffset now)
    {
        Key = key;
        Scope = scope;
        RequestHash = requestHash;
        CreatedAt = now;
    }

    private IdempotencyRecord()
    {
    }

    public string Key { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    public int? StatusCode { get; private set; }

    public string? Response { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
