using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Operations;

/// Set-based deletes for the `Retention`, `PruneIdempotencyKeys`, `PruneOtp` and `ExpireGuestCarts` jobs.
internal sealed class OperationsMaintenance(StoreDbContext db) : IOperationsMaintenance
{
    public Task<int> DeleteNotificationsAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.notification WHERE created_at < {before}", ct);

    public Task<int> DeleteProcessedOutboxAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.outbox_message WHERE processed_at < {before}", ct);

    public Task<int> PruneIdempotencyKeysAsync(DateTimeOffset now, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.idempotency_key WHERE expires_at < {now}", ct);

    public Task<int> PruneOtpChallengesAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.otp_challenge WHERE created_at < {before}", ct);

    public Task<int> DeleteExpiredGuestCartsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.cart WHERE customer_id IS NULL AND expires_at < {now}", ct);
}
