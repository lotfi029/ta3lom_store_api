using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Wire;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Donations;

/// The cart after a write; `GuestCartId` is set on every guest write, so the endpoint (re)writes `t3s_cart` and its 30 days
/// run from the last change like the cart's own expiry (L-8, D-14).
public sealed record CartResult(CartResponse Cart, Guid? GuestCartId);

/// One transaction per cart write, under the cart row lock: an option line added twice is one upsert of its quantity
/// (D-04), never two rows.
internal sealed class CartWriter(CartAccess access, CartPricer pricer, IStoreSettings settings, IUnitOfWork uow)
{
    public async Task<Result<CartResult>> WriteAsync(CartOwner owner, bool create, Func<Cart, CartLimits, CancellationToken, Task<Result>> change,
        CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return await uow.ExecuteInTransactionAsync(token => ApplyAsync(owner, create, change, s, token), ct);
    }

    private async Task<Result<CartResult>> ApplyAsync(CartOwner owner, bool create, Func<Cart, CartLimits, CancellationToken, Task<Result>> change,
        StoreSettings s, CancellationToken ct)
    {
        var cart = await access.LockAsync(owner, create, ct);
        var active = await access.EnsureActiveAsync(owner, ct);              // L-9: after the lock, so a deletion that held it is seen
        if (active.IsFailure)
        {
            return active.Error;
        }

        if (cart is null)
        {
            return CartErrors.LineNotFound;
        }

        var changed = await change(cart, CartPricer.Limits(s), ct);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await uow.SaveChangesAsync(ct);
        var priced = await pricer.PriceAsync(cart.Lines, s, ct);
        return new CartResult(priced.ToResponse(), cart.IsGuest ? cart.Id : null);
    }
}

/// `POST /v1/cart/lines` (BR-A07, D-01 … D-05).
public sealed record AddCartLineCommand(CartOwner Owner, AddCartLineRequest Request) : ICommand<CartResult>;

internal sealed class AddCartLineCommandHandler(CartWriter writer, IApplicationDbContext db, IClock clock) : ICommandHandler<AddCartLineCommand, CartResult>
{
    public Task<Result<CartResult>> HandleAsync(AddCartLineCommand command, CancellationToken ct = default) =>
        writer.WriteAsync(command.Owner, create: true, (cart, limits, token) => AddAsync(cart, command.Request, limits, token), ct);

    private async Task<Result> AddAsync(Cart cart, AddCartLineRequest r, CartLimits limits, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (r.CategoryId is { } categoryId)
        {
            var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            return category is null ? CartErrors.TargetUnavailable : cart.AddCategoryAmount(category, MoneyInput.Parse(r.Amount!), limits, now);
        }

        var project = await db.Projects.AsNoTracking().Include(p => p.Options).FirstOrDefaultAsync(p => p.Id == r.ProjectId, ct);
        var projectCategory = project is null ? null : await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == project.CategoryId, ct);
        if (project is null || projectCategory is null)
        {
            return CartErrors.TargetUnavailable;
        }

        return r.OptionId is { } optionId
            ? cart.AddOptionLine(project, projectCategory, optionId, r.Quantity ?? 1, limits, now)
            : cart.AddProjectAmount(project, projectCategory, MoneyInput.Parse(r.Amount!), limits, now);
    }
}

internal sealed class AddCartLineCommandValidator : AbstractValidator<AddCartLineCommand>
{
    public AddCartLineCommandValidator()
    {
        RuleFor(c => c.Request).Must(r => IsOption(r) || IsProjectAmount(r) || IsCategoryAmount(r))
            .WithMessage("Send { projectId, optionId, quantity }, { projectId, amount } or { categoryId, amount }.");
        RuleFor(c => c.Request.Quantity).InclusiveBetween(1, CartLimits.MaxQuantity).When(c => c.Request.Quantity is not null);
        RuleFor(c => c.Request.Amount).Must(MoneyInput.IsPositive).When(c => c.Request.Amount is not null)
            .WithMessage("amount must be a positive \"50.00\" string.");
    }

    private static bool IsOption(AddCartLineRequest r) => r is { ProjectId: not null, OptionId: not null, CategoryId: null, Amount: null };

    private static bool IsProjectAmount(AddCartLineRequest r) => r is { ProjectId: not null, OptionId: null, CategoryId: null, Amount: not null, Quantity: null or 1 };

    private static bool IsCategoryAmount(AddCartLineRequest r) => r is { CategoryId: not null, ProjectId: null, OptionId: null, Amount: not null, Quantity: null or 1 };
}

/// `PATCH /v1/cart/lines/{id}`: the quantity of an option line or the amount of an amount line (D-03, D-04).
public sealed record UpdateCartLineCommand(CartOwner Owner, Guid LineId, UpdateCartLineRequest Request) : ICommand<CartResult>;

internal sealed class UpdateCartLineCommandHandler(CartWriter writer, IApplicationDbContext db, IClock clock) : ICommandHandler<UpdateCartLineCommand, CartResult>
{
    public Task<Result<CartResult>> HandleAsync(UpdateCartLineCommand command, CancellationToken ct = default) =>
        writer.WriteAsync(command.Owner, create: false, (cart, limits, token) => UpdateAsync(cart, command, limits, token), ct);

