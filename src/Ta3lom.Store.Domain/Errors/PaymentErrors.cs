namespace Ta3lom.Store.Domain.Errors;

/// Payments, refunds and receipts (R5-Payments, ADR-S05).
public static class PaymentErrors
{
    /// The gateway did not answer or refused to create the hosted payment; the order stays `pending_payment`.
    public static Error GatewayUnavailable => Error.Unavailable(ErrorCodes.PaymentGatewayUnavailable, "The payment gateway is unavailable. Try again.");

    /// Only a `pending_payment` order before `paymentDueAt` can be paid.
    public static Error NotPayable(OrderStatus status) =>
        Error.Conflict(ErrorCodes.PaymentNotPayable, "This order cannot be paid.").With("orderStatus", status.ToWire());

    public static Error WebhookInvalid => Error.Unauthorized(ErrorCodes.PaymentWebhookInvalid, "The webhook could not be verified.");

    public static Error UnknownProvider => Error.NotFound(ErrorCodes.PaymentUnknownProvider, "Unknown payment provider.");

    /// The refund would take more than the captured amount still refundable.
    public static Error RefundExceeds(Money refundable) =>
        Error.Unprocessable(ErrorCodes.PaymentRefundExceeds, "The amount exceeds what can be refunded.").With("refundable", refundable.ToWire());

    /// The gateway refused the refund; nothing changed.
    public static Error RefundFailed => new(ErrorCodes.PaymentRefundFailed, "The gateway refused the refund.", 502);

    public static Error RefundNotAllowed(OrderStatus status) =>
        Error.Conflict(ErrorCodes.PaymentRefundNotAllowed, "This order has no captured payment to refund.").With("orderStatus", status.ToWire());

    /// The order is in payment review: finance must accept or refund it through the review actions.
    public static Error ReviewRequired => Error.Conflict(ErrorCodes.PaymentReviewRequired, "The payment is in review.");

    public static Error NotInReview(OrderStatus status) =>
        Error.Conflict(ErrorCodes.PaymentNotInReview, "The order is not in payment review.").With("orderStatus", status.ToWire());

    public static Error ReceiptNotAvailable => Error.NotFound(ErrorCodes.ReceiptNotAvailable, "No receipt is available for this order.");

    /// A refund is still waiting for the gateway's answer (review 04 A-6): one at a time per order.
    public static Error RefundPending => Error.Conflict(ErrorCodes.PaymentRefundPending, "A refund of this order is still pending.");

    /// Accepting the review would issue a receipt for more than the gateway kept (review 04 P-9).
    public static Error ReviewPartial(Money captured, Money total) =>
        Error.Conflict(ErrorCodes.PaymentReviewPartial, "Less than the order total was captured.").With("captured", captured.ToWire()).With("total", total.ToWire());

    public static Error OrphanNotFound => Error.NotFound(ErrorCodes.PaymentOrphanNotFound, "No such orphan payment.");

    public static Error OrphanResolved => Error.Conflict(ErrorCodes.PaymentOrphanResolved, "This orphan payment is already resolved.");

    /// Below the gateway's floor of 1.00 SAR (review 04 S-1).
    public static Error BelowMinimum(Money minimum) =>
        Error.Unprocessable(ErrorCodes.CheckoutBelowMinimum, "The total is below the minimum the gateway accepts.").With("minimum", minimum.ToWire());
}
