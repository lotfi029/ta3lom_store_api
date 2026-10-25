using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `category` (BR-A01): a donation category. An active category with general gifts enabled accepts any amount (BR-A05).
public sealed class Category : AggregateRoot
{
    private Category(Guid id, string slug, string nameAr, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        NameAr = nameAr;
        AcceptsGeneralGifts = true;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Category()
    {
    }

    public string Slug { get; private set; } = null!;

    public string NameAr { get; private set; } = null!;

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public Guid? ImageAssetId { get; private set; }

    public bool AcceptsGeneralGifts { get; private set; }

    public int Sort { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// `xmin`: the optimistic concurrency token clients send back as `version`.
    public uint Version { get; private set; }

    /// D-05: an active category with general gifts enabled takes category lines.
    public bool AcceptsCategoryGifts => IsActive && AcceptsGeneralGifts;

    public static Category Create(CategoryDetails details, MediaAsset? image, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var category = new Category(Guid.CreateVersion7(), details.Slug, details.NameAr, now);
        category.Apply(details, image, now);
        category.Raise(new CatalogChanged("category", category.Id, "created"));
        return category;
    }

    public void Update(CategoryDetails details, MediaAsset? image, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details, image, now);
        Raise(new CatalogChanged("category", Id, "updated"));
    }

    /// Staff reorder the categories (`PUT /v1/admin/categories/order`).
    public void MoveTo(int sort, DateTimeOffset now)
    {
        if (Sort == sort)
        {
            return;
        }

        Sort = sort;
        UpdatedAt = now;
        Raise(new CatalogChanged("category", Id, "reordered"));
    }

    /// The caller checked <paramref name="version"/> against the row it loaded; EF checks it again on save.
    public bool IsVersion(uint version) => Version == version;

    private void Apply(CategoryDetails d, MediaAsset? image, DateTimeOffset now)
    {
        Guard.Against(!ValueObjects.Slug.IsValidCategory(d.Slug), "category.slug_invalid");
        Guard.Against(image is not null && !image.IsPublicImage, "media.image_required");
        Slug = d.Slug;
        NameAr = Guard.NotEmpty(d.NameAr).Trim();
        NameEn = Clean(d.NameEn);
        DescriptionAr = Clean(d.DescriptionAr);
        DescriptionEn = Clean(d.DescriptionEn);
        ImageAssetId = image?.Id;
        AcceptsGeneralGifts = d.AcceptsGeneralGifts;
        Sort = d.Sort;
        IsActive = d.IsActive;
        UpdatedAt = now;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// The staff-editable fields of a category.
public sealed record CategoryDetails(string Slug, string NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn,
    bool AcceptsGeneralGifts, int Sort, bool IsActive);
