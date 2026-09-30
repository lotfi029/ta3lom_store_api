using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Services;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

/// VAT (RUL-27), the ZATCA Phase-1 QR, the public donor name (RUL-23) and the enum wire values.
public sealed class DomainServiceTests
{
    [Theory]
    [Rule("RUL-27")]
    [InlineData("350.00", "15", "45.65")]
    [InlineData("25.00", "15", "3.26")]
    [InlineData("100.00", "5", "4.76")]
    [InlineData("350.00", "0", "0.00")]
    [InlineData("350.00", "-1", "0.00")]
    public void Vat_is_included_in_the_price(string amount, string rate, string vat) =>
        VatCalculator.Included(Money.Parse(amount), decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)).ToWire().ShouldBe(vat);

    [Fact]
    public void Zatca_qr_round_trips_the_five_tlv_values()
    {
        var issued = new DateTimeOffset(2026, 9, 30, 12, 30, 15, TimeSpan.FromHours(3));

        var qr = ZatcaQr.Encode("جمعية التعليم الإلكتروني", "300000000000003", issued, new Money(350m), new Money(45.65m));

        var values = ZatcaQr.Decode(qr);
        values[1].ShouldBe("جمعية التعليم الإلكتروني");
        values[2].ShouldBe("300000000000003");
        values[3].ShouldBe("2026-09-30T09:30:15Z");
        values[4].ShouldBe("350.00");
        values[5].ShouldBe("45.65");
        values.Count.ShouldBe(5);
    }

    [Fact]
    public void Zatca_tlv_values_are_at_most_255_bytes()
    {
        var tooLong = new string('ع', 128);                                               // 256 UTF-8 bytes

        Should.Throw<DomainException>(() => ZatcaQr.Encode(tooLong, "3", DateTimeOffset.UnixEpoch, Money.Zero, Money.Zero))
            .Code.ShouldBe("zatca.tlv_value_too_long");
    }

    [Theory]
    [Rule("RUL-23")]
    [InlineData(true, "نورة العتيبي", "فاعل خير", "Anonymous")]
    [InlineData(false, null, "فاعل خير", "Anonymous")]
    [InlineData(false, "  ", "فاعل خير", "Anonymous")]
    [InlineData(false, "نورة", "نورة", "نورة")]
    [InlineData(false, " نورة  بنت  عبدالله ", "نورة ع.", "نورة ع.")]
    [InlineData(false, "Sara Khan", "Sara K.", "Sara K.")]
    public void Public_donor_name_is_the_first_name_and_last_initial_or_anonymous(bool anonymous, string? name, string ar, string en) =>
        DonorDisplay.For(anonymous, name).ShouldBe((ar, en));

    [Fact]
    public void Enum_wire_values_are_snake_case_with_explicit_overrides()
    {
        OrderStatus.PendingPayment.ToWire().ShouldBe("pending_payment");
        UnitStatus.AwaitingAllocation.ToWire().ShouldBe("awaiting_allocation");
        PaymentMethod.ApplePay.ToWire().ShouldBe("applepay");
        PaymentMethod.StcPay.ToWire().ShouldBe("stcpay");
        DocumentSeries.Drc.ToWire().ShouldBe("DRC");
        LineKind.Buy.ToWire().ShouldBe("buy");
    }

    [Fact]
    public void Enum_wire_values_parse_back_exactly()
    {
        EnumWire.Parse<OrderStatus>("partially_refunded").ShouldBe(OrderStatus.PartiallyRefunded);
        EnumWire.Parse<DocumentSeries>("INV").ShouldBe(DocumentSeries.Inv);
        EnumWire.TryParse<PaymentMethod>("applepay", out var method).ShouldBeTrue();
        method.ShouldBe(PaymentMethod.ApplePay);
        EnumWire.TryParse<PaymentMethod>("apple_pay", out var unknown).ShouldBeFalse();
        unknown.ShouldBe(default);
        EnumWire.TryParse<PaymentMethod>(null, out _).ShouldBeFalse();
        EnumWire.TryParse<DocumentSeries>("inv", out _).ShouldBeFalse();
        Should.Throw<KeyNotFoundException>(() => EnumWire.Parse<OrderStatus>("PAID"));
    }

    [Fact]
    public void Every_enum_value_round_trips_through_its_wire_value()
    {
        var enums = typeof(OrderStatus).Assembly.GetTypes().Where(t => t.IsEnum && t.Namespace == typeof(OrderStatus).Namespace).ToList();
        enums.ShouldNotBeEmpty();
        var parse = typeof(EnumWire).GetMethod(nameof(EnumWire.Parse))!;
        var toWire = typeof(EnumWire).GetMethod(nameof(EnumWire.ToWire))!;

        foreach (var type in enums)
        {
            foreach (var value in Enum.GetValues(type))
            {
                var wire = (string)toWire.MakeGenericMethod(type).Invoke(null, [value])!;
                parse.MakeGenericMethod(type).Invoke(null, [wire]).ShouldBe(value);
            }
        }
    }
}
