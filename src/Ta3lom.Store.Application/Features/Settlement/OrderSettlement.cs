using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement;

/// The settlement transaction of database/01 §4.4 without the `payment_event` inbox step (doc 15 §2): order row lock,
/// `MarkPaid` (RUL-29/33/35), one effect per line (RUL-39), goal counters, zakat in, dedication cards, document numbers,
/// `OrderPaid` + `RenderDocuments` — all in one unit of work on the retrying execution strategy.
internal sealed class OrderSettlement(
    IUnitOfWork uow,
    IOrderRepository orders,
    IApplicationDbContext db,
    IEnumerable<ILineSettlementEffect> effects,
    SettlementLedger ledger,
    IStoreSettings settings,
    IClock clock) : IOrderSettlement
{
    public async Task<Result<SettlementOutcome>> SettleAsync(PaymentConfirmation confirmation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var s = await settings.GetAsync(ct);
        var effectList = effects.ToList();
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var order = await orders.LoadForUpdateAsync(confirmation.OrderId, token);          // serialise per order
            if (order is null)
            {
                return Result.Success(SettlementOutcome.NotFound);
            }

            var now = clock.UtcNow;
            switch (order.MarkPaid(Money.Round(confirmation.CapturedAmount), now))
            {
                case PaidOutcome.AlreadyPaid:
                    return Result.Success(SettlementOutcome.AlreadyPaid);                     // no effects, nothing changes
                case PaidOutcome.Review:
                    return Result.Success(SettlementOutcome.PaymentReview);                   // status + FinanceAlert commit, no effects
            }

            var formatIds = order.Lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).ToList();
            var kinds = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Kind, token);
            var context = new SettlementContext(order, s, kinds, now);

            // One effect per line (plan 14 §13.4); every line case has one since S-23.
            foreach (var line in order.Lines.OrderBy(l => l.LineNo))
            {
                await effectList.Single(e => e.Handles(line, context)).ApplyAsync(line, context, token);
            }

            await ledger.ApplyGiftsAsync(order, now, token);
            await ledger.IssueDocumentsAsync(order, s, now, token);
            order.CompleteSettlement(now);                                                     // fulfilment roll-up, OrderPaid, RenderDocuments
            return Result.Success(SettlementOutcome.Paid);
        }, ct);
    }
}
