using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertBookDetail;

/// `PUT /v1/admin/products/{id}/book` (#60, RUL-05).
public sealed record UpsertBookDetailCommand(Guid ProductId, BookDetailInput Input) : ICommand<AdminProductResponse>;

internal sealed class UpsertBookDetailCommandHandler(
    IProductRepository products,
    IGenericRepository<BookDetail> books,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpsertBookDetailCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(UpsertBookDetailCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        if (product.Type != ProductType.Book)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["The product is not a book."] });
        }

        var i = command.Input;
        var detail = await books.GetByIdAsync(product.Id, ct);
        if (detail is null)
        {
            detail = new BookDetail(product.Id, i.AuthorAr);
            books.Add(detail);
        }

        detail.Update(i.AuthorAr, i.AuthorEn, i.Pages, i.Isbn, i.PublishedYear);
        product.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}

internal sealed class UpsertBookDetailCommandValidator : AbstractValidator<UpsertBookDetailCommand>
{
    public UpsertBookDetailCommandValidator()
    {
        RuleFor(c => c.Input.AuthorAr).NotEmpty();
        RuleFor(c => c.Input.Pages).GreaterThan(0).When(c => c.Input.Pages is not null);
    }
}
