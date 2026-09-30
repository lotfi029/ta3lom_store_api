using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.Catalog;
using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListAdminProducts;

/// `GET /v1/admin/products` (#54): every status; filter by type, status, category slug, `q` (title/slug contains).
public sealed record ListAdminProductsQuery(string? Type, string? Status, string? Category, string? Q, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AdminProductRow>>;

internal sealed class ListAdminProductsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListAdminProductsQuery, PagedResult<AdminProductRow>>
{
    public async Task<Result<PagedResult<AdminProductRow>>> HandleAsync(ListAdminProductsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var products = db.Products.AsNoTracking();
        if (EnumWire.TryParse<ProductType>(query.Type, out var type))
        {
            products = products.Where(p => p.Type == type);
        }

        if (EnumWire.TryParse<ProductStatus>(query.Status, out var status))
        {
            products = products.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var categoryId = await db.Categories.AsNoTracking().Where(c => c.Slug == query.Category).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            products = products.Where(p => p.TitleAr.Contains(q) || (p.TitleEn != null && p.TitleEn.Contains(q)) || p.Slug.Contains(q));
        }

        var total = await products.CountAsync(ct);
        var rows = await products.OrderBy(p => p.Type).ThenBy(p => p.Sort).ThenBy(p => p.Slug).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var coursePrices = await db.CourseDetails.AsNoTracking().Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, c => c.Price, ct);
        var bookPrices = (await db.BookFormats.AsNoTracking().Where(f => ids.Contains(f.ProductId) && f.IsActive).ToListAsync(ct))
            .GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.Min(f => f.Price.Amount));
        var causeTargets = await db.CauseDetails.AsNoTracking().Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, c => c.TargetAmount, ct);
        var goals = await db.Goals.AsNoTracking().Where(g => ids.Contains(g.ProductId)).ToDictionaryAsync(g => g.ProductId, ct);
        var offers = await db.SponsorshipOffers.AsNoTracking().Where(o => ids.Contains(o.ProductId) && o.IsActive).Select(o => o.ProductId).ToListAsync(ct);

        return new PagedResult<AdminProductRow>([.. rows.Select(p =>
        {
            string? price = coursePrices.TryGetValue(p.Id, out var cp) ? cp.ToWire() : bookPrices.TryGetValue(p.Id, out var bp) ? Wire.Money(bp) : null;
            int? percent = causeTargets.TryGetValue(p.Id, out var target) ? GoalMath.Percent(goals.GetValueOrDefault(p.Id)?.RaisedAmount ?? 0m, target) : null;
            return new AdminProductRow(p.Id, p.Type.ToWire(), p.Slug, p.TitleAr, p.Status.ToWire(), price, percent, p.UpdatedAt, offers.Contains(p.Id));
        })], total, page.SafePage, page.Take);
    }
}

internal sealed class ListAdminProductsQueryValidator : AbstractValidator<ListAdminProductsQuery>
{
    public ListAdminProductsQueryValidator()
    {
        RuleFor(q => q.Type).Must(t => t is null || EnumWire.TryParse<ProductType>(t, out _)).WithMessage("type must be course, book or cause.");
        RuleFor(q => q.Status).Must(s => s is null || EnumWire.TryParse<ProductStatus>(s, out _)).WithMessage("status must be draft, published or archived.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}
