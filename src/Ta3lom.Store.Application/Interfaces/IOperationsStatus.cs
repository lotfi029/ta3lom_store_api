namespace Ta3lom.Store.Application.Interfaces;

/// Operational health that only SQL can answer: the integrity checks and the dead-letter backlog.
public interface IOperationsStatus
{
    /// Runs every query of the embedded integrity script; each must return 0 rows.
    Task<IReadOnlyList<IntegrityCheckResult>> RunIntegrityChecksAsync(CancellationToken ct = default);

    /// Outbox messages that were dead-lettered (`dead_lettered = true`).
    Task<int> DeadOutboxCountAsync(CancellationToken ct = default);
}

/// One check: its title (the comment above it) and how many offending rows it returned.
public sealed record IntegrityCheckResult(int Number, string Title, int Rows);
