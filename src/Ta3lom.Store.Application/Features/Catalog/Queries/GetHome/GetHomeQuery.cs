using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetHome;

/// `GET /v1/catalog/home` (#1, FR-S01): the store home in the prototype's order. Featured = published products
/// with a badge; impact figures are computed from `v_goal` (their source is open, OP-2).
public sealed record GetHomeQuery : IQuery<HomeResponse>;

internal sealed class GetHomeQueryHandler(CatalogReader reader, IApplicationDbContext db, IClock clock) : IQueryHandler<GetHomeQuery, HomeResponse>
{
    public async Task<Result<HomeResponse>> HandleAsync(GetHomeQuery query, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var published = await reader.Published.OrderBy(p => p.Sort).ThenBy(p => p.Slug).ToListAsync(ct);
        var cards = (await reader.CardsAsync(published, ct)).ToDictionary(c => c.Id);
        var generalFundId = await db.CauseDetails.AsNoTracking().Where(c => c.IsGeneralFund).Select(c => (Guid?)c.ProductId).FirstOrDefaultAsync(ct);
        var activeOffers = await db.SponsorshipOffers.AsNoTracking().Where(o => o.IsActive && o.StartsAt <= now && o.EndsAt > now).Select(o => o.ProductId).ToListAsync(ct);

        IReadOnlyList<ProductCardResponse> Pick(Func<Domain.Entities.Catalog.Product, bool> filter, int take) =>
            [.. published.Where(filter).Take(take).Select(p => cards[p.Id])];

        var featured = Pick(p => p.Badge != null, 4);
        var causes = Pick(p => p.Type == ProductType.Cause && p.Id != generalFundId, 8);
        var courses = Pick(p => p.Type == ProductType.Course, 8);
        var sponsorable = Pick(p => activeOffers.Contains(p.Id), 8);
        var books = Pick(p => p.Type == ProductType.Book, 8);

        var goals = await db.Goals.AsNoTracking().ToListAsync(ct);
        var seats = goals.Where(g => g.Type == "course").Sum(g => (long)(g.FundedUnits ?? 0));
        var bookUnits = goals.Where(g => g.Type == "book").Sum(g => (long)(g.FundedUnits ?? 0));
        var donors = goals.Sum(g => (long)(g.DonorCount ?? 0));
        IReadOnlyList<ImpactFigure> impact =
        [
            new(seats, "مقعداً مكفولاً", "seats sponsored"),
            new(bookUnits, "نسخة وُزّعت", "books delivered"),
            new(donors, "متبرعاً وكافلاً", "donors & sponsors"),
            new(97, "٪ من التبرعات تصل مباشرة للبرامج", "% of gifts reach programs directly"),
        ];

        GeneralFundBand? band = null;
        if (generalFundId is { } fundId && cards.TryGetValue(fundId, out var fund) && fund.Goal is { } g)
        {
            band = new GeneralFundBand(fund.Slug, g.Raised, g.Target, g.Percent, g.DonorCount);
        }

        return new HomeResponse(featured, causes, courses, sponsorable, books, impact, band);
    }
}
