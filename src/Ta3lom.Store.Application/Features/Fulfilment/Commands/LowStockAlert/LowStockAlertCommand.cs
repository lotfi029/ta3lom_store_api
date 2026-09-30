using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;

namespace Ta3lom.Store.Application.Features.Fulfilment.Commands.LowStockAlert;

/// `LowStockAlert` job (daily 08:00 Riyadh, 09 §6; FR-S43): every active print format of a non-archived product whose
/// `available ≤ coalesce(low_stock_threshold, low_stock_default_threshold)` gets one outbox `LowStockAlert` (staff e-mail),
/// at most one per format per Riyadh day — a second run the same day adds nothing. Returns the number of alerts queued.
public sealed record LowStockAlertCommand : ICommand<int>;

internal sealed class LowStockAlertCommandHandler(IApplicationDbContext db, FulfilmentReader reader, IOutbox outbox, IUnitOfWork uow, IClock clock)
    : ICommandHandler<LowStockAlertCommand, int>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<int>> HandleAsync(LowStockAlertCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var local = now.ToOffset(Riyadh);
        var day = DateOnly.FromDateTime(local.DateTime);
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();

        var low = await reader.StockAsync(null, lowOnly: true, ct);
        if (low.Count == 0)
        {
            return 0;
        }

        var lowIds = low.Select(r => r.FormatId).ToList();
        var live = await (from f in db.BookFormats.AsNoTracking()
                          join p in db.Products.AsNoTracking() on f.ProductId equals p.Id
                          where lowIds.Contains(f.Id) && f.IsActive && p.Status != ProductStatus.Archived
                          select f.Id).ToListAsync(ct);

        var sentToday = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(Domain.Events.LowStockAlert) && m.OccurredAt >= dayStart)
            .Select(m => m.Payload).ToListAsync(ct);

        var queued = 0;
        foreach (var row in low.Where(r => live.Contains(r.FormatId)))
        {
            var id = row.FormatId.ToString();
            if (sentToday.Exists(p => p.Contains(id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;                                                                  // once per format per day
            }

            outbox.Enqueue(new Domain.Events.LowStockAlert(row.FormatId, row.Sku, row.TitleAr, row.Available, row.EffectiveThreshold, day));
            queued++;
        }

        if (queued > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return queued;
    }
}
