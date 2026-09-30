using System.Globalization;

namespace Ta3lom.Store.Domain.ValueObjects;

/// SAR amount, exactly two decimals, never floating point (RUL-31).
/// Rounding is half away from zero, which is what PostgreSQL round(numeric, 2) does —
/// .NET's default (banker's rounding) would disagree with the database on x.xx5 values.
public readonly record struct Money : IComparable<Money>
{
    public const string Currency = "SAR";
    private const decimal Max = 9_999_999_999.99m;              // numeric(12,2)

    public Money(decimal amount)
    {
        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("money.precision", $"{amount.ToString(CultureInfo.InvariantCulture)} has more than two decimals");
        }

        if (Math.Abs(amount) > Max)
        {
            throw new DomainException("money.overflow", $"{amount.ToString(CultureInfo.InvariantCulture)} exceeds numeric(12,2)");
        }

        Amount = amount;
    }

    public decimal Amount { get; }

    public static Money Zero { get; } = new(0m);

    public bool IsPositive => Amount > 0;

    public static Money Round(decimal value) => new(decimal.Round(value, 2, MidpointRounding.AwayFromZero));

    /// Parses the wire format ("350.00") in the invariant culture, whatever the thread culture is.
    public static Money Parse(string wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        return new(decimal.Parse(wire, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    public static Money FromHalalas(long halalas) => new(halalas / 100m);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);

    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);

    public static Money operator *(Money a, int quantity) => new(a.Amount * quantity);

    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;

    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;

    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;

    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Multiply(Money left, int quantity) => left * quantity;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    /// Gateway minor units, exact.
    public long ToHalalas() => decimal.ToInt64(Amount * 100m);

    public string ToWire() => Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public override string ToString() => $"{ToWire()} {Currency}";
}

public static class MoneyExtensions
{
    public static Money Sum(this IEnumerable<Money> source) => source.Aggregate(Money.Zero, (a, b) => a + b);
}
