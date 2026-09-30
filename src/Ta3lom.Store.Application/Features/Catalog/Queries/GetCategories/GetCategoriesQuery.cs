using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetCategories;

/// `GET /v1/catalog/categories` (#2): active categories only (FR-S02), by `sort` then creation order (v7 ids).
public sealed record GetCategoriesQuery : IQuery<IReadOnlyList<CategoryResponse>>;

internal sealed class GetCategoriesQueryHandler(IApplicationDbContext db) : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CategoryResponse>>> HandleAsync(GetCategoriesQuery query, CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Sort).ThenBy(c => c.Id)
            .Select(c => new CategoryResponse(c.Slug, c.NameAr, c.NameEn, c.Sort)).ToListAsync(ct);
}
