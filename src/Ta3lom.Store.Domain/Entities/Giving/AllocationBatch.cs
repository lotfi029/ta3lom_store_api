namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `allocation_batch` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class AllocationBatch : AggregateRoot
{
    public AllocationBatch(Guid productId, string createdBy, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        CreatedBy = Guard.NotNull(createdBy);
        CreatedAt = now;
    }

    private AllocationBatch()
    {
    }

    public Guid ProductId { get; private set; }

    public string? Note { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}
