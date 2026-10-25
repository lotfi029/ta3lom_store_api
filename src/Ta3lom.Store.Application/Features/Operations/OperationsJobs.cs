using System.Globalization;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Operations;

/// `IntegrityChecks` job (04:00 daily): every failing check of the embedded integrity script raises one staff alert
/// (kind `integrity_check_failed`). Returns the number of failing checks.
public sealed record IntegrityChecksCommand : ICommand<int>;

internal sealed class IntegrityChecksCommandHandler(IOperationsStatus ops, IOutbox outbox, IUnitOfWork uow) : ICommandHandler<IntegrityChecksCommand, int>
{
    public const string AlertKind = "integrity_check_failed";

    public async Task<Result<int>> HandleAsync(IntegrityChecksCommand command, CancellationToken ct = default)
    {
        var failing = (await ops.RunIntegrityChecksAsync(ct)).Where(c => c.Rows > 0).ToList();
        foreach (var c in failing)
        {
            outbox.Enqueue(new StaffAlert(AlertKind, null, string.Create(CultureInfo.InvariantCulture, $"check {c.Number} — {c.Title}: {c.Rows} row(s)")));
        }

        if (failing.Count > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return failing.Count;
    }
}

/// `Retention` job (Fridays 04:30, PDPL): notifications older than 12 months and processed outbox rows older than 30 days are
/// deleted. Returns the rows deleted.
public sealed record RetentionCommand : ICommand<int>;

internal sealed class RetentionCommandHandler(IOperationsMaintenance maintenance, IClock clock) : ICommandHandler<RetentionCommand, int>
{
    public async Task<Result<int>> HandleAsync(RetentionCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return await maintenance.DeleteNotificationsAsync(now.AddMonths(-12), ct)
               + await maintenance.DeleteProcessedOutboxAsync(now.AddDays(-30), ct);
    }
}

/// `PruneIdempotencyKeys` job (hourly, D-14): keys past their 24-hour expiry. Returns the rows deleted.
public sealed record PruneIdempotencyKeysCommand : ICommand<int>;

internal sealed class PruneIdempotencyKeysCommandHandler(IOperationsMaintenance maintenance, IClock clock) : ICommandHandler<PruneIdempotencyKeysCommand, int>
{
    public async Task<Result<int>> HandleAsync(PruneIdempotencyKeysCommand command, CancellationToken ct = default) =>
        await maintenance.PruneIdempotencyKeysAsync(clock.UtcNow, ct);
}

/// `PruneOtp` job (hourly, D-14): OTP challenges older than 24 hours. Returns the rows deleted.
public sealed record PruneOtpCommand : ICommand<int>;

internal sealed class PruneOtpCommandHandler(IOperationsMaintenance maintenance, IClock clock) : ICommandHandler<PruneOtpCommand, int>
{
    public async Task<Result<int>> HandleAsync(PruneOtpCommand command, CancellationToken ct = default) =>
        await maintenance.PruneOtpChallengesAsync(clock.UtcNow - SessionPolicy.OtpRetention, ct);
}

/// `ExpireGuestCarts` job (daily 03:00, D-14): guest carts idle for 30 days (`expires_at` passed). Returns the carts deleted.
public sealed record ExpireGuestCartsCommand : ICommand<int>;

internal sealed class ExpireGuestCartsCommandHandler(IOperationsMaintenance maintenance, IClock clock) : ICommandHandler<ExpireGuestCartsCommand, int>
{
    public async Task<Result<int>> HandleAsync(ExpireGuestCartsCommand command, CancellationToken ct = default) =>
        await maintenance.DeleteExpiredGuestCartsAsync(clock.UtcNow, ct);
}
