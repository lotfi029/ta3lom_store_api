using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Features.Outbox;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Outbox;

/// `Store:Outbox` — the dispatcher can be switched off (e.g. a one-off CLI host).
public sealed class OutboxOptions
{
    public const string Section = "Store:Outbox";

    public bool Enabled { get; set; } = true;
}

/// Nudged after a SaveChanges that added outbox rows, so a paid order's SMS goes out within about a second.
internal sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Nudge()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // already nudged
            }
        }
    }

    public Task WaitAsync(CancellationToken ct) => _signal.WaitAsync(ct);

    public void Dispose() => _signal.Dispose();
}

/// Claims batches of 20 with `FOR UPDATE SKIP LOCKED` and a per-row lease (`claimed_until`, 5 minutes; no transaction is held
/// during SMS/SMTP calls), runs one handler per message in its own scope, retries with exponential backoff (10 s … 6 h,
/// ±20 %), and dead-letters a message after <see cref="MaxAttempts"/> attempts (`dead_lettered`, one staff alert). A lease
/// that ran out (a crashed instance) makes the row claimable again, so several instances are safe. The outbox message id
/// is the dedupe key of the provider call (`notification.dedupe_key`).
internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopes,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int MaxAttempts = 10;
    private const int BatchSize = 20;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        Task? tick = null, nudged = null;                      // kept across loops: a PeriodicTimer allows one pending wait only
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int claimed;
                do
                {
                    claimed = await RunOnceAsync(stoppingToken);
                }
                while (claimed == BatchSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoopFailed(logger, ex);                                               // e.g. database down: try again next tick
            }

            tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
            nudged ??= signal.WaitAsync(stoppingToken);
            var woke = await Task.WhenAny(tick, nudged);
            if (woke.IsCanceled)
            {
                return;
            }

            if (woke == tick)
            {
                tick = null;
            }
            else
            {
                nudged = null;
            }
        }
    }

    /// Claims and handles one batch; returns the number claimed (also used by tests and the dev tooling).
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<OutboxRow> batch;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Time.IClock>().UtcNow;
            var leaseEnd = now + Lease;
            batch = await db.Database.SqlQuery<OutboxRow>($"""
                UPDATE store.outbox_message m
                   SET attempts = m.attempts + 1, claimed_until = {leaseEnd}
                 WHERE m.id IN (SELECT id FROM store.outbox_message
                                 WHERE processed_at IS NULL AND NOT dead_lettered AND next_attempt_at <= {now}
                                   AND (claimed_until IS NULL OR claimed_until < {now})
                                 ORDER BY next_attempt_at
                                 LIMIT {BatchSize}
                                 FOR UPDATE SKIP LOCKED)
                RETURNING m.id, m.type, m.payload::text AS payload, m.occurred_at, m.attempts   -- snake_case: the naming convention maps OutboxRow
                """).ToListAsync(ct);
        }

        // RETURNING has no order: handle the batch oldest first, in PostgreSQL's (occurred_at, uuid) order, so a newer
        // change is still pending while an older one runs (the site-rebuild debounce relies on it).
        foreach (var row in batch.OrderBy(r => r.OccurredAt).ThenBy(r => r.Id, PostgresUuidOrder.Instance))
        {
            await HandleAsync(row, ct);
        }

        return batch.Count;
    }

    private async Task HandleAsync(OutboxRow row, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<StoreDbContext>();
        var clock = services.GetRequiredService<Application.Abstractions.Time.IClock>();
        try
        {
            var handler = services.GetServices<IOutboxHandler>().FirstOrDefault(h => h.MessageType == row.Type)
                          ?? throw new InvalidOperationException($"No outbox handler for type {row.Type}.");
            await handler.HandleAsync(new OutboxEnvelope(row.Id, row.Type, row.Payload, row.OccurredAt, row.Attempts), ct);
            var done = clock.UtcNow;
            await db.Database.ExecuteSqlAsync($"""
                UPDATE store.outbox_message SET processed_at = {done}, claimed_until = NULL, last_error = NULL WHERE id = {row.Id}
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var deadLetter = row.Attempts >= MaxAttempts;
            var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            var retryAt = clock.UtcNow + Backoff(row.Attempts);
            await using var retryScope = scopes.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<StoreDbContext>();
            await retryDb.Database.ExecuteSqlAsync($"""
                UPDATE store.outbox_message
                   SET claimed_until = NULL, last_error = {error}, dead_lettered = {deadLetter}, next_attempt_at = {retryAt}
                 WHERE id = {row.Id}
                """, ct);
            if (deadLetter && row.Type != nameof(StaffAlert))
            {
                retryScope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new StaffAlert("outbox_dead_letter", null,
                    string.Create(CultureInfo.InvariantCulture, $"{row.Type} {row.Id} after {row.Attempts} attempts: {error}")));
                await retryDb.SaveChangesAsync(ct);
            }

            LogHandlerFailed(logger, row.Id, row.Type, row.Attempts, deadLetter, ex);
        }
    }

    /// 10 s, 20 s, 40 s … capped at 6 h, ±20 % jitter.
    internal static TimeSpan Backoff(int attempts)
    {
        var seconds = Math.Min(10 * Math.Pow(2, Math.Max(attempts, 1) - 1), 6 * 3600);
        return TimeSpan.FromSeconds(seconds * RandomNumberGenerator.GetInt32(800, 1201) / 1000.0);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempts}; dead letter: {DeadLetter}")]
    private static partial void LogHandlerFailed(ILogger logger, Guid messageId, string type, int attempts, bool deadLetter, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatcher loop failed")]
    private static partial void LogLoopFailed(ILogger logger, Exception ex);

    internal sealed record OutboxRow(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt, int Attempts);
}

/// PostgreSQL compares uuids byte by byte in big-endian order; <see cref="Guid.CompareTo(Guid)"/> does not.
internal sealed class PostgresUuidOrder : IComparer<Guid>
{
    public static readonly PostgresUuidOrder Instance = new();

    public int Compare(Guid x, Guid y) => x.ToByteArray(bigEndian: true).AsSpan().SequenceCompareTo(y.ToByteArray(bigEndian: true));
}
