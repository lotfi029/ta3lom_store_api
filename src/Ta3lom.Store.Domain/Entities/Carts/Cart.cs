using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Domain.Entities.Carts;

/// Table `cart` (RUL-11 … RUL-18, 09 §5.9). One active cart per customer; guests are identified by the `t3s_cart` cookie.
public sealed class Cart : AggregateRoot
{
    public const int GuestLifetimeDays = 30;                                         // RUL-16

    private readonly List<CartLine> _lines = [];

    public Cart(DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Status = CartStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
        ExpiresAt = now.AddDays(GuestLifetimeDays);
    }

    private Cart()
    {
    }

    public Guid? CustomerId { get; private set; }

    public CartStatus Status { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<CartLine> Lines => _lines;

    public bool IsGuest => CustomerId is null;

    public Money Total => _lines.Select(l => l.Amount).Sum();

    public static Cart ForCustomer(Guid customerId, DateTimeOffset now) => new(now) { CustomerId = Guard.NotEmpty(customerId), ExpiresAt = null };

    /// A guest cart is usable while active and not past its expiry (RUL-16).
    public bool IsUsable(DateTimeOffset now) => Status == CartStatus.Active && (ExpiresAt is null || ExpiresAt > now);

    public Result<CartLine> AddLine(CartLineSpec s, CartProduct p, CustomerOwnership owned, CartLimits limits, DateTimeOffset now)
    {
        var ruleError = CartLineRules.Check(s, p, limits, now);                     // RUL-02, RUL-12, RUL-19, RUL-21, RUL-30
        if (ruleError is not null)
        {
            return ruleError;
        }

        if (s.Kind == LineKind.Buy && p.Type == ProductType.Course)
        {
            if (owned.Owns(p.ProductId))
            {
                return CartErrors.CourseOwned;                                       // RUL-13
            }

            if (owned.UnpaidOrderFor(p.ProductId) is { } orderRef)
            {
                return CartErrors.CourseInUnpaidOrder(orderRef);
            }

            if (_lines.Any(l => l.Kind == LineKind.Buy && l.ProductId == p.ProductId))
            {
                return CartErrors.DuplicateCourse;
            }
        }

        var quantity = CartLineRules.NormaliseQuantity(s, p);
        var unitPrice = CartLineRules.UnitPrice(s, p);                               // RUL-15 (display only)
        var twin = _lines.FirstOrDefault(l => l.IsMergeableWith(s));                 // RUL-14
        if (twin is not null)
        {
            var merged = twin.Quantity + quantity;
            var max = CartLineRules.MaxQuantity(s.Kind, p);
            if (merged > max)
            {
                return CartErrors.QuantityLimit(max);
            }

            if (p.Format is { Kind: BookFormatKind.Print } print && merged > print.Available)
            {
                return CartErrors.OutOfStock(print.Available);
            }

            if (Total - twin.Amount + (unitPrice * merged) > limits.CartMaxTotal)
            {
                return CartErrors.TotalExceeded(limits.CartMaxTotal);               // RUL-18
            }

            twin.SetQuantity(merged, unitPrice);
            Touch(now);
            return twin;
        }

        var line = CartLine.Create(Id, s, quantity, unitPrice, now);
        if (_lines.Count >= CartLimits.MaxLines)
        {
            return CartErrors.TooManyLines;
        }

        if (Total + line.Amount > limits.CartMaxTotal)
        {
            return CartErrors.TotalExceeded(limits.CartMaxTotal);
        }

        _lines.Add(line);
        Touch(now);
        return line;
    }

    /// `units` lines only (11 §3.5).
    public Result UpdateQuantity(Guid lineId, int quantity, CartProduct p, CartLimits limits, DateTimeOffset now)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return CartErrors.LineNotFound;
        }

        if (line.Mode != LineMode.Units)
        {
            return CartErrors.ModeNotAllowed;
        }

        var max = CartLineRules.MaxQuantity(line.Kind, p);
        if (quantity < 1 || quantity > max)
        {
            return CartErrors.QuantityLimit(max);
        }

