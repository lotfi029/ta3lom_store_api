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
        if (EnumWire.TryParse<ProjectStatus>(query.Status, out var status))
        {
            projects = projects.Where(p => p.Status == status);
        }

        if (query.CategoryId is { } categoryId)
        {
            projects = projects.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var ids = await search.SearchAsync(query.Q, null, ct);
            projects = projects.Where(p => ids.Contains(p.Id));
        }

        var rows =
            from p in projects
            join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
            orderby p.UpdatedAt descending, p.Id
            select new
            {
                p.Id,
                p.Slug,
                p.Status,
                p.TitleAr,
                p.CategoryId,
                CategoryNameAr = c.NameAr,
                p.Sort,
                ActiveOptions = p.Options.Count(o => o.IsActive),
                p.AllowOpenAmount,
                p.PublishedAt,
                p.UpdatedAt,
            };
        var total = await rows.CountAsync(ct);
        var items = await rows.Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<AdminProjectRow>([.. items.Select(r => new AdminProjectRow(r.Id, r.Slug, r.Status.ToWire(), r.TitleAr, r.CategoryId,
            r.CategoryNameAr, r.Sort, r.ActiveOptions, r.AllowOpenAmount, r.PublishedAt, r.UpdatedAt))], total, page.SafePage, page.Take);
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
