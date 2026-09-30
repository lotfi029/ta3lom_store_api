using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Allocation;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Application.Features.Beneficiaries;

namespace Ta3lom.Store.Application.Features.Allocation.Queries;

/// `GET /v1/admin/allocation/backlog` (#109, RUL-48/49): per product, from `v_allocation_backlog`, oldest waiting first.
public sealed record AllocationBacklogQuery : IQuery<IReadOnlyList<BacklogRow>>;

internal sealed class AllocationBacklogQueryHandler(IApplicationDbContext db, AllocationReader reader) : IQueryHandler<AllocationBacklogQuery, IReadOnlyList<BacklogRow>>
{
    public async Task<Result<IReadOnlyList<BacklogRow>>> HandleAsync(AllocationBacklogQuery query, CancellationToken ct = default)
    {
        var rows = await (from v in db.AllocationBacklog.AsNoTracking()
                          join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
                          select new { v, p.TitleAr }).ToListAsync(ct);
        var result = new List<BacklogRow>();
        foreach (var r in rows.OrderBy(r => r.v.OldestAwaiting ?? DateTimeOffset.MaxValue).ThenBy(r => r.v.Slug, StringComparer.Ordinal))
        {
            var offer = await reader.CurrentOfferAsync(r.v.ProductId, ct);
            result.Add(new BacklogRow(r.v.ProductId, r.v.Slug, r.TitleAr, r.v.Awaiting, r.v.Overdue30, r.v.Allocated, r.v.Completed,
                r.v.OldestAwaiting, offer?.GroupLabelAr));
        }

        return result;
    }
}

/// `GET /v1/admin/allocation/products/{productId}/units` (#110): FIFO order (`funded_at, id`), default `awaiting_allocation`.
public sealed record ListUnitsQuery(Guid ProductId, string? Status, int Page, int PageSize) : IQuery<PagedResult<UnitRow>>;

internal sealed class ListUnitsQueryHandler(IApplicationDbContext db, AllocationReader reader) : IQueryHandler<ListUnitsQuery, PagedResult<UnitRow>>
{
    public async Task<Result<PagedResult<UnitRow>>> HandleAsync(ListUnitsQuery query, CancellationToken ct = default)
    {
        var status = UnitStatus.AwaitingAllocation;
        if (!string.IsNullOrWhiteSpace(query.Status) && !EnumWire.TryParse(query.Status.Trim(), out status))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["status"] = ["must be awaiting_allocation|allocated|completed|lapsed|cancelled"] });
        }

        var q = db.SponsoredUnits.AsNoTracking().Where(u => u.ProductId == query.ProductId && u.Status == status);
        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var units = await q.OrderBy(u => u.FundedAt).ThenBy(u => u.Id).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<UnitRow>(await reader.RowsAsync(units, ct), total, page.SafePage, page.Take);
    }
}

/// `GET /v1/admin/allocation/products/{productId}/candidates` (#111): approved beneficiaries of the offer's group (RUL-47 a, b),
/// optionally by region or zakat-eligible only, without a live enrolment in the course (c, for linked customers). Masked.
public sealed record ListCandidatesQuery(Guid ProductId, string? Region, bool ZakatOnly, int Page, int PageSize) : IQuery<PagedResult<BeneficiaryRow>>;

internal sealed class ListCandidatesQueryHandler(IApplicationDbContext db, AllocationReader reader) : IQueryHandler<ListCandidatesQuery, PagedResult<BeneficiaryRow>>
{
    public async Task<Result<PagedResult<BeneficiaryRow>>> HandleAsync(ListCandidatesQuery query, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == query.ProductId).Select(p => new { p.Type }).FirstOrDefaultAsync(ct);
        var offer = await reader.CurrentOfferAsync(query.ProductId, ct);
        if (product is null || offer is null)
        {
            return Error.NotFound(ErrorCodes.ProductNotFound, "No sponsorship offer for this product.");
        }

        var q = db.Beneficiaries.AsNoTracking().Where(b => b.EligibilityStatus == EligibilityStatus.Approved && b.BeneficiaryGroup == offer.BeneficiaryGroup);
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            q = q.Where(b => b.Region == query.Region.Trim());
        }

        if (query.ZakatOnly)
        {
            q = q.Where(b => b.ZakatEligible);
        }

        if (product.Type == ProductType.Course)
        {
            q = q.Where(b => b.CustomerId == null || !db.Enrolments.Any(e => e.CustomerId == b.CustomerId && e.ProductId == query.ProductId
                && (e.Status == EnrolmentStatus.PendingSync || e.Status == EnrolmentStatus.Active || e.Status == EnrolmentStatus.Completed)));
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(b => b.VerifiedAt).ThenBy(b => b.Code).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<BeneficiaryRow>([.. rows.Select(BeneficiaryRules.ToRow)], total, page.SafePage, page.Take);
    }
}

/// `GET /v1/admin/allocation/batches/{batchId}` (#113).
public sealed record GetAllocationBatchQuery(Guid BatchId) : IQuery<AllocationBatchResponse>;

internal sealed class GetAllocationBatchQueryHandler(IApplicationDbContext db, AllocationReader reader) : IQueryHandler<GetAllocationBatchQuery, AllocationBatchResponse>
{
    public async Task<Result<AllocationBatchResponse>> HandleAsync(GetAllocationBatchQuery query, CancellationToken ct = default)
    {
        var batch = await (from b in db.AllocationBatches.AsNoTracking()
                           join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
                           where b.Id == query.BatchId
                           select new { b, p.Slug }).FirstOrDefaultAsync(ct);
        if (batch is null)
        {
            return Error.NotFound(ErrorCodes.NotFound, "Allocation batch not found.");
        }

        var units = await db.SponsoredUnits.AsNoTracking().Where(u => u.AllocationBatchId == query.BatchId).OrderBy(u => u.FundedAt).ToListAsync(ct);
        return new AllocationBatchResponse(batch.b.Id, batch.b.ProductId, batch.Slug, batch.b.Note, batch.b.CreatedBy, batch.b.CreatedAt,
            await reader.RowsAsync(units, ct));
    }
}
