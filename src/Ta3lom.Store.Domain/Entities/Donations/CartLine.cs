namespace Ta3lom.Store.Domain.Entities.Donations;

/// Table `cart_line` (D-01, D-02): exactly one target — a project (option × quantity, or an open amount) or a category
/// (an amount). `amount = unit_amount × quantity`; amount lines have quantity 1.
public sealed class CartLine : Entity
{
    private CartLine(Guid cartId, TargetKind kind, Guid? projectId, Guid? categoryId, Guid? optionId, int quantity, Money unitAmount, DateTimeOffset now)
        : base(Guid.CreateVersion7())
    {
        Guard.Against(kind == TargetKind.Project ? projectId is null || categoryId is not null : categoryId is null || projectId is not null || optionId is not null,
            "cart.line_target_invalid");
        Guard.Against(!unitAmount.IsPositive, "cart.line_amount_invalid");
        CartId = cartId;
        TargetKind = kind;
        ProjectId = projectId;
        CategoryId = categoryId;
        OptionId = optionId;
        Quantity = Guard.InRange(quantity, 1, CartLimits.MaxQuantity);
        UnitAmount = unitAmount;
        Amount = unitAmount * quantity;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private CartLine()
    {
    }

    public Guid CartId { get; private set; }

    public TargetKind TargetKind { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? OptionId { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitAmount { get; private set; }

    public Money Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsAmountLine => OptionId is null;

    internal static CartLine ForOption(Guid cartId, Guid projectId, Guid optionId, int quantity, Money unitAmount, DateTimeOffset now) =>
        new(cartId, TargetKind.Project, projectId, null, optionId, quantity, unitAmount, now);

    internal static CartLine ForProjectAmount(Guid cartId, Guid projectId, Money amount, DateTimeOffset now) =>
        new(cartId, TargetKind.Project, projectId, null, null, 1, amount, now);

    internal static CartLine ForCategory(Guid cartId, Guid categoryId, Money amount, DateTimeOffset now) =>
        new(cartId, TargetKind.Category, null, categoryId, null, 1, amount, now);

    internal CartLine CopyTo(Guid cartId, DateTimeOffset now) => new(cartId, TargetKind, ProjectId, CategoryId, OptionId, Quantity, UnitAmount, now);

    internal void SetQuantity(int quantity, Money unitAmount, DateTimeOffset now)
    {
        Quantity = Guard.InRange(quantity, 1, CartLimits.MaxQuantity);
        UnitAmount = unitAmount;
        Amount = unitAmount * quantity;
        UpdatedAt = now;
    }

    internal void SetAmount(Money amount, DateTimeOffset now)
    {
        Guard.Against(!amount.IsPositive, "cart.line_amount_invalid");
        UnitAmount = amount;
        Amount = amount;
        UpdatedAt = now;
    }
}
