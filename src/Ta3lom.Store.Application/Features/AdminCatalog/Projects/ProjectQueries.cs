using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Projects;

/// `GET /v1/admin/projects` with status, category, q, page and pageSize (any status), filtered and paged in SQL.
public sealed record ListAdminProjectsQuery(string? Status, Guid? CategoryId, string? Q, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AdminProjectRow>>;

internal sealed class ListAdminProjectsQueryHandler(IApplicationDbContext db, ICatalogSearch search) : IQueryHandler<ListAdminProjectsQuery, PagedResult<AdminProjectRow>>
{
    public async Task<Result<PagedResult<AdminProjectRow>>> HandleAsync(ListAdminProjectsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var projects = db.Projects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            // L-11: search, filters and paging in one SQL; the page keeps the search rank.
            var found = await search.SearchPageAsync(new CatalogSearchRequest(query.Q, query.Status, null, query.CategoryId, ActiveCategoryOnly: false,
                page.Skip, page.Take), ct);
            var hits = await Rows(projects.Where(p => found.Ids.Contains(p.Id))).ToListAsync(ct);
            var rank = found.Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return new PagedResult<AdminProjectRow>([.. hits.OrderBy(r => rank[r.Id]).Select(r => r.ToRow())], found.Total, page.SafePage, page.Take);
        }

        if (EnumWire.TryParse<ProjectStatus>(query.Status, out var status))
        {
            projects = projects.Where(p => p.Status == status);
        }

        if (query.CategoryId is { } categoryId)
        {
            projects = projects.Where(p => p.CategoryId == categoryId);
        }

        var total = await projects.CountAsync(ct);
        var items = await Rows(projects.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id).Skip(page.Skip).Take(page.Take)).ToListAsync(ct);
        return new PagedResult<AdminProjectRow>([.. items.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id).Select(r => r.ToRow())], total, page.SafePage, page.Take);
    }

    private IQueryable<RowData> Rows(IQueryable<Domain.Entities.Catalog.Project> projects) =>
        from p in projects
        join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
        select new RowData
        {
            Id = p.Id,
            Slug = p.Slug,
            Status = p.Status,
            TitleAr = p.TitleAr,
            CategoryId = p.CategoryId,
            CategoryNameAr = c.NameAr,
            Sort = p.Sort,
            ActiveOptions = p.Options.Count(o => o.IsActive),
            AllowOpenAmount = p.AllowOpenAmount,
            PublishedAt = p.PublishedAt,
            UpdatedAt = p.UpdatedAt,
        };

    /// Member-init projection (EF translates it); the enum becomes its wire string after materialisation.
    private sealed class RowData
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public ProjectStatus Status { get; init; }

        public string TitleAr { get; init; } = string.Empty;

        public Guid CategoryId { get; init; }

        public string CategoryNameAr { get; init; } = string.Empty;

        public int Sort { get; init; }

        public int ActiveOptions { get; init; }

        public bool AllowOpenAmount { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public AdminProjectRow ToRow() =>
            new(Id, Slug, Status.ToWire(), TitleAr, CategoryId, CategoryNameAr, Sort, ActiveOptions, AllowOpenAmount, PublishedAt, UpdatedAt);
    }
}

internal sealed class ListAdminProjectsQueryValidator : AbstractValidator<ListAdminProjectsQuery>
{
    public ListAdminProjectsQueryValidator()
    {
        RuleFor(q => q.Status).Must(s => s is null or "draft" or "published" or "archived").WithMessage("status must be draft, published or archived.");
        RuleFor(q => q.Q).MaximumLength(100);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

/// `GET /v1/admin/projects/{id}`: the editor view with options (active or not), old slugs and what publishing still needs.
public sealed record GetAdminProjectQuery(Guid Id) : IQuery<AdminProjectResponse>;

internal sealed class GetAdminProjectQueryHandler(AdminProjectReader reader) : IQueryHandler<GetAdminProjectQuery, AdminProjectResponse>
{
    public async Task<Result<AdminProjectResponse>> HandleAsync(GetAdminProjectQuery query, CancellationToken ct = default) =>
        await reader.GetAsync(query.Id, ct) is { } project ? project : CatalogErrors.ProjectNotFound;
}

/// `GET /v1/admin/projects/{id}/preview`: the public page of a project in any status.
public sealed record PreviewProjectQuery(Guid Id) : IQuery<ProjectDetail>;

internal sealed class PreviewProjectQueryHandler(CatalogReader reader, IStoreSettings settings) : IQueryHandler<PreviewProjectQuery, ProjectDetail>
{
    public async Task<Result<ProjectDetail>> HandleAsync(PreviewProjectQuery query, CancellationToken ct = default) =>
        await reader.DetailAsync(query.Id, null, (await settings.GetAsync(ct)).MinGiftAmount, publishedOnly: false, ct) is { } detail
            ? detail
            : CatalogErrors.ProjectNotFound;
}

internal sealed class AdminProjectReader(IApplicationDbContext db, IFileUrls urls)
{
    public async Task<AdminProjectResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().Include(x => x.Options).Include(x => x.SlugHistory).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null)
        {
            return null;
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == p.CategoryId, ct);
        var cover = p.CoverAssetId is { } coverId ? await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == coverId, ct) : null;
        return new AdminProjectResponse(p.Id, p.CategoryId, p.Slug, p.Status.ToWire(), p.TitleAr, p.TitleEn, p.SummaryAr, p.SummaryEn, p.BodyAr, p.BodyEn,
            p.CoverAssetId, cover is null ? null : urls.Public(cover.StorageKey), p.AllowOpenAmount, p.MinAmount?.ToWire(), p.Badge?.ToWire(), p.Sort,
            p.SeoTitleAr, p.SeoTitleEn, p.SeoDescAr, p.SeoDescEn,
            [.. p.Options.OrderBy(o => o.Sort).Select(o => new AdminProjectOption(o.Id, o.LabelAr, o.LabelEn, o.Amount.ToWire(), o.Sort, o.IsActive))],
            [.. p.SlugHistory.OrderBy(h => h.ChangedAt).Select(h => h.OldSlug)], p.MissingForPublish(category, cover),
            p.PublishedAt, p.CreatedAt, p.UpdatedAt, p.Version);
    }
}
