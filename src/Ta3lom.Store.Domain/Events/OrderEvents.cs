namespace Ta3lom.Store.Domain.Events;

/// Outbox messages of the ordering and settlement flow (09 §4.3/§4.4, §5.11). Each becomes an `outbox_message` row
/// (`type` = record name, `payload` = camelCase JSON) in the transaction that raised it; S-17 dispatches them.
public sealed record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;

/// RUL-63: confirmation SMS + receipt email.
public sealed record OrderPaid(Guid OrderId, string Reference) : IDomainEvent;

/// RUL-59: render the invoice and/or donation receipt PDFs issued in the settlement.
public sealed record RenderDocuments(Guid OrderId) : IDomainEvent;

public sealed record OrderExpired(Guid OrderId, string Reference, bool CartRestored) : IDomainEvent;

public sealed record OrderCancelled(Guid OrderId, string Reference, string Reason) : IDomainEvent;

/// RUL-22: the card SMS for one dedicated gift line, after payment.
public sealed record DedicationCard(Guid DedicationId, Guid OrderId) : IDomainEvent;

/// Staff e-mail (`staff_alert`): `payment_review_amount`, `payment_review_late_capture`, `duplicate_course`,
/// `settlement_effect_missing` … Never shown to customers.
public sealed record FinanceAlert(string Kind, Guid? OrderId, string? Reference, string? Detail) : IDomainEvent;

public static class FinanceAlertKinds
{
    public const string AmountMismatch = "payment_review_amount";
    public const string LateCapture = "payment_review_late_capture";
    public const string DuplicateCourse = "duplicate_course";
    public const string EffectMissing = "settlement_effect_missing";
}
