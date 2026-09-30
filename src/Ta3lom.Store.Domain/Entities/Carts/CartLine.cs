namespace Ta3lom.Store.Domain.Entities.Carts;

/// Table `cart_line`. Prices are indicative (RUL-15); checkout reprices from the catalogue.
public sealed class CartLine : Entity
{
    public CartLine(Guid cartId, Guid productId, LineKind kind, LineMode mode, Money unitPrice, Money amount, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        CartId = Guard.NotEmpty(cartId);
        ProductId = Guard.NotEmpty(productId);
        Kind = kind;
        Mode = mode;
        Quantity = 1;
        UnitPrice = unitPrice;
        Amount = amount;
        AddedAt = now;
    }

    private CartLine()
    {
    }

    public Guid CartId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid? BookFormatId { get; private set; }

    public LineKind Kind { get; private set; }

    public LineMode Mode { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; }

    public Money Amount { get; private set; }

    public GiftIntent? Intent { get; private set; }

    /// Owned JSON (`cart_line.dedication` jsonb, RUL-21).
    public CartDedication? Dedication { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public bool IsGift => Kind != LineKind.Buy;

    internal static CartLine Create(Guid cartId, CartLineSpec s, int quantity, Money unitPrice, DateTimeOffset now) =>
        new(cartId, s.ProductId, s.Kind, s.Mode, unitPrice, s.Mode == LineMode.Amount ? unitPrice : unitPrice * quantity, now)
        {
            BookFormatId = s.Kind == LineKind.Buy ? s.BookFormatId : null,
            Quantity = quantity,
            Intent = s.Kind == LineKind.Buy ? null : s.Intent ?? GiftIntent.Sadaqa,          // RUL-19 default
            Dedication = s.Kind == LineKind.Buy ? null : s.Dedication,
        };

    /// RUL-14: same product, format, kind, `units` mode and intent, and neither side dedicated.
    internal bool IsMergeableWith(CartLineSpec s) =>
        Mode == LineMode.Units && s.Mode == LineMode.Units && Dedication is null && s.Dedication is null
        && ProductId == s.ProductId && BookFormatId == (s.Kind == LineKind.Buy ? s.BookFormatId : null) && Kind == s.Kind
        && Intent == (s.Kind == LineKind.Buy ? null : s.Intent ?? GiftIntent.Sadaqa);

    internal bool IsMergeableWith(CartLine other) =>
        IsMergeableWith(new CartLineSpec(other.ProductId, other.BookFormatId, other.Kind, other.Mode, other.Quantity, null, other.Intent, other.Dedication));

    internal void SetQuantity(int quantity, Money unitPrice)
    {
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = unitPrice * quantity;
    }

    internal CartLine MoveTo(Guid cartId, DateTimeOffset now) => new(cartId, ProductId, Kind, Mode, UnitPrice, Amount, now)
    {
        BookFormatId = BookFormatId,
        Quantity = Quantity,
        Intent = Intent,
        Dedication = Dedication?.Copy(),
    };
}
