using FsCheck;
using FsCheck.Fluent;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// T-3 (plan 14 S-23, database/01 §4.5): the pooling algorithm of `SponsorshipPool.Add`.
public sealed class PoolingTests
{
    private static readonly Guid Offer = Guid.CreateVersion7();
    private static readonly Guid Product = Guid.CreateVersion7();
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    /// Feeds the contributions one by one, as settlements do, and returns everything the pool produced.
    private static (SponsorshipPool Pool, List<PoolContribution> Open, List<SponsoredUnit> Units, List<UnitFunding> Fundings) Run(
        Money seat, IEnumerable<(Money Amount, bool Zakat)> contributions)
    {
        var pool = new SponsorshipPool(Offer, T0);
        var all = new List<PoolContribution>();
        var units = new List<SponsoredUnit>();
        var fundings = new List<UnitFunding>();
        var i = 0;
        foreach (var (amount, zakat) in contributions)
        {
            var c = PoolContribution.FromLine(Offer, Guid.CreateVersion7(), amount, zakat, T0.AddSeconds(i++));
            all.Add(c);
            var result = pool.Add(c, [.. all.Where(x => x.Remaining > Money.Zero)], Product, seat, T0.AddSeconds(i));
            units.AddRange(result.Units);
            fundings.AddRange(result.Fundings);
        }

        return (pool, all, units, fundings);
    }

    [Fact]
    [Rule("RUL-45")]
    public void Documented_example_100_150_120_on_a_350_seat()
    {
        var (pool, open, units, fundings) = Run(new Money(350m), [(new Money(100m), false), (new Money(150m), false), (new Money(120m), false)]);

        units.Count.ShouldBe(1);
        units[0].Source.ShouldBe(UnitSource.Pooled);
        pool.Balance.ShouldBe(new Money(20m));
        fundings.Select(f => f.Amount.Amount).ShouldBe([100m, 150m, 100m]);
        open.Select(c => c.Remaining.Amount).ShouldBe([0m, 0m, 20m]);
    }

    [Fact]
    [Rule("RUL-45")]
    public void Invariants_hold_for_any_sequence_of_contributions()
    {
        var amounts = Gen.Choose(1, 150_000).Select(h => Money.FromHalalas(h));                    // 0.01 … 1500.00 SAR
        var contribution = Gen.Zip(amounts, Gen.Elements(false, false, false, true));
        var scenario = Gen.Zip(Gen.Choose(1_000, 100_000).Select(h => Money.FromHalalas(h)), Gen.ListOf(contribution));

        Prop.ForAll(scenario.ToArbitrary(), s =>
        {
            var (seat, list) = s;
            var (pool, open, units, fundings) = Run(seat, list);
            var converted = open.Select(c => c.Amount - c.Remaining).Sum();

            return pool.Balance == open.Select(c => c.Remaining).Sum()                            // balance = Σ remaining
                   && seat * units.Count == converted                                              // units × seat = Σ converted
                   && fundings.Select(f => f.Amount).Sum() == converted
                   && units.All(u => fundings.Where(f => f.SponsoredUnitId == u.Id).Select(f => f.Amount).Sum() == seat)
                   && pool.Balance < seat                                                          // whole seats converted
                   && units.All(u => u.HasZakat == fundings.Any(f => f.SponsoredUnitId == u.Id
                                                                    && open.Single(c => c.Id == f.PoolContributionId).IsZakat)); // RUL-47d
        }).QuickCheckThrowOnFailure();
    }

    [Fact]
    [Rule("RUL-46")]
    public void Sweep_moves_the_non_zakat_remainder_and_keeps_zakat()
    {
        var (pool, open, _, _) = Run(new Money(350m), [(new Money(100m), false), (new Money(80m), true)]);

        var swept = pool.Sweep(open, T0.AddDays(30));

        swept.ShouldBe(new Money(100m));
        pool.Balance.ShouldBe(new Money(80m));
        open[0].SweptToFundAt.ShouldNotBeNull();
        open[1].SweptToFundAt.ShouldBeNull();
    }
}
