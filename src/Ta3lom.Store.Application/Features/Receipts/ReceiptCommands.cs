using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Receipts;

/// `GET /v1/me/donations/{reference}/receipt` (the signed-in donor) and `GET /v1/orders/{reference}/receipt?t=` (status
/// token): the order's DR, or its DRC with `kind = drc`. Not yours → 404 `order.not_found`; no such receipt → 404
/// `receipt.not_available`. A command: the first download may render and store the PDF.
public sealed record DownloadReceiptCommand(string Reference, string? Token, Guid? CustomerId, string? Kind) : ICommand<ReceiptFile>;

internal sealed class DownloadReceiptCommandHandler(IApplicationDbContext db, IOrderStatusTokens tokens, ReceiptFiles receipts)
    : ICommandHandler<DownloadReceiptCommand, ReceiptFile>
{
    public async Task<Result<ReceiptFile>> HandleAsync(DownloadReceiptCommand command, CancellationToken ct = default)
    {
        var reference = Order.NormalizeReference(command.Reference);
        var order = await db.Orders.AsNoTracking().Where(o => o.Reference == reference)
            .Select(o => new { o.Id, o.CustomerId, o.StatusTokenHash }).FirstOrDefaultAsync(ct);
        var allowed = order is not null
                      && ((command.CustomerId is { } customerId && order.CustomerId == customerId)
                          || (command.Token is { } token && tokens.Matches(token, order.StatusTokenHash)));
        if (!allowed)
        {
            return OrderErrors.NotFound;
        }

        var series = string.Equals(command.Kind, "drc", StringComparison.OrdinalIgnoreCase) ? ReceiptSeries.Drc : ReceiptSeries.Dr;
        var receiptId = await db.DonationReceipts.AsNoTracking().Where(r => r.OrderId == order!.Id && r.Series == series)
            .OrderByDescending(r => r.IssuedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        return receiptId is { } id ? await receipts.GetOrRenderAsync(id, ct) : PaymentErrors.ReceiptNotAvailable;
    }
}
