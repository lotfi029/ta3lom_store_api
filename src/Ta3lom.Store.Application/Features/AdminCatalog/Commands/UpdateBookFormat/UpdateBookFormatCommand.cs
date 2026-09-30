using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.AddBookFormat;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateBookFormat;

/// `PUT /v1/admin/formats/{formatId}` (#62, RUL-05): no stock fields; the kind cannot change.
public sealed record UpdateBookFormatCommand(Guid FormatId, BookFormatInput Input) : ICommand<AdminBookFormat>;

internal sealed class UpdateBookFormatCommandHandler(
    IGenericRepository<BookFormat> formats,
    IGenericRepository<MediaAsset> media,
    IProductRepository products,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpdateBookFormatCommand, AdminBookFormat>
{
    public async Task<Result<AdminBookFormat>> HandleAsync(UpdateBookFormatCommand command, CancellationToken ct = default)
    {
        var format = await formats.GetByIdAsync(command.FormatId, ct);
        if (format is null)
        {
            return GeneralErrors.NotFound;
        }

        var i = command.Input;
        if (i.Kind != format.Kind.ToWire())
        {
            return Error.Validation(new Dictionary<string, string[]> { ["kind"] = ["The format kind cannot change."] });
        }

        if (await FormatRules.CheckFileAsync(format.Kind, i.FileAssetId, media, ct) is { } fileError)
        {
            return fileError;
        }

        if (await formats.ExistsAsync(f => f.Sku == i.Sku && f.Id != format.Id, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["sku"] = ["The SKU is already used."] });
        }

        format.Update(i.Sku, AdminInput.Money(i.Price), AdminInput.OptionalMoney(i.CompareAtPrice), i.IsActive, i.WeightGrams, i.LowStockThreshold, i.FileAssetId);
        (await products.GetByIdAsync(format.ProductId, ct))?.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return AdminCatalogReader.Format(format);
    }
}

internal sealed class UpdateBookFormatCommandValidator : AbstractValidator<UpdateBookFormatCommand>
{
    public UpdateBookFormatCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new BookFormatInputValidator());
}
