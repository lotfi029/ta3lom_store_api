using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetLiveFigures;

/// `GET /v1/catalog/live?slugs=a,b,c` (#5, FR-S07): up to 50 slugs; unknown or unpublished slugs are omitted.
public sealed record GetLiveFiguresQuery(IReadOnlyList<string> Slugs) : IQuery<LiveFiguresResponse>;

internal sealed class GetLiveFiguresQueryHandler(CatalogReader reader, IClock clock) : IQueryHandler<GetLiveFiguresQuery, LiveFiguresResponse>
{
    public async Task<Result<LiveFiguresResponse>> HandleAsync(GetLiveFiguresQuery query, CancellationToken ct = default)
    {
        var slugs = query.Slugs.Distinct(StringComparer.Ordinal).ToList();
        var products = await reader.Published.Where(p => slugs.Contains(p.Slug)).ToListAsync(ct);
        var ordered = slugs.Select(s => products.FirstOrDefault(p => p.Slug == s)).Where(p => p is not null).Select(p => p!).ToList();
        return new LiveFiguresResponse(await reader.LiveAsync(ordered, ct), clock.UtcNow);
    }
}

internal sealed class GetLiveFiguresQueryValidator : AbstractValidator<GetLiveFiguresQuery>
{
    public GetLiveFiguresQueryValidator()
    {
        RuleFor(q => q.Slugs).NotEmpty().Must(s => s.Count <= 50).WithMessage("At most 50 slugs.");
    }
}
