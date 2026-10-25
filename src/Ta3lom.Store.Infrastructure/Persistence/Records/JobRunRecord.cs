namespace Ta3lom.Store.Infrastructure.Persistence.Records;

/// Table `job_run`: one row per job and scheduled fire time. Every instance's Quartz (RAM store) fires each job; the
/// instance whose insert wins runs it (L-4). Rows older than 30 days are pruned on the next claim.
internal sealed class JobRunRecord
{
    private JobRunRecord()
    {
    }

    public string Job { get; private set; } = null!;

    public DateTimeOffset FireTime { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }
}
