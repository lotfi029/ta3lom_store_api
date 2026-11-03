namespace Ta3lom.Store.Domain.Events;

/// Any staff change to a category, a project or its options: the public cache is evicted and the static web site is
/// rebuilt (debounced) from the outbox.
public sealed record CatalogChanged(string Entity, Guid Id, string Change) : IDomainEvent;

/// A donor placed a donation order (`pending_payment`). Money travels as the wire string.
public sealed record OrderPlaced(Guid OrderId, string Reference, string TotalAmount, int LineCount) : IDomainEvent;

/// Staff cancelled a pending order.
public sealed record OrderCancelled(Guid OrderId, string Reference, string CancelledBy) : IDomainEvent;

/// A captured payment settled the order (`paid`, receipt DR issued): SMS + e-mail to the donor, PDF rendered.
public sealed record OrderPaid(Guid OrderId, string Reference, string TotalAmount, Guid ReceiptId) : IDomainEvent;

/// A captured payment could not settle its order (amount mismatch, late, double payment): finance is alerted.
public sealed record PaymentReviewRequired(Guid OrderId, string Reference, Guid PaymentId, string Reason, string CapturedAmount) : IDomainEvent;

/// An unpaid order passed its `paymentDueAt` (nothing restored: the cart was emptied at checkout).
public sealed record OrderExpired(Guid OrderId, string Reference) : IDomainEvent;

/// A refund succeeded at the gateway. `ReceiptId` is the DRC when the order became fully refunded.
public sealed record RefundProcessed(Guid OrderId, string Reference, Guid RefundId, string Amount, bool Full, Guid? ReceiptId) : IDomainEvent;

/// A receipt (DR or DRC) was issued: render its PDF once into private storage.
public sealed record ReceiptIssued(Guid ReceiptId, Guid OrderId, string Number) : IDomainEvent;
