using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Services.Payments;

/// Plan 14 §0 / doc 15 §3: no gateway in this backend. The order stays `pending_payment` until the payment service
/// (or `POST /dev/orders/{ref}/confirm-payment`) settles it; the checkout response carries no redirect.
internal sealed class NullPaymentInitiator : IPaymentInitiator
{
    public Task<Result<PaymentStart>> StartAsync(OrderPaymentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(Result.Success(new PaymentStart(null, null, request.PaymentDueAt)));
    }
}
