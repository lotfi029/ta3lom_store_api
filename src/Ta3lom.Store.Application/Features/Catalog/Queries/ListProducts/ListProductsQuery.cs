using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.ListProducts;

/// `GET /v1/catalog/products` (#3, FR-S02/S03, RUL-02): published products only; `sort` = featured | newest |
/// price_asc | price_desc; `q` is normalised with `ar_normalize()` by the search port.
public sealed record ListProductsQuery(string? Type, string? Category, bool? Sponsorable, string? Q, string? Sort, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<ProductCardResponse>>;

internal sealed class ListProductsQueryHandler(CatalogReader reader, IApplicationDbContext db, ICatalogSearch search, IClock clock)
    : IQueryHandler<ListProductsQuery, PagedResult<ProductCardResponse>>
{
    public async Task<Result<PagedResult<ProductCardResponse>>> HandleAsync(ListProductsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        IReadOnlyList<Guid>? matches = string.IsNullOrWhiteSpace(query.Q) ? null : await search.SearchPublishedAsync(query.Q.Trim(), ct);
        var products = await CatalogFilters.ApplyAsync(reader.Published, db, clock.UtcNow, query.Type, query.Category, query.Sponsorable, matches, ct);

        // Ordering needs prices for price_*: small catalogue (tens of products), sorted in memory after filtering.
        // A search keeps the relevance order of the search port.
        var all = await products.ToListAsync(ct);
        if (matches is not null)
        {
            all = [.. all.OrderBy(p => matches.ToList().IndexOf(p.Id))];
        }

        var cards = await reader.CardsAsync(all, ct);
        IEnumerable<ProductCardResponse> ordered = (query.Sort ?? "featured") switch
        {
            "newest" => cards.OrderByDescending(c => all.First(p => p.Id == c.Id).PublishedAt),
            "price_asc" => cards.OrderBy(c => c.Price is null ? decimal.MaxValue : decimal.Parse(c.Price, System.Globalization.CultureInfo.InvariantCulture)),
            "price_desc" => cards.OrderByDescending(c => c.Price is null ? decimal.MinValue : decimal.Parse(c.Price, System.Globalization.CultureInfo.InvariantCulture)),
            _ when query.Q is { Length: > 0 } => cards,
            _ => cards.OrderBy(c => all.First(p => p.Id == c.Id).Sort).ThenBy(c => c.Slug, StringComparer.Ordinal),
        };
        var list = ordered.ToList();
        return new PagedResult<ProductCardResponse>([.. list.Skip(page.Skip).Take(page.Take)], list.Count, page.SafePage, page.Take);
    }
}

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(q => q.Type).Must(t => t is null or "course" or "book" or "cause").WithMessage("type must be course, book or cause.");
        RuleFor(q => q.Sort).Must(s => s is null or "featured" or "newest" or "price_asc" or "price_desc")
            .WithMessage("sort must be featured, newest, price_asc or price_desc.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(q => q.Q).MaximumLength(100);
    }
}

/// Filters shared by the card list and the slug list.
internal static class CatalogFilters
{
    public static async Task<IQueryable<Product>> ApplyAsync(IQueryable<Product> products, IApplicationDbContext db, DateTimeOffset now,
        string? type, string? category, bool? sponsorable, IReadOnlyList<Guid>? matches, CancellationToken ct)
    {
        if (type is not null && EnumWire.TryParse<ProductType>(type, out var productType))
        {
            products = products.Where(p => p.Type == productType);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryId = await db.Categories.AsNoTracking().Where(c => c.Slug == category).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (sponsorable == true)
        {
            products = products.Where(p => db.SponsorshipOffers.Any(o => o.ProductId == p.Id && o.IsActive && o.StartsAt <= now && o.EndsAt > now));
        }

        if (matches is not null)
        {
            var ids = matches.ToList();
            products = products.Where(p => ids.Contains(p.Id));
        }

        return products;
    }
}
