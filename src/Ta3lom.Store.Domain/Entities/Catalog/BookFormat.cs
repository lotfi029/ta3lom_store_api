namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `book_format` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class BookFormat : Entity
{
    public BookFormat(Guid productId, BookFormatKind kind, string sku, Money price) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        Kind = kind;
        Sku = Guard.NotNull(sku);
        Price = price;
        IsActive = true;
        StockReserved = 0;
    }

    private BookFormat()
    {
    }

    public Guid ProductId { get; private set; }

    public BookFormatKind Kind { get; private set; }

    public string Sku { get; private set; } = null!;

    public Money Price { get; private set; }

    public Money? CompareAtPrice { get; private set; }

    public bool IsActive { get; private set; }

    public int? StockOnHand { get; private set; }

    public int StockReserved { get; private set; }

    public int? WeightGrams { get; private set; }

    public int? LowStockThreshold { get; private set; }

    public Guid? FileAssetId { get; private set; }
}
