using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Payments;

/// F-D4: gapless receipt numbers. One upsert per number: the counter row is locked by the UPDATE until the settling
/// transaction ends, so concurrent settlements queue on it, and a rollback returns the number with the transaction.
internal sealed class DocumentNumbers(StoreDbContext db) : IDocumentNumbers
{
    public async Task<int> NextAsync(ReceiptSeries series, int year, CancellationToken ct = default)
    {
        var wire = series.ToWire();
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO store.document_counter (series, year, next_number) VALUES ({wire}, {year}, 2)
            ON CONFLICT (series, year) DO UPDATE SET next_number = document_counter.next_number + 1
            RETURNING next_number - 1 AS "Value"
            """).ToListAsync(ct);
        return numbers[0];
    }
}
