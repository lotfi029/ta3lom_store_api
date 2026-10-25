using Ta3lom.Store.Application.Abstractions.Wire;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Application.Tests.Wire;

/// Wire money (D-02): ASCII, Arabic-Indic and Eastern Arabic digits and the Arabic decimal separator are accepted;
/// group separators and anything else are a validation error, never an exception.
public sealed class MoneyInputTests
{
    [Theory]
    [InlineData("50", "50.00")]
    [InlineData("50.5", "50.50")]
    [InlineData("٥٠", "50.00")]
    [InlineData("١٢٫٥", "12.50")]
    [InlineData("۱۲.۵۰", "12.50")]
    [InlineData("0.01", "0.01")]
    public void Valid_amounts_parse_to_money(string wire, string expected)
    {
        MoneyInput.TryParse(wire, out var money).ShouldBeTrue();

        money.ToWire().ShouldBe(expected);
        MoneyInput.IsPositive(wire).ShouldBeTrue();
        MoneyInput.Parse(wire).ShouldBe(money);
    }

    [Theory]
    [InlineData("5x")]
    [InlineData("٥x")]
    [InlineData("1,000")]
    [InlineData("١٬٠٠٠")]
    [InlineData("1.234")]
    [InlineData("-5")]
    [InlineData(" 5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345678901")]
    [InlineData("𝟓𝟎")]
    public void Anything_else_is_invalid_and_never_throws(string? wire)
    {
        MoneyInput.TryParse(wire, out _).ShouldBeFalse();
        MoneyInput.IsPositive(wire).ShouldBeFalse();
        MoneyInput.IsValid(wire).ShouldBeFalse();
    }

    [Fact]
    public void Zero_is_valid_but_not_positive()
    {
        MoneyInput.IsValid("0").ShouldBeTrue();
        MoneyInput.IsPositive("٠٫٠٠").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_maps_digits_and_the_decimal_separator_only()
    {
        MoneyInput.Normalize("٠١٢٣٤٥٦٧٨٩٫۰۱۲۳۴۵۶۷۸۹").ShouldBe("0123456789.0123456789");
        MoneyInput.Normalize("١٬٠").ShouldBe("1٬0");
        Should.Throw<FormatException>(() => MoneyInput.Parse("5x"));
        Money.Zero.IsPositive.ShouldBeFalse();
    }
}
