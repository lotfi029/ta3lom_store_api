using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;
using static Ta3lom.Store.Domain.Tests.Carts.CartData;

namespace Ta3lom.Store.Domain.Tests.Carts;

/// The cart aggregate (RUL-11 … RUL-18, RUL-29 restore).
public sealed class CartTests
{
    private static readonly DateTimeOffset T1 = T0.AddHours(1);

    private static CartLine Add(Cart cart, CartLineSpec s, CartProduct p, CartLimits? limits = null, CustomerOwnership? owned = null)
    {
        var result = cart.AddLine(s, p, owned ?? CustomerOwnership.None, limits ?? Limits(), T0);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static Error Fails(Cart cart, CartLineSpec s, CartProduct p, CartLimits? limits = null, CustomerOwnership? owned = null)
    {
        var result = cart.AddLine(s, p, owned ?? CustomerOwnership.None, limits ?? Limits(), T1);
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    [Fact]
    [Rule("RUL-16")]
    public void A_guest_cart_is_active_and_expires_thirty_days_after_creation()
    {
        var cart = new Cart(T0);

        cart.Status.ShouldBe(CartStatus.Active);
        cart.IsGuest.ShouldBeTrue();
        cart.CustomerId.ShouldBeNull();
        cart.CreatedAt.ShouldBe(T0);
        cart.UpdatedAt.ShouldBe(T0);
        cart.ExpiresAt.ShouldBe(T0.AddDays(Cart.GuestLifetimeDays));
        cart.Lines.ShouldBeEmpty();
        cart.Total.ShouldBe(Money.Zero);
    }

    [Fact]
    [Rule("RUL-16")]
    public void A_customer_cart_never_expires()
    {
        var customerId = Guid.CreateVersion7();

        var cart = Cart.ForCustomer(customerId, T0);

        cart.CustomerId.ShouldBe(customerId);
        cart.IsGuest.ShouldBeFalse();
        cart.ExpiresAt.ShouldBeNull();
        cart.IsUsable(T0.AddYears(5)).ShouldBeTrue();
        Should.Throw<DomainException>(() => Cart.ForCustomer(Guid.Empty, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-16")]
    public void A_guest_cart_is_usable_until_its_expiry()
    {
        var cart = new Cart(T0);

        cart.IsUsable(T0.AddDays(30).AddTicks(-1)).ShouldBeTrue();
        cart.IsUsable(T0.AddDays(30)).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-16")]
    public void Every_change_pushes_the_guest_expiry_thirty_days_from_the_change()
    {
        var cart = new Cart(T0);
        var cause = Cause();

        var line = cart.AddLine(Donate(cause), cause, CustomerOwnership.None, Limits(), T1).Value;
        cart.ExpiresAt.ShouldBe(T1.AddDays(30));

        cart.RemoveLine(line.Id, T1.AddDays(3)).IsSuccess.ShouldBeTrue();
        cart.UpdatedAt.ShouldBe(T1.AddDays(3));
        cart.ExpiresAt.ShouldBe(T1.AddDays(33));
    }

    [Fact]
    public void A_converted_cart_is_no_longer_usable()
    {
        var cart = new Cart(T0);

        cart.MarkConverted(T1);

        cart.Status.ShouldBe(CartStatus.Converted);
        cart.UpdatedAt.ShouldBe(T1);
        cart.IsUsable(T1).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-02")]
    public void Line_rule_failures_are_returned_and_nothing_is_added()
    {
        var cart = new Cart(T0);
        var draft = Course(published: false);

        Fails(cart, Buy(draft), draft).Code.ShouldBe(ErrorCodes.CartProductUnavailable);
        cart.Lines.ShouldBeEmpty();
        cart.UpdatedAt.ShouldBe(T0);
    }

    [Fact]
    [Rule("RUL-13")]
    public void An_owned_course_cannot_be_bought_again()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var course = Course();

        var error = Fails(cart, Buy(course), course, owned: Owning(course.ProductId));

        error.Code.ShouldBe(ErrorCodes.CartCourseOwned);
        error.Status.ShouldBe(409);
    }

    [Fact]
    [Rule("RUL-13")]
    public void A_course_in_an_unpaid_order_cannot_be_bought_again()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var course = Course();

        var error = Fails(cart, Buy(course), course, owned: Unpaid(course.ProductId, "T3-26-000042"));

        error.Code.ShouldBe(ErrorCodes.CartCourseInUnpaidOrder);
        error.Extensions!["orderRef"].ShouldBe("T3-26-000042");
    }

    [Fact]
    [Rule("RUL-13")]
    public void A_course_already_in_the_cart_as_buy_is_a_duplicate()
    {
        var cart = new Cart(T0);
        var course = Course();
        Add(cart, Buy(course), course);

        Fails(cart, Buy(course), course).Code.ShouldBe(ErrorCodes.CartDuplicateCourse);
        cart.Lines.Count.ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-13")]
    public void Sponsoring_an_owned_course_is_always_allowed()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var course = Sponsorable();
        var owned = Owning(course.ProductId);

        Add(cart, Sponsor(course, 2), course, owned: owned);
        Add(cart, SponsorAmount(course, 50m), course, owned: owned);

        cart.Lines.Count.ShouldBe(2);
    }

    [Fact]
    [Rule("RUL-12")]
    public void A_course_line_is_one_seat_at_the_course_price()
    {
        var cart = new Cart(T0);
        var course = Course(price: 350m);

        var line = Add(cart, Buy(course, 3), course);

        line.Quantity.ShouldBe(1);
        line.UnitPrice.ShouldBe(new Money(350m));
        line.Amount.ShouldBe(new Money(350m));
        line.Kind.ShouldBe(LineKind.Buy);
        line.Mode.ShouldBe(LineMode.Units);
        line.IsGift.ShouldBeFalse();
        line.Intent.ShouldBeNull();
        line.CartId.ShouldBe(cart.Id);
        line.ProductId.ShouldBe(course.ProductId);
        line.AddedAt.ShouldBe(T0);
    }

    [Fact]
    [Rule("RUL-05")]
    public void A_book_line_keeps_its_format_and_prices_the_quantity()
    {
        var cart = new Cart(T0);
        var print = Book(Print(available: 10, price: 80m));

        var line = Add(cart, Buy(print, 3), print);

        line.BookFormatId.ShouldBe(print.Format!.Id);
        line.Quantity.ShouldBe(3);
        line.Amount.ShouldBe(new Money(240m));
        cart.Total.ShouldBe(new Money(240m));
    }

    [Fact]
    [Rule("RUL-19")]
    public void A_gift_line_defaults_to_sadaqa_and_keeps_its_dedication()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        var dedication = Dedication();

        var line = Add(cart, Donate(cause, 2, dedication: dedication), cause);

        line.IsGift.ShouldBeTrue();
        line.Intent.ShouldBe(GiftIntent.Sadaqa);
        line.Dedication.ShouldBeSameAs(dedication);
        line.BookFormatId.ShouldBeNull();
        line.Amount.ShouldBe(new Money(3_000m));
    }

    [Fact]
    [Rule("RUL-12")]
    public void An_amount_line_is_worth_its_amount()
    {
        var cart = new Cart(T0);
        var sponsorable = Sponsorable();

        var line = Add(cart, SponsorAmount(sponsorable, 120m), sponsorable);

        line.Mode.ShouldBe(LineMode.Amount);
        line.Quantity.ShouldBe(1);
        line.UnitPrice.ShouldBe(new Money(120m));
        line.Amount.ShouldBe(new Money(120m));
    }

    [Fact]
    [Rule("RUL-14")]
    public void An_identical_units_line_increases_the_quantity()
    {
        var cart = new Cart(T0);
        var sponsorable = Sponsorable();
        var first = Add(cart, Sponsor(sponsorable, 2), sponsorable);

        var result = cart.AddLine(Sponsor(sponsorable, 3), sponsorable, CustomerOwnership.None, Limits(), T1);

        result.Value.ShouldBeSameAs(first);
        cart.Lines.Count.ShouldBe(1);
        first.Quantity.ShouldBe(5);
        first.Amount.ShouldBe(Seat * 5);
        cart.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    [Rule("RUL-14")]
    public void Amount_lines_dedicated_lines_and_other_intents_never_merge()
    {
        var cart = new Cart(T0);
        var zakatOffer = Sponsorable(Offer(zakat: true));

        Add(cart, SponsorAmount(zakatOffer, 50m), zakatOffer);
        Add(cart, SponsorAmount(zakatOffer, 50m), zakatOffer);
        Add(cart, Sponsor(zakatOffer, 1, dedication: Dedication()), zakatOffer);
        Add(cart, Sponsor(zakatOffer, 1, dedication: Dedication()), zakatOffer);
        Add(cart, Sponsor(zakatOffer, 1), zakatOffer);
        Add(cart, Sponsor(zakatOffer, 1, GiftIntent.Zakat), zakatOffer);

        cart.Lines.Count.ShouldBe(6);
    }

    [Fact]
    [Rule("RUL-14")]
    public void Explicit_sadaqa_merges_with_the_default_intent()
    {
        var cart = new Cart(T0);
        var cause = Cause();

        Add(cart, Donate(cause, 1), cause);
        Add(cart, Donate(cause, 1, GiftIntent.Sadaqa), cause);

        cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    [Rule("RUL-12")]
    public void A_merge_above_the_line_maximum_is_refused()
    {
        var cart = new Cart(T0);
        var sponsorable = Sponsorable();
        Add(cart, Sponsor(sponsorable, 600), sponsorable, Limits(1_000_000m));

        var error = Fails(cart, Sponsor(sponsorable, 400), sponsorable, Limits(1_000_000m));

        error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        error.Extensions!["max"].ShouldBe(999);
        cart.Lines.Single().Quantity.ShouldBe(600);
    }

    [Fact]
    [Rule("RUL-12")]
    public void A_digital_book_added_twice_stays_at_one()
    {
        var cart = new Cart(T0);
        var digital = Book(Digital());
        Add(cart, Buy(digital), digital);

        Fails(cart, Buy(digital), digital).Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        cart.Lines.Single().Quantity.ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-30")]
    public void A_merge_above_the_available_print_stock_is_refused()
    {
        var cart = new Cart(T0);
        var print = Book(Print(available: 5));
        Add(cart, Buy(print, 3), print);

        var error = Fails(cart, Buy(print, 3), print);

        error.Code.ShouldBe(ErrorCodes.CartOutOfStock);
        error.Extensions!["available"].ShouldBe(5);
    }

    [Fact]
    [Rule("RUL-18")]
    public void A_merge_above_the_cart_total_is_refused()
    {
        var cart = new Cart(T0);
        var sponsorable = Sponsorable();
        Add(cart, Sponsor(sponsorable, 2), sponsorable, Limits(1_000m));

        var error = Fails(cart, Sponsor(sponsorable, 1), sponsorable, Limits(1_000m));

        error.Code.ShouldBe(ErrorCodes.CartTotalExceeded);
        error.Extensions!["max"].ShouldBe("1000.00");
        cart.Total.ShouldBe(new Money(700m));
    }

    [Fact]
    [Rule("RUL-18")]
    public void A_new_line_above_the_cart_total_is_refused()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        Add(cart, DonateAmount(cause, 90_000m), cause);

        Fails(cart, DonateAmount(cause, 10_000.01m), cause).Code.ShouldBe(ErrorCodes.CartTotalExceeded);
        Add(cart, DonateAmount(cause, 10_000m), cause);
        cart.Total.ShouldBe(new Money(100_000m));
    }

    [Fact]
    [Rule("RUL-18")]
    public void A_cart_holds_at_most_fifty_lines()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        for (var i = 0; i < CartLimits.MaxLines; i++)
        {
            Add(cart, DonateAmount(cause, 10m), cause);
        }

        Fails(cart, DonateAmount(cause, 10m), cause).Code.ShouldBe(ErrorCodes.CartTooManyLines);
        cart.Lines.Count.ShouldBe(50);
    }

    [Fact]
    public void Update_quantity_changes_a_units_line()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        var line = Add(cart, Donate(cause, 1), cause);

        cart.UpdateQuantity(line.Id, 4, cause, Limits(), T1).IsSuccess.ShouldBeTrue();

        line.Quantity.ShouldBe(4);
        line.Amount.ShouldBe(new Money(6_000m));
        cart.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    public void Update_quantity_of_an_unknown_line_is_not_found()
    {
        var cart = new Cart(T0);

        var result = cart.UpdateQuantity(Guid.CreateVersion7(), 1, Cause(), Limits(), T1);

        result.Error.Code.ShouldBe(ErrorCodes.CartLineNotFound);
        result.Error.Status.ShouldBe(404);
    }

    [Fact]
    [Rule("RUL-12")]
    public void Update_quantity_is_refused_on_amount_lines()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        var line = Add(cart, DonateAmount(cause, 50m), cause);

        cart.UpdateQuantity(line.Id, 2, cause, Limits(), T1).Error.Code.ShouldBe(ErrorCodes.CartModeNotAllowed);
    }

    [Theory]
    [Rule("RUL-12")]
    [InlineData(0)]
    [InlineData(21)]
    public void Update_quantity_stays_within_one_and_the_line_maximum(int quantity)
    {
        var cart = new Cart(T0);
        var print = Book(Print(available: 100));
        var line = Add(cart, Buy(print, 2), print);

        var error = cart.UpdateQuantity(line.Id, quantity, print, Limits(), T1).Error;

        error.Code.ShouldBe(ErrorCodes.CartQuantityLimit);
        error.Extensions!["max"].ShouldBe(20);
        line.Quantity.ShouldBe(2);
    }

    [Fact]
    [Rule("RUL-30")]
    public void Update_quantity_above_the_print_stock_is_out_of_stock()
    {
        var cart = new Cart(T0);
        var print = Book(Print(available: 5));
        var line = Add(cart, Buy(print, 2), print);

        cart.UpdateQuantity(line.Id, 6, print, Limits(), T1).Error.Code.ShouldBe(ErrorCodes.CartOutOfStock);
        cart.UpdateQuantity(line.Id, 5, print, Limits(), T1).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Rule("RUL-18")]
    public void Update_quantity_above_the_cart_total_is_refused()
    {
        var cart = new Cart(T0);
        var sponsorable = Sponsorable();
        var line = Add(cart, Sponsor(sponsorable, 1), sponsorable, Limits(1_000m));

        cart.UpdateQuantity(line.Id, 3, sponsorable, Limits(1_000m), T1).Error.Code.ShouldBe(ErrorCodes.CartTotalExceeded);
        cart.UpdateQuantity(line.Id, 2, sponsorable, Limits(1_000m), T1).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Remove_line_and_clear()
    {
        var cart = new Cart(T0);
        var cause = Cause();
        var line = Add(cart, Donate(cause), cause);
        Add(cart, DonateAmount(cause, 20m), cause);

        cart.RemoveLine(Guid.CreateVersion7(), T1).Error.Code.ShouldBe(ErrorCodes.CartLineNotFound);
        cart.RemoveLine(line.Id, T1).IsSuccess.ShouldBeTrue();
        cart.Lines.Count.ShouldBe(1);

        cart.Clear(T1.AddMinutes(1));
        cart.Lines.ShouldBeEmpty();
        cart.UpdatedAt.ShouldBe(T1.AddMinutes(1));
    }

    [Fact]
    [Rule("RUL-17")]
    public void Assigning_a_guest_cart_drops_owned_courses_with_a_notice_and_stops_expiry()
    {
        var cart = new Cart(T0);
        var owned = Course();
        var other = Course();
        var cause = Cause();
        Add(cart, Buy(owned), owned);
        Add(cart, Buy(other), other);
        Add(cart, Donate(cause), cause);
        var customerId = Guid.CreateVersion7();

        var notices = cart.AssignTo(customerId, Owning(owned.ProductId), T1);

        notices.ShouldBe([new CartMergeResult(CartNotices.DroppedOwnedCourse, owned.ProductId)]);
        cart.CustomerId.ShouldBe(customerId);
        cart.ExpiresAt.ShouldBeNull();
        cart.UpdatedAt.ShouldBe(T1);
        cart.Lines.Select(l => l.ProductId).ShouldBe([other.ProductId, cause.ProductId]);
        Should.Throw<DomainException>(() => cart.AssignTo(Guid.Empty, CustomerOwnership.None, T1));
    }

    [Fact]
    [Rule("RUL-17")]
    public void Merging_moves_guest_lines_into_the_customer_cart()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var cause = Cause();
        var dedication = Dedication();
        var moved = Add(guest, Donate(cause, 2, dedication: dedication), cause);

        var notices = guest.MergeInto(target, CustomerOwnership.None, l => 999, Limits(), T1);

        notices.ShouldBeEmpty();
        guest.Status.ShouldBe(CartStatus.Merged);
        guest.Lines.ShouldBeEmpty();
        guest.UpdatedAt.ShouldBe(T1);
        var line = target.Lines.ShouldHaveSingleItem();
        line.Id.ShouldNotBe(moved.Id);
        line.CartId.ShouldBe(target.Id);
        line.Quantity.ShouldBe(2);
        line.Amount.ShouldBe(new Money(3_000m));
        line.Intent.ShouldBe(GiftIntent.Sadaqa);
        line.AddedAt.ShouldBe(T1);
        line.Dedication.ShouldNotBeSameAs(dedication);
        line.Dedication!.ToName.ShouldBe(dedication.ToName);
        line.Dedication.ToMobile.ShouldBe(dedication.ToMobile);
        line.Dedication.FromName.ShouldBe(dedication.FromName);
        line.Dedication.TemplateKey.ShouldBe(dedication.TemplateKey);
        line.Dedication.Text.ShouldBe(dedication.Text);
        target.UpdatedAt.ShouldBe(T1);
        target.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-17")]
    public void Merging_drops_owned_courses_with_a_notice()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var course = Course();
        Add(guest, Buy(course), course);

        var notices = guest.MergeInto(target, Owning(course.ProductId), l => 1, Limits(), T1);

        notices.ShouldBe([new CartMergeResult(CartNotices.DroppedOwnedCourse, course.ProductId)]);
        target.Lines.ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-14")]
    public void Merging_a_course_already_in_the_customer_cart_keeps_one_line()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var course = Course();
        Add(guest, Buy(course), course);
        Add(target, Buy(course), course);

        var notices = guest.MergeInto(target, CustomerOwnership.None, l => 1, Limits(), T1);

        notices.ShouldBe([new CartMergeResult(CartNotices.Combined, course.ProductId)]);
        target.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-14")]
    public void Merging_identical_units_lines_adds_quantities_up_to_the_line_maximum()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var print = Book(Print(available: 50));
        var sponsorable = Sponsorable();
        Add(guest, Buy(print, 15), print);
        Add(target, Buy(print, 10), print);
        Add(guest, Sponsor(sponsorable, 2), sponsorable);
        Add(target, Sponsor(sponsorable, 3), sponsorable);

        var notices = guest.MergeInto(target, CustomerOwnership.None, l => l.Kind == LineKind.Buy ? 20 : 999, Limits(), T1);

        notices.ShouldBe([new CartMergeResult(CartNotices.Combined, print.ProductId), new CartMergeResult(CartNotices.Combined, sponsorable.ProductId)]);
        target.Lines.Count.ShouldBe(2);
        target.Lines[0].Quantity.ShouldBe(20);
        target.Lines[0].Amount.ShouldBe(new Money(1_600m));
        target.Lines[1].Quantity.ShouldBe(5);
    }

    [Fact]
    [Rule("RUL-18")]
    public void Merging_leaves_behind_lines_that_would_break_the_cart_total()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var sponsorable = Sponsorable();
        var cause = Cause();
        Add(target, Sponsor(sponsorable, 2), sponsorable, Limits(1_000m));
        Add(guest, Sponsor(sponsorable, 1), sponsorable, Limits(1_000m));
        Add(guest, DonateAmount(cause, 400m), cause, Limits(1_000m));

        var notices = guest.MergeInto(target, CustomerOwnership.None, l => 999, Limits(1_000m), T1);

        notices.ShouldBe([
            new CartMergeResult(ErrorCodes.CartTotalExceeded, sponsorable.ProductId),
            new CartMergeResult(ErrorCodes.CartTotalExceeded, cause.ProductId),
        ]);
        target.Total.ShouldBe(new Money(700m));
        guest.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    [Rule("RUL-18")]
    public void Merging_into_a_full_cart_leaves_the_line_behind()
    {
        var guest = new Cart(T0);
        var target = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var cause = Cause();
        for (var i = 0; i < CartLimits.MaxLines; i++)
        {
            Add(target, DonateAmount(cause, 10m), cause);
        }

        Add(guest, DonateAmount(cause, 10m), cause);

        var notices = guest.MergeInto(target, CustomerOwnership.None, l => 999, Limits(), T1);

        notices.ShouldBe([new CartMergeResult(ErrorCodes.CartTooManyLines, cause.ProductId)]);
        target.Lines.Count.ShouldBe(50);
    }

    [Fact]
    public void Merging_needs_a_target_and_a_quantity_rule()
    {
        var guest = new Cart(T0);

        Should.Throw<ArgumentNullException>(() => guest.MergeInto(null!, CustomerOwnership.None, l => 1, Limits(), T1));
        Should.Throw<ArgumentNullException>(() => guest.MergeInto(new Cart(T0), CustomerOwnership.None, null!, Limits(), T1));
    }

    private static IReadOnlyList<OrderLine> OrderLines(params OrderLineDraft[] drafts) =>
        Order.Place("T3-26-000001", Guid.CreateVersion7(), new OrderContact("نورة", "+966551234567", "n@example.com"), "ar", false,
            new OrderShipping(Money.Zero, Money.Zero, null), Guid.CreateVersion7(), drafts, T0, T0.AddHours(1)).Order.Lines;

    private static OrderLineDraft Draft(LineKind kind, LineMode mode, int quantity, decimal unit, decimal amount, GiftIntent? intent = null) =>
        new(Guid.CreateVersion7(), kind == LineKind.Donate ? ProductType.Cause : ProductType.Book, kind == LineKind.Buy ? Guid.CreateVersion7() : null,
            kind == LineKind.Sponsor ? Guid.CreateVersion7() : null, kind, mode, quantity, new Money(unit), new Money(amount), 0m, Money.Zero, intent,
            "عنوان", null, null);

    [Fact]
    [Rule("RUL-29")]
    public void An_expired_order_line_comes_back_to_the_cart()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var lines = OrderLines(
            Draft(LineKind.Buy, LineMode.Units, 2, 80m, 160m),
            Draft(LineKind.Sponsor, LineMode.Amount, 1, 75m, 75m, GiftIntent.Sadaqa),
            Draft(LineKind.Donate, LineMode.Units, 3, 100m, 300m, GiftIntent.Zakat));
        var dedication = Dedication();

        cart.RestoreLine(lines[0], dedication, T1).ShouldBeTrue();
        cart.RestoreLine(lines[1], null, T1).ShouldBeTrue();
        cart.RestoreLine(lines[2], dedication, T1).ShouldBeTrue();

        var buy = cart.Lines[0];
        buy.BookFormatId.ShouldBe(lines[0].BookFormatId);
        buy.Quantity.ShouldBe(2);
        buy.Amount.ShouldBe(new Money(160m));
        buy.Dedication.ShouldBeNull();
        cart.Lines[1].Amount.ShouldBe(new Money(75m));
        cart.Lines[1].Mode.ShouldBe(LineMode.Amount);
        cart.Lines[2].Intent.ShouldBe(GiftIntent.Zakat);
        cart.Lines[2].Dedication.ShouldBeSameAs(dedication);
        cart.Lines[2].Amount.ShouldBe(new Money(300m));
        cart.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    [Rule("RUL-29")]
    public void A_line_is_not_restored_when_the_product_is_already_in_the_cart_or_the_cart_is_full()
    {
        var cart = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var line = OrderLines(Draft(LineKind.Donate, LineMode.Units, 1, 100m, 100m))[0];

        cart.RestoreLine(line, null, T1).ShouldBeTrue();
        cart.RestoreLine(line, null, T1).ShouldBeFalse();

        var full = Cart.ForCustomer(Guid.CreateVersion7(), T0);
        var cause = Cause();
        for (var i = 0; i < CartLimits.MaxLines; i++)
        {
            Add(full, DonateAmount(cause, 10m), cause);
        }

        full.RestoreLine(line, null, T1).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => cart.RestoreLine(null!, null, T1));
    }

    [Fact]
    public void A_cart_line_needs_a_cart_and_a_product()
    {
        Should.Throw<DomainException>(() => new CartLine(Guid.Empty, Guid.CreateVersion7(), LineKind.Buy, LineMode.Units, Seat, Seat, T0));
        Should.Throw<DomainException>(() => new CartLine(Guid.CreateVersion7(), Guid.Empty, LineKind.Buy, LineMode.Units, Seat, Seat, T0));

        var line = new CartLine(Guid.CreateVersion7(), Guid.CreateVersion7(), LineKind.Donate, LineMode.Units, Seat, Seat, T0);
        line.Quantity.ShouldBe(1);
        line.IsGift.ShouldBeTrue();
    }

    [Fact]
    public void Ownership_answers_owned_and_unpaid_courses()
    {
        var course = Guid.CreateVersion7();

        CustomerOwnership.None.Owns(course).ShouldBeFalse();
        CustomerOwnership.None.UnpaidOrderFor(course).ShouldBeNull();
        Owning(course).Owns(course).ShouldBeTrue();
        Unpaid(course, "T3-26-000007").UnpaidOrderFor(course).ShouldBe("T3-26-000007");
    }
}
