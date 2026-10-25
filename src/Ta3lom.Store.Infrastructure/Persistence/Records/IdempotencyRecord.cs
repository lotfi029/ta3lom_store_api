namespace Ta3lom.Store.Infrastructure.Persistence.Records;

/// Table `idempotency_key`: stored responses for `Idempotency-Key` requests. An HTTP mechanism, not a domain concept, so it
/// lives in Infrastructure. `status` is `in_progress` until the response is stored (`completed`).
internal sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public string Scope { get; private set; } = null!;

    public string Key { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}
