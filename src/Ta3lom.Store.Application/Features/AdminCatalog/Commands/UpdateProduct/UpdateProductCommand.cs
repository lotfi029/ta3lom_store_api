using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateProduct;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateProduct;

/// `PUT /v1/admin/products/{id}` (#57): the type cannot change once an order exists (RUL-01).
public sealed record UpdateProductCommand(Guid Id, ProductInput Input) : ICommand<AdminProductResponse>;

internal sealed class UpdateProductCommandHandler(IProductRepository products, AdminCatalogReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<UpdateProductCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(UpdateProductCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.Id, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        var i = command.Input;
        var type = EnumWire.Parse<ProductType>(i.Type);
        if (type != product.Type && await products.HasOrdersAsync(product.Id, ct))
        {
            return AdminCatalogErrors.TypeLocked;
        }

        if (await products.SlugTakenAsync(i.Slug, product.Id, ct))
        {
            return AdminCatalogErrors.SlugTaken;
        }

        product.Update(type, hasOrders: false, i.Slug, i.CategoryId, i.CoverAssetId, i.TitleAr, i.TitleEn, i.SummaryAr, i.SummaryEn, i.BodyAr, i.BodyEn,
            i.Badge is null ? null : EnumWire.Parse<ProductBadge>(i.Badge), i.Sort, i.SeoTitleAr, i.SeoTitleEn, i.SeoDescAr, i.SeoDescEn, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

internal sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new ProductInputValidator());
}
