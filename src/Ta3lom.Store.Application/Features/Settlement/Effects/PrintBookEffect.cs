using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-39/30 `buy` print book (S-21): the reservation becomes a sale (`stock_on_hand −= q`, `stock_reserved −= q`), a `sale`
/// stock movement, and the order's single shipment (`preparing`, address snapshot); the line is `preparing` (RUL-40).
internal sealed class PrintBookEffect(IStockRepository stock, IGenericRepository<StockMovement> movements, IGenericRepository<Shipment> shipments)
    : ILineSettlementEffect
{
    private readonly Dictionary<Guid, Shipment> _shipments = [];                        // one shipment per order (UNIQUE order_id)

    public bool Handles(OrderLine line, SettlementContext context) =>
        line.Kind == LineKind.Buy && line.ProductType == ProductType.Book && context.FormatKind(line) == BookFormatKind.Print;

    public async Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        var order = context.Order;
        var formatId = line.BookFormatId!.Value;
        await stock.CommitSaleAsync(formatId, line.Quantity, ct);
        movements.Add(StockMovement.Record(formatId, -line.Quantity, StockMovementReason.Sale, "order", order.Reference, null, "settlement", context.Now));
        if (!_shipments.ContainsKey(order.Id))
        {
            var shipment = new Shipment(order.Id, (order.ShippingAddress ?? throw new InvalidOperationException("A print order needs an address.")).Copy());
            shipments.Add(shipment);
            _shipments[order.Id] = shipment;
        }

        line.SetFulfilment(FulfilmentStatus.Preparing);
    }
}
