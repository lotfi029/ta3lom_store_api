using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Orders.Commands.ExpireUnpaidOrders;

/// `ExpireUnpaidOrders` job (every 5 min, RUL-29, RUL-30; order side only): unpaid orders past `payment_due_at` become
/// `expired`, their print reservations are released and their lines return to the customer's active cart (products
/// already there are skipped). One transaction per order, so a failure never blocks the others.
public sealed record ExpireUnpaidOrdersCommand(int BatchSize = 100) : ICommand<int>;

internal sealed class ExpireUnpaidOrdersCommandHandler(
    IOrderRepository orders,
    ICartRepository carts,
    OrderStock stock,
    IApplicationDbContext db,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ExpireUnpaidOrdersCommand, int>
{
    public async Task<Result<int>> HandleAsync(ExpireUnpaidOrdersCommand command, CancellationToken ct = default)
    {
        var due = await orders.DueForExpiryAsync(clock.UtcNow, command.BatchSize, ct);
        var expired = 0;
        foreach (var orderId in due)
        {
            var result = await uow.ExecuteInTransactionAsync(async token =>
            {
                var now = clock.UtcNow;
                var order = await orders.LoadForUpdateAsync(orderId, token);
                if (order is null || !order.CanBeCancelled || now < order.PaymentDueAt)
                {
                    return Result.Success(false);                                     // settled or changed meanwhile
                }

                await stock.ReleaseAsync(order, token);                                // RUL-30
                var restored = await RestoreCartAsync(order, now, token);              // RUL-29
                return Result.Success(order.Expire(now, restored));
            }, ct);
            expired += result is { IsSuccess: true, Value: true } ? 1 : 0;
        }

        return expired;
    }

    private async Task<bool> RestoreCartAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var dedications = await db.Dedications.AsNoTracking().Where(d => lineIds.Contains(d.OrderLineId)).ToDictionaryAsync(d => d.OrderLineId, ct);
        var cart = await carts.GetOrCreateForCustomerForUpdateAsync(order.CustomerId, now, ct);
        var restored = false;
        foreach (var line in order.Lines.OrderBy(l => l.LineNo))
        {
            var dedication = dedications.TryGetValue(line.Id, out var d)
                ? new CartDedication(d.ToName, d.ToMobile, d.FromName, d.TemplateKey == Dedication.CustomTemplateKey ? null : d.TemplateKey, d.CustomText)
                : null;
            restored |= cart.RestoreLine(line, dedication, now);
        }

        return restored;
    }
}
