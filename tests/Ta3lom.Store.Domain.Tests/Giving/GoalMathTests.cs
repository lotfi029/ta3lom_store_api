using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// T-5: goal display (RUL-08) and public donor names (RUL-23).
public sealed class GoalMathTests
{
    [Theory]
    [Rule("RUL-08")]
    [InlineData("72400.00", "190000.00", 38)]                  // 38.1 % → 38 (rounded down)
    [InlineData("189999.99", "190000.00", 99)]                 // never rounded up to 100 before the goal
    [InlineData("190000.00", "190000.00", 100)]
    [InlineData("250000.00", "190000.00", 131)]                // the raw number may exceed 100 %; the bar caps it
    [InlineData("0.00", "190000.00", 0)]
    public void Progress_is_rounded_down_and_not_capped_in_the_API(string raised, string target, int percent) =>
        GoalMath.Percent(decimal.Parse(raised, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(target, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(percent);

    [Fact]
    [Rule("RUL-08")]
    public void Unit_progress_and_days_left_in_Riyadh_days()
    {
        GoalMath.Percent(184, 300).ShouldBe(61);
        GoalMath.Percent(5, 0).ShouldBe(0);
        var endsAt = new DateTimeOffset(2026, 12, 31, 20, 59, 59, TimeSpan.Zero);             // 23:59:59 on 31 Dec in Riyadh
        GoalMath.DaysLeft(endsAt, new DateOnly(2026, 12, 31), TimeSpan.FromHours(3)).ShouldBe(0);
        GoalMath.DaysLeft(endsAt, new DateOnly(2026, 12, 1), TimeSpan.FromHours(3)).ShouldBe(30);
        GoalMath.DaysLeft(null, new DateOnly(2026, 12, 1), TimeSpan.FromHours(3)).ShouldBeNull();
    }

    [Theory]
    [Rule("RUL-23")]
    [InlineData(true, "نورة عبدالله العتيبي", "فاعل خير")]
    [InlineData(false, "نورة عبدالله العتيبي", "نورة ا.")]
    [InlineData(false, "نورة", "نورة")]
    [InlineData(false, null, "فاعل خير")]
    public void The_feed_shows_an_anonymous_giver_as_faael_khair_and_never_a_full_name(bool anonymous, string? name, string shown) =>
        DonorDisplay.For(anonymous, name).Ar.ShouldBe(shown);
}
