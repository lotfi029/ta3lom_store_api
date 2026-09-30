using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.PublishProduct;

/// `POST /v1/admin/products/{id}/publish` (#64, RUL-02, RUL-03): refuses with `missing[]` when the Arabic fields,
/// the cover (with alt), the type block, a price or format, or valid dates are missing.
public sealed record PublishProductCommand(Guid ProductId) : ICommand<AdminProductResponse>;

internal sealed class PublishProductCommandHandler(
    IProductRepository products,
    IApplicationDbContext db,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<PublishProductCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(PublishProductCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        var missing = await MissingAsync(product, ct);
        if (missing.Count > 0)
        {
            return AdminCatalogErrors.PublishIncomplete(missing);
        }

        product.Publish(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }

    private async Task<List<string>> MissingAsync(Product p, CancellationToken ct)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(p.TitleAr))
        {
            missing.Add("titleAr");
        }

        if (string.IsNullOrWhiteSpace(p.SummaryAr))
        {
            missing.Add("summaryAr");
        }

        if (p.CoverAssetId is null)
        {
            missing.Add("cover");
        }
        else if (!await db.MediaAssets.AnyAsync(m => m.Id == p.CoverAssetId && m.Kind == MediaKind.Image && m.AltAr != null, ct))
        {
            missing.Add("cover.altAr");
        }

        switch (p.Type)
        {
            case ProductType.Course:
                if (!await db.CourseDetails.AnyAsync(c => c.ProductId == p.Id, ct))
                {
                    missing.Add("course");
                }

                break;
            case ProductType.Book:
                if (!await db.BookDetails.AnyAsync(b => b.ProductId == p.Id, ct))
                {
                    missing.Add("book");
                }

                if (!await db.BookFormats.AnyAsync(f => f.ProductId == p.Id && f.IsActive, ct))
                {
                    missing.Add("formats");
                }

                break;
            case ProductType.Cause:
                var cause = await db.CauseDetails.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == p.Id, ct);
                if (cause is null)
                {
                    missing.Add("cause");
                }
                else if (cause.EndsAt is null && !cause.IsGeneralFund)
                {
                    missing.Add("cause.endsAt");
                }

                break;
        }

        return missing;
    }
}
