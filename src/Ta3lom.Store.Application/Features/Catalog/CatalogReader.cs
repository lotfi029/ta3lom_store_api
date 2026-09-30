using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.ReadModels;
using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Application.Features.Catalog;

/// Composes catalogue read models (11 §2) from batched `AsNoTracking` reads: never one query per product.
/// Goal figures come from `v_goal`, which already adds the seed_* opening balances (RUL-08).
internal sealed class CatalogReader(IApplicationDbContext db, IFileUrls files, IClock clock)
{
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    public IQueryable<Product> Published => db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Published);

    public async Task<IReadOnlyList<ProductCardResponse>> CardsAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var parts = await LoadAsync(products, ct);
        return [.. products.Select(p => Card(p, parts))];
    }

    public async Task<ProductDetailResponse> DetailAsync(Product p, CancellationToken ct)
    {
        var parts = await LoadAsync([p], ct);
        var now = clock.UtcNow;
        parts.Goals.TryGetValue(p.Id, out var goal);

        CourseBlock? course = null;
        if (parts.Courses.TryGetValue(p.Id, out var c))
        {
            var modules = await db.CourseModules.AsNoTracking().Where(m => m.ProductId == p.Id).OrderBy(m => m.Sort)
                .Select(m => new CourseModuleResponse(m.Sort, m.TitleAr, m.TitleEn, m.DurationSeconds)).ToListAsync(ct);
            course = new CourseBlock(c.Price.ToWire(), c.CompareAtPrice?.ToWire(), c.Lessons, c.DurationMinutes, c.LevelAr, c.LevelEn,
                c.InstructorAr, c.InstructorEn, c.AccessDays, c.LearnerCount, modules);
        }

        BookBlock? book = null;
        if (p.Type == ProductType.Book)
        {
            var detail = await db.BookDetails.AsNoTracking().FirstOrDefaultAsync(b => b.ProductId == p.Id, ct);
            var formats = parts.Formats.GetValueOrDefault(p.Id, []);
            if (detail is not null)
            {
                book = new BookBlock(detail.AuthorAr, detail.AuthorEn, detail.Pages, detail.Isbn, detail.PublishedYear,
                    [.. formats.Select(f => new BookFormatResponse(f.Id, f.Kind.ToWire(), f.Sku, f.Price.ToWire(), f.CompareAtPrice?.ToWire(),
                        f.Kind == BookFormatKind.Print ? Available(f) : null,
                        f.Kind == BookFormatKind.Print ? CatalogLimits.PrintMaxQuantity : 1))]);
            }
        }

        CauseBlock? cause = null;
        if (parts.Causes.TryGetValue(p.Id, out var k))
        {
            var raised = goal?.RaisedAmount ?? 0m;
            cause = new CauseBlock(k.UnitAmount?.ToWire(), k.UnitLabelAr, k.UnitLabelEn, Wire.Money(k.TargetAmount), Wire.Money(raised),
                GoalMath.Percent(raised, k.TargetAmount), goal?.DonorCount ?? 0, k.AllowCustomAmount, k.MinAmount.ToWire(), k.ZakatEligible,
                k.BeneficiariesAr, k.BeneficiariesEn, k.CloseOnTarget, k.IsGeneralFund, k.StartsAt, k.EndsAt,
                GoalMath.DaysLeft(k.EndsAt, clock.RiyadhToday, RiyadhOffset), k.IsOpen(now, raised));
        }

        SponsorshipDetail? sponsorship = null;
        if (parts.Offers.TryGetValue(p.Id, out var o))
        {
            var funded = goal?.FundedUnits ?? 0;
            sponsorship = new SponsorshipDetail(o.Id, o.SeatPrice.ToWire(), o.UnitLabelAr, o.UnitLabelEn, o.TargetUnits, funded,
                GoalMath.Percent(funded, o.TargetUnits), goal?.DonorCount ?? 0, o.GroupLabelAr, o.GroupLabelEn, o.AllowPartial, o.ZakatEligible,
                o.StartsAt, o.EndsAt, o.IsOpen(now, funded));
        }

        var related = await RelatedAsync(p, ct);
        return new ProductDetailResponse(p.Id, p.Type.ToWire(), p.Slug, p.TitleAr, p.TitleEn, p.SummaryAr, p.SummaryEn, p.BodyAr, p.BodyEn,
            p.Badge?.ToWire(), Category(p, parts), Cover(p.CoverAssetId, parts), new SeoResponse(p.SeoTitleAr, p.SeoTitleEn, p.SeoDescAr, p.SeoDescEn),
            course, book, cause, sponsorship, related);
    }

    public async Task<IReadOnlyList<LiveItem>> LiveAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var parts = await LoadAsync(products, ct);
        var now = clock.UtcNow;
        return [.. products.Select(p =>
        {
            parts.Goals.TryGetValue(p.Id, out var goal);
            var (price, compareAt, formats) = Prices(p, parts);
            LiveSponsorship? sponsorship = null;
            if (parts.Offers.TryGetValue(p.Id, out var o))
            {
                var funded = goal?.FundedUnits ?? 0;
                sponsorship = new LiveSponsorship(funded, o.TargetUnits, GoalMath.Percent(funded, o.TargetUnits), goal?.DonorCount ?? 0, o.IsOpen(now, funded));
            }

            LiveGoal? live = null;
            if (parts.Causes.TryGetValue(p.Id, out var k))
            {
                var raised = goal?.RaisedAmount ?? 0m;
                live = new LiveGoal(Wire.Money(raised), Wire.Money(k.TargetAmount), GoalMath.Percent(raised, k.TargetAmount), goal?.DonorCount ?? 0,
                    GoalMath.DaysLeft(k.EndsAt, clock.RiyadhToday, RiyadhOffset), k.IsOpen(now, raised));
            }

            IReadOnlyList<LiveFormat>? liveFormats = p.Type == ProductType.Book
                ? [.. parts.Formats.GetValueOrDefault(p.Id, []).Select(f => new LiveFormat(f.Kind.ToWire(), f.Price.ToWire(), f.Kind == BookFormatKind.Print ? Available(f) : null))]
                : null;
            _ = formats;
            return new LiveItem(p.Slug, p.Type == ProductType.Book ? null : price, p.Type == ProductType.Book ? null : compareAt, liveFormats, sponsorship, live);
        })];
    }

    public ImageResponse? Image(MediaAsset? asset) =>
        asset is null ? null : new ImageResponse(files.Public(asset.StorageKey), asset.AltAr, asset.AltEn, asset.Width, asset.Height);

    private static int Available(BookFormat f) => Math.Max(0, (f.StockOnHand ?? 0) - f.StockReserved);

    private async Task<IReadOnlyList<RelatedProduct>> RelatedAsync(Product p, CancellationToken ct)
    {
        // Same category first, then the same type (courses and books); causes relate to other causes.
        var others = await Published.Where(x => x.Id != p.Id && x.Type == p.Type)
            .OrderBy(x => p.CategoryId != null && x.CategoryId == p.CategoryId ? 0 : 1).ThenBy(x => x.Sort).ThenBy(x => x.Slug)
            .Take(4).ToListAsync(ct);
        var parts = await LoadAsync(others, ct);
        return [.. others.Select(x => new RelatedProduct(x.Slug, x.Type.ToWire(), x.TitleAr, x.TitleEn, Prices(x, parts).Price))];
    }

    private ProductCardResponse Card(Product p, Parts parts)
    {
        var now = clock.UtcNow;
        parts.Goals.TryGetValue(p.Id, out var goal);
        var (price, compareAt, formats) = Prices(p, parts);

        SponsorshipCard? sponsorship = null;
        if (parts.Offers.TryGetValue(p.Id, out var o))
        {
            var funded = goal?.FundedUnits ?? 0;
            sponsorship = new SponsorshipCard(o.Id, o.SeatPrice.ToWire(), o.UnitLabelAr, o.UnitLabelEn, o.TargetUnits, funded,
                GoalMath.Percent(funded, o.TargetUnits), goal?.DonorCount ?? 0, o.EndsAt, o.IsOpen(now, funded));
        }

        GoalCard? goalCard = null;
        if (parts.Causes.TryGetValue(p.Id, out var k))
        {
            var raised = goal?.RaisedAmount ?? 0m;
            goalCard = new GoalCard(k.UnitAmount?.ToWire(), k.UnitLabelAr, k.UnitLabelEn, Wire.Money(k.TargetAmount), Wire.Money(raised),
                GoalMath.Percent(raised, k.TargetAmount), goal?.DonorCount ?? 0, k.EndsAt, k.IsOpen(now, raised), k.ZakatEligible, k.IsGeneralFund);
        }

        return new ProductCardResponse(p.Id, p.Type.ToWire(), p.Slug, p.TitleAr, p.TitleEn, p.SummaryAr, p.SummaryEn, p.Badge?.ToWire(),
            Category(p, parts), Cover(p.CoverAssetId, parts), price, compareAt, formats, sponsorship, goalCard);
    }

    private static (string? Price, string? CompareAt, IReadOnlyList<FormatPrice>? Formats) Prices(Product p, Parts parts)
    {
        if (parts.Courses.TryGetValue(p.Id, out var c))
        {
            return (c.Price.ToWire(), c.CompareAtPrice?.ToWire(), null);
        }

        if (p.Type == ProductType.Book)
        {
            var formats = parts.Formats.GetValueOrDefault(p.Id, []);
            var cheapest = formats.OrderBy(f => f.Price.Amount).FirstOrDefault();
            return (cheapest?.Price.ToWire(), cheapest?.CompareAtPrice?.ToWire(), [.. formats.Select(f => new FormatPrice(f.Kind.ToWire(), f.Price.ToWire()))]);
        }

        return (null, null, null);
    }

    private static CategoryRef? Category(Product p, Parts parts) =>
        p.CategoryId is { } id && parts.Categories.TryGetValue(id, out var c) ? new CategoryRef(c.Slug, c.NameAr, c.NameEn) : null;

    private ImageResponse? Cover(Guid? assetId, Parts parts) =>
        assetId is { } id && parts.Media.TryGetValue(id, out var m) ? Image(m) : null;

    private async Task<Parts> LoadAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var ids = products.Select(p => p.Id).ToList();
        var categoryIds = products.Where(p => p.CategoryId != null).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var mediaIds = products.Where(p => p.CoverAssetId != null).Select(p => p.CoverAssetId!.Value).Distinct().ToList();

        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var media = await db.MediaAssets.AsNoTracking().Where(m => mediaIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var courses = await db.CourseDetails.AsNoTracking().Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, ct);
        var causes = await db.CauseDetails.AsNoTracking().Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, ct);
        var offers = await db.SponsorshipOffers.AsNoTracking().Where(o => ids.Contains(o.ProductId) && o.IsActive).ToDictionaryAsync(o => o.ProductId, ct);
        var formats = (await db.BookFormats.AsNoTracking().Where(f => ids.Contains(f.ProductId) && f.IsActive).OrderBy(f => f.Kind).ToListAsync(ct))
            .GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var goals = await db.Goals.AsNoTracking().Where(g => ids.Contains(g.ProductId)).ToDictionaryAsync(g => g.ProductId, ct);
        return new Parts(categories, media, courses, causes, offers, formats, goals);
    }

    private sealed record Parts(
        Dictionary<Guid, Category> Categories,
        Dictionary<Guid, MediaAsset> Media,
        Dictionary<Guid, CourseDetail> Courses,
        Dictionary<Guid, CauseDetail> Causes,
        Dictionary<Guid, SponsorshipOffer> Offers,
        Dictionary<Guid, List<BookFormat>> Formats,
        Dictionary<Guid, GoalView> Goals);
}
