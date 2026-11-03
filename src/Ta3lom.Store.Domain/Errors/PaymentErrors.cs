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
}
