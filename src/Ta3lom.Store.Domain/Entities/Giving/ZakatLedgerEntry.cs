namespace Ta3lom.Store.Domain.Entities.Giving;

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

    /// RUL-20: zakat received on a paid gift line.
    public static ZakatLedgerEntry In(Guid productId, Money amount, Guid orderLineId, DateTimeOffset now) =>
        new(productId, ZakatDirection.In, amount, "settlement", now) { OrderLineId = orderLineId };

    /// RUL-20: a finance disbursement voucher for cause zakat (`reference` = voucher number, `at` = the disbursement date).
    public static ZakatLedgerEntry Voucher(Guid productId, Money amount, string voucherNo, DateTimeOffset disbursedAt, string actor) =>
        new(productId, ZakatDirection.Out, amount, actor, disbursedAt) { Reference = Guard.NotNull(voucherNo) };

    /// RUL-20: the zakat share of a sponsored unit leaves the ring-fence at allocation (`reference` = unit id; once per unit).
    public static ZakatLedgerEntry OutForUnit(Guid productId, Money amount, Guid unitId, string actor, DateTimeOffset now) =>
        new(productId, ZakatDirection.Out, amount, actor, now) { Reference = unitId.ToString() };
}
