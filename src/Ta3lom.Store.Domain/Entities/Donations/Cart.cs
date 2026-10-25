using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Domain.Entities.Donations;

/// Table `cart` (BR-A07): a server-side donation cart, either a guest's (`customer_id` null, expires 30 days after the last
/// change) or a donor's (one per donor). Lines follow D-01 … D-05; the line price is only indicative (checkout re-prices).
public sealed class Cart : AggregateRoot
{
    private readonly List<CartLine> _lines = [];

    private Cart(Guid? customerId, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        CustomerId = customerId;
        CreatedAt = now;
        Touch(now);
    }

    private Cart()
    {
    }

    public Guid? CustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// Guests only: 30 days after the last change (D-14).
    public DateTimeOffset? ExpiresAt { get; private set; }

    public IReadOnlyList<CartLine> Lines => _lines;

    public bool IsGuest => CustomerId is null;

    public Money Total => _lines.Select(l => l.Amount).Sum();

    public static Cart CreateGuest(DateTimeOffset now) => new(null, now);

    public static Cart CreateFor(Guid customerId, DateTimeOffset now) => new(Guard.NotEmpty(customerId), now);

    /// D-01/D-04: a project option × quantity. A second line for the same option adds to the first one's quantity.
    public Result<CartLine> AddOptionLine(Project project, Category category, Guid optionId, int quantity, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (!ProjectAccepts(project, category))
        {
            return CartErrors.TargetUnavailable;
        }

        if (project.ActiveOption(optionId) is not { } option)
        {
            return CartErrors.OptionUnavailable;
        }

        var existing = _lines.FirstOrDefault(l => l.OptionId == optionId);
        var newQuantity = (existing?.Quantity ?? 0) + quantity;
        if (quantity < 1 || newQuantity > CartLimits.MaxQuantity)
        {
            return CartErrors.QuantityLimit(CartLimits.MaxQuantity);
        }

        if (existing is null && _lines.Count >= limits.MaxLines)
        {
            return CartErrors.TooManyLines(limits.MaxLines);
        }

        // L-11: the line is re-priced at the option's current amount, so the check uses that, not its stored amount.
        if (Total - (existing?.Amount ?? Money.Zero) + (option.Amount * newQuantity) > limits.MaxTotal)
        {
            return CartErrors.TotalExceeded(limits.MaxTotal);
        }

        if (existing is not null)
        {
            existing.SetQuantity(newQuantity, option.Amount, now);
            Touch(now);
            return existing;
        }

        var line = CartLine.ForOption(Id, project.Id, option.Id, quantity, option.Amount, now);
        _lines.Add(line);
        Touch(now);
        return line;
    }

    /// D-01/D-03: an open amount for a project that allows it. Amount lines never merge (D-04).
    public Result<CartLine> AddProjectAmount(Project project, Category category, Money amount, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (!ProjectAccepts(project, category))
        {
            return CartErrors.TargetUnavailable;
        }

        if (!project.AllowOpenAmount)
        {
            return CartErrors.OpenAmountNotAllowed;
        }

        return AddAmountLine(amount, project.MinimumOpenAmount(limits.MinGift), limits, now,
            () => CartLine.ForProjectAmount(Id, project.Id, amount, now));
    }

    /// BR-A05: a general gift to a category (always an amount).
    public Result<CartLine> AddCategoryAmount(Category category, Money amount, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(limits);
        if (!category.AcceptsCategoryGifts)
        {
            return CartErrors.TargetUnavailable;
        }

        return AddAmountLine(amount, limits.MinGift, limits, now, () => CartLine.ForCategory(Id, category.Id, amount, now));
    }

    /// Changes the quantity of an option line.
    public Result ChangeQuantity(Guid lineId, int quantity, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (_lines.FirstOrDefault(l => l.Id == lineId) is not { OptionId: not null } line)
        {
            return CartErrors.LineNotFound;
        }

        if (quantity is < 1 or > CartLimits.MaxQuantity)
        {
            return CartErrors.QuantityLimit(CartLimits.MaxQuantity);
        }

        if (Total - line.Amount + (line.UnitAmount * quantity) > limits.MaxTotal)
        {
            return CartErrors.TotalExceeded(limits.MaxTotal);
        }

        line.SetQuantity(quantity, line.UnitAmount, now);
        Touch(now);
        return Result.Success();
    }

