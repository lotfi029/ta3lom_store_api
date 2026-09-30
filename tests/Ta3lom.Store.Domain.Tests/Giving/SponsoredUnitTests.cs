using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// The sponsored unit lifecycle (RUL-43, RUL-47d, RUL-48, RUL-50).
public sealed class SponsoredUnitTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Product = Guid.CreateVersion7();
    private static readonly Guid Offer = Guid.CreateVersion7();

    private static SponsoredUnit Full(bool zakat = false) => SponsoredUnit.Full(Product, Offer, Guid.CreateVersion7(), new Money(350m), zakat, T0);

    [Fact]
    [Rule("RUL-43")]
    public void A_full_unit_is_linked_to_its_order_line_and_awaits_allocation()
    {
        var line = Guid.CreateVersion7();

        var u = SponsoredUnit.Full(Product, Offer, line, new Money(350m), true, T0);

        u.ProductId.ShouldBe(Product);
        u.SponsorshipOfferId.ShouldBe(Offer);
        u.OrderLineId.ShouldBe(line);
        u.Source.ShouldBe(UnitSource.Full);
        u.UnitPrice.ShouldBe(new Money(350m));
        u.HasZakat.ShouldBeTrue();
        u.Status.ShouldBe(UnitStatus.AwaitingAllocation);
        u.FundedAt.ShouldBe(T0);
        u.BeneficiaryId.ShouldBeNull();
        Should.Throw<DomainException>(() => SponsoredUnit.Full(Product, Offer, Guid.Empty, new Money(1m), false, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-47")]
    public void A_pooled_unit_becomes_zakat_funded_when_marked()
    {
        var u = SponsoredUnit.Pooled(Product, Offer, new Money(350m), T0);

        u.Source.ShouldBe(UnitSource.Pooled);
        u.OrderLineId.ShouldBeNull();
        u.HasZakat.ShouldBeFalse();

        u.MarkZakatFunded();
        u.HasZakat.ShouldBeTrue();
    }

    [Fact]
    [Rule("RUL-50")]
    public void Allocation_records_the_beneficiary_batch_and_actor()
    {
        var u = Full();
        var beneficiary = Guid.CreateVersion7();
        var batch = Guid.CreateVersion7();

        var e = u.Allocate(beneficiary, batch, "officer@ta3lom.sa", T0.AddDays(2));

        u.Status.ShouldBe(UnitStatus.Allocated);
        u.BeneficiaryId.ShouldBe(beneficiary);
        u.AllocationBatchId.ShouldBe(batch);
        u.AllocatedAt.ShouldBe(T0.AddDays(2));
        u.AllocatedBy.ShouldBe("officer@ta3lom.sa");
        e.UnitId.ShouldBe(u.Id);
        e.FromStatus.ShouldBe("awaiting_allocation");
        e.ToStatus.ShouldBe("allocated");
        e.Actor.ShouldBe("officer@ta3lom.sa");
        e.At.ShouldBe(T0.AddDays(2));
        u.DomainEvents.ShouldBe([new SponsorUnitAllocated(u.Id, Product, u.OrderLineId)]);
    }

    [Fact]
    [Rule("RUL-50")]
    public void Only_awaiting_units_are_allocated()
    {
        var u = Full();
        u.Allocate(Guid.CreateVersion7(), Guid.CreateVersion7(), "a", T0);

        Should.Throw<DomainException>(() => u.Allocate(Guid.CreateVersion7(), Guid.CreateVersion7(), "a", T0)).Code.ShouldBe("unit.not_awaiting");
    }

    [Fact]
    [Rule("RUL-50")]
    public void An_allocated_unit_completes_once()
    {
        var u = Full();
        u.Complete("sync", T0).ShouldBeNull();
        u.Allocate(Guid.CreateVersion7(), Guid.CreateVersion7(), "a", T0);
        u.ClearDomainEvents();

        var e = u.Complete("sync", T0.AddDays(40));

        e.ShouldNotBeNull();
        e.FromStatus.ShouldBe("allocated");
        e.ToStatus.ShouldBe("completed");
        u.Status.ShouldBe(UnitStatus.Completed);
        u.CompletedAt.ShouldBe(T0.AddDays(40));
        u.DomainEvents.ShouldBe([new SponsorUnitCompleted(u.Id, Product, u.OrderLineId)]);
        u.Complete("sync", T0.AddDays(41)).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-50")]
    public void A_lapse_returns_the_unit_to_the_queue_and_keeps_its_funded_at()
    {
        var u = Full();
        var beneficiary = Guid.CreateVersion7();
        u.Allocate(beneficiary, Guid.CreateVersion7(), "officer", T0.AddDays(1));

        var (events, former) = u.Lapse("lapse-job", T0.AddDays(61));

        former.ShouldBe(beneficiary);
        events.Select(e => (e.FromStatus, e.ToStatus)).ShouldBe([("allocated", "lapsed"), ("lapsed", "awaiting_allocation")]);
        events.ShouldAllBe(e => e.Actor == "lapse-job" && e.At == T0.AddDays(61));
        u.Status.ShouldBe(UnitStatus.AwaitingAllocation);
        u.LapsedAt.ShouldBe(T0.AddDays(61));
        u.FundedAt.ShouldBe(T0);
        u.BeneficiaryId.ShouldBeNull();
        u.AllocationBatchId.ShouldBeNull();
        u.AllocatedAt.ShouldBeNull();
        u.AllocatedBy.ShouldBeNull();

        u.Allocate(beneficiary, Guid.CreateVersion7(), "officer", T0.AddDays(62));
        u.Status.ShouldBe(UnitStatus.Allocated);
    }

    [Fact]
    [Rule("RUL-50")]
    public void Only_allocated_units_lapse()
    {
        Should.Throw<DomainException>(() => Full().Lapse("job", T0)).Code.ShouldBe("unit.not_allocated");
    }

    [Fact]
    [Rule("RUL-50")]
    public void A_unit_event_without_a_from_state_marks_the_funding()
    {
        var unit = Guid.CreateVersion7();

        var e = SponsoredUnitEvent.Transition(unit, null, UnitStatus.AwaitingAllocation, "settlement", T0);

        e.UnitId.ShouldBe(unit);
        e.FromStatus.ShouldBeNull();
        e.ToStatus.ShouldBe("awaiting_allocation");
        Should.Throw<DomainException>(() => new SponsoredUnitEvent(Guid.Empty, "x", "a", T0)).Code.ShouldBe("guard.empty");
    }
}
