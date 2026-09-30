using Ta3lom.Store.Application.Abstractions.Time;

namespace Ta3lom.Store.Testing;

/// Settable IClock for tests. Riyadh is UTC+3 all year (no daylight saving).
public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    private static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    public FakeClock()
        : this(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset UtcNow { get; private set; } = utcNow.ToUniversalTime();

    public DateOnly RiyadhToday => DateOnly.FromDateTime(ToRiyadh(UtcNow).DateTime);

    public DateTimeOffset ToRiyadh(DateTimeOffset instant) => instant.ToOffset(RiyadhOffset);

    public void Set(DateTimeOffset utcNow) => UtcNow = utcNow.ToUniversalTime();

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
