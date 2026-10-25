using Mapster;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Queries.GetStaffMe;

/// `GET /v1/admin/me`.
public sealed record GetStaffMeQuery(Guid StaffId) : IQuery<StaffMeResponse>;

internal sealed class GetStaffMeQueryHandler(IApplicationDbContext db, TypeAdapterConfig mapping) : IQueryHandler<GetStaffMeQuery, StaffMeResponse>
{
    public async Task<Result<StaffMeResponse>> HandleAsync(GetStaffMeQuery query, CancellationToken ct = default)
    {
        var me = await db.StaffUsers.AsNoTracking().Where(s => s.Id == query.StaffId)
            .ProjectToType<StaffMeResponse>(mapping).FirstOrDefaultAsync(ct);
        return me is null ? StaffErrors.NotFound : me;
    }
}
