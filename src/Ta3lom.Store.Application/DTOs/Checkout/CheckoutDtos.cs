namespace Ta3lom.Store.Application.DTOs.Checkout;

/// `POST /v1/checkout/quote` body (optional).
public sealed record QuoteRequest(Guid? ShippingAddressId);

/// 11 §6.1: the cart repriced from the catalogue (RUL-15, RUL-24, RUL-26, RUL-27, RUL-31). `cartIssues` carries
/// cart-level failures (RUL-18 limits) that belong to no single line.
public sealed record QuoteResponse(IReadOnlyList<QuoteLine> Lines, string PurchasesTotal, string ShippingTotal, string GiftsTotal, string VatTotal,
    string GrandTotal, bool PriceChanged, bool RequiresShipping, bool HasGifts, bool Valid, IReadOnlyList<string> CartIssues);

/// `status`: `ok` | `changed` | `invalid` (with `code`).
public sealed record QuoteLine(Guid LineId, string Status, string? Code, string? PreviousUnitPrice, string UnitPrice, string Amount, string VatAmount);

/// `POST /v1/checkout` (11 §6.2).
public sealed record CheckoutRequest(CheckoutContact? Contact, bool IsAnonymous, Guid? ShippingAddressId, AddressInput? ShippingAddress, bool SaveAddress,
    string PaymentMethod, bool AcceptTerms, string ExpectedGrandTotal, string? Locale);

public sealed record CheckoutContact(string? FullName, string? Email);

/// Saudi National Address (11 §5.2 `AddressInput`); `recipientMobile` as `05XXXXXXXX`.
public sealed record AddressInput(string RecipientName, string RecipientMobile, string City, string? District, string? ShortAddress, string? Street,
    string? BuildingNo, string? PostalCode);

/// `201` body of `POST /v1/checkout`. With `NullPaymentInitiator` the payment carries no redirect (plan 14 §0).
public sealed record CheckoutResponse(Guid OrderId, string Reference, string Status, string GrandTotal, DateTimeOffset PaymentDueAt, string StatusToken,
    CheckoutPayment Payment);

public sealed record CheckoutPayment(Guid? PaymentId, string? Provider, string Method, string? RedirectUrl, string? SdkPayload, DateTimeOffset ExpiresAt);

/// `GET /v1/orders/{ref}/status` (11 §6.3). With the status token only, `dedications` and `documents` are null.
public sealed record OrderStatusResponse(string Reference, string Status, string GrandTotal, DateTimeOffset? PaidAt, DateTimeOffset PaymentDueAt,
    OrderPaymentStatus? Payment, bool CanRetry, bool CartRestored, OrderStatusSummary Summary);

public sealed record OrderPaymentStatus(string Status, string? Method, string? FailureMessage);

public sealed record OrderStatusSummary(IReadOnlyList<StatusPurchase> Purchases, IReadOnlyList<StatusGift> Gifts,
    IReadOnlyList<StatusDedication>? Dedications, IReadOnlyList<StatusDocument>? Documents);

/// `next`: `open_course` | `download_book` | `track_shipment`.
public sealed record StatusPurchase(string TitleAr, string? TitleEn, string Next);

/// `units` is null for amount lines; `next` = `view_giving`.
public sealed record StatusGift(string TitleAr, string? TitleEn, int? Units, string Next);

public sealed record StatusDedication(string ToName, string CardUrl);

public sealed record StatusDocument(string Kind, string Number, bool Ready);
