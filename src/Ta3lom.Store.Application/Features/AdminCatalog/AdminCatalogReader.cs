using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.Catalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog;

/// Builds the admin views (11 §9.3–§9.4) from `AsNoTracking` reads, any product status.
internal sealed class AdminCatalogReader(IApplicationDbContext db)
{
    public async Task<AdminProductResponse?> ProductAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null)
        {
            return null;
        }

        var course = await db.CourseDetails.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == id, ct);
        var modules = await db.CourseModules.AsNoTracking().Where(m => m.ProductId == id).OrderBy(m => m.Sort)
            .Select(m => new AdminModule(m.Id, m.Sort, m.TitleAr, m.TitleEn, m.DurationSeconds)).ToListAsync(ct);
        var book = await db.BookDetails.AsNoTracking().FirstOrDefaultAsync(b => b.ProductId == id, ct);
        var formats = await db.BookFormats.AsNoTracking().Where(f => f.ProductId == id).OrderBy(f => f.Kind).ToListAsync(ct);
        var cause = await db.CauseDetails.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == id, ct);
        var offer = await db.SponsorshipOffers.AsNoTracking().FirstOrDefaultAsync(o => o.ProductId == id && o.IsActive, ct);
        var goal = await db.GoalProgress.AsNoTracking().FirstOrDefaultAsync(g => g.ProductId == id, ct);
        var hasOrders = await db.OrderLines.AnyAsync(l => l.ProductId == id, ct);

        return new AdminProductResponse(p.Id, p.Type.ToWire(), p.Slug, p.Status.ToWire(), p.CategoryId, p.CoverAssetId, p.TitleAr, p.TitleEn,
            p.SummaryAr, p.SummaryEn, p.BodyAr, p.BodyEn, p.Badge?.ToWire(), p.Sort, p.SeoTitleAr, p.SeoTitleEn, p.SeoDescAr, p.SeoDescEn,
            p.PublishedAt, p.CreatedAt, p.UpdatedAt, hasOrders,
            course is null ? null : new AdminCourseBlock(course.Price.ToWire(), course.CompareAtPrice?.ToWire(), course.Lessons, course.DurationMinutes,
                course.LevelAr, course.LevelEn, course.InstructorAr, course.InstructorEn, course.AccessDays, course.LmsCourseRef, course.LearnerCount),
            modules,
            book is null ? null : new AdminBookBlock(book.AuthorAr, book.AuthorEn, book.Pages, book.Isbn, book.PublishedYear),
            [.. formats.Select(Format)],
            cause is null ? null : new AdminCauseBlock(cause.UnitAmount?.ToWire(), cause.UnitLabelAr, cause.UnitLabelEn, Wire.Money(cause.TargetAmount),
                cause.AllowCustomAmount, cause.MinAmount.ToWire(), cause.ZakatEligible, cause.BeneficiariesAr, cause.BeneficiariesEn,
                cause.CloseOnTarget, cause.IsGeneralFund, cause.StartsAt, cause.EndsAt),
            offer is null ? null : await OfferAsync(offer, ct),
            goal is null ? null : Goal(goal));
    }

    public static AdminBookFormat Format(BookFormat f) =>
        new(f.Id, f.Kind.ToWire(), f.Sku, f.Price.ToWire(), f.CompareAtPrice?.ToWire(), f.IsActive, f.StockOnHand, f.StockReserved,
            f.Kind == BookFormatKind.Print ? f.Available : null, f.WeightGrams, f.LowStockThreshold, f.FileAssetId);

    public static GoalFiguresResponse Goal(GoalProgress g) =>
        new(Wire.Money(g.RaisedAmount), g.FundedUnits, g.DonorCount, Wire.Money(g.SeedRaised), g.SeedUnits, g.SeedDonors,
            Wire.Money(g.RaisedAmount + g.SeedRaised), g.FundedUnits + g.SeedUnits, g.DonorCount + g.SeedDonors);

    public async Task<OfferResponse> OfferAsync(SponsorshipOffer o, CancellationToken ct)
    {
        var balance = await db.SponsorshipPools.AsNoTracking().Where(p => p.SponsorshipOfferId == o.Id).Select(p => (Money?)p.Balance).FirstOrDefaultAsync(ct);
        var units = await db.SponsoredUnits.AsNoTracking().Where(u => u.SponsorshipOfferId == o.Id)
            .GroupBy(u => u.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        return new OfferResponse(o.Id, o.ProductId, o.SeatPrice.ToWire(), o.UnitLabelAr, o.UnitLabelEn, o.TargetUnits, o.BeneficiaryGroup,
            o.GroupLabelAr, o.GroupLabelEn, o.AllowPartial, o.ZakatEligible, o.CloseOnTarget, o.StartsAt, o.EndsAt, o.IsActive,
            new PoolBalance((balance ?? Money.Zero).ToWire()), units.Sum(u => u.Count),
            units.Where(u => u.Status == UnitStatus.AwaitingAllocation).Sum(u => u.Count));
    }
}
