using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog;

/// Shared projections of the public catalogue: a project row with its category, cover and active options in one SQL query
/// (a LEFT JOIN for the options), so a listing page is one round trip.
internal sealed class CatalogReader(IApplicationDbContext db, IFileUrls urls)
{
    /// A member-init projection (not a constructor), so EF Core can still filter and sort on its members.
    internal sealed class CardRow
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string TitleAr { get; init; } = string.Empty;

        public string? TitleEn { get; init; }

        public string? SummaryAr { get; init; }

        public string? SummaryEn { get; init; }

        public string? CoverKey { get; init; }

        public string? CoverAltAr { get; init; }

        public string? CoverAltEn { get; init; }

        public ProjectBadge? Badge { get; init; }

        public string CategorySlug { get; init; } = string.Empty;

        public bool AllowOpenAmount { get; init; }

        public Money? MinAmount { get; init; }

        public int Sort { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        public List<CardOption> Options { get; init; } = [];
    }

    /// An active option of a card row (API-9), loaded in the listing's own query.
    internal sealed class CardOption
    {
        public Guid Id { get; init; }

        public string LabelAr { get; init; } = string.Empty;

        public string? LabelEn { get; init; }

        public Money Amount { get; init; }
    }

    /// Published projects in active categories (D-05/D-07: archived and draft projects drop out of the listings).
    public IQueryable<CardRow> PublishedCards() =>
        from p in db.Projects.AsNoTracking()
        join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
        join m in db.MediaAssets.AsNoTracking() on p.CoverAssetId equals m.Id into covers
        from cover in covers.DefaultIfEmpty()
        where p.Status == ProjectStatus.Published && c.IsActive
        select new CardRow
        {
            Id = p.Id,
            Slug = p.Slug,
            TitleAr = p.TitleAr,
            TitleEn = p.TitleEn,
            SummaryAr = p.SummaryAr,
            SummaryEn = p.SummaryEn,
            CoverKey = cover != null ? cover.StorageKey : null,
            CoverAltAr = cover != null ? cover.AltAr : null,
            CoverAltEn = cover != null ? cover.AltEn : null,
            Badge = p.Badge,
            CategorySlug = c.Slug,
            AllowOpenAmount = p.AllowOpenAmount,
            MinAmount = p.MinAmount,
            Sort = p.Sort,
            PublishedAt = p.PublishedAt,
            Options = p.Options.Where(o => o.IsActive).OrderBy(o => o.Sort).ThenBy(o => o.Id)
                .Select(o => new CardOption { Id = o.Id, LabelAr = o.LabelAr, LabelEn = o.LabelEn, Amount = o.Amount }).ToList(),
        };

    public ProjectCard ToCard(CardRow r, Money minGift) =>
        new(r.Id, r.Slug, r.TitleAr, r.TitleEn, r.SummaryAr, r.SummaryEn, r.CoverKey is null ? null : urls.Public(r.CoverKey), r.CoverAltAr,
            r.Badge?.ToWire(), r.CategorySlug, r.AllowOpenAmount, (r.MinAmount ?? minGift).ToWire(),
            r.Options.Count == 0 ? null : r.Options.Min(o => o.Amount).ToWire(),
            [.. r.Options.Select(o => new ProjectOptionView(o.Id, o.LabelAr, o.LabelEn, o.Amount.ToWire()))], r.CoverAltEn);

    /// Active categories with the number of their published projects.
    public async Task<List<CategorySummary>> ActiveCategoriesAsync(CancellationToken ct)
    {
        var rows = await (
            from c in db.Categories.AsNoTracking()
            join m in db.MediaAssets.AsNoTracking() on c.ImageAssetId equals m.Id into images
            from image in images.DefaultIfEmpty()
            where c.IsActive
            orderby c.Sort, c.NameAr
            select new
            {
                c.Id,
                c.Slug,
                c.NameAr,
                c.NameEn,
                c.DescriptionAr,
                c.DescriptionEn,
                ImageKey = image != null ? image.StorageKey : null,
                ImageAlt = image != null ? image.AltAr : null,
                ImageAltEn = image != null ? image.AltEn : null,
                c.AcceptsGeneralGifts,
                Count = db.Projects.Count(p => p.CategoryId == c.Id && p.Status == ProjectStatus.Published),
            }).ToListAsync(ct);
        return [.. rows.Select(r => new CategorySummary(r.Id, r.Slug, r.NameAr, r.NameEn, r.DescriptionAr, r.DescriptionEn,
            r.ImageKey is null ? null : urls.Public(r.ImageKey), r.ImageAlt, r.AcceptsGeneralGifts, r.Count, r.ImageAltEn))];
    }

    /// The detail of one project (any status: the admin preview uses it too), with its active options. One query.
    public async Task<ProjectDetail?> DetailAsync(Guid? id, string? slug, Money minGift, bool publishedOnly, CancellationToken ct)
    {
        var query =
            from p in db.Projects.AsNoTracking()
            join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
            join m in db.MediaAssets.AsNoTracking() on p.CoverAssetId equals m.Id into covers
            from cover in covers.DefaultIfEmpty()
            where (id == null || p.Id == id) && (slug == null || p.Slug == slug)
                  && (!publishedOnly || (p.Status == ProjectStatus.Published && c.IsActive))
            select new
            {
                Project = p,
                Category = new CategoryRef(c.Id, c.Slug, c.NameAr, c.NameEn),
                CoverKey = cover != null ? cover.StorageKey : null,
                CoverAltAr = cover != null ? cover.AltAr : null,
                CoverAltEn = cover != null ? cover.AltEn : null,
                Options = p.Options.Where(o => o.IsActive).OrderBy(o => o.Sort).ThenBy(o => o.Id).Select(o => new { o.Id, o.LabelAr, o.LabelEn, o.Amount }).ToList(),
            };
        var row = await query.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var project = row.Project;
        return new ProjectDetail(project.Id, project.Slug, project.Status.ToWire(), project.TitleAr, project.TitleEn, project.SummaryAr, project.SummaryEn,
            project.BodyAr, project.BodyEn, row.CoverKey is null ? null : urls.Public(row.CoverKey), row.CoverAltAr, row.CoverAltEn, project.Badge?.ToWire(),
            row.Category, project.AllowOpenAmount, project.MinimumOpenAmount(minGift).ToWire(),
            [.. row.Options.Select(o => new ProjectOptionView(o.Id, o.LabelAr, o.LabelEn, o.Amount.ToWire()))],
            project.SeoTitleAr, project.SeoTitleEn, project.SeoDescAr, project.SeoDescEn, project.PublishedAt);
    }
}
