using Mapster;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.Staff.Queries.ListStaff;

/// `GET /v1/admin/staff`, ordered by email.
public sealed record ListStaffQuery : IQuery<IReadOnlyList<StaffUserResponse>>;

internal sealed class ListStaffQueryHandler(IApplicationDbContext db, TypeAdapterConfig mapping)
    : IQueryHandler<ListStaffQuery, IReadOnlyList<StaffUserResponse>>
{
    public async Task<Result<IReadOnlyList<StaffUserResponse>>> HandleAsync(ListStaffQuery query, CancellationToken ct = default) =>
        await db.StaffUsers.AsNoTracking().OrderBy(s => s.Email).ProjectToType<StaffUserResponse>(mapping).ToListAsync(ct);
}
