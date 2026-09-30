using System.Globalization;
using FluentValidation;
using Ta3lom.Store.Application.DTOs.Dev;
using Ta3lom.Store.Application.Features.Orders;
using Ta3lom.Store.Application.Features.Settlement;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Dev.Commands.ConfirmPayment;

/// `POST /dev/orders/{ref}/confirm-payment` (doc 15 §2.4, Development/Staging only): simulates a verified capture so the
/// backend can be tested without a gateway — success, a repeat (same or new `confirmationId`) and a wrong `amount`.
public sealed record ConfirmPaymentCommand(string Reference, string? Amount, string? ConfirmationId) : ICommand<DevSettlementResponse>;

internal sealed class ConfirmPaymentCommandHandler(OrderReader reader, IOrderSettlement settlement, IGenericRepository<Payment> payments,
    IUnitOfWork uow, IClock clock)
    : ICommandHandler<ConfirmPaymentCommand, DevSettlementResponse>
{
    private const string DevProvider = "dev";

    public async Task<Result<DevSettlementResponse>> HandleAsync(ConfirmPaymentCommand command, CancellationToken ct = default)
    {
        var order = await reader.FindAsync(command.Reference, ct);
        if (order is null)
        {
            return CheckoutErrors.OrderNotFound;
        }

        var amount = command.Amount is null ? order.GrandTotal.Amount : decimal.Parse(command.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var confirmation = new PaymentConfirmation(order.Id, command.ConfirmationId ?? $"dev:{Guid.CreateVersion7():N}", amount, DevProvider,
            $"dev_{order.Reference}", "card", clock.UtcNow);
        var outcome = await settlement.SettleAsync(confirmation, ct);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        // Stand-in for the payment service's capture record: one `dev` payment per order (UNIQUE provider + id).
        if (outcome.Value is SettlementOutcome.Paid or SettlementOutcome.PaymentReview
            && !await payments.ExistsAsync(p => p.Provider == DevProvider && p.ProviderPaymentId == confirmation.ProviderPaymentId, ct))
        {
            payments.Add(Payment.Captured(order.Id, DevProvider, confirmation.ProviderPaymentId, PaymentMethod.Card, Money.Round(amount), clock.UtcNow));
            await uow.SaveChangesAsync(ct);
        }

        var after = await reader.FindAsync(command.Reference, ct);
        return new DevSettlementResponse(order.Reference, outcome.Value.ToWire(), after!.Status.ToWire());
    }
}

internal sealed class ConfirmPaymentCommandValidator : AbstractValidator<ConfirmPaymentCommand>
{
    public ConfirmPaymentCommandValidator() =>
        RuleFor(c => c.Amount).Matches(@"^\d{1,10}\.\d{2}$").When(c => c.Amount is not null).WithMessage("amount must be a money string such as \"350.00\".");
}
