using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateProduct;

/// `POST /v1/admin/products` (#55, RUL-01, RUL-03): a new product starts as `draft` with its goal counters row.
public sealed record CreateProductCommand(ProductInput Input) : ICommand<AdminProductResponse>;

internal sealed class CreateProductCommandHandler(
    IProductRepository products,
    IGenericRepository<GoalProgress> goals,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<CreateProductCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(CreateProductCommand command, CancellationToken ct = default)
    {
        var i = command.Input;
        if (await products.SlugTakenAsync(i.Slug, null, ct))
        {
            return AdminCatalogErrors.SlugTaken;
        }

        var now = clock.UtcNow;
        var type = EnumWire.Parse<ProductType>(i.Type);
        var product = new Product(type, i.Slug, i.TitleAr, i.SummaryAr, now);
        product.Update(type, hasOrders: false, i.Slug, i.CategoryId, i.CoverAssetId, i.TitleAr, i.TitleEn, i.SummaryAr, i.SummaryEn, i.BodyAr, i.BodyEn,
            i.Badge is null ? null : EnumWire.Parse<ProductBadge>(i.Badge), i.Sort, i.SeoTitleAr, i.SeoTitleEn, i.SeoDescAr, i.SeoDescEn, now);
        products.Add(product);
        goals.Add(new GoalProgress(product.Id, now));
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

/// Shape of `ProductInput` (11 §9.3), shared by create and update.
internal sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    public ProductInputValidator()
    {
        RuleFor(i => i.Type).Must(t => EnumWire.TryParse<ProductType>(t, out _)).WithMessage("type must be course, book or cause.");
        RuleFor(i => i.Slug).NotEmpty().Matches(AdminInput.ProductSlug()).WithMessage("slug must match ^[a-z0-9-]{2,80}$.");
        RuleFor(i => i.TitleAr).NotEmpty().Length(2, 160);
        RuleFor(i => i.SummaryAr).NotEmpty();
        RuleFor(i => i.Badge).Must(b => b is null || EnumWire.TryParse<ProductBadge>(b, out _)).WithMessage("badge must be best, new or urgent.");
    }
}

internal sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new ProductInputValidator());
}
