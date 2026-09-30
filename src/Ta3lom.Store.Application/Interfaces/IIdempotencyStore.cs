namespace Ta3lom.Store.Application.Interfaces;

/// `idempotency_key` rows (11 §1.5, 09 §5.16): first writer wins on (scope, key); a NULL status means in progress.
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> ClaimAsync(string scope, string key, string requestHash, CancellationToken ct = default);

    Task CompleteAsync(string scope, string key, int statusCode, string responseJson, CancellationToken ct = default);

    /// Server error: the client may retry with the same key.
    Task ReleaseAsync(string scope, string key, CancellationToken ct = default);
}

public enum IdempotencyClaimStatus
{
    Claimed,
    Replay,
    Mismatch,
    InProgress,
}

public sealed record IdempotencyClaim(IdempotencyClaimStatus Status, int? StatusCode = null, string? ResponseJson = null);
