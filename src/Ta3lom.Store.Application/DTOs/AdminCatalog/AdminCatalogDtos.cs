using System.Text.Json;

namespace Ta3lom.Store.Application.DTOs.AdminCatalog;

// 11 §9.3–§9.6 and §9.17. Money inputs and outputs are two-decimal strings; dates of causes and offers are
// entered in Asia/Riyadh wall time (`…Local`, RUL-09) and returned in UTC.

public sealed record ProductInput(string Type, string Slug, Guid? CategoryId, Guid? CoverAssetId, string TitleAr, string? TitleEn,
    string SummaryAr, string? SummaryEn, string? BodyAr, string? BodyEn, string? Badge, int Sort,
    string? SeoTitleAr, string? SeoTitleEn, string? SeoDescAr, string? SeoDescEn);

public sealed record CourseDetailInput(string Price, string? CompareAtPrice, int Lessons, int DurationMinutes, string? LevelAr, string? LevelEn,
    string InstructorAr, string? InstructorEn, int? AccessDays, string? LmsCourseRef, int LearnerCount);

public sealed record CourseModuleInput(int Sort, string TitleAr, string? TitleEn, int DurationSeconds);

public sealed record BookDetailInput(string AuthorAr, string? AuthorEn, int? Pages, string? Isbn, int? PublishedYear);

public sealed record BookFormatInput(string Kind, string Sku, string Price, string? CompareAtPrice, bool IsActive, int? WeightGrams,
    int? LowStockThreshold, Guid? FileAssetId);

public sealed record CauseDetailInput(string? UnitAmount, string? UnitLabelAr, string? UnitLabelEn, string TargetAmount, bool AllowCustomAmount,
    string MinAmount, bool ZakatEligible, string? BeneficiariesAr, string? BeneficiariesEn, bool CloseOnTarget, bool IsGeneralFund,
    DateTime? StartsAtLocal, DateTime? EndsAtLocal);

public sealed record GoalSeedInput(string SeedRaised, int SeedUnits, int SeedDonors, string Reason);

public sealed record GoalFiguresResponse(string RaisedAmount, int FundedUnits, int DonorCount, string SeedRaised, int SeedUnits, int SeedDonors,
    string TotalRaised, int TotalUnits, int TotalDonors);

public sealed record AdminCourseBlock(string Price, string? CompareAtPrice, int Lessons, int DurationMinutes, string? LevelAr, string? LevelEn,
    string InstructorAr, string? InstructorEn, int? AccessDays, string? LmsCourseRef, int LearnerCount);

public sealed record AdminModule(Guid Id, int Sort, string TitleAr, string? TitleEn, int DurationSeconds);

public sealed record AdminBookBlock(string AuthorAr, string? AuthorEn, int? Pages, string? Isbn, int? PublishedYear);

/// `BookFormat` of 11 §9.3 (admin view, with stock).
public sealed record AdminBookFormat(Guid Id, string Kind, string Sku, string Price, string? CompareAtPrice, bool IsActive, int? StockOnHand,
    int StockReserved, int? Available, int? WeightGrams, int? LowStockThreshold, Guid? FileAssetId);

public sealed record AdminCauseBlock(string? UnitAmount, string? UnitLabelAr, string? UnitLabelEn, string TargetAmount, bool AllowCustomAmount,
    string MinAmount, bool ZakatEligible, string? BeneficiariesAr, string? BeneficiariesEn, bool CloseOnTarget, bool IsGeneralFund,
    DateTimeOffset StartsAt, DateTimeOffset? EndsAt);

public sealed record PoolBalance(string Balance);

/// `Offer` (11 §9.4).
public sealed record OfferResponse(Guid Id, Guid ProductId, string SeatPrice, string UnitLabelAr, string UnitLabelEn, int TargetUnits,
    string BeneficiaryGroup, string GroupLabelAr, string? GroupLabelEn, bool AllowPartial, bool ZakatEligible, bool CloseOnTarget,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsActive, PoolBalance Pool, int FundedUnits, int AwaitingAllocation);

public sealed record OfferInput(string SeatPrice, string? UnitLabelAr, string? UnitLabelEn, int TargetUnits, string BeneficiaryGroup,
    string GroupLabelAr, string? GroupLabelEn, bool AllowPartial, bool ZakatEligible, bool CloseOnTarget, DateTime? StartsAtLocal, DateTime EndsAtLocal);

public sealed record CloseOfferInput(bool SweepPool);

/// Close result: the sweep (RUL-46) is filled from S-42; until then `swept` is null.
public sealed record CloseOfferResponse(OfferResponse Offer, string? Swept, Guid? PairId);

/// `AdminProduct` (11 §9.3): every block, any status.
public sealed record AdminProductResponse(Guid Id, string Type, string Slug, string Status, Guid? CategoryId, Guid? CoverAssetId,
    string TitleAr, string? TitleEn, string SummaryAr, string? SummaryEn, string? BodyAr, string? BodyEn, string? Badge, int Sort,
    string? SeoTitleAr, string? SeoTitleEn, string? SeoDescAr, string? SeoDescEn, DateTimeOffset? PublishedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, bool TypeLocked, AdminCourseBlock? Course, IReadOnlyList<AdminModule> Modules, AdminBookBlock? Book,
    IReadOnlyList<AdminBookFormat> Formats, AdminCauseBlock? Cause, OfferResponse? ActiveOffer, GoalFiguresResponse? Goal);

/// `AdminProductRow` (11 §9.3).
public sealed record AdminProductRow(Guid Id, string Type, string Slug, string TitleAr, string Status, string? Price, int? GoalPercent,
    DateTimeOffset UpdatedAt, bool HasActiveOffer);

public sealed record CategoryInput(string Slug, string NameAr, string? NameEn, int Sort, bool IsActive);

public sealed record AdminCategoryResponse(Guid Id, string Slug, string NameAr, string? NameEn, int Sort, bool IsActive);

/// `MediaAsset` (11 §9.6); book files have no URL.
public sealed record MediaAssetResponse(Guid Id, string Kind, bool IsPublic, string? Url, string Mime, long SizeBytes, int? Width, int? Height,
    string? AltAr, string? AltEn, DateTimeOffset CreatedAt);

public sealed record MediaAltInput(string? AltAr, string? AltEn);

/// `Setting` (11 §9.17): `value` is the JSON value, typed per key.
public sealed record SettingResponse(string Key, JsonElement Value, DateTimeOffset UpdatedAt, string? UpdatedBy);

public sealed record SettingInput(JsonElement Value);
