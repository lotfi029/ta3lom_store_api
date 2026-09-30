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
}
