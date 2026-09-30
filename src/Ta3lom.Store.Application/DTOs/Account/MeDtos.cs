namespace Ta3lom.Store.Application.DTOs.Account;

/// `GET /v1/me` (11 §5.1).
public sealed record ProfileResponse(Guid Id, string Mobile, string? FullName, string? Email, string Locale, bool AnonymousDefault, bool MarketingOptIn,
    DateTimeOffset? PdplConsentAt, DateTimeOffset CreatedAt);

/// `PATCH /v1/me`: only the fields sent change; the mobile never does (RUL-61).
public sealed record UpdateProfileRequest(string? FullName, string? Email, string? Locale, bool? AnonymousDefault, bool? MarketingOptIn, bool? PdplConsent);

/// 11 §5.2 (the mobile in local `05…` form).
public sealed record AddressResponse(Guid Id, string RecipientName, string RecipientMobile, string City, string? District, string? ShortAddress,
    string? Street, string? BuildingNo, string? PostalCode, bool IsDefault);

/// 11 §5.3.
public sealed record EnrolmentItem(Guid Id, EnrolmentProduct Product, string Source, string Status, int ProgressPct, DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? CertificateNo, bool CanLaunch);

public sealed record EnrolmentProduct(string Slug, string TitleAr, string? TitleEn, string? CoverUrl);

/// 11 §5.6.
public sealed record BooksResponse(IReadOnlyList<BookDownloadItem> Downloads, IReadOnlyList<BookShipmentItem> Shipments);

public sealed record BookDownloadItem(Guid GrantId, string Slug, string TitleAr, string? TitleEn, string OrderRef, int DownloadCount, int MaxDownloads, bool Revoked);

public sealed record BookShipmentItem(string OrderRef, string Status, string? Carrier, string? TrackingNo, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt,
    IReadOnlyList<BookShipmentLine> Lines);

public sealed record BookShipmentLine(string TitleAr, string? TitleEn, int Quantity);

/// 11 §5.8 — donor view: never a beneficiary identity (RUL-50).
public sealed record GivingResponse(GivingTotals Totals, IReadOnlyList<GivingItem> Items, int Total, int Page, int PageSize);

public sealed record GivingTotals(string Given, string Zakat, int UnitsSponsored, int PooledContributions, int Dedications);

public sealed record GivingItem(Guid OrderLineId, string OrderRef, DateTimeOffset? PaidAt, string Kind, string Mode, string Slug, string TitleAr, string? TitleEn,
    int Quantity, string Amount, string? Intent, GivingUnits? Units, string? GroupLabelAr, string? GroupLabelEn, GivingPooled? Pooled,
    GivingLatestUpdate? LatestUpdate, GivingDedication? Dedication);

/// `awaitingAllocation` includes lapsed seats being re-allocated (RUL-50).
public sealed record GivingUnits(int AwaitingAllocation, int Allocated, int Completed);

public sealed record GivingPooled(string Converted, string Remaining, string SeatPrice, string ShareOfSeat, DateTimeOffset? SweptToFundAt);

public sealed record GivingLatestUpdate(string TitleAr, string? TitleEn, DateOnly OccurredOn);

public sealed record GivingDedication(string ToName, string CardUrl, string SmsStatus);

/// 11 §5.9 — group and region only, never the beneficiary.
public sealed record GivingLineDetail(Guid OrderLineId, string Kind, IReadOnlyList<GivingUnitView> Units, IReadOnlyList<GivingUpdateView> Updates);

public sealed record GivingUnitView(string Ref, string Status, string? Region, string? GroupLabelAr, DateTimeOffset FundedAt, DateTimeOffset? AllocatedAt,
    DateTimeOffset? CompletedAt, string? Share, IReadOnlyList<GivingUnitStep> Timeline);

public sealed record GivingUnitStep(string Status, DateTimeOffset At);

public sealed record GivingUpdateView(string TitleAr, string? TitleEn, string? BodyAr, string? BodyEn, int? UnitsDelivered, DateOnly OccurredOn);

/// 11 §5.10 `OrderSummary`.
public sealed record OrderSummary(string Reference, DateTimeOffset PlacedAt, string Status, string GrandTotal, int LineCount, bool HasInvoice, bool HasReceipt);

/// `DELETE /v1/me` body (11 §5.12).
public sealed record DeleteAccountRequest(bool Confirm);

/// A private PDF stream for #37.
public sealed record DocumentFile(Stream Content, string FileName);

/// `GET /v1/me/export` (#38, FR-S27, database/03 §7): everything personal the store holds about the customer. Beneficiary
/// data is never included.
public sealed record MeExport(DateTimeOffset ExportedAt, ProfileResponse Profile, IReadOnlyList<AddressResponse> Addresses,
    IReadOnlyList<Orders.OrderDetailResponse> Orders, IReadOnlyList<GivingItem> Giving, IReadOnlyList<EnrolmentItem> Enrolments,
    IReadOnlyList<BookDownloadItem> DownloadGrants, IReadOnlyList<ExportDedication> DedicationsSent);

public sealed record ExportDedication(string OrderRef, string ToName, string ToMobile, string? FromName, string TemplateKey, string? Text, string SmsStatus,
    DateTimeOffset? SentAt);
