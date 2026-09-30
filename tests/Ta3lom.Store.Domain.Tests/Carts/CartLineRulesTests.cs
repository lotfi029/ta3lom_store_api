using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.ValueObjects;
using static Ta3lom.Store.Domain.Tests.Carts.CartData;

namespace Ta3lom.Store.Domain.Tests.Carts;

/// The RUL-12 matrix, RUL-19 intent and RUL-21 dedication checks of `CartLineRules`.
public sealed class CartLineRulesTests
{
    private static string? Code(CartLineSpec s, CartProduct p, CartLimits? limits = null) => CartLineRules.Check(s, p, limits ?? Limits(), T0)?.Code;

    [Fact]
    [Rule("RUL-02")]
    public void Unpublished_products_accept_no_lines()
    {
        Code(Buy(Course(published: false)), Course(published: false)).ShouldBe(ErrorCodes.CartProductUnavailable);
        var cause = Cause(published: false);
        Code(Donate(cause), cause).ShouldBe(ErrorCodes.CartProductUnavailable);
    }

    [Fact]
    [Rule("RUL-12")]
    public void Max_quantity_is_one_for_courses_and_digital_twenty_for_print_and_999_for_gifts()
    {
        CartLineRules.MaxQuantity(LineKind.Buy, Course()).ShouldBe(1);
        CartLineRules.MaxQuantity(LineKind.Buy, Book(Digital())).ShouldBe(1);
        CartLineRules.MaxQuantity(LineKind.Buy, Book(Print())).ShouldBe(20);
        CartLineRules.MaxQuantity(LineKind.Sponsor, Sponsorable()).ShouldBe(999);
        CartLineRules.MaxQuantity(LineKind.Donate, Cause()).ShouldBe(CartLineRules.GiftMaxQuantity);
    }

    [Fact]
    [Rule("RUL-12")]
    public void Course_and_digital_quantities_are_forced_to_one_and_amount_lines_count_once()
    {
        var course = Course();
        var digital = Book(Digital());
        var print = Book(Print());
        var sponsorable = Sponsorable();

        CartLineRules.NormaliseQuantity(Buy(course, 3), course).ShouldBe(1);
        CartLineRules.NormaliseQuantity(Buy(digital, 4), digital).ShouldBe(1);
        CartLineRules.NormaliseQuantity(Buy(print, 7), print).ShouldBe(7);
        CartLineRules.NormaliseQuantity(Sponsor(sponsorable, 12), sponsorable).ShouldBe(12);
        CartLineRules.NormaliseQuantity(SponsorAmount(sponsorable, 50m) with { Quantity = 5 }, sponsorable).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-12")]
    public void A_course_with_quantity_three_is_allowed_as_one_seat()
    {
        var course = Course();

        Code(Buy(course, 3), course).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-12")]
    public void Buy_lines_are_units_only_without_intent_and_never_on_a_cause()
    {
        var course = Course();
        var cause = Cause();

        Code(Buy(course) with { Mode = LineMode.Amount, Amount = new Money(50m) }, course).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Buy(course) with { Intent = GiftIntent.Sadaqa }, course).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Buy(cause), cause).ShouldBe(ErrorCodes.CartModeNotAllowed);
    }

