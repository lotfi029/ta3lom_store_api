using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// FR-S42, RUL-63: SMS `shipment_shipped` (reference, tracking) to the order's contact mobile, once.
internal sealed class ShipmentShippedHandler(IApplicationDbContext db, NotificationDispatch dispatch) : OutboxHandler<ShipmentShipped>
{
    protected override async Task HandleAsync(ShipmentShipped message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var info = await (from s in db.Shipments.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on s.OrderId equals o.Id
                          where s.Id == message.ShipmentId
                          select new { o.Id, o.Reference, o.ContactMobile, o.Locale, s.Carrier, s.TrackingNo }).FirstAsync(ct);
        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, info.ContactMobile, "shipment_shipped", info.Locale,
            new { reference = info.Reference, tracking = $"{info.Carrier} {info.TrackingNo}".Trim() }, info.Id, ct);
    }
}

/// FR-S42: SMS `shipment_delivered`, once.
internal sealed class ShipmentDeliveredHandler(IApplicationDbContext db, NotificationDispatch dispatch) : OutboxHandler<ShipmentDelivered>
{
    protected override async Task HandleAsync(ShipmentDelivered message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == message.OrderId, ct);
        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, order.ContactMobile, "shipment_delivered", order.Locale,
            new { reference = order.Reference }, order.Id, ct);
    }
}

/// FR-S43 (S-41): staff e-mail `staff_alert`, once per message.
internal sealed class LowStockAlertHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<LowStockAlert>
{
    protected override Task HandleAsync(LowStockAlert message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new
            {
                subject = string.Create(CultureInfo.InvariantCulture, $"LowStockAlert {message.Sku}"),
                body = string.Create(CultureInfo.InvariantCulture,
                    $"مخزون منخفض: {message.TitleAr}\nsku: {message.Sku}\navailable: {message.Available}\nthreshold: {message.Threshold}\nday: {message.Day:yyyy-MM-dd}"),
            },
            null, ct);
}
