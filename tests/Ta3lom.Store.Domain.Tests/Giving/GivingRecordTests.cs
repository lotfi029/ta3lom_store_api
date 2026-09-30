using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// Cause updates (RUL-54), distributions (RUL-47), the zakat ledger (RUL-20), allocation batches and pool guards.
public sealed class GivingRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Product = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 9, 28);

    private static CauseUpdateDetails Details(bool notify = false, string title = "تسليم 10 مبردات") =>
        new(title, "10 coolers delivered", "نص", "Body", Guid.CreateVersion7(), 10, Day, true, notify);

    [Fact]
    [Rule("RUL-54")]
    public void A_cause_update_is_public_and_does_not_notify_by_default()
    {
        var d = Details();

        var u = CauseUpdate.Post(Product, d, "staff@ta3lom.sa", T0);

        u.ProductId.ShouldBe(Product);
        u.TitleAr.ShouldBe(d.TitleAr);
        u.TitleEn.ShouldBe(d.TitleEn);
        u.BodyAr.ShouldBe("نص");
        u.BodyEn.ShouldBe("Body");
        u.AssetId.ShouldBe(d.AssetId);
        u.UnitsDelivered.ShouldBe(10);
        u.OccurredOn.ShouldBe(Day);
        u.IsPublic.ShouldBeTrue();
        u.NotifyDonors.ShouldBeFalse();
        u.NotifiedAt.ShouldBeNull();
        u.CanDelete.ShouldBeTrue();
        u.CreatedAt.ShouldBe(T0);
        u.CreatedBy.ShouldBe("staff@ta3lom.sa");
        u.DomainEvents.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => CauseUpdate.Post(Product, null!, "a", T0));
        Should.Throw<ArgumentNullException>(() => u.Edit(null!, T0));
        Should.Throw<DomainException>(() => new CauseUpdate(Product, "t", Day, null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-54")]
    public void Notifying_donors_is_queued_once_and_the_update_can_no_longer_be_deleted()
    {
        var u = CauseUpdate.Post(Product, Details(), "staff", T0);

        u.Edit(Details(notify: true, title: "تحديث"), T0.AddHours(1));

        u.NotifyDonors.ShouldBeTrue();
        u.NotifiedAt.ShouldBe(T0.AddHours(1));
        u.CanDelete.ShouldBeFalse();
        u.TitleAr.ShouldBe("تحديث");
        u.DomainEvents.ShouldBe([new CauseUpdatePublished(u.Id, Product)]);

        u.Edit(Details(notify: false) with { IsPublic = false }, T0.AddHours(2));
        u.Edit(Details(notify: true), T0.AddHours(3));

        u.NotifyDonors.ShouldBeTrue();
        u.NotifiedAt.ShouldBe(T0.AddHours(1));
        u.IsPublic.ShouldBeTrue();
        u.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-47")]
    public void A_distribution_is_completed_with_its_delivery_date_and_proof()
    {
        var unit = Guid.CreateVersion7();
        var format = Guid.CreateVersion7();
        var proof = Guid.CreateVersion7();

        var d = Distribution.ForUnit(unit, format);
        d.DeliveredOn.ShouldBeNull();

        d.Deliver(Day, proof);

        d.SponsoredUnitId.ShouldBe(unit);
        d.BookFormatId.ShouldBe(format);
        d.DeliveredOn.ShouldBe(Day);
        d.ProofAssetId.ShouldBe(proof);
        Should.Throw<DomainException>(() => Distribution.ForUnit(unit, Guid.Empty)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void An_allocation_batch_keeps_a_trimmed_note()
    {
        var b = AllocationBatch.Create(Product, "  first batch ", "officer", T0);

        b.ProductId.ShouldBe(Product);
        b.Note.ShouldBe("first batch");
        b.CreatedBy.ShouldBe("officer");
        b.CreatedAt.ShouldBe(T0);
        AllocationBatch.Create(Product, " ", "officer", T0).Note.ShouldBeNull();
        AllocationBatch.Create(Product, null, "officer", T0).Note.ShouldBeNull();
        Should.Throw<DomainException>(() => AllocationBatch.Create(Guid.Empty, null, "o", T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-20")]
    public void Zakat_received_is_an_in_row_for_its_order_line()
    {
        var line = Guid.CreateVersion7();

        var e = ZakatLedgerEntry.In(Product, new Money(500m), line, T0);

        e.ProductId.ShouldBe(Product);
        e.Direction.ShouldBe(ZakatDirection.In);
        e.Amount.ShouldBe(new Money(500m));
        e.OrderLineId.ShouldBe(line);
        e.Reference.ShouldBeNull();
        e.Actor.ShouldBe("settlement");
        e.At.ShouldBe(T0);
    }

    [Fact]
    [Rule("RUL-20")]
    public void Zakat_outflows_are_a_voucher_or_a_unit_share()
    {
        var unit = Guid.CreateVersion7();

        var voucher = ZakatLedgerEntry.Voucher(Product, new Money(300m), "V-2026-001", T0.AddDays(-2), "finance@ta3lom.sa");
        var share = ZakatLedgerEntry.OutForUnit(Product, new Money(350m), unit, "officer", T0);

        voucher.Direction.ShouldBe(ZakatDirection.Out);
        voucher.Reference.ShouldBe("V-2026-001");
        voucher.At.ShouldBe(T0.AddDays(-2));
        voucher.Actor.ShouldBe("finance@ta3lom.sa");
        voucher.OrderLineId.ShouldBeNull();
        share.Direction.ShouldBe(ZakatDirection.Out);
        share.Reference.ShouldBe(unit.ToString());
        Should.Throw<DomainException>(() => ZakatLedgerEntry.Voucher(Product, Money.Zero, null!, T0, "a")).Code.ShouldBe("guard.null");
        Should.Throw<DomainException>(() => new ZakatLedgerEntry(Guid.Empty, ZakatDirection.In, Money.Zero, "a", T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void Unit_funding_links_a_unit_to_a_contribution()
    {
        var unit = Guid.CreateVersion7();
        var contribution = Guid.CreateVersion7();

        var f = new UnitFunding(unit, contribution, new Money(100m));

        f.SponsoredUnitId.ShouldBe(unit);
        f.PoolContributionId.ShouldBe(contribution);
        f.Amount.ShouldBe(new Money(100m));
        Should.Throw<DomainException>(() => new UnitFunding(Guid.Empty, contribution, Money.Zero)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-45")]
    public void A_pool_accepts_only_its_own_offer_and_a_positive_seat_price()
    {
        var offer = Guid.CreateVersion7();
        var pool = new SponsorshipPool(offer, T0);
        var mine = PoolContribution.FromLine(offer, Guid.CreateVersion7(), new Money(100m), false, T0);
        var other = PoolContribution.FromLine(Guid.CreateVersion7(), Guid.CreateVersion7(), new Money(100m), false, T0);

        pool.SponsorshipOfferId.ShouldBe(offer);
        pool.Balance.ShouldBe(Money.Zero);
        pool.UpdatedAt.ShouldBe(T0);
        Should.Throw<DomainException>(() => pool.Add(other, [other], Product, new Money(350m), T0)).Code.ShouldBe("pool.offer_mismatch");
        Should.Throw<DomainException>(() => pool.Add(mine, [mine, other], Product, new Money(350m), T0)).Code.ShouldBe("pool.offer_mismatch");
        Should.Throw<DomainException>(() => pool.Add(mine, [mine], Product, Money.Zero, T0)).Code.ShouldBe("pool.seat_price");
        Should.Throw<ArgumentNullException>(() => pool.Add(null!, [], Product, new Money(350m), T0));
        Should.Throw<ArgumentNullException>(() => pool.Add(mine, null!, Product, new Money(350m), T0));
        Should.Throw<ArgumentNullException>(() => pool.Sweep(null!, T0));
    }

    [Fact]
    [Rule("RUL-45")]
    public void A_pool_whose_open_contributions_do_not_cover_the_balance_is_an_invariant_breach()
    {
        var offer = Guid.CreateVersion7();
        var pool = new SponsorshipPool(offer, T0);
        var c = PoolContribution.FromLine(offer, Guid.CreateVersion7(), new Money(400m), false, T0);

        Should.Throw<DomainException>(() => pool.Add(c, [], Product, new Money(350m), T0)).Code.ShouldBe("pool.invariant");

        var small = new SponsorshipPool(offer, T0);
        var d = PoolContribution.FromLine(offer, Guid.CreateVersion7(), new Money(100m), false, T0);
        Should.Throw<DomainException>(() => small.Add(d, [], Product, new Money(350m), T0)).Code.ShouldBe("pool.invariant");
    }

    [Fact]
    [Rule("RUL-46")]
    public void Sweeping_an_empty_pool_moves_nothing()
    {
        var offer = Guid.CreateVersion7();
        var pool = new SponsorshipPool(offer, T0);
        var zakat = PoolContribution.FromLine(offer, Guid.CreateVersion7(), new Money(100m), true, T0);
        pool.Add(zakat, [zakat], Product, new Money(350m), T0);

        pool.Sweep([zakat], T0.AddDays(30)).ShouldBe(Money.Zero);

        pool.Balance.ShouldBe(new Money(100m));
        pool.UpdatedAt.ShouldBe(T0);
        zakat.SweptToFundAt.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-45")]
    public void A_contribution_enters_the_pool_in_full_and_is_consumed_fifo()
    {
        var offer = Guid.CreateVersion7();
        var line = Guid.CreateVersion7();

        var c = PoolContribution.FromLine(offer, line, new Money(100m), true, T0);

        c.SponsorshipOfferId.ShouldBe(offer);
        c.OrderLineId.ShouldBe(line);
        c.Amount.ShouldBe(new Money(100m));
        c.Remaining.ShouldBe(new Money(100m));
        c.IsZakat.ShouldBeTrue();
        c.CreatedAt.ShouldBe(T0);
        c.Consume(new Money(30m)).ShouldBe(new Money(30m));
        c.Consume(new Money(500m)).ShouldBe(new Money(70m));
        c.Remaining.ShouldBe(Money.Zero);
        new PoolContribution(offer, line, new Money(1m), new Money(1m), T0).IsZakat.ShouldBeFalse();
    }
}
