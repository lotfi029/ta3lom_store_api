using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Application.DTOs.Orders;

namespace Ta3lom.Store.Application.Features.Orders.Queries;

/// `GET /v1/admin/orders` (#85, FR-S73): `q` = reference (prefix) or mobile (any format), plus status, Riyadh date range and a
/// line kind; newest first.
public sealed record SearchOrdersQuery(string? Q, string? Status, DateOnly? From, DateOnly? To, string? Kind, int Page, int PageSize)
    : IQuery<PagedResult<AdminOrderRow>>;

internal sealed class SearchOrdersQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchOrdersQuery, PagedResult<AdminOrderRow>>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<PagedResult<AdminOrderRow>>> HandleAsync(SearchOrdersQuery query, CancellationToken ct = default)
    {
        var orders = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            orders = SaudiMobile.TryNormalize(q, out var mobile)
                ? orders.Where(o => o.ContactMobile == mobile)
                : orders.Where(o => EF.Functions.Like(o.Reference, q.ToUpperInvariant().Replace("%", "", StringComparison.Ordinal) + "%"));
        }

        if (query.Status is not null)
        {
            if (!EnumWire.TryParse<OrderStatus>(query.Status, out var status))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown order status."] });
            }

            orders = orders.Where(o => o.Status == status);
        }

        if (query.Kind is not null)
        {
            if (!EnumWire.TryParse<LineKind>(query.Kind, out var kind))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["kind"] = ["kind must be buy, sponsor or donate."] });
            }

            orders = orders.Where(o => db.OrderLines.Any(l => l.OrderId == o.Id && l.Kind == kind));
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
            orders = orders.Where(o => o.PlacedAt >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
            orders = orders.Where(o => o.PlacedAt < end);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await orders.CountAsync(ct);
        var rows = await orders.OrderByDescending(o => o.PlacedAt).Skip(page.Skip).Take(page.Take)
            .Select(o => new
            {
                o.Reference,
                o.Status,
                o.PlacedAt,
                o.PaidAt,
                o.ContactName,
                o.ContactMobile,
                o.GrandTotal,
                Kinds = db.OrderLines.Where(l => l.OrderId == o.Id).Select(l => l.Kind).ToList(),
            }).ToListAsync(ct);
        return new PagedResult<AdminOrderRow>([.. rows.Select(r => new AdminOrderRow(r.Reference, r.Status.ToWire(), r.PlacedAt, r.PaidAt, r.ContactName,
            SaudiMobile.Mask(r.ContactMobile), r.GrandTotal.ToWire(), r.Kinds.Count, [.. r.Kinds.Distinct().Select(k => k.ToWire())]))],
            total, page.SafePage, page.Take);
    }
}

/// `GET /v1/admin/orders/{ref}` (#86): lines, payments, events, documents, notifications, refunds and shipment (11 §9.9).
public sealed record GetAdminOrderQuery(string Reference) : IQuery<AdminOrderDetailResponse>;

internal sealed class GetAdminOrderQueryHandler(OrderReader reader) : IQueryHandler<GetAdminOrderQuery, AdminOrderDetailResponse>
{
    public async Task<Result<AdminOrderDetailResponse>> HandleAsync(GetAdminOrderQuery query, CancellationToken ct = default)
    {
        var order = await reader.FindAsync(query.Reference.ToUpper(CultureInfo.InvariantCulture), ct);
        return order is null ? CheckoutErrors.OrderNotFound : await reader.AdminDetailAsync(order, ct);
    }
}
