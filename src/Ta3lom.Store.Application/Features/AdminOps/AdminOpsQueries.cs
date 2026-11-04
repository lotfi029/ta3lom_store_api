using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.AdminOps;

internal static class RiyadhDay
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// Midnight in Riyadh as a UTC instant (Npgsql writes timestamptz parameters in UTC only).
    public static DateTimeOffset Start(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();
}

/// `GET /v1/admin/dashboard` (all staff).
public sealed record GetDashboardQuery : IQuery<DashboardResponse>;

internal sealed class GetDashboardQueryHandler(IApplicationDbContext db, IClock clock) : IQueryHandler<GetDashboardQuery, DashboardResponse>
{
    public const int TopCount = 5;

    public async Task<Result<DashboardResponse>> HandleAsync(GetDashboardQuery query, CancellationToken ct = default)
    {
        var today = clock.RiyadhToday;
        var dayStart = RiyadhDay.Start(today);
        var weekStart = RiyadhDay.Start(today.AddDays(-(int)today.DayOfWeek));
        var pending = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.PendingPayment);
        var top = await (
            from s in db.CatalogStats.AsNoTracking()
            join p in db.Projects.AsNoTracking() on s.TargetId equals p.Id
            where s.TargetKind == TargetKind.Project
            orderby s.PendingAmount descending, p.Id
            select new { p.Id, p.Slug, p.TitleAr, s.PendingOrders, s.PendingAmount }).Take(TopCount).ToListAsync(ct);
        var monthStart = RiyadhDay.Start(new DateOnly(today.Year, today.Month, 1));
        var paid = db.Orders.AsNoTracking().Where(o => o.PaidAt != null && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Refunded));
        var since = weekStart < monthStart ? weekStart : monthStart;            // a week can start in the previous month
        var paidMonth = await paid.Where(o => o.PaidAt >= since).Select(o => new { o.PaidAt, o.TotalAmount, o.RefundedAmount }).ToListAsync(ct);
        DashboardPaid Paid(DateTimeOffset from)
        {
            var rows = paidMonth.Where(o => o.PaidAt >= from).ToList();
            return new DashboardPaid(rows.Select(o => o.TotalAmount - o.RefundedAmount).Sum().ToWire(), rows.Count);
        }

        var paidLines = await (
            from l in db.OrderLines.AsNoTracking()
            join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
            where l.ProjectId != null && o.Status == OrderStatus.Paid
            select new { ProjectId = l.ProjectId!.Value, l.OrderId, l.Amount }).ToListAsync(ct);
        var topPaidIds = paidLines.GroupBy(l => l.ProjectId)
            .Select(g => new { g.Key, Orders = g.Select(x => x.OrderId).Distinct().Count(), Amount = g.Select(x => x.Amount).Sum() })
            .OrderByDescending(g => g.Amount.Amount).ThenBy(g => g.Key).Take(TopCount).ToList();
        var keys = topPaidIds.Select(t => t.Key).ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => keys.Contains(p.Id)).Select(p => new { p.Id, p.Slug, p.TitleAr }).ToListAsync(ct);
        return new DashboardResponse(
            await db.Projects.CountAsync(p => p.Status == ProjectStatus.Published, ct),
            await db.Categories.CountAsync(c => c.IsActive, ct),
            await db.Customers.CountAsync(c => c.Status == CustomerStatus.Active, ct),
            await pending.CountAsync(o => o.CreatedAt >= dayStart, ct),
            await pending.CountAsync(o => o.CreatedAt >= weekStart, ct),
            [.. top.Select(t => new DashboardProject(t.Id, t.Slug, t.TitleAr, t.PendingOrders, t.PendingAmount.ToWire()))])
        {
            PaidToday = Paid(dayStart),
            PaidThisWeek = Paid(weekStart),
            PaidThisMonth = Paid(monthStart),
            PendingOrders = await pending.CountAsync(ct),
            InReviewOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.PaymentReview, ct),
            TopProjectsByPaidAmount = [.. topPaidIds.Join(projects, t => t.Key, p => p.Id, (t, p) => new DashboardPaidProject(p.Id, p.Slug, p.TitleAr, t.Orders, t.Amount.ToWire()))],
        };
    }
}

