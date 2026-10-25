using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Catalog.Queries;

/// `GET /v1/catalog/projects` (BR-A06): published projects, filtered by category slug and an Arabic-aware search, sorted
/// and paged in SQL. An unknown category gives an empty page. With `q` the order is the search rank.
public sealed record ListProjectsQuery(string? Q, string? Category, string? Sort, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ProjectCard>>;

internal sealed class ListProjectsQueryHandler(CatalogReader reader, ICatalogSearch search, IStoreSettings settings)
    : IQueryHandler<ListProjectsQuery, PagedResult<ProjectCard>>
{
    public static readonly string[] Sorts = ["featured", "newest", "title"];

    public async Task<Result<PagedResult<ProjectCard>>> HandleAsync(ListProjectsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var minGift = (await settings.GetAsync(ct)).MinGiftAmount;
        var cards = reader.PublishedCards();
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            cards = cards.Where(c => c.CategorySlug == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var found = await search.SearchPageAsync(new CatalogSearchRequest(query.Q, ProjectStatus.Published.ToWire(),
                string.IsNullOrWhiteSpace(query.Category) ? null : query.Category, null, ActiveCategoryOnly: true, page.Skip, page.Take), ct);
            var rows = await reader.PublishedCards().Where(c => found.Ids.Contains(c.Id)).ToListAsync(ct);
            var rank = found.Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return new PagedResult<ProjectCard>([.. rows.OrderBy(r => rank[r.Id]).Select(r => reader.ToCard(r, minGift))], found.Total, page.SafePage, page.Take);
        }

        var total = await cards.CountAsync(ct);
        cards = query.Sort switch
        {
            "newest" => cards.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id),
            "title" => cards.OrderBy(c => c.TitleAr).ThenBy(c => c.Id),
            _ => cards.OrderBy(c => c.Sort).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id),
        };
        var pageRows = await cards.Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<ProjectCard>([.. pageRows.Select(r => reader.ToCard(r, minGift))], total, page.SafePage, page.Take);
    }
}

internal sealed class ListProjectsQueryValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(100);
        RuleFor(q => q.Category).MaximumLength(Slug.CategoryMax);
        RuleFor(q => q.Sort).Must(s => s is null || ListProjectsQueryHandler.Sorts.Contains(s)).WithMessage("sort must be featured, newest or title.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

/// `GET /v1/catalog/projects?fields=slug`: every published slug, for prerendering the site. Any other `fields` is a 400.
public sealed record ListProjectSlugsQuery(string Fields) : IQuery<ProjectSlugsResponse>;

internal sealed class ListProjectSlugsQueryValidator : AbstractValidator<ListProjectSlugsQuery>
{
    public ListProjectSlugsQueryValidator() =>
        RuleFor(q => q.Fields).Equal("slug").WithMessage("fields supports only 'slug'.");
}

internal sealed class ListProjectSlugsQueryHandler(CatalogReader reader) : IQueryHandler<ListProjectSlugsQuery, ProjectSlugsResponse>
{
    public async Task<Result<ProjectSlugsResponse>> HandleAsync(ListProjectSlugsQuery query, CancellationToken ct = default) =>
        new ProjectSlugsResponse(await reader.PublishedCards().OrderBy(c => c.Slug).Select(c => c.Slug).ToListAsync(ct));
}

/// `GET /v1/catalog/projects/{slug}` (BR-A06, D-08): a published project, or `{ redirectTo }` for an old slug.
public sealed record GetProjectBySlugQuery(string Slug) : IQuery<ProjectLookup>;

/// Exactly one of the two is set.
public sealed record ProjectLookup(ProjectDetail? Project, SlugRedirectResponse? Redirect);

internal sealed class GetProjectBySlugQueryHandler(CatalogReader reader, IApplicationDbContext db, IStoreSettings settings)
    : IQueryHandler<GetProjectBySlugQuery, ProjectLookup>
{
    public async Task<Result<ProjectLookup>> HandleAsync(GetProjectBySlugQuery query, CancellationToken ct = default)
    {
        var minGift = (await settings.GetAsync(ct)).MinGiftAmount;
        if (await reader.DetailAsync(null, query.Slug, minGift, publishedOnly: true, ct) is { } project)
        {
            return new ProjectLookup(project, null);
        }

        // L-12: redirect only to a published project of an active category (the page it lands on must exist).
        var current = await (
            from h in db.ProjectSlugHistory.AsNoTracking()
            join p in db.Projects.AsNoTracking() on h.ProjectId equals p.Id
            join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
            where h.OldSlug == query.Slug && p.Status == ProjectStatus.Published && c.IsActive
            select p.Slug).FirstOrDefaultAsync(ct);
        return current is null ? CatalogErrors.ProjectNotFound : new ProjectLookup(null, new SlugRedirectResponse(current));
    }
}

/// `GET /v1/catalog/categories`.
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategorySummary>>;

internal sealed class ListCategoriesQueryHandler(CatalogReader reader) : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategorySummary>>
{
    public async Task<Result<IReadOnlyList<CategorySummary>>> HandleAsync(ListCategoriesQuery query, CancellationToken ct = default) =>
        await reader.ActiveCategoriesAsync(ct);
}

/// `GET /v1/catalog/home`: the first published projects (badged first), the categories and the amount presets.
public sealed record GetHomeQuery : IQuery<HomeResponse>;

internal sealed class GetHomeQueryHandler(CatalogReader reader, IStoreSettings settings) : IQueryHandler<GetHomeQuery, HomeResponse>
{
    public const int FeaturedCount = 6;

    public async Task<Result<HomeResponse>> HandleAsync(GetHomeQuery query, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var featured = await reader.PublishedCards().OrderBy(c => c.Badge == null).ThenBy(c => c.Sort).ThenBy(c => c.Id).Take(FeaturedCount).ToListAsync(ct);
        var categories = await reader.ActiveCategoriesAsync(ct);
        return new HomeResponse([.. featured.Select(r => reader.ToCard(r, s.MinGiftAmount))], categories, [.. s.AmountPresets.Select(p => p.ToWire())]);
    }
}

/// `GET /v1/catalog/config`: minimum gift, presets and limits (D-03, D-04).
public sealed record GetCatalogConfigQuery : IQuery<CatalogConfigResponse>;

internal sealed class GetCatalogConfigQueryHandler(IStoreSettings settings) : IQueryHandler<GetCatalogConfigQuery, CatalogConfigResponse>
{
    public async Task<Result<CatalogConfigResponse>> HandleAsync(GetCatalogConfigQuery query, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        return new CatalogConfigResponse(s.MinGiftAmount.ToWire(), s.MaxLineAmount.ToWire(), s.CartMaxLines, s.CartMaxTotal.ToWire(),
            CartLimits.MaxQuantity, [.. s.AmountPresets.Select(p => p.ToWire())]);
    }
}
