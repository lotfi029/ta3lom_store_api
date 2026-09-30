using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateCategory;

/// `POST /v1/admin/categories` (#73, FR-S72). Categories are deactivated, never deleted.
public sealed record CreateCategoryCommand(CategoryInput Input) : ICommand<AdminCategoryResponse>;

internal sealed class CreateCategoryCommandHandler(ICategoryRepository categories, IUnitOfWork uow) : ICommandHandler<CreateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> HandleAsync(CreateCategoryCommand command, CancellationToken ct = default)
    {
        var i = command.Input;
        if (await categories.SlugTakenAsync(i.Slug, null, ct))
        {
            return AdminCatalogErrors.CategorySlugTaken;
        }

        var category = new Category(i.Slug, i.NameAr);
        category.Update(i.Slug, i.NameAr, i.NameEn, i.Sort, i.IsActive);
        categories.Add(category);
        await uow.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    public static AdminCategoryResponse ToResponse(Category c) => new(c.Id, c.Slug, c.NameAr, c.NameEn, c.Sort, c.IsActive);
}

internal sealed class CategoryInputValidator : AbstractValidator<CategoryInput>
{
    public CategoryInputValidator()
    {
        RuleFor(i => i.Slug).NotEmpty().Matches(AdminInput.CategorySlug()).WithMessage("slug must match ^[a-z0-9-]{2,60}$.");
        RuleFor(i => i.NameAr).NotEmpty().MaximumLength(120);
    }
}

internal sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new CategoryInputValidator());
}
