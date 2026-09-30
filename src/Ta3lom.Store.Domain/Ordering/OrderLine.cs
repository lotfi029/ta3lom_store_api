using Ta3lom.Store.Domain.Catalog;
using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Ordering;

/// Table `order_line` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class OrderLine : Entity
{
    public OrderLine(Guid orderId, short lineNo, Guid productId, ProductType productType, LineKind kind, LineMode mode, int quantity, Money unitPrice, Money amount, string titleAr) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        LineNo = lineNo;
        ProductId = Guard.NotEmpty(productId);
        ProductType = productType;
        Kind = kind;
        Mode = mode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = amount;
        VatRate = 0m;
        VatAmount = Money.Zero;
        TitleAr = Guard.NotNull(titleAr);
        FulfilmentStatus = FulfilmentStatus.Pending;
        RefundedAmount = Money.Zero;
    }

    private OrderLine()
    {
    }

    public Guid OrderId { get; private set; }

    public short LineNo { get; private set; }

    public Guid ProductId { get; private set; }

    public ProductType ProductType { get; private set; }

    public Guid? BookFormatId { get; private set; }

    public Guid? SponsorshipOfferId { get; private set; }

    public LineKind Kind { get; private set; }

    public LineMode Mode { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; }

    public Money Amount { get; private set; }

    public decimal VatRate { get; private set; }

    public Money VatAmount { get; private set; }

    public GiftIntent? Intent { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public FulfilmentStatus FulfilmentStatus { get; private set; }

    public Money RefundedAmount { get; private set; }
}
