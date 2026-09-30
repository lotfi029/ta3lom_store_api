using System.Text.Json;
using Ta3lom.Store.Application.DTOs.Account;

namespace Ta3lom.Store.Application.DTOs.AdminOps;

/// `GET /v1/admin/dashboard` (11 §9.2, FR-S74). Periods are Riyadh days ending today.
public sealed record DashboardResponse(PeriodTotals Sales, PeriodTotals Gifts, int OrdersAwaitingPayment, int OrdersInReview,
    IReadOnlyList<TopProduct> TopProducts, IReadOnlyList<CampaignProgress> Campaigns, AllocationSummary Allocation, IReadOnlyList<DashboardAlert> Alerts);

public sealed record PeriodTotals(string Today, string Last7Days, string Last30Days);

public sealed record TopProduct(string Slug, string Amount);

public sealed record CampaignProgress(string Slug, int Percent, DateTimeOffset? EndsAt);

public sealed record AllocationSummary(long Awaiting, long Overdue30, DateTimeOffset? OldestAwaiting);

/// `allocation.overdue`, `outbox.dead`, `integrity.failed`.
public sealed record DashboardAlert(string Code, long Count);

/// `GET /v1/admin/reports/{name}` JSON (11 §9.16): rows keyed by camelCase column names; money as strings.
public sealed record ReportResponse(string Name, DateOnly From, DateOnly To, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// A report file (`text/csv`, UTF-8 with BOM).
public sealed record ReportFile(byte[] Content, string FileName);

/// 11 §9.20 `CustomerRow` (staff view: full mobile).
public sealed record CustomerRow(Guid Id, string Mobile, string? FullName, string? Email, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt,
    int OrdersCount);

/// `Customer360`: profile, orders, giving totals, enrolments.
public sealed record Customer360(CustomerRow Customer, IReadOnlyList<OrderSummary> Orders, GivingTotals Giving, IReadOnlyList<EnrolmentItem> Enrolments);

public sealed record BlockCustomerRequest(string? Reason);

/// 11 §9.21: `diff` is the stored JSON object (personal fields already masked as `***`).
public sealed record AuditEntry(long Id, DateTimeOffset At, string Actor, string Action, string Entity, string? EntityId, JsonElement? Diff);
