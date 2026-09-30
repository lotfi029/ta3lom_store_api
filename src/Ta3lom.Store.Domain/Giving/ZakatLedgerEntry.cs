using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Giving;

/// Table `zakat_ledger` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class ZakatLedgerEntry
{
    public ZakatLedgerEntry(Guid productId, ZakatDirection direction, Money amount, string actor, DateTimeOffset now)
    {
        ProductId = Guard.NotEmpty(productId);
        Direction = direction;
        Amount = amount;
        At = now;
        Actor = Guard.NotNull(actor);
    }

    private ZakatLedgerEntry()
    {
    }

    public long Id { get; private set; }

    public Guid ProductId { get; private set; }

    public ZakatDirection Direction { get; private set; }

    public Money Amount { get; private set; }

    public Guid? OrderLineId { get; private set; }

    public string? Reference { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Actor { get; private set; } = null!;
}
