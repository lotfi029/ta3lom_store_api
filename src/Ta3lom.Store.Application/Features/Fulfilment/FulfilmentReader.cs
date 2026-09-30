using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Fulfilment;

internal sealed class FulfilmentReader(IApplicationDbContext db, IStoreSettings settings)
{
    public async Task<ShipmentResponse> ShipmentAsync(Shipment s, CancellationToken ct) => (await ShipmentsAsync([s], ct))[0];

    public async Task<IReadOnlyList<ShipmentResponse>> ShipmentsAsync(IReadOnlyList<Shipment> shipments, CancellationToken ct)
    {
        var orderIds = shipments.Select(s => s.OrderId).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Reference, o.PlacedAt }).ToDictionaryAsync(o => o.Id, ct);
        var lines = await (from l in db.OrderLines.AsNoTracking()
                           join f in db.BookFormats.AsNoTracking() on l.BookFormatId equals f.Id
                           where orderIds.Contains(l.OrderId) && f.Kind == BookFormatKind.Print
                           orderby l.LineNo
                           select new { l.OrderId, f.Sku, l.TitleAr, l.Quantity }).ToListAsync(ct);
        return [.. shipments.Select(s =>
        {
            var a = s.Address;
            return new ShipmentResponse(s.Id, orders[s.OrderId].Reference, s.Status.ToWire(),
                new AdminShippingAddress(a.RecipientName, a.RecipientMobile, a.City, a.District, a.ShortAddress, a.Street, a.BuildingNo, a.PostalCode),
                [.. lines.Where(l => l.OrderId == s.OrderId).Select(l => new ShipmentLine(l.Sku, l.TitleAr, l.Quantity))],
                s.Carrier, s.TrackingNo, orders[s.OrderId].PlacedAt, s.ShippedAt, s.DeliveredAt);
        })];
    }

    public async Task<IReadOnlyList<StockRow>> StockAsync(Guid? formatId, bool lowOnly, CancellationToken ct)
    {
        var fallback = (await settings.GetAsync(ct)).LowStockDefaultThreshold;
        var rows = await (from f in db.BookFormats.AsNoTracking()
                          join p in db.Products.AsNoTracking() on f.ProductId equals p.Id
                          where f.Kind == BookFormatKind.Print && (formatId == null || f.Id == formatId)
                          orderby p.Slug, f.Sku
                          select new { f.Id, f.Sku, p.TitleAr, OnHand = f.StockOnHand ?? 0, f.StockReserved, f.LowStockThreshold }).ToListAsync(ct);
        return [.. rows.Select(r =>
        {
            var threshold = r.LowStockThreshold ?? fallback;
            var available = Math.Max(0, r.OnHand - r.StockReserved);
            return new StockRow(r.Id, r.Sku, r.TitleAr, r.OnHand, r.StockReserved, available, r.LowStockThreshold, threshold, available <= threshold);
        }).Where(r => !lowOnly || r.IsLow)];
    }
}
