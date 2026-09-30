using Ta3lom.Store.Application.DTOs.Orders;

namespace Ta3lom.Store.Application.DTOs.Fulfilment;

/// `Shipment` (11 §9.12).
public sealed record ShipmentResponse(Guid Id, string OrderRef, string Status, AdminShippingAddress Address, IReadOnlyList<ShipmentLine> Lines,
    string? Carrier, string? TrackingNo, DateTimeOffset PlacedAt, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);

public sealed record ShipmentLine(string Sku, string TitleAr, int Quantity);

public sealed record ShipRequest(string Carrier, string TrackingNo);

public sealed record DeliverRequest(DateOnly? DeliveredOn);

public sealed record ReturnShipmentRequest(string Note, bool Restock);

/// `StockRow` (11 §9.7).
public sealed record StockRow(Guid FormatId, string Sku, string TitleAr, int OnHand, int Reserved, int Available, int? LowStockThreshold,
    int EffectiveThreshold, bool IsLow);

public sealed record StockMovementResponse(long Id, int Delta, string Reason, string? RefType, string? RefId, string? Note, DateTimeOffset At, string Actor);

/// `POST /admin/stock/{formatId}/movements`: reason `restock` | `adjustment` | `return`.
public sealed record StockMovementRequest(int Delta, string Reason, string? Note);

/// `AdminOrderRow` for `GET /admin/orders` (11 §9.9); the mobile is masked.
public sealed record AdminOrderRow(string Reference, string Status, DateTimeOffset PlacedAt, DateTimeOffset? PaidAt, string ContactName,
    string ContactMobile, string GrandTotal, int LineCount, IReadOnlyList<string> Kinds);
