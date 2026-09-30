using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Checkout;

namespace Ta3lom.Store.Application.Features.Checkout.Queries.GetOrderStatus;

/// `GET /v1/orders/{ref}/status` (#42, FR-S35): the owner's bearer, or `?t=` status token; never by reference alone.
/// With the token only, dedications and document numbers are left out.
public sealed record GetOrderStatusQuery(string Reference, string? StatusToken) : IQuery<OrderStatusResponse>;

internal sealed class GetOrderStatusQueryHandler(IApplicationDbContext db, ICurrentActor actor, IOrderStatusTokens tokens, IWebLinks links, IClock clock)
    : IQueryHandler<GetOrderStatusQuery, OrderStatusResponse>
{
    public async Task<Result<OrderStatusResponse>> HandleAsync(GetOrderStatusQuery query, CancellationToken ct = default)
    {
        var isCustomer = actor.Kind == ActorKind.Customer;
        if (!isCustomer && string.IsNullOrEmpty(query.StatusToken))
        {
            return GeneralErrors.Unauthenticated;
        }

        var order = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Reference == query.Reference, ct);
        var owner = order is not null && isCustomer && order.CustomerId == actor.Id;
        if (order is null || (!owner && !tokens.Verify(order.Id, query.StatusToken)))
        {
            return CheckoutErrors.OrderNotFound;
        }

        var payment = await db.Payments.AsNoTracking().Where(p => p.OrderId == order.Id).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        var formatIds = order.Lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).ToList();
        var printFormats = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id) && f.Kind == BookFormatKind.Print)
            .Select(f => f.Id).ToListAsync(ct);

        var lines = order.Lines.OrderBy(l => l.LineNo).ToList();
        List<StatusPurchase> purchases = [.. lines.Where(l => l.Kind == LineKind.Buy).Select(l => new StatusPurchase(l.TitleAr, l.TitleEn,
            l.ProductType == ProductType.Course ? "open_course" : printFormats.Contains(l.BookFormatId ?? Guid.Empty) ? "track_shipment" : "download_book"))];
        List<StatusGift> gifts = [.. lines.Where(l => l.Kind != LineKind.Buy)
            .Select(l => new StatusGift(l.TitleAr, l.TitleEn, l.Mode == LineMode.Units ? l.Quantity : null, "view_giving"))];

        List<StatusDedication>? dedications = null;
        List<StatusDocument>? documents = null;
        if (owner)
        {
            var lineIds = lines.Select(l => l.Id).ToList();
            var cards = await db.Dedications.AsNoTracking().Where(d => lineIds.Contains(d.OrderLineId))
                .Select(d => new { d.ToName, d.CardToken }).ToListAsync(ct);
            dedications = [.. cards.Select(d => new StatusDedication(d.ToName, links.Card(order.Locale, d.CardToken)))];
            var invoices = await db.Invoices.AsNoTracking().Where(i => i.OrderId == order.Id)
                .Select(i => new StatusDocument("invoice", i.Number, i.PdfAssetId != null)).ToListAsync(ct);
            var receipts = await db.DonationReceipts.AsNoTracking().Where(d => d.OrderId == order.Id)
                .Select(d => new StatusDocument("receipt", d.Number, d.PdfAssetId != null)).ToListAsync(ct);
            documents = [.. invoices, .. receipts];
        }

        var canRetry = order.Status == OrderStatus.PaymentFailed && clock.UtcNow < order.PaymentDueAt;   // RUL-37
        return new OrderStatusResponse(order.Reference, order.Status.ToWire(), order.GrandTotal.ToWire(), order.PaidAt, order.PaymentDueAt,
            payment is null ? null : new OrderPaymentStatus(payment.Status.ToWire(), payment.Method?.ToWire(), payment.FailureMessage),
            canRetry, order.Status == OrderStatus.Expired,
            new OrderStatusSummary(purchases, gifts, dedications, documents));
    }
}
