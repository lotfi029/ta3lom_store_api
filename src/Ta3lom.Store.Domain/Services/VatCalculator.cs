namespace Ta3lom.Store.Domain.Services;

/// RUL-27: prices are VAT-inclusive; `vat = round(amount × rate ÷ (100 + rate), 2)`, `net = amount − vat`.
/// 350.00 at 15 % → vat 45.65, net 304.35.
public static class VatCalculator
{
    public static Money Included(Money amount, decimal ratePercent) =>
        ratePercent <= 0m ? Money.Zero : Money.Round(amount.Amount * ratePercent / (100m + ratePercent));
}
