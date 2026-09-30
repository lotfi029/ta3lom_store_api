using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Carts;

/// Table `cart_line` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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

    public string? Dedication { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}
