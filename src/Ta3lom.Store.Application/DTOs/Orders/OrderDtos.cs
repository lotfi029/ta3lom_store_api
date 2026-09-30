namespace Ta3lom.Store.Application.DTOs.Orders;

/// `OrderDetail` (11 §5.10).
public sealed record OrderDetailResponse(string Reference, string Status, DateTimeOffset PlacedAt, DateTimeOffset? PaidAt, string PurchasesTotal,
    string ShippingTotal, string GiftsTotal, string VatTotal, string GrandTotal, string RefundedTotal, bool IsAnonymous,
    IReadOnlyList<OrderLineResponse> Lines, IReadOnlyList<OrderPaymentItem> Payments, IReadOnlyList<OrderDocumentItem> Documents,
    OrderShipmentInfo? Shipment);

/// `format` only on book purchases; `dedication` only on dedicated gift lines.
public sealed record OrderLineResponse(short LineNo, string Kind, string Mode, string TitleAr, string? TitleEn, string? Format, int Quantity,
    string UnitPrice, string Amount, string VatAmount, string? Intent, string FulfilmentStatus, string RefundedAmount, OrderLineDedication? Dedication);

public sealed record OrderLineDedication(string ToName, string SmsStatus);

public sealed record OrderPaymentItem(string? Method, string Status, string Amount, DateTimeOffset CreatedAt);

public sealed record OrderDocumentItem(string Kind, string Number, DateTimeOffset IssuedAt, bool Ready);

public sealed record OrderShipmentInfo(string Status, string? Carrier, string? TrackingNo);

/// `AdminOrderDetail` (11 §9.9) = `OrderDetail` plus customer, contact, address, payments, events, notifications, refunds.
public sealed record AdminOrderDetailResponse(string Reference, string Status, DateTimeOffset PlacedAt, DateTimeOffset? PaidAt,
    DateTimeOffset PaymentDueAt, DateTimeOffset? CancelledAt, string PurchasesTotal, string ShippingTotal, string GiftsTotal, string VatTotal,
    string GrandTotal, string RefundedTotal, bool IsAnonymous, IReadOnlyList<OrderLineResponse> Lines, IReadOnlyList<OrderDocumentItem> Documents,
    OrderShipmentInfo? Shipment, AdminOrderCustomer Customer, AdminOrderContact Contact, AdminShippingAddress? ShippingAddress,
    IReadOnlyList<AdminOrderPayment> Payments, IReadOnlyList<AdminPaymentEvent> Events, IReadOnlyList<AdminOrderNotification> Notifications,
    IReadOnlyList<AdminOrderRefund> Refunds, Guid? SourceCartId);

public sealed record AdminOrderCustomer(Guid Id, string Mobile, string? FullName);

public sealed record AdminOrderContact(string Name, string Mobile, string Email);

public sealed record AdminShippingAddress(string RecipientName, string RecipientMobile, string City, string? District, string? ShortAddress,
    string? Street, string? BuildingNo, string? PostalCode);

public sealed record AdminOrderPayment(Guid Id, string Provider, string? ProviderPaymentId, string? Method, string Amount, string Status,
    string? FailureCode, DateTimeOffset CreatedAt);

public sealed record AdminPaymentEvent(string ProviderEventId, string EventType, bool SignatureValid, DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt, string? Error);

/// `recipient` masked (`05•••••567`, `n•••@example.com`).
public sealed record AdminOrderNotification(string Channel, string TemplateKey, string Recipient, string Status, DateTimeOffset? SentAt);

public sealed record AdminOrderRefund(Guid Id, string Amount, string Status, string Reason, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

/// `POST /v1/admin/orders/{ref}/cancel` body.
public sealed record CancelOrderRequest(string Reason);
public sealed record ResendDocumentsRequest(IReadOnlyList<string> Channels);
