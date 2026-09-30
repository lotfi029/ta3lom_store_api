namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `download_grant` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class DownloadGrant : AggregateRoot
{
    public DownloadGrant(Guid customerId, Guid orderLineId, Guid bookFormatId, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        CustomerId = Guard.NotEmpty(customerId);
        OrderLineId = Guard.NotEmpty(orderLineId);
        BookFormatId = Guard.NotEmpty(bookFormatId);
        MaxDownloads = 5;
        DownloadCount = 0;
        CreatedAt = now;
    }

    private DownloadGrant()
    {
    }

    public Guid CustomerId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Guid BookFormatId { get; private set; }

    public int MaxDownloads { get; private set; }

    public int DownloadCount { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
