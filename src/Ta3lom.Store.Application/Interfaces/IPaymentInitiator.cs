namespace Ta3lom.Store.Application.Interfaces;

/// Doc 15 §3: implemented by the payment service; called by checkout (#41) after the order commits.
/// This backend ships `NullPaymentInitiator` (no redirect), so orders stop at `pending_payment` (plan 14 §0).
public interface IPaymentInitiator
{
    Task<Result<PaymentStart>> StartAsync(OrderPaymentRequest request, CancellationToken ct);
}

public sealed record OrderPaymentRequest(
    Guid OrderId, string Reference, decimal Amount, string Currency, string CustomerName, string CustomerMobileE164, string CustomerEmail,
    string Locale, string ReturnUrl, bool HasGiftLines, DateTimeOffset PaymentDueAt);

public sealed record PaymentStart(string? RedirectUrl, string? SdkPayload, DateTimeOffset ExpiresAt);
