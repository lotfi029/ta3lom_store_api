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

    /// RUL-05: print formats carry stock (changed only through stock movements); digital formats need a private
    /// `book_file` asset. A new print format starts with zero stock.
    public static BookFormat Create(Guid productId, BookFormatKind kind, string sku, Money price)
    {
        var format = new BookFormat(productId, kind, sku, price);
        if (kind == BookFormatKind.Print)
        {
            format.StockOnHand = 0;
        }

        return format;
    }

    public void Update(string sku, Money price, Money? compareAtPrice, bool isActive, int? weightGrams, int? lowStockThreshold, Guid? fileAssetId)
    {
        Guard.Against(compareAtPrice is { } c && c <= price, "format.compare_at_not_higher");
        Guard.Against(Kind == BookFormatKind.Digital && fileAssetId is null, "format.digital_file_required");
        Sku = Guard.NotEmpty(sku);
        Price = price;
        CompareAtPrice = compareAtPrice;
        IsActive = isActive;
        WeightGrams = Kind == BookFormatKind.Print ? weightGrams : null;
        LowStockThreshold = Kind == BookFormatKind.Print ? lowStockThreshold : null;
        FileAssetId = Kind == BookFormatKind.Digital ? fileAssetId : null;
    }

    public int Available => Math.Max(0, (StockOnHand ?? 0) - StockReserved);
}
