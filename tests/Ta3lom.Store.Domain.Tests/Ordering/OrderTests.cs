using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Ordering;

/// The order aggregate (RUL-23, RUL-27 … RUL-31, RUL-33, RUL-35, RUL-40).
public sealed class OrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = T0.AddMinutes(60);
    private static readonly OrderContact Contact = new("نورة العتيبي", "+966551234567", "noura@example.com");
    private static readonly ShippingAddress Address = new("نورة العتيبي", "+966551234567", "الرياض", "العليا", "RAAA1234", "شارع التحلية", "1234", "12211");

    private static OrderLineDraft Buy(decimal amount, int quantity = 1, Guid? formatId = null, decimal vat = 0m, CartDedication? dedication = null) =>
        new(Guid.CreateVersion7(), formatId is null ? ProductType.Course : ProductType.Book, formatId, Guid.CreateVersion7(), LineKind.Buy, LineMode.Units,
            quantity, new Money(amount / quantity), new Money(amount), 15m, new Money(vat), GiftIntent.Sadaqa, "شراء", "Purchase", dedication);

    private static OrderLineDraft Gift(LineKind kind, decimal amount, GiftIntent intent = GiftIntent.Sadaqa, CartDedication? dedication = null) =>
        new(Guid.CreateVersion7(), kind == LineKind.Donate ? ProductType.Cause : ProductType.Course, null, Guid.CreateVersion7(), kind, LineMode.Amount,
            1, new Money(amount), new Money(amount), 15m, Money.Zero, intent, "هدية", null, dedication);

    private static (Order Order, IReadOnlyList<Dedication> Dedications) Place(IReadOnlyList<OrderLineDraft> lines, bool anonymous = false,
        OrderShipping? shipping = null) =>
        Order.Place("T3-26-000001", Guid.CreateVersion7(), Contact, "en", anonymous, shipping ?? new OrderShipping(Money.Zero, Money.Zero, null),
            Guid.CreateVersion7(), lines, T0, Due);

    private static Order PlaceSimple(decimal amount = 350m) => Place([Buy(amount)]).Order;

    [Theory]
    [Rule("RUL-28")]
    [InlineData(2026, 42, "T3-26-000042")]
    [InlineData(2030, 123456, "T3-30-123456")]
    [InlineData(2100, 7, "T3-00-000007")]
    public void Reference_is_T3_two_digit_year_and_six_digit_sequence(int year, long sequence, string expected) =>
        Order.FormatReference(year, sequence).ShouldBe(expected);

    [Fact]
    [Rule("RUL-31")]
    public void Grand_total_is_purchases_plus_shipping_plus_gifts()
    {
        var formatId = Guid.CreateVersion7();
        var shipping = new OrderShipping(new Money(25m), new Money(3.26m), Address);

        var (order, _) = Place([Buy(350m, vat: 45.65m), Buy(160m, 2, formatId, 20.87m), Gift(LineKind.Sponsor, 700m), Gift(LineKind.Donate, 100m)],
            shipping: shipping);

        order.PurchasesTotal.ShouldBe(new Money(510m));
        order.ShippingTotal.ShouldBe(new Money(25m));
        order.GiftsTotal.ShouldBe(new Money(800m));
        order.GrandTotal.ShouldBe(new Money(1_335m));
        order.RefundedTotal.ShouldBe(Money.Zero);
        order.ShippingAddress.ShouldBeSameAs(Address);
    }

    [Fact]
    [Rule("RUL-27")]
    public void Vat_total_is_the_included_vat_of_purchases_and_shipping()
    {
        var shipping = new OrderShipping(new Money(25m), new Money(3.26m), Address);

        var (order, _) = Place([Buy(350m, vat: 45.65m), Gift(LineKind.Donate, 100m)], shipping: shipping);

        order.VatTotal.ShouldBe(new Money(48.91m));
    }

    [Fact]
    [Rule("RUL-27")]
    public void Gift_lines_carry_no_vat_and_buy_lines_no_intent()
    {
        var (order, _) = Place([Buy(350m, vat: 45.65m), Gift(LineKind.Sponsor, 350m, GiftIntent.Zakat)]);

        var buy = order.Lines[0];
        buy.VatRate.ShouldBe(15m);
        buy.VatAmount.ShouldBe(new Money(45.65m));
        buy.Intent.ShouldBeNull();
        buy.SponsorshipOfferId.ShouldBeNull();
        var gift = order.Lines[1];
        gift.VatRate.ShouldBe(0m);
        gift.VatAmount.ShouldBe(Money.Zero);
        gift.Intent.ShouldBe(GiftIntent.Zakat);
        gift.SponsorshipOfferId.ShouldNotBeNull();
    }

    [Fact]
    public void Placing_snapshots_the_order_and_its_lines()
    {
        var customerId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var formatId = Guid.CreateVersion7();
        var draft = Buy(160m, 2, formatId);

        var (order, dedications) = Order.Place("T3-26-000009", customerId, Contact, "en", false,
            new OrderShipping(Money.Zero, Money.Zero, null), cartId, [draft], T0, Due);

        order.Reference.ShouldBe("T3-26-000009");
        order.CustomerId.ShouldBe(customerId);
        order.SourceCartId.ShouldBe(cartId);
        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.Currency.ShouldBe("SAR");
        order.Locale.ShouldBe("en");
        order.ContactName.ShouldBe(Contact.Name);
        order.ContactMobile.ShouldBe(Contact.MobileE164);
        order.ContactEmail.ShouldBe(Contact.Email);
        order.TermsAcceptedAt.ShouldBe(T0);
        order.PlacedAt.ShouldBe(T0);
        order.PaymentDueAt.ShouldBe(Due);
        order.PaidAt.ShouldBeNull();
        order.FulfilledAt.ShouldBeNull();
        order.CancelledAt.ShouldBeNull();
        order.ShippingAddress.ShouldBeNull();
        dedications.ShouldBeEmpty();
        order.DomainEvents.ShouldBe([new OrderPlaced(order.Id, "T3-26-000009")]);

        var line = order.Lines.ShouldHaveSingleItem();
        line.OrderId.ShouldBe(order.Id);
        line.LineNo.ShouldBe((short)1);
        line.ProductId.ShouldBe(draft.ProductId);
        line.ProductType.ShouldBe(ProductType.Book);
        line.BookFormatId.ShouldBe(formatId);
        line.Kind.ShouldBe(LineKind.Buy);
        line.Mode.ShouldBe(LineMode.Units);
        line.Quantity.ShouldBe(2);
        line.UnitPrice.ShouldBe(new Money(80m));
        line.Amount.ShouldBe(new Money(160m));
        line.TitleAr.ShouldBe("شراء");
        line.TitleEn.ShouldBe("Purchase");
        line.FulfilmentStatus.ShouldBe(FulfilmentStatus.Pending);
        line.RefundedAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Line_numbers_follow_the_draft_order()
    {
        var (order, _) = Place([Buy(10m), Gift(LineKind.Donate, 20m), Gift(LineKind.Sponsor, 30m)]);

        order.Lines.Select(l => l.LineNo).ShouldBe([(short)1, (short)2, (short)3]);
    }

    [Fact]
    [Rule("RUL-23")]
    public void Anonymity_applies_only_to_orders_with_gifts()
    {
        Place([Buy(350m)], anonymous: true).Order.IsAnonymous.ShouldBeFalse();
        Place([Buy(350m), Gift(LineKind.Donate, 50m)], anonymous: true).Order.IsAnonymous.ShouldBeTrue();
        Place([Gift(LineKind.Donate, 50m)], anonymous: false).Order.IsAnonymous.ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-21")]
    public void Each_dedicated_gift_line_gets_one_dedication_and_purchases_none()
    {
        var template = new CartDedication("سارة", "+966559876543", "  ", "eid", null);
        var freeText = new CartDedication("خالد", "+966551112223", "أحمد", null, "كل عام وأنت بخير");

        var (order, dedications) = Place([
            Buy(350m, dedication: template),
            Gift(LineKind.Sponsor, 350m, dedication: template),
            Gift(LineKind.Donate, 100m, dedication: freeText),
            Gift(LineKind.Donate, 100m),
        ]);

        dedications.Count.ShouldBe(2);
        var first = dedications[0];
        first.OrderLineId.ShouldBe(order.Lines[1].Id);
        first.ToName.ShouldBe("سارة");
        first.ToMobile.ShouldBe("+966559876543");
        first.FromName.ShouldBeNull();
        first.TemplateKey.ShouldBe("eid");
        first.CustomText.ShouldBeNull();
        first.SmsStatus.ShouldBe(DedicationSmsStatus.Pending);
        first.SentAt.ShouldBeNull();
        var second = dedications[1];
        second.OrderLineId.ShouldBe(order.Lines[2].Id);
        second.FromName.ShouldBe("أحمد");
        second.TemplateKey.ShouldBe(Dedication.CustomTemplateKey);
        second.CustomText.ShouldBe("كل عام وأنت بخير");
    }

    [Fact]
    [Rule("RUL-22")]
    public void Card_tokens_are_random_url_safe_and_22_characters()
    {
        var d = new CartDedication("سارة", "+966559876543", null, "eid", null);

        var (_, dedications) = Place([Gift(LineKind.Donate, 10m, dedication: d), Gift(LineKind.Donate, 10m, dedication: d)]);

        dedications.Select(x => x.CardToken).ShouldAllBe(t => t.Length == 22 && t.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'));
        dedications[0].CardToken.ShouldNotBe(dedications[1].CardToken);
    }

    [Fact]
    public void An_order_needs_lines_and_a_positive_total()
    {
        Should.Throw<DomainException>(() => Place([]));
        Should.Throw<DomainException>(() => Place([Buy(0m)]));
        Should.Throw<ArgumentNullException>(() => Order.Place("T3-26-000001", Guid.CreateVersion7(), null!, "ar", false,
            new OrderShipping(Money.Zero, Money.Zero, null), Guid.CreateVersion7(), [Buy(1m)], T0, Due));
        Should.Throw<ArgumentNullException>(() => Order.Place("T3-26-000001", Guid.CreateVersion7(), Contact, "ar", false,
            null!, Guid.CreateVersion7(), [Buy(1m)], T0, Due));
        Should.Throw<ArgumentNullException>(() => Order.Place("T3-26-000001", Guid.CreateVersion7(), Contact, "ar", false,
            new OrderShipping(Money.Zero, Money.Zero, null), Guid.CreateVersion7(), null!, T0, Due));
    }

    [Fact]
    public void The_constructor_starts_pending_payment_with_zero_subtotals()
    {
        var order = new Order("T3-26-000002", Guid.CreateVersion7(), new Money(100m), "نورة", "+966551234567", "n@example.com", T0, Due, T0);

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.GrandTotal.ShouldBe(new Money(100m));
        order.PurchasesTotal.ShouldBe(Money.Zero);
        order.GiftsTotal.ShouldBe(Money.Zero);
        order.VatTotal.ShouldBe(Money.Zero);
        order.IsAnonymous.ShouldBeFalse();
        order.Locale.ShouldBe("ar");
        order.Lines.ShouldBeEmpty();
        Should.Throw<DomainException>(() => new Order("T3", Guid.Empty, new Money(1m), "n", "m", "e", T0, Due, T0));
    }

    [Theory]
    [InlineData(OrderStatus.Paid, true)]
    [InlineData(OrderStatus.Fulfilled, true)]
    [InlineData(OrderStatus.PartiallyRefunded, true)]
    [InlineData(OrderStatus.Refunded, true)]
    [InlineData(OrderStatus.PendingPayment, false)]
    [InlineData(OrderStatus.PaymentFailed, false)]
    [InlineData(OrderStatus.PaymentReview, false)]
    [InlineData(OrderStatus.Expired, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void Paid_states(OrderStatus status, bool paid) => Order.IsPaidState(status).ShouldBe(paid);

    [Fact]
    [Rule("RUL-33")]
    public void A_capture_of_the_grand_total_marks_the_order_paid_once()
    {
        var order = PlaceSimple();

        order.MarkPaid(new Money(350m), T0.AddMinutes(5)).ShouldBe(PaidOutcome.Paid);
        order.Status.ShouldBe(OrderStatus.Paid);
        order.PaidAt.ShouldBe(T0.AddMinutes(5));

        order.MarkPaid(new Money(350m), T0.AddMinutes(9)).ShouldBe(PaidOutcome.AlreadyPaid);
        order.PaidAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    [Rule("RUL-35")]
    public void A_capture_of_another_amount_parks_the_order_in_review_with_a_finance_alert()
    {
        var order = PlaceSimple();
        order.ClearDomainEvents();

        order.MarkPaid(new Money(300m), T0.AddMinutes(5)).ShouldBe(PaidOutcome.Review);

        order.Status.ShouldBe(OrderStatus.PaymentReview);
        order.PaidAt.ShouldBeNull();
        order.DomainEvents.ShouldBe([new FinanceAlert(FinanceAlertKinds.AmountMismatch, order.Id, order.Reference, "captured 300.00, expected 350.00")]);
    }

    [Fact]
    [Rule("RUL-35")]
    public void A_capture_on_an_expired_or_cancelled_order_goes_to_review()
    {
        var expired = PlaceSimple();
        expired.Expire(Due, cartRestored: true).ShouldBeTrue();
        expired.ClearDomainEvents();
        var cancelled = PlaceSimple();
        cancelled.Cancel("customer asked", T0).ShouldBeTrue();

        expired.MarkPaid(new Money(350m), Due.AddMinutes(1)).ShouldBe(PaidOutcome.Review);
        cancelled.MarkPaid(new Money(350m), Due.AddMinutes(1)).ShouldBe(PaidOutcome.Review);

        expired.Status.ShouldBe(OrderStatus.PaymentReview);
        cancelled.Status.ShouldBe(OrderStatus.PaymentReview);
        expired.DomainEvents.ShouldBe([new FinanceAlert(FinanceAlertKinds.LateCapture, expired.Id, expired.Reference, "350.00")]);
    }

    [Fact]
    [Rule("RUL-35")]
    public void A_further_capture_on_an_order_in_review_changes_nothing()
    {
        var order = PlaceSimple();
        order.MarkPaid(new Money(1m), T0).ShouldBe(PaidOutcome.Review);
        order.ClearDomainEvents();

        order.MarkPaid(new Money(350m), T0.AddMinutes(1)).ShouldBe(PaidOutcome.Review);

        order.Status.ShouldBe(OrderStatus.PaymentReview);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-40")]
    public void Settlement_fulfils_the_order_when_every_line_is_terminal_and_queues_the_messages()
    {
        var (order, _) = Place([Buy(350m), Gift(LineKind.Donate, 100m)]);
        order.MarkPaid(new Money(450m), T0).ShouldBe(PaidOutcome.Paid);
        order.ClearDomainEvents();
        order.Lines[0].SetFulfilment(FulfilmentStatus.Enrolled);
        order.Lines[1].SetFulfilment(FulfilmentStatus.Received);

        order.CompleteSettlement(T0.AddSeconds(1));

        order.Status.ShouldBe(OrderStatus.Fulfilled);
        order.FulfilledAt.ShouldBe(T0.AddSeconds(1));
        order.DomainEvents.ShouldBe([new OrderPaid(order.Id, order.Reference), new RenderDocuments(order.Id)]);
    }

    [Fact]
    [Rule("RUL-40")]
    public void A_print_line_on_its_way_keeps_the_order_paid()
    {
        var (order, _) = Place([Buy(160m, 2, Guid.CreateVersion7()), Gift(LineKind.Donate, 100m)]);
        order.MarkPaid(new Money(260m), T0);
        order.Lines[0].SetFulfilment(FulfilmentStatus.Preparing);
        order.Lines[1].SetFulfilment(FulfilmentStatus.Received);

        order.CompleteSettlement(T0);
        order.Status.ShouldBe(OrderStatus.Paid);

        order.Lines[0].SetFulfilment(FulfilmentStatus.Shipped);
        order.RecomputeFulfilment(T0.AddDays(1));
        order.Status.ShouldBe(OrderStatus.Paid);

        order.Lines[0].SetFulfilment(FulfilmentStatus.Delivered);
        order.RecomputeFulfilment(T0.AddDays(2));
        order.Status.ShouldBe(OrderStatus.Fulfilled);
        order.FulfilledAt.ShouldBe(T0.AddDays(2));
    }

    [Fact]
    [Rule("RUL-40")]
    public void An_unpaid_order_is_never_fulfilled()
    {
        var order = PlaceSimple();
        order.Lines[0].SetFulfilment(FulfilmentStatus.Enrolled);

        order.RecomputeFulfilment(T0);

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.FulfilledAt.ShouldBeNull();
    }

    [Theory]
    [Rule("RUL-40")]
    [InlineData(FulfilmentStatus.Enrolled, true)]
    [InlineData(FulfilmentStatus.Available, true)]
    [InlineData(FulfilmentStatus.Delivered, true)]
    [InlineData(FulfilmentStatus.Funded, true)]
    [InlineData(FulfilmentStatus.Received, true)]
    [InlineData(FulfilmentStatus.Refunded, true)]
    [InlineData(FulfilmentStatus.Pending, false)]
    [InlineData(FulfilmentStatus.PendingSync, false)]
    [InlineData(FulfilmentStatus.Preparing, false)]
    [InlineData(FulfilmentStatus.Shipped, false)]
    [InlineData(FulfilmentStatus.Returned, false)]
    public void Terminal_line_states(FulfilmentStatus status, bool terminal)
    {
        var line = PlaceSimple().Lines[0];

        line.SetFulfilment(status);

        line.FulfilmentStatus.ShouldBe(status);
        line.IsTerminal.ShouldBe(terminal);
    }

    [Fact]
    [Rule("RUL-29")]
    public void Staff_cancel_only_from_pending_payment()
    {
        var order = PlaceSimple();
        order.ClearDomainEvents();
        order.CanBeCancelled.ShouldBeTrue();

        order.Cancel("duplicate order", T0.AddMinutes(3)).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledAt.ShouldBe(T0.AddMinutes(3));
        order.DomainEvents.ShouldBe([new OrderCancelled(order.Id, order.Reference, "duplicate order")]);
        order.CanBeCancelled.ShouldBeFalse();
        order.Cancel("again", T0.AddMinutes(4)).ShouldBeFalse();
        order.CancelledAt.ShouldBe(T0.AddMinutes(3));
    }

    [Fact]
    [Rule("RUL-29")]
    public void A_paid_order_cannot_be_cancelled_or_expire()
    {
        var order = PlaceSimple();
        order.MarkPaid(new Money(350m), T0);

        order.Cancel("too late", T0).ShouldBeFalse();
        order.Expire(Due.AddHours(1), cartRestored: false).ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    [Rule("RUL-29")]
    public void An_order_expires_only_once_the_payment_window_has_passed()
    {
        var order = PlaceSimple();
        order.ClearDomainEvents();

        order.Expire(Due.AddTicks(-1), cartRestored: true).ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.PendingPayment);

        order.Expire(Due, cartRestored: true).ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Expired);
        order.DomainEvents.ShouldBe([new OrderExpired(order.Id, order.Reference, true)]);
    }

    [Fact]
    [Rule("RUL-30")]
    public void Print_reservations_are_the_buy_lines_of_print_formats()
    {
        var print = Guid.CreateVersion7();
        var digital = Guid.CreateVersion7();
        var (order, _) = Place([Buy(160m, 2, print), Buy(40m, 1, digital), Buy(350m), Gift(LineKind.Donate, 10m)]);

        order.PrintReservations(new HashSet<Guid> { print }).ShouldBe([(print, 2)]);
        order.PrintReservations(new HashSet<Guid>()).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-61")]
    public void Anonymising_replaces_the_contact_and_recipient_and_keeps_the_city()
    {
        var (order, _) = Place([Buy(80m, 1, Guid.CreateVersion7())], shipping: new OrderShipping(new Money(25m), Money.Zero, Address));

        order.AnonymiseContact();

        order.ContactName.ShouldBe(Order.DeletedContact);
        order.ContactMobile.ShouldBe(Order.DeletedContact);
        order.ContactEmail.ShouldBe(Order.DeletedContact);
        var a = order.ShippingAddress.ShouldNotBeNull();
        a.RecipientName.ShouldBe(Order.DeletedContact);
        a.RecipientMobile.ShouldBe(Order.DeletedContact);
        a.City.ShouldBe("الرياض");
        a.District.ShouldBeNull();
        a.ShortAddress.ShouldBeNull();
        a.Street.ShouldBeNull();
        a.BuildingNo.ShouldBeNull();
        a.PostalCode.ShouldBeNull();
        order.GrandTotal.ShouldBe(new Money(105m));
    }

    [Fact]
    public void Anonymising_an_order_without_an_address_keeps_it_without()
    {
        var order = PlaceSimple();

        order.AnonymiseContact();

        order.ShippingAddress.ShouldBeNull();
        order.ContactName.ShouldBe(Order.DeletedContact);
    }

    [Fact]
    public void Settlement_effects_can_raise_events_on_the_order()
    {
        var order = PlaceSimple();
        order.ClearDomainEvents();
        var alert = new FinanceAlert(FinanceAlertKinds.DuplicateCourse, order.Id, order.Reference, null);

        order.RaiseEvent(alert);

        order.DomainEvents.ShouldBe([alert]);
    }
}
