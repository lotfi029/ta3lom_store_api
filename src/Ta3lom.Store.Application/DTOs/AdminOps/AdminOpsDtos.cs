namespace Ta3lom.Store.Application.DTOs.AdminOps;

/// `GET /v1/admin/dashboard` (BR-A13): counts and the projects with the largest pending amount. Paid totals arrive with
/// R5-Payments. "Today" and "this week" are Riyadh dates; the week starts on Sunday.
public sealed record DashboardResponse(int PublishedProjects, int ActiveCategories, int ActiveDonors, int PendingOrdersToday, int PendingOrdersThisWeek,
    IReadOnlyList<DashboardProject> TopProjectsByPendingAmount)
{
    /// Paid donations (R5-Payments, plan §4): Riyadh today / this week (from Sunday) / this month, net of refunds.
    public DashboardPaid PaidToday { get; init; } = DashboardPaid.None;

    public DashboardPaid PaidThisWeek { get; init; } = DashboardPaid.None;

    public DashboardPaid PaidThisMonth { get; init; } = DashboardPaid.None;

    /// Orders waiting for payment now, and orders in payment review.
    public int PendingOrders { get; init; }

    public int InReviewOrders { get; init; }

    /// Captured payments with no order of ours, not yet resolved by finance (review 04 A-3).
    public int OpenOrphanPayments { get; init; }

    /// Top 5 projects by paid amount (paid lines of paid/partially refunded orders).
    public IReadOnlyList<DashboardPaidProject> TopProjectsByPaidAmount { get; init; } = [];
}

/// `amount` = paid minus refunded (`"0.00"`), `count` = orders paid in the period.
public sealed record DashboardPaid(string Amount, int Count)
{
    public static DashboardPaid None { get; } = new("0.00", 0);
}

public sealed record DashboardPaidProject(Guid ProjectId, string Slug, string TitleAr, int PaidOrders, string PaidAmount);

public sealed record DashboardProject(Guid ProjectId, string Slug, string TitleAr, int PendingOrders, string PendingAmount);

