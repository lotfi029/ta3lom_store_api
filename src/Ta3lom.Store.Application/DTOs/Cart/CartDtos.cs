namespace Ta3lom.Store.Application.DTOs.Cart;

/// `Cart` (11 §3.1). `id` is null for a visitor without a cart row.
public sealed record CartResponse(Guid? Id, bool IsGuest, IReadOnlyList<CartLineResponse> Purchases, IReadOnlyList<CartLineResponse> Gifts,
    CartSummaryResponse Summary, IReadOnlyList<CartNoticeItem> Notices, DateTimeOffset? ExpiresAt);

/// A cart line; `unitPrice` is indicative (RUL-15). `seatEquivalent` only on sponsor `amount` lines.
public sealed record CartLineResponse(Guid Id, Guid ProductId, string Slug, string Type, string Kind, string Mode, string TitleAr, string? TitleEn,
    CartLineFormat? BookFormat, int Quantity, int MaxQuantity, string UnitPrice, string Amount, string? UnitLabelAr, string? UnitLabelEn,
    string? SeatEquivalent, string? Intent, CartDedicationResponse? Dedication, IReadOnlyList<string> Issues);

public sealed record CartLineFormat(Guid Id, string Kind);

/// Dedication with the recipient mobile masked (`05•••••543`).
public sealed record CartDedicationResponse(string ToName, string ToMobile, string? FromName, string? TemplateKey, string? Text);

public sealed record CartSummaryResponse(string PurchasesTotal, string VatIncluded, string ShippingTotal, string GiftsTotal, string GrandTotal,
    int LineCount, bool RequiresShipping, bool HasGifts);

public sealed record CartNoticeItem(string Code, string Slug);

/// `GET /v1/cart/summary` (11 §3.3): header badge.
public sealed record CartBadgeResponse(int LineCount, string GrandTotal);

/// `POST /v1/cart/lines` (11 §3.4).
public sealed record AddCartLineRequest(Guid ProductId, Guid? BookFormatId, string Kind, string Mode, int Quantity, string? Amount, string? Intent,
    DedicationInput? Dedication);

public sealed record DedicationInput(string ToName, string ToMobile, string? FromName, string? TemplateKey, string? Text);

/// `PATCH /v1/cart/lines/{lineId}` (11 §3.5).
public sealed record UpdateCartLineRequest(int Quantity);
