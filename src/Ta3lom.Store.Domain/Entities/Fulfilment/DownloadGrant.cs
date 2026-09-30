namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `download_grant` (RUL-41): a digital book purchase, `max_downloads` watermarked downloads, reset by staff, revoked on refund.
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

    public bool IsRevoked => RevokedAt is not null;

    public int Remaining => Math.Max(0, MaxDownloads - DownloadCount);

    /// RUL-39: one grant per digital purchase line; the limit comes from the `download_limit` setting.
    public static DownloadGrant Issue(Guid customerId, Guid orderLineId, Guid bookFormatId, int maxDownloads, DateTimeOffset now) =>
        new(customerId, orderLineId, bookFormatId, now) { MaxDownloads = Math.Max(1, maxDownloads) };

    /// RUL-41: staff reset (FR-S41); a revoked grant stays revoked.
    public bool Reset()
    {
        if (IsRevoked)
        {
            return false;
        }

        DownloadCount = 0;
        return true;
    }

    /// RUL-58: a refunded digital line loses its downloads.
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
