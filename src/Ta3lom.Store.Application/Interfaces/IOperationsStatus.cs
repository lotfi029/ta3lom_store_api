namespace Ta3lom.Store.Application.Interfaces;

/// Operational health that only SQL can answer (database/03 §6, §8): the nightly integrity checks and the dead-letter
/// backlog. Used by the dashboard alerts (S-33) and the `IntegrityChecks` job (S-35).
public interface IOperationsStatus
{
    /// Runs every query of `db/reference/003_integrity_checks.sql`; each must return 0 rows.
    Task<IReadOnlyList<IntegrityCheckResult>> RunIntegrityChecksAsync(CancellationToken ct = default);

    /// Outbox messages dead-lettered after 24 h (`next_attempt_at = infinity`, not processed).
    Task<int> DeadOutboxCountAsync(CancellationToken ct = default);
}

/// One check: its title (the comment above it) and how many offending rows it returned.
public sealed record IntegrityCheckResult(int Number, string Title, int Rows);
