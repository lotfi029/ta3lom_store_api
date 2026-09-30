namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `refund_line` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class RefundLine
{
    public RefundLine(Guid refundId, Guid orderLineId, Money amount)
    {
        RefundId = Guard.NotEmpty(refundId);
        OrderLineId = Guard.NotEmpty(orderLineId);
        Amount = amount;
    }

    private RefundLine()
    {
    }

    public Guid RefundId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Money Amount { get; private set; }

    public int? Units { get; private set; }
}
