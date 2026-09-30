using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// RUL-42: grants LMS access; on failure the message retries (the enrolment stays `pending_sync`). On success the enrolment
/// becomes `active`, its purchase line `enrolled`, and the order `fulfilled` once every line is terminal (RUL-40).
internal sealed class LmsEnrolHandler(IApplicationDbContext db, IGenericRepository<Enrolment> enrolments, IOrderRepository orders,
    ILmsGateway lms, IUnitOfWork uow, IClock clock) : OutboxHandler<LmsEnrol>
{
    protected override async Task HandleAsync(LmsEnrol message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var enrolment = await enrolments.GetByIdAsync(message.EnrolmentId, ct);
        if (enrolment is null || enrolment.Status != EnrolmentStatus.PendingSync)
        {
            return;                                                                         // already synced or revoked
        }

        var result = await lms.EnrolAsync(await LmsViewAsync(db, enrolment, ct), ct);
        if (!result.Ok)
        {
            throw new OutboxDeliveryException($"LMS enrol failed for {enrolment.Id}: {result.Error}");
        }

        await uow.ExecuteInTransactionAsync(async token =>
        {
            enrolment.MarkSynced(result.Reference ?? enrolment.Id.ToString("N"));
            if (enrolment.OrderLineId is { } lineId)
            {
                var orderId = await db.OrderLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => l.OrderId).FirstAsync(token);
                var order = await orders.LoadForUpdateAsync(orderId, token);
                order!.Lines.Single(l => l.Id == lineId).SetFulfilment(FulfilmentStatus.Enrolled);
                order.RecomputeFulfilment(clock.UtcNow);
            }
        }, ct);
    }

    internal static async Task<LmsEnrolment> LmsViewAsync(IApplicationDbContext db, Enrolment e, CancellationToken ct)
    {
        var course = await (from p in db.Products.AsNoTracking()
                            join c in db.CourseDetails.AsNoTracking() on p.Id equals c.ProductId into cd
                            from c in cd.DefaultIfEmpty()
                            where p.Id == e.ProductId
                            select new { p.Slug, Ref = c == null ? null : c.LmsCourseRef }).FirstAsync(ct);
        return new LmsEnrolment(e.Id, e.CustomerId, course.Slug, course.Ref, e.LmsEnrolmentRef, e.ExpiresAt);
    }
}

/// RUL-50/58: withdraws LMS access for a revoked enrolment (idempotent in the adapter).
internal sealed class LmsRevokeHandler(IApplicationDbContext db, ILmsGateway lms) : OutboxHandler<LmsRevoke>
{
    protected override async Task HandleAsync(LmsRevoke message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == message.EnrolmentId, ct);
        if (enrolment is null)
        {
            return;
        }

        var result = await lms.RevokeAsync(await LmsEnrolHandler.LmsViewAsync(db, enrolment, ct), ct);
        if (!result.Ok)
        {
            throw new OutboxDeliveryException($"LMS revoke failed for {enrolment.Id}: {result.Error}");
        }
    }
}