    private async Task<Result> UpdateAsync(Cart cart, UpdateCartLineCommand command, CartLimits limits, CancellationToken ct)
    {
        if (cart.Lines.FirstOrDefault(l => l.Id == command.LineId) is not { } line)
        {
            return CartErrors.LineNotFound;
        }

        if (!line.IsAmountLine)
        {
            return command.Request.Quantity is { } quantity
                ? cart.ChangeQuantity(line.Id, quantity, limits, clock.UtcNow)
                : Error.Validation(new Dictionary<string, string[]> { ["quantity"] = ["An option line changes its quantity."] });
        }

        if (command.Request.Amount is null)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["amount"] = ["An amount line changes its amount."] });
        }

        var minimum = limits.MinGift;
        if (line.ProjectId is { } projectId)
        {
            var projectMinimum = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.MinAmount).FirstOrDefaultAsync(ct);
            minimum = projectMinimum ?? limits.MinGift;
        }

        return cart.ChangeAmount(line.Id, MoneyInput.Parse(command.Request.Amount), minimum, limits, clock.UtcNow);
    }
}

internal sealed class UpdateCartLineCommandValidator : AbstractValidator<UpdateCartLineCommand>
{
    public UpdateCartLineCommandValidator()
    {
        RuleFor(c => c.Request).Must(r => (r.Quantity is null) != (r.Amount is null)).WithMessage("Send either quantity or amount.");
        RuleFor(c => c.Request.Quantity).InclusiveBetween(1, CartLimits.MaxQuantity).When(c => c.Request.Quantity is not null);
        RuleFor(c => c.Request.Amount).Must(MoneyInput.IsPositive).When(c => c.Request.Amount is not null)
            .WithMessage("amount must be a positive \"50.00\" string.");
    }
}

/// `DELETE /v1/cart/lines/{id}`.
public sealed record RemoveCartLineCommand(CartOwner Owner, Guid LineId) : ICommand<CartResult>;

internal sealed class RemoveCartLineCommandHandler(CartWriter writer, IClock clock) : ICommandHandler<RemoveCartLineCommand, CartResult>
{
    public Task<Result<CartResult>> HandleAsync(RemoveCartLineCommand command, CancellationToken ct = default) =>
        writer.WriteAsync(command.Owner, create: false, (cart, _, _) => Task.FromResult(cart.RemoveLine(command.LineId, clock.UtcNow)), ct);
}

/// `DELETE /v1/cart`: removes every line (no cart is fine).
public sealed record ClearCartCommand(CartOwner Owner) : ICommand;

internal sealed class ClearCartCommandHandler(CartAccess access, IUnitOfWork uow, IClock clock) : ICommandHandler<ClearCartCommand>
{
    public async Task<Result> HandleAsync(ClearCartCommand command, CancellationToken ct = default)
    {
        var active = await access.EnsureActiveAsync(command.Owner, ct);
        if (active.IsFailure)
        {
            return active;
        }

        await uow.ExecuteInTransactionAsync(async token =>
        {
            var cart = await access.LockAsync(command.Owner, create: false, token);
            cart?.Clear(clock.UtcNow);
        }, ct);
        return Result.Success();
    }
}

/// `GET /v1/cart` and `POST /v1/checkout/quote` (re-priced from the database, with problems).
public sealed record GetCartQuery(CartOwner Owner) : IQuery<CartResponse>;

internal sealed class GetCartQueryHandler(CartReader reader, CartPricer pricer, IStoreSettings settings) : IQueryHandler<GetCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(GetCartQuery query, CancellationToken ct = default)
    {
        var cart = await reader.FindAsync(query.Owner, ct);
        return (cart is null ? PricedCart.Empty : await pricer.PriceAsync(cart.Lines, await settings.GetAsync(ct), ct)).ToResponse();
    }
}

/// `GET /v1/cart/summary`: line count and the indicative total.
public sealed record GetCartSummaryQuery(CartOwner Owner) : IQuery<CartSummaryResponse>;

/// API-5: the total is re-priced with the same `CartPricer` as GET /v1/cart (the catalogue as it is now; a constant number
/// of queries), so the header badge and the cart page always show the same amount.
internal sealed class GetCartSummaryQueryHandler(CartReader reader, CartPricer pricer, IStoreSettings settings)
    : IQueryHandler<GetCartSummaryQuery, CartSummaryResponse>
{
    public async Task<Result<CartSummaryResponse>> HandleAsync(GetCartSummaryQuery query, CancellationToken ct = default)
    {
        var cart = await reader.FindAsync(query.Owner, ct);
        if (cart is null || cart.Lines.Count == 0)
        {
            return new CartSummaryResponse(0, Money.Zero.ToWire());
        }

        var priced = await pricer.PriceAsync(cart.Lines, await settings.GetAsync(ct), ct);
        return new CartSummaryResponse(cart.Lines.Count, priced.Total.ToWire());
    }
}

/// Read-only access to the cart of a request (no lock).
internal sealed class CartReader(IApplicationDbContext db, IClock clock)
{
    public async Task<Cart?> FindAsync(CartOwner owner, CancellationToken ct)
    {
        var carts = db.Carts.AsNoTracking().Include(c => c.Lines);
        if (owner.CustomerId is { } customerId)
        {
            return await carts.FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
        }

        if (owner.GuestCartId is not { } id)
        {
            return null;
        }

        var now = clock.UtcNow;
        return await carts.FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == null && c.ExpiresAt > now, ct);
    }
}
