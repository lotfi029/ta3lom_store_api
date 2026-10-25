using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Donations;

/// Who a cart request is for: a signed-in donor, or a guest with the id from the `t3s_cart` cookie (if any).
public sealed record CartOwner(Guid? CustomerId, Guid? GuestCartId);

/// A cart line priced from the catalogue as it is now (D-05: the cart price is only indicative).
internal sealed record PricedLine(CartLine Line, string? ProjectSlug, string? CategorySlug, string TitleAr, string? TitleEn, string? OptionLabelAr,
    string? OptionLabelEn, Money UnitAmount, Money Amount, IReadOnlyList<string> Problems)
{
    public OrderLineDraft ToDraft() =>
        new(Line.TargetKind, Line.ProjectId, Line.CategoryId, Line.OptionId, TitleAr, TitleEn, OptionLabelAr, OptionLabelEn, Line.Quantity, UnitAmount);
}

internal sealed record PricedCart(IReadOnlyList<PricedLine> Lines, Money Total, IReadOnlyList<CartProblemResponse> Problems)
{
    public static PricedCart Empty { get; } = new([], Money.Zero, []);

    public CartResponse ToResponse() => new(
        [.. Lines.Select(l => new CartLineResponse(l.Line.Id, l.Line.TargetKind.ToWire(), l.Line.ProjectId, l.ProjectSlug, l.Line.CategoryId, l.CategorySlug,
            l.Line.OptionId, l.TitleAr, l.TitleEn, l.OptionLabelAr, l.OptionLabelEn, l.Line.Quantity, l.UnitAmount.ToWire(), l.Amount.ToWire(), l.Problems))],
        Lines.Count, Total.ToWire(), Problems);
}

/// Re-prices cart lines from the database in two queries (projects with options, categories) and lists every rule a line
/// breaks now (D-03 … D-05). The checkout uses the same pricing and refuses the order when any problem remains.
internal sealed class CartPricer(IApplicationDbContext db)
{
    public const string Unknown = "—";

    public static CartLimits Limits(StoreSettings s) => new(s.MinGiftAmount, s.MaxLineAmount, s.CartMaxLines, s.CartMaxTotal);

    public async Task<PricedCart> PriceAsync(IReadOnlyList<CartLine> lines, StoreSettings settings, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            return PricedCart.Empty;
        }

        var projectIds = lines.Where(l => l.ProjectId != null).Select(l => l.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Include(p => p.Options).Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var categoryIds = projects.Values.Select(p => p.CategoryId).Concat(lines.Where(l => l.CategoryId != null).Select(l => l.CategoryId!.Value)).Distinct().ToList();
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var priced = lines.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => l.ProjectId is { } projectId
                ? PriceProjectLine(l, projects.GetValueOrDefault(projectId), categories, settings)
                : PriceCategoryLine(l, categories.GetValueOrDefault(l.CategoryId!.Value), settings))
            .ToList();
        var total = priced.Select(p => p.Amount).Sum();
        List<CartProblemResponse> problems = [.. priced.SelectMany(p => p.Problems.Select(code => new CartProblemResponse(p.Line.Id, code)))];
        if (priced.Count > settings.CartMaxLines)
        {
            problems.Add(new CartProblemResponse(null, ErrorCodes.CartTooManyLines));
        }

        if (total > settings.CartMaxTotal)
        {
            problems.Add(new CartProblemResponse(null, ErrorCodes.CartTotalExceeded));
        }

        return new PricedCart(priced, total, problems);
    }

    private static PricedLine PriceProjectLine(CartLine line, Project? project, IReadOnlyDictionary<Guid, Category> categories, StoreSettings settings)
    {
        var category = project is null ? null : categories.GetValueOrDefault(project.CategoryId);
        List<string> problems = [];
        if (project is null || category is null || !project.IsPublished || !category.IsActive)
        {
            problems.Add(ErrorCodes.CartTargetUnavailable);
        }

        if (line.OptionId is { } optionId)
        {
            var option = project?.Options.FirstOrDefault(o => o.Id == optionId);
            if (option is not { IsActive: true })
            {
                problems.Add(ErrorCodes.CartOptionUnavailable);
            }

            var unit = option?.Amount ?? line.UnitAmount;
            return new PricedLine(line, project?.Slug, category?.Slug, project?.TitleAr ?? Unknown, project?.TitleEn, option?.LabelAr, option?.LabelEn,
                unit, unit * line.Quantity, problems);
        }

        if (project is { AllowOpenAmount: false })
        {
            problems.Add(ErrorCodes.CartOpenAmountNotAllowed);
        }

        AmountProblems(line.Amount, project?.MinimumOpenAmount(settings.MinGiftAmount) ?? settings.MinGiftAmount, settings, problems);
        return new PricedLine(line, project?.Slug, category?.Slug, project?.TitleAr ?? Unknown, project?.TitleEn, null, null, line.Amount, line.Amount, problems);
    }

    private static PricedLine PriceCategoryLine(CartLine line, Category? category, StoreSettings settings)
    {
        List<string> problems = [];
        if (category is not { AcceptsCategoryGifts: true })
        {
            problems.Add(ErrorCodes.CartTargetUnavailable);
        }

        AmountProblems(line.Amount, settings.MinGiftAmount, settings, problems);
        return new PricedLine(line, null, category?.Slug, category?.NameAr ?? Unknown, category?.NameEn, null, null, line.Amount, line.Amount, problems);
    }

    private static void AmountProblems(Money amount, Money minimum, StoreSettings settings, List<string> problems)
    {
        if (amount < minimum)
        {
            problems.Add(ErrorCodes.CartAmountBelowMinimum);
        }

        if (amount > settings.MaxLineAmount)
        {
            problems.Add(ErrorCodes.CartAmountAboveMaximum);
        }
    }
}

/// Opens the cart of a request under its row lock. A guest cart that is missing, expired or became a donor's reads as none;
/// a donor must be active for every write (D-13).
internal sealed class CartAccess(ICartRepository carts, ICustomerRepository customers, IClock clock)
{
    public async Task<Result> EnsureActiveAsync(CartOwner owner, CancellationToken ct)
    {
        if (owner.CustomerId is not { } customerId)
        {
            return Result.Success();
        }

        return await customers.GetByIdAsync(customerId, ct) is { IsActive: true } ? Result.Success() : AuthErrors.AccountBlocked;
    }

    /// The locked cart, or a new one (added, not saved) when <paramref name="create"/> is set; `IsNew` tells the endpoint to
    /// write the guest cookie.
    public async Task<(Cart? Cart, bool IsNew)> LockAsync(CartOwner owner, bool create, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (owner.CustomerId is { } customerId)
        {
            return create
                ? (await carts.GetOrCreateForCustomerForUpdateAsync(customerId, now, ct), false)
                : (await carts.GetForCustomerForUpdateAsync(customerId, ct), false);
        }

        var guest = owner.GuestCartId is { } id ? await carts.GetForUpdateAsync(id, ct) : null;
        if (guest is { IsGuest: true } && guest.ExpiresAt > now)
        {
            return (guest, false);
        }

        if (!create)
        {
            return (null, false);
        }

        var cart = Cart.CreateGuest(now);
        carts.Add(cart);
        return (cart, true);
    }
}
