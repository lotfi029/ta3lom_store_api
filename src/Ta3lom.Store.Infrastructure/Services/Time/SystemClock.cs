using Ta3lom.Store.Application.Abstractions.Time;

namespace Ta3lom.Store.Infrastructure.Services.Time;

/// IClock over TimeProvider. Business dates are Asia/Riyadh; storage is UTC. The optional offset is moved only by
/// `POST /dev/clock` (Development only, e2e of payment expiry); it is zero everywhere else.
public sealed class SystemClock(TimeProvider timeProvider, DevClockOffset? offset = null) : IClock
{
    public static TimeZoneInfo Riyadh { get; } = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow() + (offset?.Value ?? TimeSpan.Zero);

    public DateOnly RiyadhToday => DateOnly.FromDateTime(ToRiyadh(UtcNow).DateTime);

    public DateTimeOffset ToRiyadh(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Riyadh);
}

/// Development-only time travel (`/dev/clock`): the offset added to the system time. Never moved outside Development
/// because the only route that changes it is mapped in Development only.
public sealed class DevClockOffset
{
    private long _ticks;

    public TimeSpan Value => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public void Reset() => Interlocked.Exchange(ref _ticks, 0);
}
