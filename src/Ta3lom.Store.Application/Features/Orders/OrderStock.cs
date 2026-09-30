using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Orders;

/// RUL-30: the print quantities an unpaid order still holds, released on `expired` or `cancelled`.
internal sealed class OrderStock(IApplicationDbContext db, IStockRepository stock)
{
    public async Task ReleaseAsync(Order order, CancellationToken ct)
    {
        var formatIds = order.Lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).ToList();
        var print = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id) && f.Kind == BookFormatKind.Print).Select(f => f.Id).ToListAsync(ct);
        await stock.ReleaseAsync(order.PrintReservations(print.ToHashSet()), ct);
    }
}
