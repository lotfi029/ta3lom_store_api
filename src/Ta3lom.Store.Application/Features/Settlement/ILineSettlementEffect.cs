using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement;

/// One paid-order effect per line case (plan 14 §13.4, RUL-39). Every effect runs inside the settlement transaction and
/// uses Domain methods and repositories only; row locks live in Infrastructure repositories. Exactly one effect must
/// handle each line, otherwise settlement is refused with `settlement.effect_missing`.
public interface ILineSettlementEffect
{
    bool Handles(OrderLine line, SettlementContext context);

    Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct);
}

/// What an effect may read about the order being settled.
public sealed class SettlementContext(Order order, StoreSettings settings, IReadOnlyDictionary<Guid, BookFormatKind> formatKinds, DateTimeOffset now)
{
    public Order Order { get; } = order;

    public StoreSettings Settings { get; } = settings;

    public DateTimeOffset Now { get; } = now;

    /// The book format kind of a `buy` book line (null for other lines).
    public BookFormatKind? FormatKind(OrderLine line) =>
        line.BookFormatId is { } id && formatKinds.TryGetValue(id, out var kind) ? kind : null;
}
