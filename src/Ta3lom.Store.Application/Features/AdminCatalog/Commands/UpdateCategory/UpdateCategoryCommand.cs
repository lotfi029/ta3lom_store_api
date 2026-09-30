using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateCategory;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateCategory;

/// `PUT /v1/admin/categories/{id}` (#74).
public sealed record UpdateCategoryCommand(Guid Id, CategoryInput Input) : ICommand<AdminCategoryResponse>;

internal sealed class UpdateCategoryCommandHandler(ICategoryRepository categories, IUnitOfWork uow) : ICommandHandler<UpdateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> HandleAsync(UpdateCategoryCommand command, CancellationToken ct = default)
    {
        var category = await categories.GetByIdAsync(command.Id, ct);
        if (category is null)
        {
            return GeneralErrors.NotFound;
        }

        var i = command.Input;
        if (await categories.SlugTakenAsync(i.Slug, category.Id, ct))
        {
            return AdminCatalogErrors.CategorySlugTaken;
        }

        category.Update(i.Slug, i.NameAr, i.NameEn, i.Sort, i.IsActive);
        await uow.SaveChangesAsync(ct);
        return CreateCategoryCommandHandler.ToResponse(category);
    }
}

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new CategoryInputValidator());
}