        if (line.Kind == LineKind.Buy && p.Format is { Kind: BookFormatKind.Print } print && quantity > print.Available)
        {
            return CartErrors.OutOfStock(print.Available);
        }

        if (Total - line.Amount + (line.UnitPrice * quantity) > limits.CartMaxTotal)
        {
            return CartErrors.TotalExceeded(limits.CartMaxTotal);
        }

        line.SetQuantity(quantity, line.UnitPrice);
        Touch(now);
        return Result.Success();
    }

    public Result RemoveLine(Guid lineId, DateTimeOffset now)
    {
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
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

    /// RUL-17: a guest cart becomes the customer's cart when they have none; owned courses are dropped with a notice.
    public IReadOnlyList<CartMergeResult> AssignTo(Guid customerId, CustomerOwnership owned, DateTimeOffset now)
    {
        CustomerId = Guard.NotEmpty(customerId);
        var notices = DropOwnedCourses(owned);
        ExpiresAt = null;
        UpdatedAt = now;
        return notices;
    }

    /// RUL-17: moves the guest lines into the customer's cart. Identical lines combine (RUL-14) up to the line maximum;
    /// owned courses are dropped; lines that would break RUL-18 stay behind with a notice.
    public IReadOnlyList<CartMergeResult> MergeInto(Cart target, CustomerOwnership owned, Func<CartLine, int> maxQuantity, CartLimits limits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(maxQuantity);
        var notices = DropOwnedCourses(owned).ToList();
        foreach (var line in _lines)
        {
            if (line.Kind == LineKind.Buy && target._lines.Any(l => l.Kind == LineKind.Buy && l.ProductId == line.ProductId
                && l.BookFormatId == line.BookFormatId && maxQuantity(l) == 1))
            {
                notices.Add(new CartMergeResult(CartNotices.Combined, line.ProductId));    // same course or digital book
                continue;
            }

            var twin = target._lines.FirstOrDefault(l => l.IsMergeableWith(line));
            if (twin is not null)
            {
                var max = maxQuantity(line);
                var quantity = Math.Min(twin.Quantity + line.Quantity, max);
                if (target.Total - twin.Amount + (twin.UnitPrice * quantity) > limits.CartMaxTotal)
                {
                    notices.Add(new CartMergeResult(ErrorCodes.CartTotalExceeded, line.ProductId));
                    continue;
                }

                twin.SetQuantity(quantity, twin.UnitPrice);
                notices.Add(new CartMergeResult(CartNotices.Combined, line.ProductId));
                continue;
            }

            if (target._lines.Count >= CartLimits.MaxLines)
            {
                notices.Add(new CartMergeResult(ErrorCodes.CartTooManyLines, line.ProductId));
                continue;
            }

            if (target.Total + line.Amount > limits.CartMaxTotal)
            {
                notices.Add(new CartMergeResult(ErrorCodes.CartTotalExceeded, line.ProductId));
                continue;
            }

            target._lines.Add(line.MoveTo(target.Id, now));
        }

        target.Touch(now);
        _lines.Clear();
        Status = CartStatus.Merged;
        UpdatedAt = now;
        return notices;
    }

    /// Checkout turned the cart into an order (S-15).
    public void MarkConverted(DateTimeOffset now)
    {
        Status = CartStatus.Converted;
        UpdatedAt = now;
    }

    private List<CartMergeResult> DropOwnedCourses(CustomerOwnership owned)
    {
        var dropped = _lines.Where(l => l.Kind == LineKind.Buy && owned.Owns(l.ProductId)).ToList();
        foreach (var line in dropped)
        {
            _lines.Remove(line);
        }

        return [.. dropped.Select(l => new CartMergeResult(CartNotices.DroppedOwnedCourse, l.ProductId))];
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ExpiresAt = CustomerId is null ? now.AddDays(GuestLifetimeDays) : null;    // RUL-16
    }
}
