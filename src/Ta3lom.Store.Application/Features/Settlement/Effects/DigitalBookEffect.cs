using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-39 `buy` digital book (S-20): one `download_grant` with `download_limit` downloads (no expiry); the line is `available`.
internal sealed class DigitalBookEffect(IGenericRepository<DownloadGrant> grants) : ILineSettlementEffect
{
    public bool Handles(OrderLine line, SettlementContext context) =>
        line.Kind == LineKind.Buy && line.ProductType == ProductType.Book && context.FormatKind(line) == BookFormatKind.Digital;

    public Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        grants.Add(DownloadGrant.Issue(context.Order.CustomerId, line.Id, line.BookFormatId!.Value, context.Settings.DownloadLimit, context.Now));
        line.SetFulfilment(FulfilmentStatus.Available);
        return Task.CompletedTask;
    }
}
