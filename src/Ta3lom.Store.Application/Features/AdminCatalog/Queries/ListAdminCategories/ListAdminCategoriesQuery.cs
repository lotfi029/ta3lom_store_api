using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminCatalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListAdminCategories;

/// `GET /v1/admin/categories` (#72): including inactive ones.
public sealed record ListAdminCategoriesQuery : IQuery<IReadOnlyList<AdminCategoryResponse>>;

internal sealed class ListAdminCategoriesQueryHandler(IApplicationDbContext db) : IQueryHandler<ListAdminCategoriesQuery, IReadOnlyList<AdminCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<AdminCategoryResponse>>> HandleAsync(ListAdminCategoriesQuery query, CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.Sort).ThenBy(c => c.Id)
            .Select(c => new AdminCategoryResponse(c.Id, c.Slug, c.NameAr, c.NameEn, c.Sort, c.IsActive)).ToListAsync(ct);
}
