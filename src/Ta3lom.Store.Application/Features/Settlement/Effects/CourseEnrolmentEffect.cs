using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-39 `buy` course (S-19): an `enrolment` (source `purchase`, `pending_sync`) and the `LmsEnrol` outbox message; the line
/// is `pending_sync` until the LMS accepts. RUL-13 race: a live enrolment already exists → no second enrolment, the line stays
/// `pending` (refund due) and finance gets a `duplicate_course` alert.
internal sealed class CourseEnrolmentEffect(IApplicationDbContext db, IEnrolmentRepository enrolments) : ILineSettlementEffect
{
    public bool Handles(OrderLine line, SettlementContext context) => line.Kind == LineKind.Buy && line.ProductType == ProductType.Course;

    public async Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        var order = context.Order;
        var accessDays = await db.CourseDetails.AsNoTracking().Where(c => c.ProductId == line.ProductId).Select(c => c.AccessDays).FirstOrDefaultAsync(ct);
        var enrolment = Enrolment.ForPurchase(order.CustomerId, line.ProductId, line.Id, accessDays, context.Now);
        if (!await enrolments.TryAddLiveAsync(enrolment, ct))
        {
            order.RaiseEvent(new FinanceAlert(FinanceAlertKinds.DuplicateCourse, order.Id, order.Reference,
                FormattableString.Invariant($"line {line.LineNo}: the customer already has a live enrolment; refund the line")));
            return;
        }

        line.SetFulfilment(FulfilmentStatus.PendingSync);
        order.RaiseEvent(new LmsEnrol(enrolment.Id));
    }
}
