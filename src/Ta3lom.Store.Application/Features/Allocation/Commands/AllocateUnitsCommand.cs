using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Allocation;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Application.Features.Allocation.Commands;

/// `POST /v1/admin/allocation/products/{productId}/allocate` (#112; RUL-20, RUL-47, RUL-48, RUL-52; database/01 §4.6, 09 §5.6).
/// One transaction, **all-or-nothing**: the oldest awaiting units are locked `FOR UPDATE SKIP LOCKED` (two officers never
/// get the same unit), the selected beneficiaries `FOR UPDATE`; every beneficiary is checked against RUL-47 (a)–(e) and all
/// failures are returned at once (`allocation.rejected`). Zakat-funded units are matched first to zakat-eligible people.
/// Course seats find or create the beneficiary's `customer` by mobile, link it, add a `sponsored` enrolment (`LmsEnrol`) and
/// queue `BeneficiaryInvite`; book copies get a `distribution` row in the print format. The zakat share of a unit leaves the
/// ledger once (`out`, reference = unit). Fewer free units than beneficiaries → `unitsShort`, the extras are not allocated.
public sealed record AllocateUnitsCommand(Guid ProductId, IReadOnlyList<Guid> BeneficiaryIds, string? Note) : ICommand<AllocationResult>;

internal sealed class AllocateUnitsCommandHandler(
    IApplicationDbContext db,
    ISponsoredUnitRepository units,
    IBeneficiaryRepository beneficiaries,
    ICustomerRepository customers,
    IEnrolmentRepository enrolments,
    IGenericRepository<AllocationBatch> batches,
    IGenericRepository<SponsoredUnitEvent> events,
    IGenericRepository<Distribution> distributions,
    IGenericRepository<ZakatLedgerEntry> zakat,
    IOutbox outbox,
    ICurrentActor actor,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<AllocateUnitsCommand, AllocationResult>
{
    public async Task<Result<AllocationResult>> HandleAsync(AllocateUnitsCommand command, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == command.ProductId).Select(p => new { p.Id, p.Type }).FirstOrDefaultAsync(ct);
        if (product is null)
        {
            return Error.NotFound(ErrorCodes.ProductNotFound, "Product not found.");
        }

        var isCourse = product.Type == ProductType.Course;
        var accessDays = isCourse
            ? await db.CourseDetails.AsNoTracking().Where(c => c.ProductId == product.Id).Select(c => c.AccessDays).FirstOrDefaultAsync(ct)
            : null;
        var printFormat = isCourse
            ? (Guid?)null
            : await db.BookFormats.AsNoTracking().Where(f => f.ProductId == product.Id && f.Kind == BookFormatKind.Print).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
        var by = actor.Name ?? "staff";

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var free = await units.LockOldestAwaitingAsync(product.Id, command.BeneficiaryIds.Count, token);   // RUL-48
            if (free.Count == 0)
            {
                return Result.Failure<AllocationResult>(AllocationErrors.NoUnits);
            }

            var locked = await beneficiaries.LockManyAsync(command.BeneficiaryIds, token);
            if (locked.Count != command.BeneficiaryIds.Count)
            {
                return Result.Failure<AllocationResult>(Error.Validation(new Dictionary<string, string[]> { ["beneficiaryIds"] = ["unknown beneficiary id"] }));
            }

            var selected = command.BeneficiaryIds.Select(id => locked.Single(b => b.Id == id)).ToList();
            var offerIds = free.Select(u => u.SponsorshipOfferId).Distinct().ToList();
            var groups = await db.SponsorshipOffers.AsNoTracking().Where(o => offerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.BeneficiaryGroup, token);

            // (c) needs the customer behind the mobile even before the beneficiary is linked (RUL-47c, RUL-52).
            var customerOf = new Dictionary<Guid, Guid?>();
            foreach (var b in selected)
            {
                customerOf[b.Id] = b.CustomerId ?? (isCourse && b.Mobile is not null ? (await customers.FindByMobileAsync(b.Mobile, token))?.Id : null);
            }

            var known = customerOf.Values.Where(c => c is not null).Select(c => c!.Value).ToList();
            var enrolled = isCourse && known.Count > 0
                ? (await db.Enrolments.AsNoTracking().Where(e => e.ProductId == product.Id && known.Contains(e.CustomerId)
                        && (e.Status == EnrolmentStatus.PendingSync || e.Status == EnrolmentStatus.Active || e.Status == EnrolmentStatus.Completed))
                    .Select(e => e.CustomerId).ToListAsync(token)).ToHashSet()
                : [];
            bool Live(Beneficiary b) => customerOf[b.Id] is { } c && enrolled.Contains(c);

            // Unit-independent checks first (a, c, e) plus the group (b) against every free unit's offer — all failures at once.
            var offerGroups = groups.Values.ToHashSet(StringComparer.Ordinal);
            var rejections = selected
                .Select(b => new
                {
                    b.Code,
                    Error = AllocationPolicy.CheckPerson(b, isCourse, Live(b))
                    ?? (offerGroups.Contains(b.BeneficiaryGroup) ? null : ErrorCodes.AllocationGroupMismatch)
                })
                .Where(r => r.Error is not null)
                .Select(r => new AllocationRejection(r.Code, r.Error!)).ToList();
            if (rejections.Count > 0)
            {
                return Result.Failure<AllocationResult>(AllocationErrors.Rejected(rejections));
            }

            // Matching: zakat-funded units first so zakat-eligible people take them (RUL-20), then FIFO.
            var pending = new List<Beneficiary>(selected);
            var pairs = new List<(SponsoredUnit Unit, Beneficiary Beneficiary)>();
            var unmatched = new List<SponsoredUnit>();
            foreach (var u in free.OrderByDescending(u => u.HasZakat).ThenBy(u => u.FundedAt).ThenBy(u => u.Id))
            {
                var b = pending.FirstOrDefault(b => AllocationPolicy.CheckPair(b, groups[u.SponsorshipOfferId], u) is null);
                if (b is null)
                {
                    unmatched.Add(u);
                    continue;
                }

                pairs.Add((u, b));
                pending.Remove(b);
            }

            if (pending.Count > 0 && unmatched.Count > 0)
            {
                var sample = unmatched[0];
                return Result.Failure<AllocationResult>(AllocationErrors.Rejected([.. pending.Select(b => new AllocationRejection(b.Code,
                    AllocationPolicy.CheckPair(b, groups[sample.SponsorshipOfferId], sample) ?? ErrorCodes.AllocationNoMatchingUnit))]));
            }

            var batch = AllocationBatch.Create(product.Id, command.Note, by, now);
            batches.Add(batch);
            var unitRefs = pairs.Select(p => p.Unit.Id.ToString()).ToList();
            var alreadyOut = await db.ZakatLedger.AsNoTracking().Where(z => z.Direction == ZakatDirection.Out && z.Reference != null && unitRefs.Contains(z.Reference))
                .Select(z => z.Reference!).ToListAsync(token);                                       // once per unit (a lapsed unit re-allocated)
            foreach (var (u, b) in pairs)
            {
                events.Add(u.Allocate(b.Id, batch.Id, by, now));
                if (isCourse)
                {
                    var customerId = await customers.FindOrCreateByMobileAsync(b.Mobile!, now, token);     // RUL-47e, RUL-52
                    b.LinkCustomer(customerId);
                    var enrolment = Enrolment.ForSponsoredUnit(customerId, product.Id, u.Id, accessDays, now);
                    if (!await enrolments.TryAddLiveAsync(enrolment, token))
                    {
                        return Result.Failure<AllocationResult>(AllocationErrors.Conflict);            // ux_enrolment_live: roll back
                    }

                    outbox.Enqueue(new LmsEnrol(enrolment.Id));
                    outbox.Enqueue(new BeneficiaryInvite(b.Id, enrolment.Id));
                }
                else if (printFormat is { } formatId)
                {
                    distributions.Add(Distribution.ForUnit(u.Id, formatId));
                }

                var share = await ZakatShareAsync(u, token);
                if (share > Money.Zero && !alreadyOut.Contains(u.Id.ToString()))
                {
                    zakat.Add(ZakatLedgerEntry.OutForUnit(u.ProductId, share, u.Id, by, now));                // RUL-20
                }
            }

            return Result.Success(new AllocationResult(batch.Id, pairs.Count, free.Count < command.BeneficiaryIds.Count,
                [.. pairs.Select(p => new AllocatedUnit(p.Unit.Id, p.Beneficiary.Code, p.Unit.FundedAt, p.Unit.HasZakat))]));
        }, ct);
    }

    /// RUL-20: a full zakat unit is zakat in full; a pooled unit counts the parts funded by zakat contributions.
    private async Task<Money> ZakatShareAsync(SponsoredUnit u, CancellationToken ct)
    {
        if (!u.HasZakat)
        {
            return Money.Zero;
        }

        if (u.Source == UnitSource.Full)
        {
            return u.UnitPrice;
        }

        var parts = await (from f in db.UnitFundings.AsNoTracking()
                           join c in db.PoolContributions.AsNoTracking() on f.PoolContributionId equals c.Id
                           where f.SponsoredUnitId == u.Id && c.IsZakat
                           select f.Amount).ToListAsync(ct);
        return parts.Sum();
    }
}

internal sealed class AllocateUnitsCommandValidator : AbstractValidator<AllocateUnitsCommand>
{
    public AllocateUnitsCommandValidator()
    {
        RuleFor(c => c.BeneficiaryIds).NotEmpty().Must(ids => ids.Count <= 200).WithMessage("1–200 beneficiaries per batch.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("beneficiaryIds must be distinct.");
        RuleFor(c => c.Note).MaximumLength(500);
    }
}