/// `GET /v1/admin/customers` (q, status, page, pageSize): `q` is a mobile (exact) or part of a name or e-mail.
public sealed record ListCustomersQuery(string? Q, string? Status, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AdminCustomerRow>>;

internal sealed class ListCustomersQueryHandler(IApplicationDbContext db) : IQueryHandler<ListCustomersQuery, PagedResult<AdminCustomerRow>>
{
    public async Task<Result<PagedResult<AdminCustomerRow>>> HandleAsync(ListCustomersQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var customers = db.Customers.AsNoTracking();
        if (EnumWire.TryParse<CustomerStatus>(query.Status, out var status))
        {
            customers = customers.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            if (SaudiMobile.TryNormalize(q, out var mobile))
            {
                customers = customers.Where(c => c.Mobile == mobile);
            }
            else
            {
                var lower = q.ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // translated to SQL lower(...) and strpos; no culture is involved
                customers = customers.Where(c => (c.FullName != null && c.FullName.ToLower().Contains(lower)) || (c.Email != null && c.Email.ToLower().Contains(lower)));
#pragma warning restore CA1304, CA1311, CA1862
            }
        }

        var total = await customers.CountAsync(ct);
        var rows = await customers.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Skip(page.Skip).Take(page.Take)
            .Select(c => new { c.Id, c.Mobile, c.FullName, c.Email, c.Status, Orders = db.Orders.Count(o => o.CustomerId == c.Id), c.CreatedAt, c.LastLoginAt })
            .ToListAsync(ct);
        return new PagedResult<AdminCustomerRow>(
            [.. rows.Select(r => new AdminCustomerRow(r.Id, r.Mobile, r.FullName, r.Email, r.Status.ToWire(), r.Orders, r.CreatedAt, r.LastLoginAt))],
            total, page.SafePage, page.Take);
    }
}

internal sealed class ListCustomersQueryValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(100);
        RuleFor(q => q.Status).Must(s => s is null or "active" or "blocked" or "anonymised").WithMessage("status must be active, blocked or anonymised.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

/// `GET /v1/admin/customers/{id}`.
public sealed record GetCustomerQuery(Guid Id) : IQuery<AdminCustomerDetail>;

internal sealed class GetCustomerQueryHandler(IApplicationDbContext db) : IQueryHandler<GetCustomerQuery, AdminCustomerDetail>
{
    public const int RecentOrders = 20;

    public async Task<Result<AdminCustomerDetail>> HandleAsync(GetCustomerQuery query, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
        if (c is null)
        {
            return GeneralErrors.NotFound;
        }

        var orders = await db.Orders.AsNoTracking().Where(o => o.CustomerId == c.Id).OrderByDescending(o => o.CreatedAt).Take(RecentOrders).ToListAsync(ct);
        return new AdminCustomerDetail(c.Id, c.Mobile, c.FullName, c.Email, c.Locale, c.MarketingOptIn, c.Status.ToWire(), c.PdplConsentAt, c.CreatedAt,
            c.LastLoginAt, [.. orders.Select(AdminOrderMapping.ToRow)], c.Version);
    }
}

internal static class AdminOrderMapping
{
    public static AdminOrderRow ToRow(Domain.Entities.Donations.Order o) =>
        new(o.Reference, o.Status.ToWire(), o.TotalAmount.ToWire(), o.LineCount, o.DonorName, o.DonorMobile, o.CreatedAt, o.PaymentDueAt);
}

/// `GET /v1/admin/orders` (reference, mobile, status, from, to, page, pageSize); `from`/`to` are Riyadh dates, inclusive.
public sealed record ListOrdersQuery(string? Reference, string? Mobile, string? Status, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AdminOrderRow>>;

internal sealed class ListOrdersQueryHandler(IApplicationDbContext db) : IQueryHandler<ListOrdersQuery, PagedResult<AdminOrderRow>>
{
    public async Task<Result<PagedResult<AdminOrderRow>>> HandleAsync(ListOrdersQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var orders = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Reference))
        {
            var reference = Order.NormalizeReference(query.Reference);
            orders = orders.Where(o => o.Reference == reference);
        }

        if (query.Mobile is not null)
        {
            var mobile = SaudiMobile.TryNormalize(query.Mobile, out var normalized) ? normalized : query.Mobile;
            orders = orders.Where(o => o.DonorMobile == mobile);
        }

        if (EnumWire.TryParse<OrderStatus>(query.Status, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.From is { } from)
        {
            var start = RiyadhDay.Start(from);
            orders = orders.Where(o => o.CreatedAt >= start);
        }

        if (query.To is { } to)
        {
            var end = RiyadhDay.Start(to.AddDays(1));
            orders = orders.Where(o => o.CreatedAt < end);
        }

        var total = await orders.CountAsync(ct);
        var rows = await orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<AdminOrderRow>([.. rows.Select(AdminOrderMapping.ToRow)], total, page.SafePage, page.Take);
    }
}

internal sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersQueryValidator()
    {
        RuleFor(q => q.Reference).MaximumLength(20);
        RuleFor(q => q.Mobile).MaximumLength(20);
        RuleFor(q => q.Status).Must(s => s is null || EnumWire.TryParse<OrderStatus>(s, out _)).WithMessage("Unknown order status.");
        RuleFor(q => q).Must(q => q.From is null || q.To is null || q.From <= q.To).WithName("to").WithMessage("to must not be before from.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

/// `GET /v1/admin/orders/{reference}`.
public sealed record GetAdminOrderQuery(string Reference) : IQuery<AdminOrderDetail>;

internal sealed class GetAdminOrderQueryHandler(IApplicationDbContext db) : IQueryHandler<GetAdminOrderQuery, AdminOrderDetail>
{
    public async Task<Result<AdminOrderDetail>> HandleAsync(GetAdminOrderQuery query, CancellationToken ct = default)
    {
        var reference = Order.NormalizeReference(query.Reference);                   // L-17/API-2: references are stored upper-case
        var o = await db.Orders.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Reference == reference, ct);
        if (o is null)
        {
            return OrderErrors.NotFound;
        }

        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == o.Id).OrderBy(p => p.AttemptNo).ToListAsync(ct);
        var ids = payments.Select(p => p.Id).ToList();
        var events = await db.PaymentEvents.AsNoTracking().Where(e => e.PaymentId != null && ids.Contains(e.PaymentId.Value))
            .OrderBy(e => e.ReceivedAt).Select(e => new AdminPaymentEventRow(e.Type, e.ReceivedAt, e.SignatureValid, e.Outcome)).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == o.Id).OrderBy(r => r.RequestedAt).ToListAsync(ct);
        var receipts = await db.DonationReceipts.AsNoTracking().Where(r => r.OrderId == o.Id).OrderBy(r => r.IssuedAt).ToListAsync(ct);
        var refundable = o.Status == OrderStatus.Paid
            ? payments.Where(p => p.Status == PaymentStatus.Captured).Select(p => p.Refundable).Sum()
            : Money.Zero;
        return new AdminOrderDetail(o.Reference, o.Status.ToWire(), o.TotalAmount.ToWire(), o.CustomerId, o.DonorName, o.DonorEmail, o.DonorMobile, o.Locale,
            o.CreatedAt, o.PaymentDueAt, o.CancelledAt, o.CancelledBy,
            [.. o.Lines.OrderBy(l => l.LineNo).Select(l => new AdminOrderLine(l.LineNo, l.TargetKind.ToWire(), l.ProjectId, l.CategoryId, l.OptionId,
                l.TitleAr, l.TitleEn, l.OptionLabelAr, l.Quantity, l.UnitAmount.ToWire(), l.Amount.ToWire()))])
        {
            Version = o.Version,
            PaidAt = o.PaidAt,
            RefundedAmount = o.RefundedAmount.ToWire(),
            ReviewReason = o.ReviewReason?.ToWire(),
            Refundable = refundable.ToWire(),
            Payments = [.. payments.Select(p => new AdminPaymentRow(p.Id, p.AttemptNo, p.Provider, p.Status.ToWire(), p.Method.ToWire(), p.Amount.ToWire(),
                p.CapturedAmount.ToWire(), p.RefundedAmount.ToWire(), p.ReviewReason?.ToWire(), p.FailureMessage, p.InitiatedAt, p.CapturedAt))],
            Events = events,
            Refunds = [.. refunds.Select(r => new AdminRefundRow(r.Id, r.Amount.ToWire(), r.Status.ToWire(), r.Reason, r.RequestedBy, r.RequestedAt, r.CompletedAt))],
            Receipts = [.. receipts.Select(r => new AdminReceiptRow(r.DisplayNumber, r.Series.ToWire(), r.Amount.ToWire(), r.IssuedAt,
                r.VoidedByReceiptId is { } v ? receipts.FirstOrDefault(x => x.Id == v)?.DisplayNumber : null))],
        };
    }
}

/// `GET /v1/admin/audit` (actor, action, entity, entityId, page, pageSize; admin), newest first.
public sealed record ListAuditQuery(string? Actor, string? Action, string? Entity, string? EntityId, int Page = 1, int PageSize = 50) : IQuery<PagedResult<AuditRow>>;

internal sealed class ListAuditQueryHandler(IApplicationDbContext db) : IQueryHandler<ListAuditQuery, PagedResult<AuditRow>>
{
    public async Task<Result<PagedResult<AuditRow>>> HandleAsync(ListAuditQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var rows = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            rows = rows.Where(a => a.Actor == query.Actor);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            rows = rows.Where(a => a.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            rows = rows.Where(a => a.Entity == query.Entity);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            rows = rows.Where(a => a.EntityId == query.EntityId);
        }

        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.Take)
            .Select(a => new AuditRow(a.Id, a.At, a.Actor, a.Action, a.Entity, a.EntityId, a.Diff)).ToListAsync(ct);
        return new PagedResult<AuditRow>(items, total, page.SafePage, page.Take);
    }
}

internal sealed class ListAuditQueryValidator : AbstractValidator<ListAuditQuery>
{
    public ListAuditQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}
