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

/// #88: staff asked to send the order's documents again (`channels`: sms, email).
public sealed record DocumentsResendRequested(Guid OrderId, IReadOnlyList<string> Channels) : IDomainEvent;

/// RUL-22: the card SMS for one dedicated gift line, after payment.
public sealed record DedicationCard(Guid DedicationId, Guid OrderId, bool Resend = false) : IDomainEvent;

/// Staff e-mail (`staff_alert`): `payment_review_amount`, `payment_review_late_capture`, `duplicate_course`,
/// … Never shown to customers.
public sealed record FinanceAlert(string Kind, Guid? OrderId, string? Reference, string? Detail) : IDomainEvent;

public static class FinanceAlertKinds
{
    public const string AmountMismatch = "payment_review_amount";
    public const string LateCapture = "payment_review_late_capture";
    public const string DuplicateCourse = "duplicate_course";
}

/// RUL-42: grant LMS access for an enrolment (retried by the dispatcher until the LMS accepts; adapter keyed by id).
public sealed record LmsEnrol(Guid EnrolmentId) : IDomainEvent;

/// RUL-50/58: withdraw LMS access.
public sealed record LmsRevoke(Guid EnrolmentId) : IDomainEvent;

/// FR-S42: SMS `shipment_shipped` (reference, tracking).
public sealed record ShipmentShipped(Guid ShipmentId, Guid OrderId) : IDomainEvent;

/// FR-S42: SMS `shipment_delivered`.
public sealed record ShipmentDelivered(Guid ShipmentId, Guid OrderId) : IDomainEvent;

/// Staff e-mail (FR-S43, S-41): a print format's `available` is at or below its threshold (`low_stock_threshold`, else the
/// `low_stock_default_threshold` setting). At most one per format per Riyadh day.
public sealed record LowStockAlert(Guid FormatId, string Sku, string TitleAr, int Available, int Threshold, DateOnly Day) : IDomainEvent;

/// RUL-52 (sent from S-25): SMS invitation to a beneficiary with an allocated course seat (sent to the beneficiary's or the
/// guardian's mobile; never shown to donors).
public sealed record BeneficiaryInvite(Guid BeneficiaryId, Guid EnrolmentId) : IDomainEvent;

/// RUL-50 digest feed (S-45): a unit was allocated / completed. No customer message.
public sealed record SponsorUnitAllocated(Guid UnitId, Guid ProductId, Guid? OrderLineId) : IDomainEvent;

public sealed record SponsorUnitCompleted(Guid UnitId, Guid ProductId, Guid? OrderLineId) : IDomainEvent;

/// RUL-63 `SponsorDigest` job (S-45): one monthly digest for a sponsor covering `(Since, Until]`; `Period` (Riyadh `yyyy-MM`)
/// makes a second run in the same month a no-op, and `Until` is the sponsor's "last digest" time for the next run.
public sealed record SponsorDigest(Guid CustomerId, string Period, DateTimeOffset Since, DateTimeOffset Until) : IDomainEvent;

/// RUL-50: short SMS to the former beneficiary (or guardian) of a lapsed seat.
public sealed record UnitLapsed(Guid UnitId, Guid BeneficiaryId, Guid ProductId) : IDomainEvent;

/// RUL-49: units awaiting allocation past `allocation_alert_days` (staff) / `allocation_escalate_days` (manager), once per
/// product per Riyadh day.
public sealed record AllocationAlert(Guid ProductId, string Slug, int Units, DateTimeOffset OldestFundedAt, DateOnly Day) : IDomainEvent;

public sealed record AllocationEscalation(Guid ProductId, string Slug, int Units, DateTimeOffset OldestFundedAt, DateOnly Day) : IDomainEvent;

/// RUL-54: SMS to every distinct paid donor of the product, once per update (and once per donor on retry).
public sealed record CauseUpdatePublished(Guid UpdateId, Guid ProductId) : IDomainEvent;

/// RUL-46: a pool remainder moved to the general fund (account-history note for the contributors, shown from
/// `swept_to_fund_at`; nothing is sent).
public sealed record PoolSwept(Guid OfferId, Guid ProductId, Guid GeneralFundId, Guid PairId, string Amount) : IDomainEvent;

/// A product was published or archived: ask ta3lom-web to rebuild (debounced — at most one pending message).
public sealed record CatalogChanged(Guid ProductId, string Change) : IDomainEvent;
