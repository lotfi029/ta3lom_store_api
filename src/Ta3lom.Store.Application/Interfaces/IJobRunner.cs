namespace Ta3lom.Store.Application.Interfaces;

/// Development/Staging tooling (`POST /dev/jobs/{name}/run`): runs a registered Quartz job now instead of waiting for its cron.
public interface IJobRunner
{
    IReadOnlyDictionary<string, string> Jobs { get; }

    /// False when no job has that name.
    Task<bool> TriggerAsync(string jobName, CancellationToken ct = default);
}
