using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Categories;

/// Loads a category's image and checks it is a public image (R-8).
internal sealed class CategoryImages(IGenericRepository<MediaAsset> media)
{
    public async Task<Result<MediaAsset?>> ResolveAsync(Guid? imageAssetId, CancellationToken ct)
    {
        if (imageAssetId is not { } id)
        {
            return Result.Success<MediaAsset?>(null);
        }

        var asset = await media.GetByIdAsync(id, ct);
        return asset is { IsPublicImage: true } ? Result.Success<MediaAsset?>(asset) : CatalogErrors.ImageRequired;
    }

    public static CategoryDetails Details(CategoryInput i) =>
        new(i.Slug, i.NameAr, i.NameEn, i.DescriptionAr, i.DescriptionEn, i.AcceptsGeneralGifts, i.Sort, i.IsActive);
}

/// `POST /v1/admin/categories` (BR-A01).
public sealed record CreateCategoryCommand(CategoryInput Input) : ICommand<AdminCategoryResponse>;

internal sealed class CreateCategoryCommandHandler(ICategoryRepository categories, CategoryImages images, AdminCategoryReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<CreateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> HandleAsync(CreateCategoryCommand command, CancellationToken ct = default)
    {
        if (await categories.SlugInUseAsync(command.Input.Slug, null, ct))
        {
            return CatalogErrors.CategorySlugTaken;
        }

        var image = await images.ResolveAsync(command.Input.ImageAssetId, ct);
        if (image.IsFailure)
        {
            return image.Error;
        }

        var category = Category.Create(CategoryImages.Details(command.Input), image.Value, clock.UtcNow);
        categories.Add(category);
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(category.Id, ct);
    }
}

/// `PUT /v1/admin/categories/{id}` with the `version` read before (409 on a stale one).
public sealed record UpdateCategoryCommand(Guid Id, CategoryInput Input) : ICommand<AdminCategoryResponse>;

internal sealed class UpdateCategoryCommandHandler(ICategoryRepository categories, CategoryImages images, AdminCategoryReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<UpdateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> HandleAsync(UpdateCategoryCommand command, CancellationToken ct = default)
    {
        var category = await categories.GetByIdAsync(command.Id, ct);
        if (category is null)
        {
            return CatalogErrors.CategoryNotFound;
        }

        if (!category.IsVersion(command.Input.Version ?? 0))
        {
            return GeneralErrors.ConcurrencyConflict;
        }

        if (await categories.SlugInUseAsync(command.Input.Slug, category.Id, ct))
        {
            return CatalogErrors.CategorySlugTaken;
        }

        var image = await images.ResolveAsync(command.Input.ImageAssetId, ct);
        if (image.IsFailure)
        {
            return image.Error;
        }

        category.Update(CategoryImages.Details(command.Input), image.Value, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(category.Id, ct);
    }
}

/// `PUT /v1/admin/categories/order`: the new sort value of each listed category.
public sealed record ReorderCategoriesCommand(IReadOnlyList<CategoryOrderItem> Items) : ICommand<IReadOnlyList<AdminCategoryResponse>>;

internal sealed class ReorderCategoriesCommandHandler(ICategoryRepository categories, AdminCategoryReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<ReorderCategoriesCommand, IReadOnlyList<AdminCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<AdminCategoryResponse>>> HandleAsync(ReorderCategoriesCommand command, CancellationToken ct = default)
    {
        var ids = command.Items.Select(i => i.Id).ToList();
        var found = await categories.ListAsync(c => ids.Contains(c.Id), ct);
        if (found.Count != ids.Distinct().Count())
        {
            return CatalogErrors.CategoryNotFound;
        }

        var now = clock.UtcNow;
        foreach (var item in command.Items)
        {
            found.Single(c => c.Id == item.Id).MoveTo(item.Sort, now);
        }

        await uow.SaveChangesAsync(ct);
        return await reader.ListAsync(ct);
    }
}

/// `GET /v1/admin/categories` (every category, active or not).
public sealed record ListAdminCategoriesQuery : IQuery<IReadOnlyList<AdminCategoryResponse>>;

internal sealed class ListAdminCategoriesQueryHandler(AdminCategoryReader reader) : IQueryHandler<ListAdminCategoriesQuery, IReadOnlyList<AdminCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<AdminCategoryResponse>>> HandleAsync(ListAdminCategoriesQuery query, CancellationToken ct = default) =>
        await reader.ListAsync(ct);
}

internal sealed class AdminCategoryReader(IApplicationDbContext db, IFileUrls urls)
{
    public async Task<AdminCategoryResponse> GetAsync(Guid id, CancellationToken ct) => (await QueryAsync(id, ct)).Single();

    public Task<List<AdminCategoryResponse>> ListAsync(CancellationToken ct) => QueryAsync(null, ct);

    private async Task<List<AdminCategoryResponse>> QueryAsync(Guid? id, CancellationToken ct)
    {
        var rows = await (
            from c in db.Categories.AsNoTracking()
            join m in db.MediaAssets.AsNoTracking() on c.ImageAssetId equals m.Id into images
            from image in images.DefaultIfEmpty()
            where id == null || c.Id == id
            orderby c.Sort, c.NameAr
            select new { Category = c, ImageKey = image != null ? image.StorageKey : null, Count = db.Projects.Count(p => p.CategoryId == c.Id) })
            .ToListAsync(ct);
        return [.. rows.Select(r => new AdminCategoryResponse(r.Category.Id, r.Category.Slug, r.Category.NameAr, r.Category.NameEn,
            r.Category.DescriptionAr, r.Category.DescriptionEn, r.Category.ImageAssetId, r.ImageKey is null ? null : urls.Public(r.ImageKey),
            r.Category.AcceptsGeneralGifts, r.Category.Sort, r.Category.IsActive, r.Count, r.Category.UpdatedAt, r.Category.Version))];
    }
}

internal sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(c => c.Input).SetValidator(new CategoryInputValidator());
}

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(c => c.Input).SetValidator(new CategoryInputValidator());
        RuleFor(c => c.Input.Version).NotNull().WithMessage("version is required.");
    }
}

internal sealed class ReorderCategoriesCommandValidator : AbstractValidator<ReorderCategoriesCommand>
{
    public ReorderCategoriesCommandValidator()
    {
        RuleFor(c => c.Items).NotEmpty().Must(i => i.Select(x => x.Id).Distinct().Count() == i.Count).WithMessage("Each category once.");
        RuleForEach(c => c.Items).ChildRules(i => i.RuleFor(x => x.Sort).InclusiveBetween(0, 10_000));
    }
}

internal sealed class CategoryInputValidator : AbstractValidator<CategoryInput>
{
    public CategoryInputValidator()
    {
        RuleFor(i => i.Slug).NotEmpty().Must(Slug.IsValidCategory).WithMessage("slug must match ^[a-z0-9-]{2,60}$.");
        RuleFor(i => i.NameAr).NotEmpty().MaximumLength(120);
        RuleFor(i => i.NameEn).MaximumLength(120);
        RuleFor(i => i.DescriptionAr).MaximumLength(2000);
        RuleFor(i => i.DescriptionEn).MaximumLength(2000);
        RuleFor(i => i.Sort).InclusiveBetween(0, 10_000);
    }
}
