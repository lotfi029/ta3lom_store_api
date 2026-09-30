namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `stock_movement` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class StockMovement
{
    public StockMovement(Guid bookFormatId, int delta, StockMovementReason reason, string actor, DateTimeOffset now)
    {
        BookFormatId = Guard.NotEmpty(bookFormatId);
        Delta = delta;
        Reason = reason;
        At = now;
        Actor = Guard.NotNull(actor);
    }

    private StockMovement()
    {
    }

    public long Id { get; private set; }

    public Guid BookFormatId { get; private set; }

    public int Delta { get; private set; }

    public StockMovementReason Reason { get; private set; }

    public string? RefType { get; private set; }

    public string? RefId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Actor { get; private set; } = null!;

    /// RUL-30: every stock change is a ledger row (integrity check 3: Σ delta = `stock_on_hand`).
    public static StockMovement Record(Guid bookFormatId, int delta, StockMovementReason reason, string refType, string? refId, string? note,
        string actor, DateTimeOffset now)
    {
        Guard.Against(delta == 0, "stock.delta_zero");
        return new StockMovement(bookFormatId, delta, reason, actor, now) { RefType = refType, RefId = refId, Note = note };
    }
}
