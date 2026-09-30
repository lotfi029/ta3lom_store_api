using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.SetGoalSeed;

/// `PUT /v1/admin/products/{id}/goal-seed` (#67, database/03 §3): opening balances from offline giving; the
/// change and its reason are audited (audit_log row of the staff actor).
public sealed record SetGoalSeedCommand(Guid ProductId, GoalSeedInput Input) : ICommand<GoalFiguresResponse>;

internal sealed class SetGoalSeedCommandHandler(
    IProductRepository products,
    IGenericRepository<GoalProgress> goals,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<SetGoalSeedCommand, GoalFiguresResponse>
{
    public async Task<Result<GoalFiguresResponse>> HandleAsync(SetGoalSeedCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        var now = clock.UtcNow;
        var goal = await goals.GetByIdAsync(product.Id, ct);
        if (goal is null)
        {
            goal = new GoalProgress(product.Id, now);
            goals.Add(goal);
        }

        goal.SetSeed(AdminInput.Decimal(command.Input.SeedRaised), command.Input.SeedUnits, command.Input.SeedDonors, now);
        product.Touch(now);
        await uow.SaveChangesAsync(ct);
        return AdminCatalogReader.Goal(goal);
    }
}

internal sealed class SetGoalSeedCommandValidator : AbstractValidator<SetGoalSeedCommand>
{
    public SetGoalSeedCommandValidator()
    {
        RuleFor(c => c.Input.SeedRaised).Must(AdminInput.IsMoney).WithMessage("seedRaised must be a money string.");
        RuleFor(c => c.Input.SeedUnits).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Input.SeedDonors).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Input.Reason).NotEmpty().MaximumLength(500);
    }
}
