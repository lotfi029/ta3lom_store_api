using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Application.Features.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.Giving;

/// `GET /v1/admin/products/{id}/goal-adjustments` (#136, RUL-46): newest first.
public sealed record ListGoalAdjustmentsQuery(Guid ProductId, int Page, int PageSize) : IQuery<PagedResult<GoalAdjustmentRow>>;

/// `POST /v1/admin/products/{id}/goal-adjustments` (#137, FR-S58): a finance correction to `raised_amount` and/or
/// `funded_units` (signed, with a note; audited). Empty, or a counter would go negative → `goal.adjustment_invalid`.
public sealed record AddGoalCorrectionCommand(Guid ProductId, GoalCorrectionInput Input) : ICommand<GoalCorrectionResponse>;

internal sealed class GoalAdjustmentHandlers(IApplicationDbContext db, IGenericRepository<GoalAdjustment> adjustments, IGoalProgressRepository goals,
    ICurrentActor actor, IUnitOfWork uow, IClock clock) :
    IQueryHandler<ListGoalAdjustmentsQuery, PagedResult<GoalAdjustmentRow>>, ICommandHandler<AddGoalCorrectionCommand, GoalCorrectionResponse>
{
    public async Task<Result<PagedResult<GoalAdjustmentRow>>> HandleAsync(ListGoalAdjustmentsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.GoalAdjustments.AsNoTracking().Where(a => a.ProductId == query.ProductId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<GoalAdjustmentRow>([.. rows.Select(Row)], total, page.SafePage, page.Take);
    }

    public async Task<Result<GoalCorrectionResponse>> HandleAsync(AddGoalCorrectionCommand command, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == command.ProductId, ct))
        {
            return Error.NotFound(ErrorCodes.ProductNotFound, "Product not found.");
        }

        var amount = Money.Round(decimal.Parse(command.Input.Amount ?? "0", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        var units = command.Input.Units;
        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            var current = await db.GoalProgress.AsNoTracking().FirstOrDefaultAsync(g => g.ProductId == command.ProductId, token);
            var raised = current?.RaisedAmount ?? 0m;
            var funded = current?.FundedUnits ?? 0;
            if ((amount == Money.Zero && units == 0) || raised + amount.Amount < 0 || funded + units < 0)
            {
                return Result.Failure<GoalAdjustment>(GivingErrors.GoalAdjustmentInvalid);
            }

            var now = clock.UtcNow;
            var adjustment = GoalAdjustment.Correction(command.ProductId, amount, units, command.Input.Note!.Trim(), actor.Name ?? "staff", now);
            adjustments.Add(adjustment);
            await goals.AddGiftAsync(command.ProductId, amount, units, 0, Money.Zero, now, token);
            await uow.SaveChangesAsync(token);
            return Result.Success(adjustment);
        }, ct);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var goal = await db.GoalProgress.AsNoTracking().FirstAsync(g => g.ProductId == command.ProductId, ct);
        return new GoalCorrectionResponse(Row(result.Value), AdminCatalogReader.Goal(goal));
    }

    private static GoalAdjustmentRow Row(GoalAdjustment a) =>
        new(a.Id, a.Kind.ToWire(), a.Amount.ToWire(), a.Units, a.SponsorshipOfferId, a.PairId, a.Note, a.At, a.Actor);
}

internal sealed class AddGoalCorrectionCommandValidator : AbstractValidator<AddGoalCorrectionCommand>
{
    public AddGoalCorrectionCommandValidator()
    {
        RuleFor(c => c.Input.Amount).Must(a => a is null || (decimal.TryParse(a, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var v) && decimal.Round(v, 2) == v && Math.Abs(v) <= 10_000_000m))
            .WithMessage("amount must be a signed money string with up to two decimals, e.g. \"-50.00\".");
        RuleFor(c => c.Input.Units).InclusiveBetween(-100_000, 100_000);
        RuleFor(c => c.Input.Note).NotEmpty().MaximumLength(500);
    }
}
