using Ta3lom.Store.Application.Abstractions.Time;

namespace Ta3lom.Store.Infrastructure.Services.Time;

/// IClock over TimeProvider. Business dates are Asia/Riyadh; storage is UTC.
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public static TimeZoneInfo Riyadh { get; } = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly RiyadhToday => DateOnly.FromDateTime(ToRiyadh(UtcNow).DateTime);

    public DateTimeOffset ToRiyadh(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Riyadh);
}
