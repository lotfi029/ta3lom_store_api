using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Payments;

/// `GET /v1/admin/payments/orphans?resolved=` (review 04 A-3; `staff:finance`): captured payments the gateway reported for
/// which the store has no attempt — newest first, open ones by default (at most 200).
public sealed record ListOrphanPaymentsQuery(bool Resolved) : IQuery<IReadOnlyList<OrphanPaymentRow>>;

internal sealed class ListOrphanPaymentsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListOrphanPaymentsQuery, IReadOnlyList<OrphanPaymentRow>>
{
    public const int Max = 200;

    public async Task<Result<IReadOnlyList<OrphanPaymentRow>>> HandleAsync(ListOrphanPaymentsQuery query, CancellationToken ct = default)
    {
        var rows = await db.PaymentEvents.AsNoTracking()
            .Where(e => e.Outcome == PaymentEvent.OrphanOutcome && (query.Resolved ? e.ResolvedAt != null : e.ResolvedAt == null))
            .OrderByDescending(e => e.ReceivedAt).Take(Max).ToListAsync(ct);
        return rows.Select(OrphanPaymentRows.From).ToList();
    }
}

/// `POST /v1/admin/payments/orphans/{id}/resolve` (`staff:finance`): finance records what it did (refunded it on the
/// dashboard, matched it to a donor, …). Once only (409 `payment.orphan_resolved`); audited.
public sealed record ResolveOrphanPaymentCommand(Guid Id, ResolveOrphanRequest Request) : ICommand<OrphanPaymentRow>;

internal sealed class ResolveOrphanPaymentCommandHandler(IPaymentEventRepository events, IAuditTrail audit, ICurrentActor actor, IUnitOfWork uow, IClock clock)
    : ICommandHandler<ResolveOrphanPaymentCommand, OrphanPaymentRow>
{
    public async Task<Result<OrphanPaymentRow>> HandleAsync(ResolveOrphanPaymentCommand command, CancellationToken ct = default)
    {
        var orphan = await events.GetAsync(command.Id, ct);
        if (orphan is null)
        {
            return PaymentErrors.OrphanNotFound;
        }

        var staff = actor.Name ?? "staff";
        var resolved = orphan.Resolve(staff, command.Request.Note, clock.UtcNow);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        audit.Record(AuditActions.OrphanPaymentResolved, "payment_event", orphan.Id.ToString(), staff, new { orphan.ProviderPaymentId });
        await uow.SaveChangesAsync(ct);
        return OrphanPaymentRows.From(orphan);
    }
}

internal sealed class ResolveOrphanPaymentCommandValidator : AbstractValidator<ResolveOrphanPaymentCommand>
{
    public ResolveOrphanPaymentCommandValidator() => RuleFor(c => c.Request.Note).NotEmpty().MaximumLength(500);
}

internal static class OrphanPaymentRows
{
    public static OrphanPaymentRow From(PaymentEvent e) =>
        new(e.Id, e.Provider, e.ProviderPaymentId, e.ProviderInvoiceId, (e.GatewayAmount ?? Money.Zero).ToWire(), e.GatewayCurrency,
            e.ReceivedAt, e.ResolvedAt, e.ResolvedBy, e.ResolutionNote);
}
