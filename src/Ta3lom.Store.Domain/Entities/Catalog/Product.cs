namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `product` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Product : AggregateRoot
{
    public Product(ProductType type, string slug, string titleAr, string summaryAr, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Type = type;
        Slug = Guard.NotNull(slug);
        Status = ProductStatus.Draft;
        TitleAr = Guard.NotNull(titleAr);
        SummaryAr = Guard.NotNull(summaryAr);
        Sort = 0;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Product()
    {
    }

    public ProductType Type { get; private set; }

    public string Slug { get; private set; } = null!;

    public ProductStatus Status { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? CoverAssetId { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public string SummaryAr { get; private set; } = null!;

    public string? SummaryEn { get; private set; }

    public string? BodyAr { get; private set; }

    public string? BodyEn { get; private set; }

    public ProductBadge? Badge { get; private set; }

    public int Sort { get; private set; }

    public string? SeoTitleAr { get; private set; }

    public string? SeoTitleEn { get; private set; }

    public string? SeoDescAr { get; private set; }

    public string? SeoDescEn { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// RUL-02: only published products appear in the catalogue or accept cart lines.
    public bool IsPublished => Status == ProductStatus.Published;

    /// Staff edit of the common fields (FR-S72). The type is immutable once the product has an order (RUL-01).
    public void Update(ProductType type, bool hasOrders, string slug, Guid? categoryId, Guid? coverAssetId, string titleAr, string? titleEn,
        string summaryAr, string? summaryEn, string? bodyAr, string? bodyEn, ProductBadge? badge, int sort,
        string? seoTitleAr, string? seoTitleEn, string? seoDescAr, string? seoDescEn, DateTimeOffset now)
    {
        Guard.Against(type != Type && hasOrders, "product.type_locked");
        Type = type;
        Slug = Guard.NotEmpty(slug);
        CategoryId = categoryId;
        CoverAssetId = coverAssetId;
        TitleAr = Guard.NotEmpty(titleAr);
        TitleEn = titleEn;
        SummaryAr = Guard.NotEmpty(summaryAr);
        SummaryEn = summaryEn;
        BodyAr = bodyAr;
        BodyEn = bodyEn;
        Badge = badge;
        Sort = sort;
        SeoTitleAr = seoTitleAr;
        SeoTitleEn = seoTitleEn;
        SeoDescAr = seoDescAr;
        SeoDescEn = seoDescEn;
        UpdatedAt = now;
    }

    /// RUL-02: the handler has checked completeness (RUL-03) before calling this.
    public void Publish(DateTimeOffset now)
    {
        Status = ProductStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = ProductStatus.Archived;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