/// `GET /v1/admin/customers`.
public sealed record AdminCustomerRow(Guid Id, string? Mobile, string? FullName, string? Email, string Status, int OrderCount, DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// `GET /v1/admin/customers/{id}`: the donor with their latest orders.
public sealed record AdminCustomerDetail(Guid Id, string? Mobile, string? FullName, string? Email, string Locale, bool MarketingOptIn, string Status,
    DateTimeOffset? PdplConsentAt, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, IReadOnlyList<AdminOrderRow> RecentOrders, uint Version);

/// `GET /v1/admin/orders`.
public sealed record AdminOrderRow(string Reference, string Status, string TotalAmount, int LineCount, string DonorName, string DonorMobile,
    DateTimeOffset CreatedAt, DateTimeOffset PaymentDueAt);

public sealed record AdminOrderLine(int LineNo, string TargetKind, Guid? ProjectId, Guid? CategoryId, Guid? OptionId, string TitleAr, string? TitleEn,
    string? OptionLabelAr, int Quantity, string UnitAmount, string Amount);

/// `GET /v1/admin/orders/{reference}`.
public sealed record AdminOrderDetail(string Reference, string Status, string TotalAmount, Guid CustomerId, string DonorName, string DonorEmail,
    string DonorMobile, string Locale, DateTimeOffset CreatedAt, DateTimeOffset PaymentDueAt, DateTimeOffset? CancelledAt, string? CancelledBy,
    IReadOnlyList<AdminOrderLine> Lines)
{
    /// The order's row version (send it back with refunds and review actions).
    public uint Version { get; init; }

    public DateTimeOffset? PaidAt { get; init; }

    public string RefundedAmount { get; init; } = "0.00";

    /// `amount_mismatch` | `late_capture` | `double_payment` while (or after) in review.
    public string? ReviewReason { get; init; }

    /// Refundable now (captured − refunded on captured, non-review payments; `"0.00"` when nothing can be refunded).
    public string Refundable { get; init; } = "0.00";

    public IReadOnlyList<AdminPaymentRow> Payments { get; init; } = [];

    /// Webhook/reconciliation events of the order's payments (type, time, outcome; never the payload).
    public IReadOnlyList<AdminPaymentEventRow> Events { get; init; } = [];

    public IReadOnlyList<AdminRefundRow> Refunds { get; init; } = [];

    public IReadOnlyList<AdminReceiptRow> Receipts { get; init; } = [];
}

public sealed record AdminPaymentRow(Guid Id, int AttemptNo, string Provider, string Status, string Method, string Amount, string CapturedAmount,
    string RefundedAmount, string? ReviewReason, string? FailureMessage, DateTimeOffset InitiatedAt, DateTimeOffset? CapturedAt);

public sealed record AdminPaymentEventRow(string Type, DateTimeOffset ReceivedAt, bool SignatureValid, string? Outcome);

/// `status`: requested (waiting for the gateway, review 04 P-3) | succeeded | failed. `source`: `store` (finance) or
/// `gateway` (made on the gateway's dashboard and imported, A-2; `requestedBy` = "moyasar").
public sealed record AdminRefundRow(Guid Id, string Amount, string Status, string Reason, string RequestedBy, DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt, string Source);

public sealed record AdminReceiptRow(string Number, string Series, string Amount, DateTimeOffset IssuedAt, string? VoidedBy);

/// `GET /v1/admin/templates`.
public sealed record TemplateResponse(string Key, string Channel, string Locale, string? Subject, string Body, DateTimeOffset UpdatedAt, string? UpdatedBy);

/// `PUT /v1/admin/templates/{key}/{channel}/{locale}`.
public sealed record TemplateInput(string? Subject, string Body);

/// `POST /v1/admin/templates/test`: renders an unsaved text with sample values and sends it — an e-mail to the calling
/// staff member, an SMS to `to`.
public sealed record TemplateTestRequest(string Channel, string? Subject, string Body, string? To);

public sealed record TemplateTestResponse(string? Subject, string Body, bool Sent);

/// `GET /v1/admin/audit`.
public sealed record AuditRow(long Id, DateTimeOffset At, string Actor, string Action, string Entity, string? EntityId, string? Diff);

// ── Payments: refunds and review (R5-Payments, plan §4) ─────────────────────────────────────────────────────────────

/// `POST /v1/admin/orders/{reference}/refunds`: an amount (`"50.00"`) of the captured payment, a reason, and the order's
/// `version` as read (409 `concurrency.conflict` otherwise). `Idempotency-Key` required.
public sealed record AdminRefundRequest(string Amount, string Reason, uint Version);

/// `POST /v1/admin/orders/{reference}/review/accept|refund`: the order's `version`; a reason for the refund.
public sealed record AdminReviewRequest(uint Version, string? Reason);

/// The order after a finance action. `Refund` (refund actions only): `succeeded`, or `pending` — the gateway gave no
/// definite answer, the refund stays requested (202) and is confirmed by the reconciliation or a retry with the same key.
public sealed record AdminPaymentActionResponse(string Reference, string Status, string TotalAmount, string RefundedAmount, uint Version,
    string? ReceiptNumber, string? Refund = null);

/// `GET /v1/admin/payments/orphans` (review 04 A-3): a captured payment with no order of ours. Amount as the gateway
/// reported it; `currency` null when the gateway sent none.
public sealed record OrphanPaymentRow(Guid Id, string Provider, string? ProviderPaymentId, string? ProviderInvoiceId, string Amount, string? Currency,
    DateTimeOffset ReceivedAt, DateTimeOffset? ResolvedAt, string? ResolvedBy, string? ResolutionNote);

/// `POST /v1/admin/payments/orphans/{id}/resolve`: what finance did about it (1–500 characters).
public sealed record ResolveOrphanRequest(string Note);