    [Fact]
    [Rule("RUL-21")]
    public void Purchases_cannot_be_dedicated()
    {
        var course = Course();

        var error = CartLineRules.Check(Buy(course) with { Dedication = Dedication() }, course, Limits(), T0);

        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.CartDedicationNotAllowed);
        error.Status.ShouldBe(422);
    }

    [Fact]
    public void A_course_without_a_price_is_unavailable()
    {
        var course = Course(price: null);

        Code(Buy(course), course).ShouldBe(ErrorCodes.CartProductUnavailable);
    }

    [Fact]
    [Rule("RUL-05")]
    public void A_book_needs_an_active_format_and_a_digital_format_needs_its_file()
    {
        var noFormat = Book(null);
        var inactive = Book(Print(active: false));
        var digitalWithoutFile = Book(Digital(hasFile: false));
        var digital = Book(Digital());

        Code(Buy(noFormat), noFormat).ShouldBe(ErrorCodes.CartProductUnavailable);
        Code(Buy(inactive), inactive).ShouldBe(ErrorCodes.CartProductUnavailable);
        Code(Buy(digitalWithoutFile), digitalWithoutFile).ShouldBe(ErrorCodes.CartProductUnavailable);
        Code(Buy(digital, 3), digital).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-12")]
    public void Print_quantity_is_capped_at_twenty()
    {
        var print = Book(Print(available: 100));

        Code(Buy(print, 20), print).ShouldBeNull();
        var error = CartLineRules.Check(Buy(print, 21), print, Limits(), T0);
        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        error.Extensions!["max"].ShouldBe(20);
    }

    [Fact]
    [Rule("RUL-30")]
    public void Print_quantity_above_the_available_stock_is_out_of_stock()
    {
        var print = Book(Print(available: 5));

        Code(Buy(print, 5), print).ShouldBeNull();
        var error = CartLineRules.Check(Buy(print, 6), print, Limits(), T0);
        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.CartOutOfStock);
        error.Extensions!["available"].ShouldBe(5);
    }

    [Fact]
    [Rule("RUL-12")]
    public void Sponsor_lines_need_a_course_or_book_with_an_offer_and_no_format()
    {
        var cause = Cause();
        var noOffer = Course();
        var sponsorable = Sponsorable();

        Code(Sponsor(cause), cause).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Sponsor(noOffer), noOffer).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Sponsor(sponsorable) with { BookFormatId = Guid.CreateVersion7() }, sponsorable).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Sponsor(sponsorable, 999), sponsorable).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-09")]
    public void A_closed_offer_or_cause_accepts_no_new_lines()
    {
        var offerClosed = Sponsorable(Offer(open: false));
        var causeClosed = Cause(CauseFacts(open: false));

        Code(Sponsor(offerClosed), offerClosed).ShouldBe(ErrorCodes.CartCampaignEnded);
        Code(Donate(causeClosed), causeClosed).ShouldBe(ErrorCodes.CartCampaignEnded);
    }

    [Fact]
    [Rule("RUL-12")]
    public void Gift_quantity_is_capped_at_999()
    {
        var sponsorable = Sponsorable();
        var cause = Cause();

        var error = CartLineRules.Check(Sponsor(sponsorable, 1_000), sponsorable, Limits(), T0);
        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        error.Extensions!["max"].ShouldBe(999);
        Code(Donate(cause, 1_000), cause).ShouldBe(ErrorCodes.CartQuantityLimit);
        Code(Donate(cause, 999), cause).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-12")]
    public void Sponsor_amount_needs_a_partial_offer_and_at_least_the_minimum_gift()
    {
        var partial = Sponsorable();
        var wholeSeatsOnly = Sponsorable(Offer(partial: false));

        Code(SponsorAmount(partial, 10m), partial).ShouldBeNull();
        Code(SponsorAmount(wholeSeatsOnly, 100m), wholeSeatsOnly).ShouldBe(ErrorCodes.CartModeNotAllowed);
        var below = CartLineRules.Check(SponsorAmount(partial, 9.99m), partial, Limits(), T0);
        below.ShouldNotBeNull();
        below.Code.ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        below.Extensions!["min"].ShouldBe("10.00");
        Code(SponsorAmount(partial, null), partial).ShouldBe(ErrorCodes.CartAmountBelowMinimum);
    }

    [Fact]
    [Rule("RUL-19")]
    public void Zakat_is_allowed_only_on_a_zakat_eligible_offer_or_cause()
    {
        var offer = Sponsorable();
        var zakatOffer = Sponsorable(Offer(zakat: true));
        var cause = Cause(CauseFacts(zakat: false));
        var zakatCause = Cause(CauseFacts(zakat: true));

        Code(Sponsor(offer, intent: GiftIntent.Zakat), offer).ShouldBe(ErrorCodes.CartZakatNotAllowed);
        Code(Sponsor(zakatOffer, intent: GiftIntent.Zakat), zakatOffer).ShouldBeNull();
        Code(Donate(cause, intent: GiftIntent.Zakat), cause).ShouldBe(ErrorCodes.CartZakatNotAllowed);
        Code(Donate(cause, intent: GiftIntent.Sadaqa), cause).ShouldBeNull();
        Code(DonateAmount(zakatCause, 50m, GiftIntent.Zakat), zakatCause).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-12")]
    public void Donate_lines_are_only_for_causes_without_a_format()
    {
        var course = Course();
        var causeWithoutFacts = Cause() with { Cause = null };
        var cause = Cause();

        Code(Donate(course), course).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Donate(causeWithoutFacts), causeWithoutFacts).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(Donate(cause) with { BookFormatId = Guid.CreateVersion7() }, cause).ShouldBe(ErrorCodes.CartModeNotAllowed);
    }

    [Fact]
    [Rule("RUL-07")]
    public void A_cause_without_a_unit_amount_accepts_amounts_only()
    {
        var generalFund = Cause(CauseFacts(unit: null));

        Code(Donate(generalFund), generalFund).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(DonateAmount(generalFund, 25m), generalFund).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-12")]
    public void Donate_amount_needs_custom_amounts_and_at_least_the_cause_minimum()
    {
        var unitsOnly = Cause(CauseFacts(custom: false));
        var cause = Cause(CauseFacts(min: 50m));

        Code(DonateAmount(unitsOnly, 100m), unitsOnly).ShouldBe(ErrorCodes.CartModeNotAllowed);
        Code(DonateAmount(cause, 50m), cause).ShouldBeNull();
        var below = CartLineRules.Check(DonateAmount(cause, 49.99m), cause, Limits(), T0);
        below.ShouldNotBeNull();
        below.Code.ShouldBe(ErrorCodes.CartAmountBelowMinimum);
        below.Extensions!["min"].ShouldBe("50.00");
        Code(DonateAmount(cause, null), cause).ShouldBe(ErrorCodes.CartAmountBelowMinimum);
    }

    [Fact]
    public void An_unknown_line_kind_is_not_allowed()
    {
        var cause = Cause();

        Code(Donate(cause) with { Kind = (LineKind)42 }, cause).ShouldBe(ErrorCodes.CartModeNotAllowed);
    }

    [Theory]
    [Rule("RUL-21")]
    [InlineData("ن", "toName")]
    [InlineData("  ن  ", "toName")]
    public void Recipient_name_is_two_to_sixty_characters(string toName, string field)
    {
        var cause = Cause();

        var error = CartLineRules.Check(Donate(cause, dedication: Dedication(toName: toName)), cause, Limits(), T0);

        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.CartDedicationInvalid);
        error.Status.ShouldBe(400);
        error.Extensions!["field"].ShouldBe(field);
    }

    [Fact]
    [Rule("RUL-21")]
    public void Dedication_field_limits()
    {
        var cause = Cause();
        string? Field(CartDedication d, bool freeText = false) =>
            CartLineRules.Check(Donate(cause, dedication: d), cause, Limits(freeText: freeText), T0)?.Extensions?["field"] as string;

        Field(Dedication(toName: new string('ن', 60))).ShouldBeNull();
        Field(Dedication(toName: new string('ن', 61))).ShouldBe("toName");
        Field(Dedication(toMobile: "0412345678")).ShouldBe("toMobile");
        Field(Dedication(fromName: new string('أ', 61))).ShouldBe("fromName");
        Field(Dedication(fromName: null)).ShouldBeNull();
        Field(Dedication(templateKey: null)).ShouldBe("templateKey");
        Field(Dedication(templateKey: "birthday")).ShouldBe("templateKey");
        Field(Dedication(templateKey: null, text: new string('x', 141)), freeText: true).ShouldBe("text");
    }

    [Fact]
    [Rule("RUL-21")]
    public void Recipient_mobile_accepts_the_local_format()
    {
        var cause = Cause();

        Code(Donate(cause, dedication: Dedication(toMobile: "0551234567")), cause).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-21")]
    public void Free_text_up_to_140_characters_only_when_the_admin_enables_it()
    {
        var cause = Cause();
        var text = Dedication(templateKey: null, text: new string('x', 140));

        Code(Donate(cause, dedication: text), cause, Limits(freeText: false)).ShouldBe(ErrorCodes.CartDedicationFreeTextDisabled);
        Code(Donate(cause, dedication: text), cause, Limits(freeText: true)).ShouldBeNull();
        Code(Donate(cause, dedication: Dedication(text: "   ")), cause, Limits(freeText: false)).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-15")]
    public void Unit_price_is_the_catalogue_price_for_units_and_the_amount_for_amount_lines()
    {
        var course = Course();
        var print = Book(Print(price: 80m));
        var sponsorable = Sponsorable();
        var cause = Cause();

        CartLineRules.UnitPrice(Buy(course), course).ShouldBe(new Money(350m));
        CartLineRules.UnitPrice(Buy(print), print).ShouldBe(new Money(80m));
        CartLineRules.UnitPrice(Sponsor(sponsorable), sponsorable).ShouldBe(Seat);
        CartLineRules.UnitPrice(Donate(cause), cause).ShouldBe(new Money(1_500m));
        CartLineRules.UnitPrice(SponsorAmount(sponsorable, 75m), sponsorable).ShouldBe(new Money(75m));
        CartLineRules.UnitPrice(SponsorAmount(sponsorable, null), sponsorable).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Unit_price_falls_back_to_zero_when_the_catalogue_has_no_price()
    {
        var course = Course(price: null);
        var book = Book(null);
        var noOffer = Course();
        var noCause = Cause() with { Cause = null };

        CartLineRules.UnitPrice(Buy(course), course).ShouldBe(Money.Zero);
        CartLineRules.UnitPrice(Buy(book), book).ShouldBe(Money.Zero);
        CartLineRules.UnitPrice(Sponsor(noOffer), noOffer).ShouldBe(Money.Zero);
        CartLineRules.UnitPrice(Donate(noCause), noCause).ShouldBe(Money.Zero);
    }

    private static CartLine LineFor(CartLineSpec s, CartProduct p)
    {
        var cart = new Cart(T0);
        return cart.AddLine(s, p, CustomerOwnership.None, Limits(), T0).Value;
    }

    [Fact]
    [Rule("RUL-24")]
    public void Issues_flag_a_missing_or_unpublished_product()
    {
        var course = Course();
        var line = LineFor(Buy(course), course);

        CartLineRules.Issues(line, null, CustomerOwnership.None, T0).ShouldBe([ErrorCodes.CartProductUnavailable]);
        CartLineRules.Issues(line, course with { IsPublished = false }, CustomerOwnership.None, T0).ShouldBe([ErrorCodes.CartProductUnavailable]);
        CartLineRules.Issues(line, course, CustomerOwnership.None, T0).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-13")]
    public void Issues_flag_an_owned_course_or_one_in_an_unpaid_order()
    {
        var course = Course();
        var line = LineFor(Buy(course), course);

        CartLineRules.Issues(line, course, Owning(course.ProductId), T0).ShouldBe([ErrorCodes.CartCourseOwned]);
        CartLineRules.Issues(line, course, Unpaid(course.ProductId, "T3-26-000001"), T0).ShouldBe([ErrorCodes.CartCourseInUnpaidOrder]);
    }

    [Fact]
    [Rule("RUL-24")]
    public void Issues_flag_an_inactive_format_and_missing_stock()
    {
        var print = Book(Print(available: 5));
        var line = LineFor(Buy(print, 4), print);

        CartLineRules.Issues(line, print, CustomerOwnership.None, T0).ShouldBeEmpty();
        CartLineRules.Issues(line, print with { Format = print.Format! with { IsActive = false } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartProductUnavailable]);
        CartLineRules.Issues(line, print with { Format = null }, CustomerOwnership.None, T0).ShouldBe([ErrorCodes.CartProductUnavailable]);
        CartLineRules.Issues(line, print with { Format = print.Format! with { Available = 3 } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartOutOfStock]);
    }

    [Fact]
    public void Issues_ignore_stock_for_a_digital_format()
    {
        var digital = Book(Digital());
        var line = LineFor(Buy(digital), digital);

        CartLineRules.Issues(line, digital, CustomerOwnership.None, T0).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-09")]
    public void Issues_flag_a_campaign_that_ended_after_the_line_was_added()
    {
        var sponsorable = Sponsorable();
        var cause = Cause();
        var sponsorLine = LineFor(Sponsor(sponsorable), sponsorable);
        var donateLine = LineFor(Donate(cause), cause);

        CartLineRules.Issues(sponsorLine, sponsorable, CustomerOwnership.None, T0).ShouldBeEmpty();
        CartLineRules.Issues(sponsorLine, sponsorable with { Offer = sponsorable.Offer! with { IsOpen = false } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartCampaignEnded]);
        CartLineRules.Issues(sponsorLine, sponsorable with { Offer = null }, CustomerOwnership.None, T0).ShouldBe([ErrorCodes.CartCampaignEnded]);
        CartLineRules.Issues(donateLine, cause, CustomerOwnership.None, T0).ShouldBeEmpty();
        CartLineRules.Issues(donateLine, cause with { Cause = cause.Cause! with { IsOpen = false } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartCampaignEnded]);
    }

    [Fact]
    [Rule("RUL-19")]
    public void Issues_flag_zakat_when_the_product_is_no_longer_zakat_eligible()
    {
        var zakatOffer = Sponsorable(Offer(zakat: true));
        var zakatCause = Cause(CauseFacts(zakat: true));
        var sponsorLine = LineFor(Sponsor(zakatOffer, intent: GiftIntent.Zakat), zakatOffer);
        var donateLine = LineFor(Donate(zakatCause, intent: GiftIntent.Zakat), zakatCause);

        CartLineRules.Issues(sponsorLine, zakatOffer, CustomerOwnership.None, T0).ShouldBeEmpty();
        CartLineRules.Issues(donateLine, zakatCause, CustomerOwnership.None, T0).ShouldBeEmpty();
        CartLineRules.Issues(sponsorLine, zakatOffer with { Offer = zakatOffer.Offer! with { ZakatEligible = false } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartZakatNotAllowed]);
        CartLineRules.Issues(donateLine, zakatCause with { Cause = zakatCause.Cause! with { ZakatEligible = false } }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartZakatNotAllowed]);
        CartLineRules.Issues(sponsorLine, zakatOffer with { Offer = null }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartCampaignEnded, ErrorCodes.CartZakatNotAllowed]);
        CartLineRules.Issues(donateLine, zakatCause with { Cause = null }, CustomerOwnership.None, T0)
            .ShouldBe([ErrorCodes.CartCampaignEnded, ErrorCodes.CartZakatNotAllowed]);
    }
}
