namespace Ta3lom.Store.Domain.Entities.Ordering;

/// Table `order_line`: an immutable snapshot of a repriced cart line (RUL-15), fulfilled by the settlement effects.
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

    internal static OrderLine FromDraft(Guid orderId, short lineNo, OrderLineDraft d) =>
        new(orderId, lineNo, d.ProductId, d.ProductType, d.Kind, d.Mode, d.Quantity, d.UnitPrice, d.Amount, d.TitleAr)
        {
            BookFormatId = d.BookFormatId,
            SponsorshipOfferId = d.Kind == LineKind.Sponsor ? d.SponsorshipOfferId : null,
            VatRate = d.Kind == LineKind.Buy ? d.VatRate : 0m,
            VatAmount = d.Kind == LineKind.Buy ? d.VatAmount : Money.Zero,               // ck_line_buy_vat
            Intent = d.Kind == LineKind.Buy ? null : d.Intent,
            TitleEn = d.TitleEn,
        };

    /// RUL-40: course `enrolled`, digital `available`, print `delivered`, sponsor `funded`, donate `received`. A `returned`
    /// print line is not terminal ("for print lines, terminal means delivered"); it ends with a refund (payment service).
    public bool IsTerminal => FulfilmentStatus is FulfilmentStatus.Enrolled or FulfilmentStatus.Available or FulfilmentStatus.Delivered
        or FulfilmentStatus.Funded or FulfilmentStatus.Received or FulfilmentStatus.Refunded;

    public void SetFulfilment(FulfilmentStatus status) => FulfilmentStatus = status;
}
