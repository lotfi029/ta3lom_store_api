namespace Ta3lom.Store.Application.Features.Settlement;

/// Doc 15 §2.1: raised by the payment service after a VERIFIED capture (webhook or reconciliation), never from a
/// return URL. In this backend only `POST /dev/orders/{ref}/confirm-payment` calls it (Development/Staging).
public sealed record PaymentConfirmation(
    Guid OrderId,
    string ConfirmationId,
    decimal CapturedAmount,
    string Provider,
    string ProviderPaymentId,
    string Method,
    DateTimeOffset CapturedAt);

public enum SettlementOutcome
{
    Paid,
    AlreadyPaid,
    PaymentReview,
    NotFound,
}

/// Doc 15 §2: the only code path that sets an order `paid` (architecture test in T-2). Idempotent: a second call for a
/// paid order returns `AlreadyPaid` and changes nothing.
public interface IOrderSettlement
{
    Task<Result<SettlementOutcome>> SettleAsync(PaymentConfirmation confirmation, CancellationToken ct);
}
