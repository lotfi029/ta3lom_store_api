namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `category` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Category : AggregateRoot
{
    public Category(string slug, string nameAr) : base(Guid.CreateVersion7())
    {
        Slug = Guard.NotNull(slug);
        NameAr = Guard.NotNull(nameAr);
        Sort = 0;
        IsActive = true;
    }

    private Category()
    {
    }

    public string Slug { get; private set; } = null!;

    public string NameAr { get; private set; } = null!;

    public string? NameEn { get; private set; }

    public int Sort { get; private set; }

    public bool IsActive { get; private set; }
}
