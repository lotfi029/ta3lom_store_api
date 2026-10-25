namespace Ta3lom.Store.Application.DTOs.AdminOps;

/// `GET /v1/admin/dashboard` (BR-A13): counts and the projects with the largest pending amount. Paid totals arrive with
/// R5-Payments. "Today" and "this week" are Riyadh dates; the week starts on Sunday.
public sealed record DashboardResponse(int PublishedProjects, int ActiveCategories, int ActiveDonors, int PendingOrdersToday, int PendingOrdersThisWeek,
    IReadOnlyList<DashboardProject> TopProjectsByPendingAmount);

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
    IReadOnlyList<AdminOrderLine> Lines);

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
