using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.DTOs.Giving;

/// `GET /v1/cards/{token}` (11 §8.1, RUL-22): **no amount field**, by design (tested by reflection in T-8).
public sealed record CardResponse(string ToName, string? MessageAr, string? MessageEn, string FromName, CardProduct Product, string Locale,
    DateTimeOffset? IssuedAt);

public sealed record CardProduct(string Slug, string TitleAr, string? TitleEn, string? CoverUrl);

/// `POST /v1/admin/dedications/{id}/resend` (#93): an optional corrected recipient mobile.
public sealed record ResendDedicationRequest(string? ToMobile);

/// `CauseUpdateInput` (11 §9.8).
public sealed record CauseUpdateInput(string? TitleAr, string? TitleEn, string? BodyAr, string? BodyEn, Guid? AssetId, int? UnitsDelivered,
    DateOnly? OccurredOn, bool IsPublic, bool NotifyDonors);

/// Staff view of a cause update (#81–#83).
public sealed record AdminCauseUpdate(Guid Id, Guid ProductId, string TitleAr, string? TitleEn, string? BodyAr, string? BodyEn, ImageResponse? Image,
    int? UnitsDelivered, DateOnly OccurredOn, bool IsPublic, bool NotifyDonors, DateTimeOffset? NotifiedAt, DateTimeOffset CreatedAt, string CreatedBy);

/// `GET /v1/admin/products/{id}/goal-adjustments` row (#136): sweeps (signed amounts, shared `pairId`) and corrections.
public sealed record GoalAdjustmentRow(long Id, string Kind, string Amount, int Units, Guid? OfferId, Guid? PairId, string? Note, DateTimeOffset At,
    string Actor);

/// `POST …/goal-adjustments` (#137): a signed correction; `amount` as a money string ("-50.00").
public sealed record GoalCorrectionInput(string? Amount, int Units, string? Note);

/// 201: the correction and the product's goal figures after it.
public sealed record GoalCorrectionResponse(GoalAdjustmentRow Adjustment, DTOs.AdminCatalog.GoalFiguresResponse Goal);
