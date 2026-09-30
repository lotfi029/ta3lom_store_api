using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Catalog;

/// Sponsorship offers (RUL-06, RUL-09, RUL-10, RUL-19).
public sealed class SponsorshipOfferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = T0.AddDays(1);
    private static readonly DateTimeOffset End = T0.AddDays(31);
    private static readonly Guid ProductId = Guid.CreateVersion7();

    private static SponsorshipOffer Offer(bool closeOnTarget = false, bool allowPartial = true) =>
        SponsorshipOffer.Create(ProductId, new Money(350m), "مقعد", "seat", 100, "rural_teachers", "معلمو القرى", "Rural teachers", allowPartial,
            closeOnTarget, Start, End, T0);

    [Fact]
    [Rule("RUL-06")]
    public void Create_sets_the_offer_terms()
    {
        var o = Offer(allowPartial: false);

        o.ProductId.ShouldBe(ProductId);
        o.SeatPrice.ShouldBe(new Money(350m));
        o.UnitLabelAr.ShouldBe("مقعد");
        o.UnitLabelEn.ShouldBe("seat");
        o.TargetUnits.ShouldBe(100);
        o.BeneficiaryGroup.ShouldBe("rural_teachers");
        o.GroupLabelAr.ShouldBe("معلمو القرى");
        o.GroupLabelEn.ShouldBe("Rural teachers");
        o.AllowPartial.ShouldBeFalse();
        o.CloseOnTarget.ShouldBeFalse();
        o.StartsAt.ShouldBe(Start);
        o.EndsAt.ShouldBe(End);
        o.IsActive.ShouldBeTrue();
        o.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    [Rule("RUL-19")]
    public void Offers_are_not_zakat_eligible_in_S1()
    {
        Offer().ZakatEligible.ShouldBeFalse();
        new SponsorshipOffer(ProductId, new Money(350m), 10, "g", "ج", End, T0).ZakatEligible.ShouldBeFalse();
    }

    [Fact]
    public void The_constructor_uses_the_seat_defaults()
    {
        var o = new SponsorshipOffer(ProductId, new Money(350m), 10, "g", "ج", End, T0);

        o.UnitLabelAr.ShouldBe("مقعد");
        o.UnitLabelEn.ShouldBe("seat");
        o.AllowPartial.ShouldBeTrue();
        o.StartsAt.ShouldBe(T0);
        Should.Throw<DomainException>(() => new SponsorshipOffer(Guid.Empty, new Money(1m), 1, "g", "ج", End, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-06")]
    public void Offer_terms_are_validated()
    {
        Should.Throw<DomainException>(() => SponsorshipOffer.Create(ProductId, new Money(350m), "مقعد", "seat", 100, "g", "ج", null, true, false, End, Start, T0))
            .Code.ShouldBe("offer.dates_invalid");
        var o = Offer();
        Should.Throw<DomainException>(() => o.Update(new Money(350m), "مقعد", "seat", 100, "g", "ج", null, true, false, Start, Start)).Code.ShouldBe("offer.dates_invalid");
        Should.Throw<DomainException>(() => o.Update(Money.Zero, "مقعد", "seat", 100, "g", "ج", null, true, false, Start, End)).Code.ShouldBe("offer.seat_price_invalid");
        Should.Throw<DomainException>(() => o.Update(new Money(350m), "مقعد", "seat", 0, "g", "ج", null, true, false, Start, End)).Code.ShouldBe("offer.target_invalid");
        Should.Throw<DomainException>(() => o.Update(new Money(350m), " ", "seat", 1, "g", "ج", null, true, false, Start, End)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => o.Update(new Money(350m), "مقعد", "seat", 1, " ", "ج", null, true, false, Start, End)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-06")]
    public void A_seat_price_change_updates_the_offer()
    {
        var o = Offer();

        o.Update(new Money(300m), "نسخة", "copy", 250, "libraries", "المكتبات", null, false, true, Start.AddDays(1), End.AddDays(10));

        o.SeatPrice.ShouldBe(new Money(300m));
        o.UnitLabelAr.ShouldBe("نسخة");
        o.UnitLabelEn.ShouldBe("copy");
        o.TargetUnits.ShouldBe(250);
        o.BeneficiaryGroup.ShouldBe("libraries");
        o.GroupLabelAr.ShouldBe("المكتبات");
        o.GroupLabelEn.ShouldBeNull();
        o.AllowPartial.ShouldBeFalse();
        o.CloseOnTarget.ShouldBeTrue();
        o.StartsAt.ShouldBe(Start.AddDays(1));
        o.EndsAt.ShouldBe(End.AddDays(10));
    }

    [Fact]
    [Rule("RUL-09")]
    public void An_offer_is_open_between_its_start_and_its_end()
    {
        var o = Offer();

        o.IsOpen(Start.AddTicks(-1), 0).ShouldBeFalse();
        o.IsOpen(Start, 0).ShouldBeTrue();
        o.IsOpen(End.AddTicks(-1), 0).ShouldBeTrue();
        o.IsOpen(End, 0).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-10")]
    public void Reaching_the_target_closes_the_offer_only_with_close_on_target()
    {
        Offer().IsOpen(Start, 150).ShouldBeTrue();
        Offer(closeOnTarget: true).IsOpen(Start, 99).ShouldBeTrue();
        Offer(closeOnTarget: true).IsOpen(Start, 100).ShouldBeFalse();
    }

    [Fact]
    public void A_closed_offer_accepts_nothing()
    {
        var o = Offer();

        o.Close();

        o.IsActive.ShouldBeFalse();
        o.IsOpen(Start, 0).ShouldBeFalse();
    }
}
