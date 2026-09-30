namespace Ta3lom.Store.Domain.Services;

/// Goal display (RUL-08): progress = raised ÷ target, rounded **down** to a whole percent. The API returns the
/// uncapped value; the client caps the bar at 100 (11 §2.3).
public static class GoalMath
{
    public static int Percent(decimal raised, decimal target) =>
        target <= 0 ? 0 : (int)decimal.Floor(raised * 100m / target);

    public static int Percent(int funded, int target) =>
        target <= 0 ? 0 : (int)Math.Floor(funded * 100m / target);

    /// Whole days until <paramref name="endsAt"/> in Asia/Riyadh calendar days (0 on the last day); null when open-ended.
    public static int? DaysLeft(DateTimeOffset? endsAt, DateOnly riyadhToday, TimeSpan riyadhOffset)
    {
        if (endsAt is null)
        {
            return null;
        }

        var lastDay = DateOnly.FromDateTime(endsAt.Value.ToOffset(riyadhOffset).DateTime);
        return Math.Max(0, lastDay.DayNumber - riyadhToday.DayNumber);
    }
}

/// Public name of a giver in the gift feed (RUL-23, 09 OP-18 default): anonymous orders show "فاعل خير";
/// others show the first name and the initial of the last name ("نورة ع."), never the full name or mobile.
public static class DonorDisplay
{
    public const string AnonymousAr = "فاعل خير";
    public const string AnonymousEn = "Anonymous";

    public static (string Ar, string En) For(bool isAnonymous, string? contactName)
    {
        if (isAnonymous || string.IsNullOrWhiteSpace(contactName))
        {
            return (AnonymousAr, AnonymousEn);
        }

        var parts = contactName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var shown = parts.Length == 1 ? parts[0] : $"{parts[0]} {parts[^1][0]}.";
        return (shown, shown);
    }
}
