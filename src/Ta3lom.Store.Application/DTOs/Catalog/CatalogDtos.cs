namespace Ta3lom.Store.Application.DTOs.Catalog;

/// A donation category on the public site; `projectCount` counts its published projects.
public sealed record CategorySummary(Guid Id, string Slug, string NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn,
    string? ImageUrl, string? ImageAltAr, bool AcceptsGeneralGifts, int ProjectCount);

/// A project in a listing. `minAmount` is the effective minimum of an open amount; `fromAmount` the lowest active option;
/// `options` the active options (sort, id), as in the detail (API-9; empty when none).
public sealed record ProjectCard(Guid Id, string Slug, string TitleAr, string? TitleEn, string? SummaryAr, string? SummaryEn,
    string? CoverUrl, string? CoverAltAr, string? Badge, string CategorySlug, bool AllowOpenAmount, string MinAmount, string? FromAmount,
    IReadOnlyList<ProjectOptionView> Options);

public sealed record ProjectOptionView(Guid Id, string LabelAr, string? LabelEn, string Amount);

public sealed record CategoryRef(Guid Id, string Slug, string NameAr, string? NameEn);

/// `GET /v1/catalog/projects/{slug}`. Body is Markdown.
public sealed record ProjectDetail(Guid Id, string Slug, string Status, string TitleAr, string? TitleEn, string? SummaryAr, string? SummaryEn,
    string? BodyAr, string? BodyEn, string? CoverUrl, string? CoverAltAr, string? CoverAltEn, string? Badge, CategoryRef Category,
    bool AllowOpenAmount, string MinAmount, IReadOnlyList<ProjectOptionView> Options,
    string? SeoTitleAr, string? SeoTitleEn, string? SeoDescAr, string? SeoDescEn, DateTimeOffset? PublishedAt);

/// An old slug (D-08): the web site redirects to the current one.
public sealed record SlugRedirectResponse(string RedirectTo);

/// `GET /v1/catalog/projects?fields=slug` (prerendering).
public sealed record ProjectSlugsResponse(IReadOnlyList<string> Slugs);

/// `GET /v1/catalog/home`.
public sealed record HomeResponse(IReadOnlyList<ProjectCard> Featured, IReadOnlyList<CategorySummary> Categories, IReadOnlyList<string> AmountPresets);

/// `GET /v1/catalog/config`: the giving limits (D-03, D-04) and presets.
public sealed record CatalogConfigResponse(string MinGiftAmount, string MaxLineAmount, int CartMaxLines, string CartMaxTotal, int MaxQuantity,
    IReadOnlyList<string> AmountPresets);

// ── Admin ────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// `POST/PUT /v1/admin/categories[/{id}]`; `version` is required on update.
public sealed record CategoryInput(string Slug, string NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, Guid? ImageAssetId,
    bool AcceptsGeneralGifts, int Sort, bool IsActive, uint? Version);

public sealed record AdminCategoryResponse(Guid Id, string Slug, string NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn,
    Guid? ImageAssetId, string? ImageUrl, bool AcceptsGeneralGifts, int Sort, bool IsActive, int ProjectCount, DateTimeOffset UpdatedAt, uint Version);

/// `PUT /v1/admin/categories/order`.
public sealed record CategoryOrderInput(IReadOnlyList<CategoryOrderItem> Items);

public sealed record CategoryOrderItem(Guid Id, int Sort);

/// `POST/PUT /v1/admin/projects[/{id}]`; money as "50.00"; `version` is required on update.
public sealed record ProjectInput(Guid CategoryId, string Slug, string TitleAr, string? TitleEn, string? SummaryAr, string? SummaryEn,
    string? BodyAr, string? BodyEn, Guid? CoverAssetId, bool AllowOpenAmount, string? MinAmount, string? Badge, int Sort,
    string? SeoTitleAr, string? SeoTitleEn, string? SeoDescAr, string? SeoDescEn, uint? Version);

/// `PUT /v1/admin/projects/{id}/options`: the full list; `id` null for a new option.
public sealed record ProjectOptionsInput(IReadOnlyList<ProjectOptionInputItem> Options, uint Version);

public sealed record ProjectOptionInputItem(Guid? Id, string LabelAr, string? LabelEn, string Amount, int Sort, bool IsActive);

public sealed record AdminProjectRow(Guid Id, string Slug, string Status, string TitleAr, Guid CategoryId, string CategoryNameAr, int Sort,
    int ActiveOptionCount, bool AllowOpenAmount, DateTimeOffset? PublishedAt, DateTimeOffset UpdatedAt);

public sealed record AdminProjectOption(Guid Id, string LabelAr, string? LabelEn, string Amount, int Sort, bool IsActive);

/// The editor view: `missingForPublish` lists what D-06 still needs.
public sealed record AdminProjectResponse(Guid Id, Guid CategoryId, string Slug, string Status, string TitleAr, string? TitleEn, string? SummaryAr,
    string? SummaryEn, string? BodyAr, string? BodyEn, Guid? CoverAssetId, string? CoverUrl, bool AllowOpenAmount, string? MinAmount, string? Badge,
    int Sort, string? SeoTitleAr, string? SeoTitleEn, string? SeoDescAr, string? SeoDescEn, IReadOnlyList<AdminProjectOption> Options,
    IReadOnlyList<string> OldSlugs, IReadOnlyList<string> MissingForPublish, DateTimeOffset? PublishedAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, uint Version);
