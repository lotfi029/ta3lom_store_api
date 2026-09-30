namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `book_detail` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class BookDetail
{
    public BookDetail(Guid productId, string authorAr)
    {
        ProductId = Guard.NotEmpty(productId);
        AuthorAr = Guard.NotNull(authorAr);
    }

    private BookDetail()
    {
    }

    public Guid ProductId { get; private set; }

    public string AuthorAr { get; private set; } = null!;

    public string? AuthorEn { get; private set; }

    public int? Pages { get; private set; }

    public string? Isbn { get; private set; }

    public int? PublishedYear { get; private set; }

    public void Update(string authorAr, string? authorEn, int? pages, string? isbn, int? publishedYear)
    {
        AuthorAr = Guard.NotEmpty(authorAr);
        AuthorEn = authorEn;
        Pages = pages;
        Isbn = isbn;
        PublishedYear = publishedYear;
    }
}
