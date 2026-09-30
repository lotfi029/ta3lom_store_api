using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Beneficiaries;

namespace Ta3lom.Store.Application.Features.Beneficiaries.Queries;

/// `GET /v1/admin/beneficiaries` (#100): masked rows filtered by group, status, zakat, region, kind and code (`q`).
public sealed record ListBeneficiariesQuery(string? Group, string? Status, bool? Zakat, string? Region, string? Kind, string? Q, int Page, int PageSize)
    : IQuery<PagedResult<BeneficiaryRow>>;

internal sealed class ListBeneficiariesQueryHandler(IApplicationDbContext db) : IQueryHandler<ListBeneficiariesQuery, PagedResult<BeneficiaryRow>>
{
    public async Task<Result<PagedResult<BeneficiaryRow>>> HandleAsync(ListBeneficiariesQuery query, CancellationToken ct = default)
    {
        var q = db.Beneficiaries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Group))
        {
            q = q.Where(b => b.BeneficiaryGroup == query.Group.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumWire.TryParse<EligibilityStatus>(query.Status.Trim(), out var status))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["status"] = ["must be pending|approved|rejected|inactive"] });
            }

            q = q.Where(b => b.EligibilityStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            if (!EnumWire.TryParse<BeneficiaryKind>(query.Kind.Trim(), out var kind))
            {
                return Error.Validation(new Dictionary<string, string[]> { ["kind"] = ["must be student|teacher|parent|school|library"] });
            }

            q = q.Where(b => b.Kind == kind);
        }

        if (query.Zakat is { } zakat)
        {
            q = q.Where(b => b.ZakatEligible == zakat);
        }

        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            q = q.Where(b => b.Region == query.Region.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var code = query.Q.Trim().ToUpperInvariant();
            q = q.Where(b => b.Code == code || b.Code == "B-" + code);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<BeneficiaryRow>([.. rows.Select(BeneficiaryRules.ToRow)], total, page.SafePage, page.Take);
    }
}

/// `GET /v1/admin/beneficiaries/{id}` (#102): masked detail with the units history.
public sealed record GetBeneficiaryQuery(Guid Id) : IQuery<BeneficiaryDetail>;

internal sealed class GetBeneficiaryQueryHandler(IApplicationDbContext db) : IQueryHandler<GetBeneficiaryQuery, BeneficiaryDetail>
{
    public async Task<Result<BeneficiaryDetail>> HandleAsync(GetBeneficiaryQuery query, CancellationToken ct = default)
    {
        var b = await db.Beneficiaries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
        if (b is null)
        {
            return BeneficiaryErrors.NotFound;
        }

        var units = await (from u in db.SponsoredUnits.AsNoTracking()
                           join p in db.Products.AsNoTracking() on u.ProductId equals p.Id
                           where u.BeneficiaryId == b.Id
                           orderby u.AllocatedAt descending
                           select new { u.Id, p.Slug, p.TitleAr, u.Status, u.AllocatedAt, u.CompletedAt, u.LapsedAt }).ToListAsync(ct);
        var row = BeneficiaryRules.ToRow(b);
        return new BeneficiaryDetail(b.Id, b.Code, row.Kind, b.BeneficiaryGroup, b.Region, b.OrganisationName, row.NameMasked, row.MobileMasked,
            b.GuardianName is not null, row.EligibilityStatus, b.ZakatEligible, b.EligibilityNotes, row.LinkedCustomer, b.VerifiedBy, b.VerifiedAt,
            b.CreatedAt, b.CreatedBy,
            [.. units.Select(u => new BeneficiaryUnit(u.Id, u.Slug, u.TitleAr, u.Status.ToWire(), u.AllocatedAt, u.CompletedAt, u.LapsedAt))]);
    }
}

/// `GET /v1/admin/beneficiaries/import/template` (#108): the CSV header row (UTF-8 with BOM).
public sealed record BeneficiaryTemplateQuery : IQuery<byte[]>;

internal sealed class BeneficiaryTemplateQueryHandler : IQueryHandler<BeneficiaryTemplateQuery, byte[]>
{
    public Task<Result<byte[]>> HandleAsync(BeneficiaryTemplateQuery query, CancellationToken ct = default) =>
        Task.FromResult<Result<byte[]>>(BeneficiaryRules.Template());
}
