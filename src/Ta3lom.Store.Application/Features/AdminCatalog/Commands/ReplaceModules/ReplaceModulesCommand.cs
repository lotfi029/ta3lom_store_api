using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.ReplaceModules;

/// `PUT /v1/admin/products/{id}/modules` (#59, FR-S04): the curriculum is replaced as a whole.
public sealed record ReplaceModulesCommand(Guid ProductId, IReadOnlyList<CourseModuleInput> Modules) : ICommand<AdminProductResponse>;

internal sealed class ReplaceModulesCommandHandler(
    IProductRepository products,
    IGenericRepository<CourseModule> modules,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ReplaceModulesCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(ReplaceModulesCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        if (product.Type != ProductType.Course)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["The product is not a course."] });
        }

        await uow.ExecuteInTransactionAsync(
            async token =>
            {
                foreach (var old in await modules.ListAsync(m => m.ProductId == product.Id, token))
                {
                    modules.Remove(old);
                }

                await uow.SaveChangesAsync(token);       // delete first: (product_id, sort) is unique
                modules.AddRange(command.Modules.Select(m => CourseModule.Create(product.Id, m.Sort, m.TitleAr, m.TitleEn, m.DurationSeconds)));
                product.Touch(clock.UtcNow);
            },
            ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

internal sealed class ReplaceModulesCommandValidator : AbstractValidator<ReplaceModulesCommand>
{
    public ReplaceModulesCommandValidator()
    {
        RuleFor(c => c.Modules).NotNull().Must(m => m.Select(x => x.Sort).Distinct().Count() == m.Count).WithMessage("sort values must be unique.");
        RuleForEach(c => c.Modules).ChildRules(m =>
        {
            m.RuleFor(x => x.TitleAr).NotEmpty();
            m.RuleFor(x => x.DurationSeconds).GreaterThanOrEqualTo(0);
        });
    }
}
