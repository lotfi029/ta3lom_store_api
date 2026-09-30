using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Giving;

/// RUL-22/23 shared rules for the card page and the card SMS.
internal static class CardRules
{
    public const string Anonymous = "فاعل خير";

    public static readonly OrderStatus[] CardStatuses = [OrderStatus.Paid, OrderStatus.Fulfilled, OrderStatus.PartiallyRefunded];

    /// A typed sender name wins; otherwise the donor's name unless the order is anonymous (RUL-23).
    public static string FromName(Dedication d, Order o) => d.FromName ?? (o.IsAnonymous ? Anonymous : o.ContactName);

    public static (string? Ar, string? En) Message(Dedication d, StoreSettings s)
    {
        if (d.TemplateKey == Dedication.CustomTemplateKey)
        {
            return (d.CustomText, d.CustomText);
        }

        var t = s.DedicationTemplates.FirstOrDefault(t => t.Key == d.TemplateKey);
        return (t?.Ar, t?.En);
    }
}

/// `GET /v1/cards/{token}` (#45, 11 §8.1; RUL-22, RUL-23): the public card once the order is paid — recipient, message,
/// sender (or "فاعل خير"), product; never the amount. Unknown, unpaid or fully refunded line → `card.not_found`.
public sealed record GetCardQuery(string Token) : IQuery<CardResponse>;

internal sealed class GetCardQueryHandler(IApplicationDbContext db, IStoreSettings settings, IFileUrls files) : IQueryHandler<GetCardQuery, CardResponse>
{
    public async Task<Result<CardResponse>> HandleAsync(GetCardQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Token) || query.Token.Length != 22)
        {
            return GivingErrors.CardNotFound;
        }

        var row = await (from d in db.Dedications.AsNoTracking()
                         join l in db.OrderLines.AsNoTracking() on d.OrderLineId equals l.Id
                         join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                         join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                         where d.CardToken == query.Token
                         select new { d, o, l.FulfilmentStatus, p.Slug, p.TitleAr, p.TitleEn, p.CoverAssetId }).FirstOrDefaultAsync(ct);
        if (row is null || !CardRules.CardStatuses.Contains(row.o.Status) || row.FulfilmentStatus == FulfilmentStatus.Refunded)
        {
            return GivingErrors.CardNotFound;
        }

        var cover = row.CoverAssetId is { } id
            ? await db.MediaAssets.AsNoTracking().Where(m => m.Id == id).Select(m => m.StorageKey).FirstOrDefaultAsync(ct)
            : null;
        var (ar, en) = CardRules.Message(row.d, await settings.GetAsync(ct));
        return new CardResponse(row.d.ToName, ar, en, CardRules.FromName(row.d, row.o),
            new CardProduct(row.Slug, row.TitleAr, row.TitleEn, cover is null ? null : files.Public(cover)), row.o.Locale, row.o.PaidAt);
    }
}

/// `POST /v1/admin/dedications/{id}/resend` (#93, FR-S60): sends the card SMS again, optionally to a corrected mobile
/// (audited). Only for paid orders.
public sealed record ResendDedicationCommand(Guid DedicationId, string? ToMobile) : ICommand;

internal sealed class ResendDedicationCommandHandler(IApplicationDbContext db, IGenericRepository<Dedication> dedications, IOutbox outbox, IUnitOfWork uow)
    : ICommandHandler<ResendDedicationCommand>
{
    public async Task<Result> HandleAsync(ResendDedicationCommand command, CancellationToken ct = default)
    {
        string? corrected = null;
        if (!string.IsNullOrWhiteSpace(command.ToMobile) && !SaudiMobile.TryNormalize(command.ToMobile.Trim(), out corrected))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["toMobile"] = ["must be a Saudi mobile (05XXXXXXXX)"] });
        }

        var d = await dedications.GetByIdAsync(command.DedicationId, ct);
        var order = d is null
            ? null
            : await (from l in db.OrderLines.AsNoTracking()
                     join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                     where l.Id == d.OrderLineId
                     select new { o.Id, o.Status }).FirstOrDefaultAsync(ct);
        if (d is null || order is null || !CardRules.CardStatuses.Contains(order.Status))
        {
            return GivingErrors.DedicationNotFound;
        }

        d.PrepareResend(corrected);
        outbox.Enqueue(new DedicationCard(d.Id, order.Id, Resend: true));
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
