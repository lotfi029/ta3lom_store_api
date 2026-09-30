using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.AddBookFormat;

/// `POST /v1/admin/products/{id}/formats` (#61, RUL-05): one format per kind; print stock starts at 0 and changes only
/// through stock movements (S-21); a digital format needs a private `book_file` asset.
public sealed record AddBookFormatCommand(Guid ProductId, BookFormatInput Input) : ICommand<AdminBookFormat>;

internal sealed class AddBookFormatCommandHandler(
    IProductRepository products,
    IGenericRepository<BookFormat> formats,
    IGenericRepository<MediaAsset> media,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<AddBookFormatCommand, AdminBookFormat>
{
    public async Task<Result<AdminBookFormat>> HandleAsync(AddBookFormatCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        var i = command.Input;
        var kind = EnumWire.Parse<BookFormatKind>(i.Kind);
        if (product.Type != ProductType.Book)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["The product is not a book."] });
        }

        if (await formats.ExistsAsync(f => f.ProductId == product.Id && f.Kind == kind, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["kind"] = [$"The book already has a {i.Kind} format."] });
        }

        if (await FormatRules.CheckFileAsync(kind, i.FileAssetId, media, ct) is { } fileError)
        {
            return fileError;
        }

        if (await formats.ExistsAsync(f => f.Sku == i.Sku, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["sku"] = ["The SKU is already used."] });
        }

        var format = BookFormat.Create(product.Id, kind, i.Sku, AdminInput.Money(i.Price));
        format.Update(i.Sku, AdminInput.Money(i.Price), AdminInput.OptionalMoney(i.CompareAtPrice), i.IsActive, i.WeightGrams, i.LowStockThreshold, i.FileAssetId);
        formats.Add(format);
        product.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return AdminCatalogReader.Format(format);
    }
}

internal static class FormatRules
{
    /// RUL-05: digital formats point to a private `book_file`.
    public static async Task<Error?> CheckFileAsync(BookFormatKind kind, Guid? fileAssetId, IGenericRepository<MediaAsset> media, CancellationToken ct)
    {
        if (kind != BookFormatKind.Digital)
        {
            return null;
        }

        if (fileAssetId is null || !await media.ExistsAsync(m => m.Id == fileAssetId && m.Kind == MediaKind.BookFile, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["fileAssetId"] = ["A digital format needs an uploaded book_file (RUL-05)."] });
        }

        return null;
    }
}

internal sealed class BookFormatInputValidator : AbstractValidator<BookFormatInput>
{
    public BookFormatInputValidator()
    {
        RuleFor(i => i.Kind).Must(k => k is "print" or "digital").WithMessage("kind must be print or digital.");
        RuleFor(i => i.Sku).NotEmpty().MaximumLength(64);
        RuleFor(i => i.Price).Must(AdminInput.IsMoney).WithMessage("price must be a money string.");
        RuleFor(i => i.CompareAtPrice).Must(AdminInput.IsOptionalMoney).WithMessage("compareAtPrice must be a money string.");
        RuleFor(i => i).Must(i => i.CompareAtPrice is null || !AdminInput.IsMoney(i.Price) || !AdminInput.IsMoney(i.CompareAtPrice)
                || AdminInput.Decimal(i.CompareAtPrice) > AdminInput.Decimal(i.Price))
            .WithName("compareAtPrice").WithMessage("compareAtPrice must be higher than price (RUL-05).");
        RuleFor(i => i.LowStockThreshold).GreaterThanOrEqualTo(0).When(i => i.LowStockThreshold is not null);
        RuleFor(i => i.WeightGrams).GreaterThan(0).When(i => i.WeightGrams is not null);
    }
}

internal sealed class AddBookFormatCommandValidator : AbstractValidator<AddBookFormatCommand>
{
    public AddBookFormatCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new BookFormatInputValidator());
}
