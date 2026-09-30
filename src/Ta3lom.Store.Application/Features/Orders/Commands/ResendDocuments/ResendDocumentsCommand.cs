using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Orders.Commands.ResendDocuments;

/// `POST /v1/admin/orders/{ref}/resend-documents` (#88, RUL-63): queues the order confirmation with the PDFs again on the
/// chosen channels. `document.not_ready` while any document of the paid order is still rendering.
public sealed record ResendDocumentsCommand(string Reference, IReadOnlyList<string> Channels) : ICommand;

internal sealed class ResendDocumentsCommandHandler(IApplicationDbContext db, IOutbox outbox, IUnitOfWork uow) : ICommandHandler<ResendDocumentsCommand>
{
    public async Task<Result> HandleAsync(ResendDocumentsCommand command, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Reference == command.Reference, ct);
        if (order is null)
        {
            return CheckoutErrors.OrderNotFound;
        }

        var issued = await db.Invoices.CountAsync(i => i.OrderId == order.Id, ct) + await db.DonationReceipts.CountAsync(r => r.OrderId == order.Id, ct);
        var pending = await db.Invoices.CountAsync(i => i.OrderId == order.Id && i.PdfAssetId == null, ct)
                      + await db.DonationReceipts.CountAsync(r => r.OrderId == order.Id && r.PdfAssetId == null, ct);
        if (!Order.IsPaidState(order.Status) || issued == 0 || pending > 0)
        {
            return DocumentErrors.NotReady;
        }

        outbox.Enqueue(new DocumentsResendRequested(order.Id, [.. command.Channels.Distinct(StringComparer.Ordinal)]));
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ResendDocumentsCommandValidator : AbstractValidator<ResendDocumentsCommand>
{
    public ResendDocumentsCommandValidator() =>
        RuleFor(c => c.Channels).NotEmpty().Must(c => c.All(x => x is "sms" or "email")).WithMessage("channels must be sms and/or email.");
}
