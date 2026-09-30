using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Giving;
using Ta3lom.Store.Application.Features.Giving.Commands;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// RUL-22 (S-26 completes the S-17 stub): the card SMS `dedication_card` to the recipient, only for a paid order and at
/// most once per line (a staff resend is an explicit new message); `sms_status`/`sent_at` follow the provider result.
internal sealed class DedicationCardHandler(IApplicationDbContext db, IGenericRepository<Dedication> dedications, NotificationDispatch dispatch,
    IWebLinks links, IUnitOfWork uow, IClock clock) : OutboxHandler<DedicationCard>
{
    protected override async Task HandleAsync(DedicationCard message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var d = await dedications.GetByIdAsync(message.DedicationId, ct);
        var info = d is null
            ? null
            : await (from l in db.OrderLines.AsNoTracking()
                     join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                     join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                     where l.Id == d.OrderLineId
                     select new { Order = o, p.TitleAr }).FirstOrDefaultAsync(ct);
        if (d is null || info is null || !CardRules.CardStatuses.Contains(info.Order.Status)
            || (!message.Resend && d.SmsStatus == DedicationSmsStatus.Sent))
        {
            return;                                                                           // unpaid, gone, or already delivered
        }

        try
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, d.ToMobile, "dedication_card", info.Order.Locale,
                new { to_name = d.ToName, from_name = CardRules.FromName(d, info.Order), product = info.TitleAr, link = links.Card(info.Order.Locale, d.CardToken) },
                info.Order.Id, ct);
        }
        catch (OutboxDeliveryException)
        {
            d.MarkFailed();
            await uow.SaveChangesAsync(ct);
            throw;                                                                            // the dispatcher retries
        }

        d.MarkSent(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
    }
}

/// RUL-54: SMS `cause_update` to every distinct paid donor of the product (by contact mobile, in the order's locale), once
/// per donor for this update — a retry skips the donors already reached.
internal sealed class CauseUpdatePublishedHandler(IApplicationDbContext db, NotificationDispatch dispatch, IWebLinks links) : OutboxHandler<CauseUpdatePublished>
{
    private static readonly OrderStatus[] Paid = [OrderStatus.Paid, OrderStatus.Fulfilled, OrderStatus.PartiallyRefunded];

    protected override async Task HandleAsync(CauseUpdatePublished message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var update = await (from u in db.CauseUpdates.AsNoTracking()
                            join p in db.Products.AsNoTracking() on u.ProductId equals p.Id
                            where u.Id == message.UpdateId
                            select new { u.TitleAr, u.TitleEn, p.Slug, ProductAr = p.TitleAr, ProductEn = p.TitleEn }).FirstOrDefaultAsync(ct);
        if (update is null)
        {
            return;
        }

        var donors = await (from o in db.Orders.AsNoTracking()
                            where Paid.Contains(o.Status) && db.OrderLines.Any(l => l.OrderId == o.Id && l.ProductId == message.ProductId && l.Kind != LineKind.Buy)
                            orderby o.PaidAt descending
                            select new { o.ContactMobile, o.Locale }).ToListAsync(ct);
        foreach (var donor in donors.DistinctBy(d => d.ContactMobile))
        {
            var en = donor.Locale == "en";
            await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, donor.ContactMobile, "cause_update", donor.Locale,
                new
                {
                    product = en ? update.ProductEn ?? update.ProductAr : update.ProductAr,
                    title = en ? update.TitleEn ?? update.TitleAr : update.TitleAr,
                    link = links.Product(donor.Locale, update.Slug),
                }, null, ct);
        }
    }
}

/// RUL-46: the sweep is shown in the contributors' account history (from `swept_to_fund_at`, S-31); nothing is sent.
internal sealed class PoolSweptHandler : OutboxHandler<PoolSwept>
{
    protected override Task HandleAsync(PoolSwept message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}

/// RUL-63 monthly digest (S-45): e-mail when the account has an address, otherwise a one-line SMS; at most once per message
/// and recipient. Sent only if something still changed in the window and the account is active.
internal sealed class SponsorDigestHandler(IApplicationDbContext db, SponsorDigestReader reader, NotificationDispatch dispatch, IWebLinks links)
    : OutboxHandler<SponsorDigest>
{
    protected override async Task HandleAsync(SponsorDigest message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == message.CustomerId && c.Status == CustomerStatus.Active)
            .Select(c => new { c.Mobile, c.Email, c.Locale, c.FullName }).FirstOrDefaultAsync(ct);
        if (customer is null)
        {
            return;
        }

        var content = await reader.ReadAsync(message.CustomerId, message.Since, message.Until, ct);
        if (content.IsEmpty)
        {
            return;
        }

        var link = links.MyGiving(customer.Locale);
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Email, customer.Email, "sponsor_digest", customer.Locale,
                new { body = SponsorDigestText.EmailBody(content, customer.Locale, customer.FullName, message.Since, message.Until, link), link }, null, ct);
        }
        else
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, customer.Mobile, "sponsor_digest", customer.Locale,
                new { summary = SponsorDigestText.Summary(content, customer.Locale), link }, null, ct);
        }
    }
}

/// Plan 14 S-35: asks ta3lom-web to rebuild after a publish/archive (debounced at enqueue); a failed dispatch is retried.
internal sealed class CatalogChangedHandler(ISiteRebuildTrigger trigger) : OutboxHandler<CatalogChanged>
{
    protected override Task HandleAsync(CatalogChanged message, OutboxEnvelope envelope, CancellationToken ct) =>
        trigger.TriggerAsync($"{message.Change} {message.ProductId}", ct);
}
