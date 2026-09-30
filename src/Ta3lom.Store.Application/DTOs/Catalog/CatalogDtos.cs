namespace Ta3lom.Store.Application.DTOs.Catalog;

// 11 §2 — public catalogue. Money is a two-decimal string; enums are their DDL strings.

public sealed record CategoryResponse(string Slug, string NameAr, string? NameEn, int Sort);

public sealed record CategoryRef(string Slug, string NameAr, string? NameEn);

public sealed record ImageResponse(string Url, string? AltAr, string? AltEn, int? Width, int? Height);

public sealed record FormatPrice(string Kind, string Price);

public sealed record SponsorshipCard(Guid OfferId, string SeatPrice, string UnitLabelAr, string UnitLabelEn, int TargetUnits,
    int FundedUnits, int Percent, int DonorCount, DateTimeOffset EndsAt, bool IsOpen);

public sealed record GoalCard(string? UnitAmount, string? UnitLabelAr, string? UnitLabelEn, string Target, string Raised, int Percent,
    int DonorCount, DateTimeOffset? EndsAt, bool IsOpen, bool ZakatEligible, bool IsGeneralFund);

/// `ProductCard` (11 §2.3).
public sealed record ProductCardResponse(Guid Id, string Type, string Slug, string TitleAr, string? TitleEn, string SummaryAr, string? SummaryEn,
    string? Badge, CategoryRef? Category, ImageResponse? Cover, string? Price, string? CompareAtPrice, IReadOnlyList<FormatPrice>? Formats,
    SponsorshipCard? Sponsorship, GoalCard? Goal);

/// `fields=slug` row for prerender (11 §2.3).
public sealed record ProductSlugResponse(string Type, string Slug, DateTimeOffset UpdatedAt);

public sealed record SeoResponse(string? TitleAr, string? TitleEn, string? DescAr, string? DescEn);

public sealed record CourseModuleResponse(int Sort, string TitleAr, string? TitleEn, int DurationSeconds);

public sealed record CourseBlock(string Price, string? CompareAtPrice, int Lessons, int DurationMinutes, string? LevelAr, string? LevelEn,
    string InstructorAr, string? InstructorEn, int? AccessDays, int LearnerCount, IReadOnlyList<CourseModuleResponse> Modules);

public sealed record BookFormatResponse(Guid Id, string Kind, string Sku, string Price, string? CompareAtPrice, int? Available, int MaxQuantity);

public sealed record BookBlock(string AuthorAr, string? AuthorEn, int? Pages, string? Isbn, int? PublishedYear, IReadOnlyList<BookFormatResponse> Formats);

public sealed record CauseBlock(string? UnitAmount, string? UnitLabelAr, string? UnitLabelEn, string Target, string Raised, int Percent, int DonorCount,
    bool AllowCustomAmount, string MinAmount, bool ZakatEligible, string? BeneficiariesAr, string? BeneficiariesEn, bool CloseOnTarget,
    bool IsGeneralFund, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, int? DaysLeft, bool IsOpen);

public sealed record SponsorshipDetail(Guid OfferId, string SeatPrice, string UnitLabelAr, string UnitLabelEn, int TargetUnits, int FundedUnits,
    int Percent, int DonorCount, string GroupLabelAr, string? GroupLabelEn, bool AllowPartial, bool ZakatEligible, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, bool IsOpen);

public sealed record RelatedProduct(string Slug, string Type, string TitleAr, string? TitleEn, string? Price);

/// `GET /v1/catalog/products/{slug}` (11 §2.4).
public sealed record ProductDetailResponse(Guid Id, string Type, string Slug, string TitleAr, string? TitleEn, string SummaryAr, string? SummaryEn,
    string? BodyAr, string? BodyEn, string? Badge, CategoryRef? Category, ImageResponse? Cover, SeoResponse Seo, CourseBlock? Course, BookBlock? Book,
    CauseBlock? Cause, SponsorshipDetail? Sponsorship, IReadOnlyList<RelatedProduct> Related);

public sealed record LiveFormat(string Kind, string Price, int? Available);

public sealed record LiveSponsorship(int FundedUnits, int TargetUnits, int Percent, int DonorCount, bool IsOpen);

public sealed record LiveGoal(string Raised, string Target, int Percent, int DonorCount, int? DaysLeft, bool IsOpen);

public sealed record LiveItem(string Slug, string? Price, string? CompareAtPrice, IReadOnlyList<LiveFormat>? Formats, LiveSponsorship? Sponsorship, LiveGoal? Goal);

/// `GET /v1/catalog/live` (11 §2.5).
public sealed record LiveFiguresResponse(IReadOnlyList<LiveItem> Items, DateTimeOffset AsOf);

/// Cause delivery update (11 §2.6).
public sealed record CauseUpdateResponse(Guid Id, string TitleAr, string? TitleEn, string? BodyAr, string? BodyEn, ImageResponse? Image,
    int? UnitsDelivered, DateOnly OccurredOn);

/// Gift feed row (11 §2.7): never a full name, never an amount for sponsorships (RUL-23).
public sealed record GiftFeedItem(string DisplayAr, string DisplayEn, string? Amount, int? Units, DateTimeOffset At);

public sealed record DedicationTemplateResponse(string Key, string Ar, string En);

public sealed record SponsorPresets(IReadOnlyList<int> Course, IReadOnlyList<int> Book);

/// `GET /v1/catalog/config` (11 §2.9).
public sealed record CatalogConfigResponse(bool VatEnabled, string VatRate, string ShippingFlatFee, string MinGiftAmount, string CartMaxTotal,
    int CartMaxLines, int PrintMaxQuantity, int GiftMaxQuantity, IReadOnlyList<string> PaymentMethods, bool DedicationFreeTextEnabled,
    IReadOnlyList<DedicationTemplateResponse> DedicationTemplates, SponsorPresets SponsorPresets, IReadOnlyList<int> CauseUnitPresets,
    IReadOnlyList<string> AmountPresets);

public sealed record ImpactFigure(long Value, string LabelAr, string LabelEn);

public sealed record GeneralFundBand(string Slug, string Raised, string Target, int Percent, int DonorCount);

/// `GET /v1/catalog/home` (11 §2.1).
public sealed record HomeResponse(IReadOnlyList<ProductCardResponse> Featured, IReadOnlyList<ProductCardResponse> Causes,
    IReadOnlyList<ProductCardResponse> Courses, IReadOnlyList<ProductCardResponse> Sponsorable, IReadOnlyList<ProductCardResponse> Books,
    IReadOnlyList<ImpactFigure> Impact, GeneralFundBand? GeneralFund);
