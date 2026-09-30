namespace Ta3lom.Store.Domain.Entities.Carts;

/// Table `cart` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Cart : AggregateRoot
{
    public Cart(DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Status = CartStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Cart()
    {
    }

    public Guid? CustomerId { get; private set; }

    public CartStatus Status { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
