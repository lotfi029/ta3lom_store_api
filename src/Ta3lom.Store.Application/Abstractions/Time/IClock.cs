namespace Ta3lom.Store.Application.Abstractions.Time;

/// The only source of time in src (09 §3.5). Business dates are Asia/Riyadh.
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly RiyadhToday { get; }

    DateTimeOffset ToRiyadh(DateTimeOffset instant);
}
