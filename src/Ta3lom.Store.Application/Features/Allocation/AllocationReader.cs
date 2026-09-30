using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Allocation;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.Allocation;

/// Shared reads for allocation: unit rows with beneficiary codes and the offer that defines a product's group.
internal sealed class AllocationReader(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<UnitRow>> RowsAsync(IReadOnlyList<SponsoredUnit> units, CancellationToken ct)
    {
        var ids = units.Where(u => u.BeneficiaryId is not null).Select(u => u.BeneficiaryId!.Value).Distinct().ToList();
        var codes = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Beneficiaries.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.Code }).ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        return [.. units.Select(u => new UnitRow(u.Id, u.Source.ToWire(), u.UnitPrice.ToWire(), u.HasZakat, u.Status.ToWire(), u.FundedAt,
            u.BeneficiaryId is { } b && codes.TryGetValue(b, out var code) ? code : null, u.AllocatedAt, u.AllocatedBy, u.CompletedAt, u.LapsedAt))];
    }

    /// The offer that defines the product's beneficiary group: the active one, else the most recent.
    public Task<SponsorshipOffer?> CurrentOfferAsync(Guid productId, CancellationToken ct) =>
        db.SponsorshipOffers.AsNoTracking().Where(o => o.ProductId == productId)
            .OrderByDescending(o => o.IsActive).ThenByDescending(o => o.StartsAt).FirstOrDefaultAsync(ct);
}
