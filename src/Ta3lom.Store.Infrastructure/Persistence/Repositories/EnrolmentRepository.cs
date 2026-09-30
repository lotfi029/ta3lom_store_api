using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class EnrolmentRepository(StoreDbContext db) : GenericRepository<Enrolment>(db), IEnrolmentRepository
{
    public async Task<bool> TryAddLiveAsync(Enrolment e, CancellationToken ct = default)
    {
        var expires = new NpgsqlParameter("expires_at", NpgsqlDbType.TimestampTz) { Value = (object?)e.ExpiresAt ?? DBNull.Value };
        var line = new NpgsqlParameter("order_line_id", NpgsqlDbType.Uuid) { Value = (object?)e.OrderLineId ?? DBNull.Value };
        var unit = new NpgsqlParameter("sponsored_unit_id", NpgsqlDbType.Uuid) { Value = (object?)e.SponsoredUnitId ?? DBNull.Value };
        var inserted = await Db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.enrolment (id, customer_id, product_id, source, order_line_id, sponsored_unit_id, status, progress_pct, granted_at, expires_at)
            VALUES ({e.Id}, {e.CustomerId}, {e.ProductId}, {e.Source.ToWire()}, {line}, {unit}, {e.Status.ToWire()}, {e.ProgressPct}, {e.GrantedAt}, {expires})
            ON CONFLICT (customer_id, product_id) WHERE status IN ('pending_sync','active','completed') DO NOTHING
            """, ct);
        return inserted == 1;
    }
}
