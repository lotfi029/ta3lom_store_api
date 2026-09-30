namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `allocation_batch` (RUL-47): one officer request; all of its units were allocated together.
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

    public static AllocationBatch Create(Guid productId, string? note, string createdBy, DateTimeOffset now) =>
        new(productId, createdBy, now) { Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
}
