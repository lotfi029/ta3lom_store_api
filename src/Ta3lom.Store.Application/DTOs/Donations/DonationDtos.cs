namespace Ta3lom.Store.Application.DTOs.Donations;

// ── Cart ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// One cart line, priced from the catalogue now (indicative: the checkout re-prices). `problems` lists what stops it
/// from being donated to now (`cart.target_unavailable`, `cart.option_unavailable`, `cart.amount_below_minimum`, …).
public sealed record CartLineResponse(Guid Id, string TargetKind, Guid? ProjectId, string? ProjectSlug, Guid? CategoryId, string? CategorySlug,
    Guid? OptionId, string TitleAr, string? TitleEn, string? OptionLabelAr, string? OptionLabelEn, int Quantity, string UnitAmount, string Amount,
    IReadOnlyList<string> Problems);

/// A problem of one line (`lineId`) or of the whole cart (`lineId` null: `cart.too_many_lines`, `cart.total_exceeded`).
public sealed record CartProblemResponse(Guid? LineId, string Code);

/// `GET /v1/cart`, every cart write and `POST /v1/checkout/quote`.
public sealed record CartResponse(IReadOnlyList<CartLineResponse> Lines, int LineCount, string TotalAmount, IReadOnlyList<CartProblemResponse> Problems);

/// `GET /v1/cart/summary` (the header badge).
public sealed record CartSummaryResponse(int LineCount, string TotalAmount);

/// `POST /v1/cart/lines` (D-01): `{ projectId, optionId, quantity }`, `{ projectId, amount }` or `{ categoryId, amount }`.
public sealed record AddCartLineRequest(Guid? ProjectId, Guid? OptionId, int? Quantity, Guid? CategoryId, string? Amount);

/// `PATCH /v1/cart/lines/{id}`: `quantity` for an option line, `amount` for an amount line.
public sealed record UpdateCartLineRequest(int? Quantity, string? Amount);

// ── Checkout and orders ──────────────────────────────────────────────────────────────────────────────────────────────

/// `POST /v1/checkout` (BR-A08): the donor confirms the name and e-mail (missing values come from the profile).
public sealed record CheckoutRequest(string? FullName, string? Email, string? Locale);

/// The created order. `payment` is null until R5-Payments adds the payment redirect.
public sealed record CheckoutResponse(string Reference, string Status, string TotalAmount, string StatusToken, CheckoutPayment? Payment);

/// Placeholder for the payment redirect of R5-Payments (always null in this release).
public sealed record CheckoutPayment(string RedirectUrl);

/// `GET /v1/orders/{reference}/status?t=` (the donor or the holder of the status token).
public sealed record OrderStatusResponse(string Reference, string Status, string TotalAmount, int LineCount, DateTimeOffset CreatedAt,
    DateTimeOffset PaymentDueAt);

// ── Donor account ────────────────────────────────────────────────────────────────────────────────────────────────────

/// `GET /v1/me` (BR-A10).
public sealed record MeResponse(Guid Id, string Mobile, string? FullName, string? Email, string Locale, bool MarketingOptIn, string Status,
    DateTimeOffset? PdplConsentAt, DateTimeOffset CreatedAt);

/// `PATCH /v1/me`: null leaves a field as it is; an empty string clears the name or e-mail.
public sealed record UpdateMeRequest(string? FullName, string? Email, string? Locale, bool? MarketingOptIn);

public sealed record DonationSummary(string Reference, string Status, string TotalAmount, int LineCount, DateTimeOffset CreatedAt);

public sealed record DonationLineResponse(int LineNo, string TargetKind, Guid? ProjectId, Guid? CategoryId, string TitleAr, string? TitleEn,
    string? OptionLabelAr, string? OptionLabelEn, int Quantity, string UnitAmount, string Amount);

/// `GET /v1/me/donations/{reference}`: the order with its line snapshots (receipts arrive with R5-Payments).
public sealed record DonationDetail(string Reference, string Status, string TotalAmount, string DonorName, string DonorEmail, DateTimeOffset CreatedAt,
    DateTimeOffset PaymentDueAt, DateTimeOffset? CancelledAt, IReadOnlyList<DonationLineResponse> Lines);

/// `GET /v1/me/export` (PDPL): the profile and every donation order.
public sealed record MeExportResponse(MeResponse Profile, IReadOnlyList<DonationDetail> Donations, DateTimeOffset ExportedAt);
