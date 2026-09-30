using System.Globalization;
using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Tests.Common;

public sealed class MoneyTests
{
    [Theory, Rule("RUL-31")]
    [InlineData("0.125", "0.13")]
    [InlineData("-0.125", "-0.13")]
    [InlineData("2.675", "2.68")]
    [InlineData("-2.675", "-2.68")]
    [InlineData("0.124", "0.12")]
    [InlineData("45.6521739", "45.65")]
    public void Round_is_half_away_from_zero_like_PostgreSQL(string input, string expected)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);

        Money.Round(value).ToWire().ShouldBe(expected);
    }

    [Fact, Rule("RUL-31")]
    public void Round_differs_from_bankers_rounding_on_midpoints() =>
        Money.Round(0.125m).Amount.ShouldNotBe(decimal.Round(0.125m, 2));

    [Theory, Rule("RUL-31")]
    [InlineData("1.005")]
    [InlineData("0.001")]
    [InlineData("-350.123")]
    public void More_than_two_decimals_is_rejected(string input)
    {
        var ex = Should.Throw<DomainException>(() => new Money(decimal.Parse(input, CultureInfo.InvariantCulture)));

        ex.Code.ShouldBe("money.precision");
    }

    [Fact, Rule("RUL-31")]
    public void Values_beyond_numeric_12_2_are_rejected()
    {
        Should.Throw<DomainException>(() => new Money(10_000_000_000.00m)).Code.ShouldBe("money.overflow");
        Should.Throw<DomainException>(() => new Money(-10_000_000_000.00m)).Code.ShouldBe("money.overflow");
        new Money(9_999_999_999.99m).Amount.ShouldBe(9_999_999_999.99m);
    }

    [Fact, Rule("RUL-31")]
    public void Money_is_SAR_only()
    {
        Money.Currency.ShouldBe("SAR");
        new Money(350m).ToString().ShouldBe("350.00 SAR");
    }

    [Theory, Rule("RUL-31")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Wire_format_is_invariant_whatever_the_thread_culture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Money.Parse("350.00").Amount.ShouldBe(350m);
            Money.Parse("-12.5").ToWire().ShouldBe("-12.50");
            new Money(1750m).ToWire().ShouldBe("1750.00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory, Rule("RUL-31")]
    [InlineData("1,000.00")]
    [InlineData("350,00")]
    [InlineData("١٢٣")]
    [InlineData("")]
    public void Parse_rejects_anything_but_the_wire_format(string wire) =>
        Should.Throw<FormatException>(() => Money.Parse(wire));

    [Fact, Rule("RUL-31")]
    public void Parse_rejects_more_than_two_decimals() =>
        Should.Throw<DomainException>(() => Money.Parse("1.005")).Code.ShouldBe("money.precision");

    [Fact, Rule("RUL-31")]
    public void Halalas_round_trip_exactly()
    {
        new Money(350.00m).ToHalalas().ShouldBe(35_000);
        new Money(45.65m).ToHalalas().ShouldBe(4_565);
        Money.FromHalalas(4_565).ShouldBe(new Money(45.65m));
        Money.FromHalalas(-1).Amount.ShouldBe(-0.01m);
    }

    [Fact, Rule("RUL-31")]
    public void Arithmetic_and_comparison()
    {
        var seat = new Money(350m);

        (seat * 5).ShouldBe(new Money(1_750m));
        (seat + new Money(0.01m)).Amount.ShouldBe(350.01m);
        (seat - new Money(45.65m)).Amount.ShouldBe(304.35m);
        (seat > Money.Zero).ShouldBeTrue();
        (Money.Zero <= seat).ShouldBeTrue();
        seat.IsPositive.ShouldBeTrue();
        Money.Zero.IsPositive.ShouldBeFalse();
        new[] { new Money(100m), new Money(150m), new Money(120m) }.Sum().ShouldBe(new Money(370m));
        Array.Empty<Money>().Sum().ShouldBe(Money.Zero);
        new[] { new Money(2m), seat, Money.Zero }.Order().First().ShouldBe(Money.Zero);
    }

    [Fact, Rule("RUL-31")]
    public void Scale_does_not_affect_equality() =>
        new Money(350m).ShouldBe(new Money(350.00m));
}
