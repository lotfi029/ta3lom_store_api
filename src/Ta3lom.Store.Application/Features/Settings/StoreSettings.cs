namespace Ta3lom.Store.Application.Features.Settings;

/// Typed view of the `setting` table. Missing keys fall back to the seeded defaults.
public sealed record StoreSettings(
    Money MinGiftAmount,
    Money MaxLineAmount,
    int CartMaxLines,
    Money CartMaxTotal,
    int PaymentWindowMinutes,
    IReadOnlyList<Money> AmountPresets,
    string? SiteNameAr,
    string? SiteNameEn,
    string? SupportMobile)
{
    /// The seeded values (D-03, D-04 and the session defaults: minimum gift 10.00).
    public static StoreSettings Defaults { get; } = new(
        new Money(10.00m), new Money(100_000.00m), 50, new Money(100_000.00m), 60,
        [new Money(50.00m), new Money(100.00m), new Money(500.00m)], null, null, null);
}
