using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.ValueObjects;

/// The named alternatives of Money's operators (CA2225) agree with the operators.
public sealed class MoneyNamedOperatorTests
{
    [Fact]
    [Rule("RUL-31")]
    public void Add_subtract_and_multiply_match_the_operators()
    {
        var seat = new Money(350m);
        var vat = new Money(45.65m);

        Money.Add(seat, vat).ShouldBe(seat + vat);
        Money.Subtract(seat, vat).ShouldBe(new Money(304.35m));
        Money.Multiply(seat, 3).ShouldBe(new Money(1_050m));
        seat.CompareTo(vat).ShouldBePositive();
    }
}
