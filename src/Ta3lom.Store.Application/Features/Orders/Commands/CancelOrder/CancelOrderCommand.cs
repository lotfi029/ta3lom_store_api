using FluentValidation;
using Ta3lom.Store.Application.DTOs.Orders;

namespace Ta3lom.Store.Application.Features.Orders.Commands.CancelOrder;

/// `POST /v1/admin/orders/{ref}/cancel` (#87, RUL-29, RUL-30): staff, only from `pending_payment`/`payment_failed`;
/// the reserved print stock is released in the same transaction. Audited (staff actor).
public sealed record CancelOrderCommand(string Reference, string Reason) : ICommand<AdminOrderDetailResponse>;

internal sealed class CancelOrderCommandHandler(IOrderRepository orders, OrderStock stock, OrderReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<CancelOrderCommand, AdminOrderDetailResponse>
{
    public async Task<Result<AdminOrderDetailResponse>> HandleAsync(CancelOrderCommand command, CancellationToken ct = default)
    {
        var found = await reader.FindAsync(command.Reference, ct);
        if (found is null)
        {
            return CheckoutErrors.OrderNotFound;
        }

        var cancelled = await uow.ExecuteInTransactionAsync(async token =>
        {
            var order = await orders.LoadForUpdateAsync(found.Id, token);
            if (order is null || !order.Cancel(command.Reason.Trim(), clock.UtcNow))
            {
                return Result.Failure(SettlementErrors.CancelNotAllowed);
            }

            await stock.ReleaseAsync(order, token);
            return Result.Success();
        }, ct);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        return await reader.AdminDetailAsync((await reader.FindAsync(command.Reference, ct))!, ct);
    }
}

internal sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}