    /// Changes the amount of an amount line; <paramref name="minimum"/> is the line's minimum (project or setting).
    public Result ChangeAmount(Guid lineId, Money amount, Money minimum, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (_lines.FirstOrDefault(l => l.Id == lineId) is not { OptionId: null } line)
        {
            return CartErrors.LineNotFound;
        }

        if (CheckAmount(amount, minimum, limits, Total - line.Amount) is { } error)
        {
            return error;
        }

        line.SetAmount(amount, now);
        Touch(now);
        return Result.Success();
    }

    public Result RemoveLine(Guid lineId, DateTimeOffset now)
    {
        if (_lines.FirstOrDefault(l => l.Id == lineId) is not { } line)
        {
            return CartErrors.LineNotFound;
        }

        _lines.Remove(line);
        Touch(now);
        return Result.Success();
    }

    public void Clear(DateTimeOffset now)
    {
        _lines.Clear();
        Touch(now);
    }

    /// Sign-in (BR-A07): the guest's lines move into this donor cart. Option lines merge into an existing line of the same
    /// option; a line that would break a limit (D-03, D-04) stays behind and is reported as dropped.
    public IReadOnlyList<CartMergeOutcome> MergeFrom(Cart guest, CartLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(guest);
        ArgumentNullException.ThrowIfNull(limits);
        Guard.Against(!guest.IsGuest || IsGuest, "cart.merge_invalid");
        List<CartMergeOutcome> outcomes = [];
        foreach (var line in guest._lines.OrderBy(l => l.CreatedAt))
        {
            var existing = line.OptionId is { } optionId ? _lines.FirstOrDefault(l => l.OptionId == optionId) : null;
            var fitsLines = existing is not null || _lines.Count < limits.MaxLines;
            var quantity = (existing?.Quantity ?? 0) + line.Quantity;
            var added = existing is not null ? existing.UnitAmount * line.Quantity : line.Amount;    // L-11: combined at the donor line's price
            if (!fitsLines || quantity > CartLimits.MaxQuantity || Total + added > limits.MaxTotal)
            {
                outcomes.Add(new CartMergeOutcome(CartMergeOutcome.Dropped, line.Id));
                continue;
            }

            if (existing is not null)
            {
                existing.SetQuantity(quantity, existing.UnitAmount, now);
                outcomes.Add(new CartMergeOutcome(CartMergeOutcome.Combined, existing.Id));
            }
            else
            {
                var moved = line.CopyTo(Id, now);
                _lines.Add(moved);
                outcomes.Add(new CartMergeOutcome(CartMergeOutcome.Moved, moved.Id));
            }
        }

        guest._lines.Clear();
        Touch(now);
        return outcomes;
    }

    private Result<CartLine> AddAmountLine(Money amount, Money minimum, CartLimits limits, DateTimeOffset now, Func<CartLine> create)
    {
        if (_lines.Count >= limits.MaxLines)
        {
            return CartErrors.TooManyLines(limits.MaxLines);
        }

        if (CheckAmount(amount, minimum, limits, Total) is { } error)
        {
            return error;
        }

        var line = create();
        _lines.Add(line);
        Touch(now);
        return line;
    }

    private static Error? CheckAmount(Money amount, Money minimum, CartLimits limits, Money otherLines)
    {
        if (amount < minimum)
        {
            return CartErrors.AmountBelowMinimum(minimum);
        }

        if (amount > limits.MaxLine)
        {
            return CartErrors.AmountAboveMaximum(limits.MaxLine);
        }

        return otherLines + amount > limits.MaxTotal ? CartErrors.TotalExceeded(limits.MaxTotal) : null;
    }

    /// D-05: a published project in an active category.
    private static bool ProjectAccepts(Project project, Category category)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(category);
        Guard.Against(project.CategoryId != category.Id, "cart.category_mismatch");
        return project.IsPublished && category.IsActive;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ExpiresAt = IsGuest ? now + SessionPolicy.GuestCartIdle : null;
    }
}

/// The limits of D-03 and D-04, read from the settings.
public sealed record CartLimits(Money MinGift, Money MaxLine, int MaxLines, Money MaxTotal)
{
    /// D-03: quantity 1…999.
    public const int MaxQuantity = 999;
}

/// One guest line after a merge: `moved`, `combined` (into an existing option line) or `dropped` (over a limit).
public sealed record CartMergeOutcome(string Kind, Guid LineId)
{
    public const string Moved = "moved";
    public const string Combined = "combined";
    public const string Dropped = "dropped";
}
