using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertCauseDetail;

/// `PUT /v1/admin/products/{id}/cause` (#63, RUL-07, RUL-10): unit amount needs a label; one general fund only;
/// dates are Riyadh wall times (RUL-09).
public sealed record UpsertCauseDetailCommand(Guid ProductId, CauseDetailInput Input) : ICommand<AdminProductResponse>;

internal sealed class UpsertCauseDetailCommandHandler(
    IProductRepository products,
    IGenericRepository<CauseDetail> causes,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpsertCauseDetailCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(UpsertCauseDetailCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        if (product.Type != ProductType.Cause)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["The product is not a cause."] });
        }

        var i = command.Input;
        if (i.IsGeneralFund && await causes.ExistsAsync(c => c.IsGeneralFund && c.ProductId != product.Id, ct))
        {
            return AdminCatalogErrors.GeneralFundExists;
        }

        var now = clock.UtcNow;
        var target = AdminInput.Decimal(i.TargetAmount);
        var detail = await causes.GetByIdAsync(product.Id, ct);
        if (detail is null)
        {
            detail = new CauseDetail(product.Id, target, now);
            causes.Add(detail);
        }

        var startsAt = i.StartsAtLocal is { } s ? AdminInput.FromRiyadh(s) : detail.StartsAt;
        var endsAt = i.EndsAtLocal is { } e ? AdminInput.FromRiyadh(e) : (DateTimeOffset?)null;
        detail.Update(AdminInput.OptionalMoney(i.UnitAmount), i.UnitLabelAr, i.UnitLabelEn, target, i.AllowCustomAmount, AdminInput.Money(i.MinAmount),
            i.ZakatEligible, i.BeneficiariesAr, i.BeneficiariesEn, i.CloseOnTarget, i.IsGeneralFund, startsAt, endsAt);
        product.Touch(now);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

internal sealed class UpsertCauseDetailCommandValidator : AbstractValidator<UpsertCauseDetailCommand>
{
    public UpsertCauseDetailCommandValidator()
    {
        RuleFor(c => c.Input.UnitAmount).Must(AdminInput.IsOptionalMoney).WithMessage("unitAmount must be a money string.");
        RuleFor(c => c.Input.UnitLabelAr).NotEmpty().When(c => c.Input.UnitAmount is not null).WithMessage("unitLabelAr is required with unitAmount (RUL-07).");
        RuleFor(c => c.Input.AllowCustomAmount).Equal(true).When(c => c.Input.UnitAmount is null).WithMessage("Without unitAmount, custom amounts must be allowed (RUL-07).");
        RuleFor(c => c.Input.TargetAmount).Must(t => AdminInput.IsMoney(t) && AdminInput.Decimal(t) > 0).WithMessage("targetAmount must be a positive money string.");
        RuleFor(c => c.Input.MinAmount).Must(m => AdminInput.IsMoney(m) && AdminInput.Decimal(m) > 0).WithMessage("minAmount must be a positive money string.");
        RuleFor(c => c.Input).Must(i => i.EndsAtLocal is null || i.StartsAtLocal is null || i.EndsAtLocal > i.StartsAtLocal)
            .WithName("endsAtLocal").WithMessage("endsAtLocal must be after startsAtLocal.");
        RuleFor(c => c.Input.EndsAtLocal).NotNull().When(c => !c.Input.IsGeneralFund).WithMessage("endsAtLocal is required except for the general fund.");
    }
}
