namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `download_log` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class DownloadLogEntry
{
    public DownloadLogEntry(Guid grantId, DateTimeOffset now)
    {
        GrantId = Guard.NotEmpty(grantId);
        At = now;
    }

    private DownloadLogEntry()
    {
    }

    public long Id { get; private set; }

    public Guid GrantId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string? IpHash { get; private set; }
}
